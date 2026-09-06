package loader

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"strings"
	"sync"

	"dynamic-runtime/extensions/config"
)

// BuiltinLoader is the v0.1 concrete Loader: it materializes Modules through
// Loader Backends selected by Artifact.BackendType.
//
//   - BackendType "builtin" is provided by the Loader itself: it materializes
//     Modules from pre-registered in-process Go factories (Source like
//     "builtin://name"); the Logical Module Type comes from Artifact.Type.
//   - Other BackendTypes (e.g. "wasm") are registered via RegisterBackend; each
//     Backend decides the Logical Module Type from its own metadata.
//
// The Loader implements Loader and ModuleRegistry, and owns its ModuleUsage
// tracker (exposed through Usage) so an upper integration adapter can
// Acquire/Release usage explicitly.
type BuiltinLoader struct {
	mu       sync.Mutex
	closed   bool
	inFlight int
	modules  map[string]*Module

	allIdle chan struct{} // closed exactly once when closed && inFlight == 0
	drained sync.Once

	builtins BuiltinRegistry
	usage    ModuleUsage
	backends map[BackendType]Backend
}

// NewBuiltinLoader creates a running BuiltinLoader with an internal Builtin
// Registry, Module Usage tracker, and the builtin Backend pre-registered under
// BackendType "builtin".
func NewBuiltinLoader() *BuiltinLoader {
	l := &BuiltinLoader{
		modules:  make(map[string]*Module),
		allIdle:  make(chan struct{}),
		builtins: NewBuiltinRegistry(),
		usage:    NewModuleUsage(),
		backends: make(map[BackendType]Backend),
	}
	l.backends[BackendBuiltin] = &builtinBackend{l: l}
	return l
}

// Usage returns the Loader-owned ModuleUsage tracker. Upper adapters (e.g. a
// Config integration adapter) Acquire/Release here so that Unload can enforce
// the in-use constraint without scanning Fibers or Config state.
func (l *BuiltinLoader) Usage() ModuleUsage { return l.usage }

// RegisterBuiltin registers an in-process implementation source.
func (l *BuiltinLoader) RegisterBuiltin(source string, factory BuiltinFactory) error {
	return l.builtins.Register(source, factory)
}

// RegisterBackend registers a Backend under a BackendType. Load dispatches to
// the registered Backend whenever Artifact.BackendType matches (after legacy
// normalization). The builtin BackendType is reserved for the Loader's own
// builtin Backend: registering it again fails with ErrBackendExists. Duplicate
// BackendTypes fail with ErrBackendExists and never replace the original.
// Registering after Close fails with ErrLoaderClosed.
func (l *BuiltinLoader) RegisterBackend(backendType BackendType, backend Backend) error {
	if backendType == "" || backend == nil {
		return ErrInvalidBackend
	}
	if backendType == BackendBuiltin {
		return ErrBackendExists
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.closed {
		return ErrLoaderClosed
	}
	if _, exists := l.backends[backendType]; exists {
		return ErrBackendExists
	}
	l.backends[backendType] = backend
	return nil
}

// Load loads artifact into a Module. Semantics per the specification:
//
//	validate Artifact -> normalize (resolve BackendType) -> Backend lookup ->
//	Backend.Load() -> Module validation -> linearize registry mutation -> Loaded.
//
// Backend selection uses ONLY Artifact.BackendType; Module.Type (the Logical
// Module Type) is decided by the Backend and never by copying the BackendType.
// Any failure leaves the registry unchanged and never affects already-loaded
// Modules. Duplicate ID fails with ErrModuleExists. User code (BuiltinFactory
// or a registered Backend) never runs under the Loader state lock.
func (l *BuiltinLoader) Load(ctx context.Context, artifact Artifact) (*Module, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if err := validateArtifactShape(artifact); err != nil {
		return nil, err
	}
	artifact, err := normalizeArtifact(artifact)
	if err != nil {
		return nil, err
	}
	if err := l.validateArtifact(artifact); err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, ctx.Err()
	}

	l.mu.Lock()
	if l.closed {
		l.mu.Unlock()
		return nil, ErrLoaderClosed
	}
	if l.modules[artifact.ID] != nil {
		l.mu.Unlock()
		return nil, ErrModuleExists
	}
	l.inFlight++
	l.mu.Unlock()

	// Phase: run the Backend OUTSIDE the Loader lock.
	defer func() {
		l.mu.Lock()
		l.inFlight--
		if l.closed && l.inFlight == 0 {
			l.drained.Do(func() { close(l.allIdle) })
		}
		l.mu.Unlock()
	}()

	if err := ctx.Err(); err != nil {
		return nil, err
	}

	backend, ok := l.backendFor(artifact.BackendType)
	if !ok {
		return nil, fmt.Errorf("%w: no backend registered for %q", ErrInvalidBackendType, artifact.BackendType)
	}
	mod, err := callBackendLoad(backend, ctx, artifact)
	if err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if mod.Factory == nil {
		return nil, fmt.Errorf("%w: backend %q returned a nil factory", ErrInvalidModule, artifact.BackendType)
	}
	if err := validateModule(mod); err != nil {
		return nil, err
	}

	// Phase 5: linearize the registry mutation. A conflict at commit time
	// discards the freshly loaded result without disturbing anything.
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.closed {
		return nil, ErrLoaderClosed
	}
	if existing := l.modules[artifact.ID]; existing != nil {
		return nil, ErrModuleExists
	}
	l.modules[artifact.ID] = &mod
	return &mod, nil
}

// Unload removes a Loaded Module (Loaded -> Absent). A Module that is still in
// use cannot be unloaded (ErrModuleInUse). Unload is atomic: the registry entry
// is only removed on success.
func (l *BuiltinLoader) Unload(ctx context.Context, id string) error {
	if ctx == nil {
		ctx = context.Background()
	}
	l.mu.Lock()
	defer l.mu.Unlock()
	if l.modules[id] == nil {
		return ErrModuleNotFound
	}
	if l.usage.InUse(id) {
		return ErrModuleInUse
	}
	delete(l.modules, id)
	return nil
}

// Get returns a copy of the Loaded Module for id.
func (l *BuiltinLoader) Get(id string) (Module, bool) {
	l.mu.Lock()
	defer l.mu.Unlock()
	m, ok := l.modules[id]
	if !ok {
		return Module{}, false
	}
	return *m, true
}

// Has reports whether id is Loaded.
func (l *BuiltinLoader) Has(id string) bool {
	l.mu.Lock()
	defer l.mu.Unlock()
	_, ok := l.modules[id]
	return ok
}

// Snapshot returns an immutable copy of the Loaded Modules at one linearization
// point. Later mutations never modify an already-returned Snapshot.
func (l *BuiltinLoader) Snapshot() []Module {
	l.mu.Lock()
	defer l.mu.Unlock()
	out := make([]Module, 0, len(l.modules))
	for _, m := range l.modules {
		out = append(out, *m)
	}
	return out
}

// Close shuts the Loader down: it rejects new Loads, waits for in-flight Loads,
// unloads every Module that is not in use, and reports Modules that remain in
// use. Close is idempotent.
func (l *BuiltinLoader) Close() error {
	return l.CloseContext(context.Background())
}

// CloseContext shuts the Loader down with a bounded wait. On timeout it returns
// ctx.Err() while the Loader remains in its closing state (it never claims all
// Modules are gone when they are not). A later CloseContext finishes the
// shutdown once conditions are truly met.
func (l *BuiltinLoader) CloseContext(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}

	l.mu.Lock()
	if !l.closed {
		l.closed = true
		if l.inFlight == 0 {
			l.drained.Do(func() { close(l.allIdle) })
		}
	}
	// Reject new Loads is implied by closed.
	idle := l.allIdle
	l.mu.Unlock()

	// Wait for in-flight loads to finish (or be canceled at a phase boundary).
	select {
	case <-idle:
	case <-ctx.Done():
		return ctx.Err()
	}

	// Unload every Module that is not in use; Modules still in use remain in
	// the registry and are reported.
	l.mu.Lock()
	var inUse []string
	for id := range l.modules {
		if !l.usage.InUse(id) {
			delete(l.modules, id)
		} else {
			inUse = append(inUse, id)
		}
	}
	l.mu.Unlock()

	if len(inUse) > 0 {
		return fmt.Errorf("%w: modules still in use: %v", ErrModuleInUse, inUse)
	}
	return nil
}

// RegisterFactories explicitly registers every currently Loaded Module's
// Factory into the given Config FactoryRegistry (keyed by Module.Type, the
// Logical Module Type). This is never automatic and never uses BackendType as a
// key. A Type collision fails with config.ErrFactoryExists and the original
// Factory stays valid; other registrations continue and errors are aggregated.
func (l *BuiltinLoader) RegisterFactories(registry config.FactoryRegistry) error {
	if registry == nil {
		return fmt.Errorf("%w: nil config factory registry", ErrInvalidModule)
	}
	mods := l.Snapshot()
	// Register in a stable order (by Module ID) so repeated registration is
	// deterministic even when several loaded Modules share a Type.
	sort.Slice(mods, func(i, j int) bool { return mods[i].ID < mods[j].ID })

	var errs []error
	for _, m := range mods {
		if err := registry.Register(m.Type, m.Factory); err != nil {
			errs = append(errs, fmt.Errorf("register type %q (module %q): %w", m.Type, m.ID, err))
		}
	}
	return errors.Join(errs...)
}

// backendFor returns the Backend registered for backendType, if any.
func (l *BuiltinLoader) backendFor(backendType BackendType) (Backend, bool) {
	l.mu.Lock()
	defer l.mu.Unlock()
	b, ok := l.backends[backendType]
	return b, ok
}

// ---------------------------------------------------------------------------
// Artifact normalization (BackendType resolution, legacy compatibility).
// ---------------------------------------------------------------------------

// validateArtifactShape validates the Backend-independent Artifact shape.
func validateArtifactShape(a Artifact) error {
	if a.ID == "" {
		return invalidArtifactf("empty artifact id")
	}
	if a.Source == "" {
		return invalidArtifactf("artifact %q has empty source", a.ID)
	}
	return nil
}

// normalizeArtifact resolves the canonical Artifact.BackendType:
//
//   - If BackendType is already set, it is authoritative. A legacy Type that
//     names a DIFFERENT known backend type is a conflict
//     (ErrArtifactTypeConflict); a non-backend Type (logical type) is kept for
//     backends that consume it (builtin).
//   - If BackendType is empty and Type names a known backend type ("builtin" /
//     "wasm"), Type is used as a legacy alias (Case B).
//   - Otherwise the artifact is treated as a legacy builtin artifact when its
//     Source is "builtin://...".
func normalizeArtifact(a Artifact) (Artifact, error) {
	out := a
	if out.BackendType != "" {
		if out.Type != "" && isKnownBackendType(out.Type) && BackendType(out.Type) != out.BackendType {
			return out, fmt.Errorf("%w: type %q conflicts with backend type %q", ErrArtifactTypeConflict, out.Type, out.BackendType)
		}
		return out, nil
	}
	if out.Type != "" && isKnownBackendType(out.Type) {
		out.BackendType = BackendType(out.Type)
		return out, nil
	}
	if strings.HasPrefix(out.Source, "builtin://") {
		out.BackendType = BackendBuiltin
		return out, nil
	}
	return out, fmt.Errorf("%w: artifact %q has no backend type (set Artifact.BackendType)", ErrInvalidBackendType, out.ID)
}

// validateArtifact validates the normalized artifact.
func (l *BuiltinLoader) validateArtifact(a Artifact) error {
	if a.BackendType == "" {
		return fmt.Errorf("%w: artifact %q has empty backend type", ErrInvalidBackendType, a.ID)
	}
	if a.BackendType == BackendBuiltin && a.Type == "" {
		return invalidArtifactf("builtin artifact %q has empty type (logical module type)", a.ID)
	}
	return nil
}

func isKnownBackendType(s string) bool {
	_, ok := knownBackendTypes[BackendType(s)]
	return ok
}

// validateModule validates a Backend-produced Module.
func validateModule(m Module) error {
	if m.ID == "" {
		return fmt.Errorf("%w: empty module id", ErrInvalidModule)
	}
	if m.Type == "" {
		return fmt.Errorf("%w: module %q has empty type", ErrInvalidModule, m.ID)
	}
	if m.Factory == nil {
		return fmt.Errorf("%w: module %q has nil factory", ErrInvalidModule, m.ID)
	}
	return nil
}

// callBackendLoad invokes a Backend and converts a panic into ErrModuleLoadPanic
// so a misbehaving Backend can never crash the Loader or corrupt its registry.
func callBackendLoad(b Backend, ctx context.Context, artifact Artifact) (module Module, err error) {
	defer func() {
		if r := recover(); r != nil {
			module = Module{}
			err = fmt.Errorf("%w: backend %q panicked: %v", ErrModuleLoadPanic, artifact.BackendType, r)
		}
	}()
	return b.Load(ctx, artifact)
}

// ---------------------------------------------------------------------------
// Builtin Backend: BackendType "builtin".
// ---------------------------------------------------------------------------

// builtinBackend materializes Modules from the in-process BuiltinRegistry.
// The Logical Module Type is taken from Artifact.Type (legacy field) and must
// NOT be a backend keyword ("builtin"/"wasm").
type builtinBackend struct{ l *BuiltinLoader }

var _ Backend = (*builtinBackend)(nil)

func (b *builtinBackend) Load(ctx context.Context, artifact Artifact) (Module, error) {
	if artifact.BackendType != BackendBuiltin {
		return Module{}, fmt.Errorf("%w: builtin backend accepts only BackendType %q, got %q", ErrInvalidBackendType, BackendBuiltin, artifact.BackendType)
	}
	if artifact.Type == "" || isKnownBackendType(artifact.Type) {
		return Module{}, fmt.Errorf("%w: builtin artifact %q requires a logical module type in Artifact.Type, got %q", ErrInvalidModuleType, artifact.ID, artifact.Type)
	}
	if err := ctx.Err(); err != nil {
		return Module{}, err
	}
	bf, ok := b.l.builtins.Lookup(artifact.Source)
	if !ok {
		return Module{}, errUnknownBuiltin(artifact.Source)
	}
	if err := ctx.Err(); err != nil {
		return Module{}, err
	}
	factory, err := callBuiltinFactory(bf)
	if err != nil {
		return Module{}, err
	}
	if factory == nil {
		return Module{}, fmt.Errorf("%w: builtin %q returned a nil factory", ErrInvalidModule, artifact.Source)
	}
	if err := ctx.Err(); err != nil {
		return Module{}, err
	}
	return Module{
		ID:      artifact.ID,
		Type:    artifact.Type,
		Version: artifact.Version,
		Factory: factory,
	}, nil
}

var (
	_ Loader         = (*BuiltinLoader)(nil)
	_ ModuleRegistry = (*BuiltinLoader)(nil)
)
