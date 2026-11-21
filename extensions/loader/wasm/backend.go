package wasm

import (
	"context"
	"errors"
	"fmt"
	"net/url"
	"os"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"

	"github.com/tetratelabs/wazero"
	"github.com/tetratelabs/wazero/api"

	"dynamic-runtime/extensions/loader"
)

// Observer is a diagnostics seam: it observes instance materialization events
// performed by the Backend. It is purely observational — it never influences
// the Kernel lifecycle, the Loader registry, or instance ownership. A nil
// Observer disables reporting. Implementations must be safe for concurrent
// use.
type Observer interface {
	// InstanceCreated reports that a fresh WASM instance (instanceID) of the
	// module moduleID was materialized for an activation.
	InstanceCreated(moduleID string, instanceID uint64)
	// InstanceDestroyed reports that the WASM instance instanceID of module
	// moduleID was destroyed when its activation ended.
	InstanceDestroyed(moduleID string, instanceID uint64)
}

// Option configures a Backend.
type Option func(*backendOptions)

type backendOptions struct {
	observer Observer
}

// WithObserver attaches an Observer (diagnostics only).
func WithObserver(o Observer) Option {
	return func(opts *backendOptions) { opts.observer = o }
}

// Backend is the WASM Module Backend v0.1. It implements loader.Backend:
// turning a "wasm" Artifact into a loader.Module whose Factory creates Kernel
// Components that materialize one fresh wazero instance per activation and
// drive the minimal runtime_component_create / runtime_component_destroy ABI.
//
// Backend owns no Module Registry and no Fiber state. It owns one wazero
// runtime (interpreter backend) used to compile modules and instantiate them.
// Close rejects new Load/Instantiate work and releases the wazero runtime; it
// must only be called once no activation still holds a live instance (Loader
// usage is the guard) — live instances belong to their activations and are
// destroyed by the Kernel unwind.
type Backend struct {
	mu       sync.Mutex
	closed   bool
	observer Observer

	rt wazero.Runtime

	// generations tracks, per module ID, the CURRENT resource generation
	// (EXT-05 + NEW-10). Loader.Unload releases the tracked generation through
	// the attempt handle's Release. Concurrent duplicate Loads share one
	// compilation and one generation (the loser's fresh compile is closed).
	generations map[string]*moduleGeneration

	nextInstance atomic.Uint64
}

// maxModuleBytes bounds one compiled module (OOM guard for untrusted
// artifacts). 64 MiB comfortably exceeds any practical guest.
const maxModuleBytes = 64 << 20

var _ loader.Backend = (*Backend)(nil)

// NewBackend creates a running WASM Backend backed by a wazero interpreter
// runtime (pure Go, no cgo, deterministic, offline).
func NewBackend(opts ...Option) *Backend {
	o := backendOptions{}
	for _, opt := range opts {
		if opt != nil {
			opt(&o)
		}
	}
	return &Backend{
		observer:    o.observer,
		generations: make(map[string]*moduleGeneration),
		// CloseOnContextDone: an in-flight guest call is aborted when its
		// context expires, so a misbehaving guest cannot monopolize the
		// activation goroutine indefinitely (paper §6.3: the sandbox must
		// contain untrusted code).
		rt: wazero.NewRuntimeWithConfig(context.Background(),
			wazero.NewRuntimeConfig().WithCloseOnContextDone(true)),
	}
}

// LoadResource implements the resource-owning protocol: the returned handle
// is bound to the exact generation this attempt produced or deduped onto
// (NEW-13 fix — the generation comes from the same critical section as the
// Module build in loadGeneration; a concurrent Unload + fresh load can never
// bind a handle to the wrong generation).
func (b *Backend) LoadResource(ctx context.Context, artifact loader.Artifact) (loader.Module, loader.ResourceHandle, error) {
	mod, err := b.Load(ctx, artifact)
	if err != nil {
		return loader.Module{}, nil, err
	}
	f, ok := mod.Factory.(*wasmFactory)
	if !ok {
		return loader.Module{}, nil, fmt.Errorf("%w: wasm factory type %T", ErrWASMBackendPanic, mod.Factory)
	}
	// The factory's ref IS the generation's ref — the identity check in the
	// handle operations makes any mismatch (Unload + fresh load raced between
	// Load and here) a safe no-op rather than a wrong-generation release.
	b.mu.Lock()
	gen := b.generations[f.ref.id]
	b.mu.Unlock()
	if gen == nil || gen.ref != f.ref {
		// The generation this Module was built from is no longer current —
		// it was superseded. Its resources are already closed; the attempt
		// has nothing left to own. Return a handle with a detached
		// generation: Commit/Abandon/Release are all identity-checked
		// no-ops for it.
		detached := &moduleGeneration{ref: f.ref, committed: true}
		return mod, &wasmResource{b: b, gen: detached}, nil
	}
	return mod, &wasmResource{b: b, gen: gen}, nil
}

// wasmResource is the attempt-scoped ownership token for one generation.
// The state field enforces the single-transition contract (NEW-14): each
// handle performs at most one of Commit/Abandon, and Release is valid only
// from Committed (or directly from Pending when the registry discards).
// Repeat calls are no-ops — a double Abandon can never release another
// attempt's holder slot.
type wasmResource struct {
	b     *Backend
	gen   *moduleGeneration
	mu    sync.Mutex
	state int32 // 0=pending 1=committed 2=abandoned 3=released
}

const (
	wasmResPending   int32 = 0
	wasmResCommitted int32 = 1
	wasmResAbandoned int32 = 2
	wasmResReleased  int32 = 3
)

func (h *wasmResource) Commit() {
	h.mu.Lock()
	defer h.mu.Unlock()
	if h.state != wasmResPending {
		return
	}
	h.b.commitGeneration(h.gen)
	h.state = wasmResCommitted
}

func (h *wasmResource) Abandon() {
	h.mu.Lock()
	defer h.mu.Unlock()
	if h.state != wasmResPending {
		return
	}
	h.state = wasmResAbandoned
	h.b.abandonGeneration(h.gen)
}

func (h *wasmResource) Release() {
	h.mu.Lock()
	defer h.mu.Unlock()
	if h.state != wasmResPending && h.state != wasmResCommitted {
		return
	}
	h.state = wasmResReleased
	h.b.releaseGeneration(h.gen)
}

// Load validates the artifact with the real wazero validator and materializes
// a loader.Module (Type "wasm").
//
// Atomicity contract: Load compiles/validates fully and only then returns a
// Module; the Loader commits it to its Registry. Any failure leaves the Loader
// Registry untouched. Backend.Load never writes the Registry.
func (b *Backend) Load(ctx context.Context, artifact loader.Artifact) (m loader.Module, err error) {
	defer func() {
		if r := recover(); r != nil {
			m = loader.Module{}
			err = fmt.Errorf("%w: %v", ErrWASMBackendPanic, r)
		}
	}()

	if ctx == nil {
		ctx = context.Background()
	}
	if err := b.checkOpen(); err != nil {
		return loader.Module{}, err
	}
	if artifact.BackendType != loader.BackendWASM {
		return loader.Module{}, fmt.Errorf("%w: wasm backend only accepts BackendType == %q, got %q", loader.ErrInvalidArtifact, loader.BackendWASM, artifact.BackendType)
	}
	if err := ctx.Err(); err != nil {
		return loader.Module{}, err
	}

	data, err := readSource(artifact.Source)
	if err != nil {
		return loader.Module{}, err
	}
	if len(data) > maxModuleBytes {
		return loader.Module{}, fmt.Errorf("%w: module %d bytes exceeds limit %d", loader.ErrInvalidArtifact, len(data), maxModuleBytes)
	}
	if err := ctx.Err(); err != nil {
		return loader.Module{}, err
	}

	_, mod, err := b.loadGeneration(ctx, artifact, data)
	if err != nil {
		return loader.Module{}, err
	}
	return mod, nil
}

// loadGeneration compiles (or dedupes onto) the resource generation for the
// artifact and returns the generation ATOMICALLY with the Module built from
// it (NEW-13: Module and generation come from one critical section — a
// concurrent Unload + fresh load between the two would bind a handle to the
// wrong generation). Fresh compiles happen outside the lock; the re-check
// under the lock registers ours or adopts the winner's.
func (b *Backend) loadGeneration(ctx context.Context, artifact loader.Artifact, data []byte) (*moduleGeneration, loader.Module, error) {
	if err := b.checkOpen(); err != nil {
		return nil, loader.Module{}, err
	}
	// EXT-05: dedupe concurrent loads of the same module ID — the loader
	// commits only one registry entry anyway, so two concurrent compiles would
	// retain two CompiledModules with no owner for the loser. Share the
	// tracked generation.
	b.mu.Lock()
	if gen, ok := b.generations[artifact.ID]; ok {
		gen.holders++
		mod := b.moduleFromGeneration(gen)
		b.mu.Unlock()
		return gen, mod, nil
	}
	b.mu.Unlock()

	compiled, err := b.rt.CompileModule(ctx, data)
	if err != nil {
		if ctxErr := ctx.Err(); ctxErr != nil {
			return nil, loader.Module{}, ctxErr
		}
		return nil, loader.Module{}, fmt.Errorf("%w: %v", ErrInvalidWASM, err)
	}
	if err := ctx.Err(); err != nil {
		_ = compiled.Close(context.Background())
		return nil, loader.Module{}, err
	}
	moduleType, err := manifestModuleType(compiled)
	if err != nil {
		_ = compiled.Close(context.Background())
		return nil, loader.Module{}, err
	}
	if err := validateABI(compiled, defaultABI); err != nil {
		_ = compiled.Close(context.Background())
		return nil, loader.Module{}, err
	}
	ref := &moduleRef{id: artifact.ID, version: artifact.Version, moduleType: moduleType, compiled: compiled}
	// Re-check under the lock: another attempt may have raced us to the same
	// ID. The loser closes its fresh compilation and adopts the winner's
	// generation.
	b.mu.Lock()
	if gen, ok := b.generations[artifact.ID]; ok {
		gen.holders++
		mod := b.moduleFromGeneration(gen)
		b.mu.Unlock()
		_ = compiled.Close(context.Background())
		return gen, mod, nil
	}
	gen := &moduleGeneration{ref: ref, holders: 1}
	b.generations[artifact.ID] = gen
	mod := b.moduleFromGeneration(gen)
	b.mu.Unlock()
	return gen, mod, nil
}

// moduleFromGeneration builds the loader.Module for one generation (the
// factory carries the generation's own ref).
func (b *Backend) moduleFromGeneration(gen *moduleGeneration) loader.Module {
	return loader.Module{
		ID:      gen.ref.id,
		Type:    gen.ref.moduleType,
		Version: gen.ref.version,
		Factory: &wasmFactory{backend: b, ref: gen.ref},
	}
}

// releaseGeneration releases gen's compiled resources (handle Release, NEW-12
// fix): identity-checked against the CURRENT generation — a superseded
// generation was already closed when replaced, so this is an idempotent
// no-op for it; a current generation is closed and untracked. The gen
// POINTER (not the id) is the identity, so a late Release racing a fresh
// same-ID load can never close the new generation.
func (b *Backend) releaseGeneration(gen *moduleGeneration) {
	b.mu.Lock()
	cur, ok := b.generations[gen.ref.id]
	if ok && cur == gen {
		delete(b.generations, gen.ref.id)
	}
	b.mu.Unlock()
	if ok && cur == gen {
		_ = gen.ref.compiled.Close(context.Background())
	}
}

// commitGeneration transfers ownership of gen to the registry (handle
// Commit): the attempt releases its holder slot and the generation is marked
// committed. Identity-checked — a superseded generation never mutates the
// current one.
func (b *Backend) commitGeneration(gen *moduleGeneration) {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.generations[gen.ref.id] != gen {
		return
	}
	gen.committed = true
	if gen.holders > 0 {
		gen.holders--
	}
}

// abandonGeneration releases one holder's claim on gen (handle Abandon):
// the ref is closed and untracked only when the LAST holder abandons without
// a commit — a failed attempt sharing a gen with a committed (or still
// in-flight) attempt never closes it out from under them. A SUPERSEDED
// generation (Unload + fresh load raced ahead) is untouched: its resources
// were already released, and the current generation is never affected
// (NEW-10 identity isolation). Idempotent.
func (b *Backend) abandonGeneration(gen *moduleGeneration) {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.generations[gen.ref.id] != gen {
		return // superseded: nothing to release
	}
	if gen.holders > 0 {
		gen.holders--
	}
	// Close only when the LAST holder abandons AND the registry has not
	// committed the generation (a committed generation is owned by the
	// registry — its own Release closes it). A shared generation with
	// remaining holders stays alive.
	if gen.holders == 0 && !gen.committed {
		delete(b.generations, gen.ref.id)
		_ = gen.ref.compiled.Close(context.Background())
	}
}

// compileForTest compiles and tracks a generation without holder accounting
// (test seam: V-series tests inspect raw generation state, never going
// through the loader's Commit/Abandon protocol).
func (b *Backend) compileForTest(ctx context.Context, artifact loader.Artifact, data []byte) (*moduleRef, error) {
	if err := b.checkOpen(); err != nil {
		return nil, err
	}
	compiled, err := b.rt.CompileModule(ctx, data)
	if err != nil {
		if ctxErr := ctx.Err(); ctxErr != nil {
			return nil, ctxErr
		}
		return nil, fmt.Errorf("%w: %v", ErrInvalidWASM, err)
	}
	if err := ctx.Err(); err != nil {
		_ = compiled.Close(context.Background())
		return nil, err
	}
	moduleType, err := manifestModuleType(compiled)
	if err != nil {
		_ = compiled.Close(context.Background())
		return nil, err
	}
	if err := validateABI(compiled, defaultABI); err != nil {
		_ = compiled.Close(context.Background())
		return nil, err
	}
	ref := &moduleRef{id: artifact.ID, version: artifact.Version, moduleType: moduleType, compiled: compiled}
	b.mu.Lock()
	if gen, ok := b.generations[artifact.ID]; ok {
		b.mu.Unlock()
		_ = compiled.Close(context.Background())
		return gen.ref, nil
	}
	b.generations[artifact.ID] = &moduleGeneration{ref: ref, holders: 0}
	b.mu.Unlock()
	return ref, nil
}

// CompiledModules reports how many compiled modules the backend currently
// retains (observability for lifecycle tests and diagnostics).
func (b *Backend) CompiledModules() int {
	b.mu.Lock()
	defer b.mu.Unlock()
	return len(b.generations)
}

// manifestModuleType extracts the Logical Module Type from the wasm module's
// own metadata: a custom section named "module_type" whose data is the type
// string (e.g. "industrial.camera"). Missing or empty metadata is an error —
// there is NO fallback to the BackendType, so the two namespaces can never be
// conflated.
func manifestModuleType(compiled wazero.CompiledModule) (string, error) {
	for _, cs := range compiled.CustomSections() {
		if cs.Name() != manifestTypeSection {
			continue
		}
		v := strings.TrimSpace(string(cs.Data()))
		if v == "" {
			return "", fmt.Errorf("%w: %q metadata is empty", loader.ErrInvalidModuleType, manifestTypeSection)
		}
		return v, nil
	}
	return "", fmt.Errorf("%w: missing %q metadata in wasm module", loader.ErrInvalidModuleType, manifestTypeSection)
}

// Close stops the Backend: subsequent Load and Instantiate calls fail with
// ErrWASMBackendClosed, and the wazero runtime (including every compiled
// module it created) is released. Close is idempotent.
//
// Contract: Close must not be called while an activation still holds a live
// instance. In the normal flow the Kernel unwinds all activations (Runtime
// Close) and Loader usage is released before the Backend is closed.
func (b *Backend) Close() error {
	b.mu.Lock()
	if b.closed {
		b.mu.Unlock()
		return nil
	}
	b.closed = true
	b.mu.Unlock()
	return b.rt.Close(context.Background())
}

// Closed reports whether Close has been called.
func (b *Backend) Closed() bool {
	b.mu.Lock()
	defer b.mu.Unlock()
	return b.closed
}

func (b *Backend) checkOpen() error {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.closed {
		return ErrWASMBackendClosed
	}
	return nil
}

// instantiate materializes one fresh wazero instance of ref for the current
// activation. Every call yields a distinct instance identity and a distinct
// module name; instances are never reused across activations. Modules that
// import host functions fail here (v0.1 defines no host API).
func (b *Backend) instantiate(ctx context.Context, ref *moduleRef) (*Instance, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if err := b.checkOpen(); err != nil {
		return nil, err
	}

	id := b.nextInstance.Add(1)
	name := ref.id + "#" + strconv.FormatUint(id, 10)
	// v0.1 drives execution only through the ABI exports: clear the default
	// "_start" auto-invocation so nothing runs except create/destroy.
	cfg := wazero.NewModuleConfig().WithName(name).WithStartFunctions()
	mod, err := b.rt.InstantiateModule(ctx, ref.compiled, cfg)
	if err != nil {
		if ctxErr := ctx.Err(); ctxErr != nil {
			return nil, ctxErr
		}
		return nil, fmt.Errorf("%w: %v", ErrWASMInstantiate, err)
	}

	inst := &Instance{backend: b, ref: ref, id: id, module: mod}
	if obs := b.observer; obs != nil {
		obs.InstanceCreated(ref.id, id)
	}
	return inst, nil
}

// Instance is one materialized WASM instance owned by exactly one Activation.
//
// The Kernel is the lifecycle authority: the owning activation's
// runtime.Context.Effect installs the instance (materialize + run
// runtime_component_create) and its inverse (Destroy: run
// runtime_component_destroy + close the wazero module) is executed by the
// Kernel exactly once when that activation unwinds. The Backend never decides
// when an instance lives or dies.
type Instance struct {
	backend *Backend
	ref     *moduleRef
	id      uint64

	module api.Module

	once      sync.Once
	destroyed atomic.Bool
}

// ID returns the Backend-unique instance identity.
func (i *Instance) ID() uint64 { return i.id }

// Destroyed reports whether Destroy has completed.
func (i *Instance) Destroyed() bool { return i.destroyed.Load() }

// begin runs the ABI create export for this activation. On failure the caller
// must Destroy the instance so no orphan remains.
func (i *Instance) begin(ctx context.Context) error {
	if err := i.callExport(ctx, defaultABI.create); err != nil {
		return fmt.Errorf("%w: %s: %v", ErrWASMInstantiate, defaultABI.create, err)
	}
	return nil
}

// callExport invokes one exported function on this instance.
func (i *Instance) callExport(ctx context.Context, name string) error {
	if i.module == nil || i.module.IsClosed() {
		return errors.New("wasm instance is closed")
	}
	fn := i.module.ExportedFunction(name)
	if fn == nil {
		return fmt.Errorf("export %q not found", name)
	}
	_, err := fn.Call(ctx)
	return err
}

// Destroy ends the instance exactly once: it runs the ABI destroy export,
// closes the wazero module instance, and reports to the Observer. It is
// idempotent — later calls are no-ops. A destroy-export or close error is
// returned but never prevents teardown (no orphan instances).
func (i *Instance) Destroy() error {
	var err error
	i.once.Do(func() {
		ctx := context.Background()
		if cerr := i.callExport(ctx, defaultABI.destroy); cerr != nil {
			err = errors.Join(err, cerr)
		}
		if i.module != nil && !i.module.IsClosed() {
			if cerr := i.module.Close(ctx); cerr != nil {
				err = errors.Join(err, cerr)
			}
		}
		i.destroyed.Store(true)
		if obs := i.backend.observer; obs != nil {
			obs.InstanceDestroyed(i.ref.id, i.id)
		}
	})
	return err
}

// readSource resolves a WASM artifact Source to bytes.
//
// v0.1 supports "file:///path/to/module.wasm"; a plain local path is accepted
// as an internal convenience for offline tests and keeps the mapping inside
// the Backend (never exposed to Config/Runtime).
func readSource(source string) ([]byte, error) {
	path, err := sourcePath(source)
	if err != nil {
		return nil, err
	}
	data, err := os.ReadFile(path)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			return nil, fmt.Errorf("%w: %s", ErrWASMSourceNotFound, source)
		}
		return nil, fmt.Errorf("read wasm source %q: %w", source, err)
	}
	return data, nil
}

func sourcePath(source string) (string, error) {
	if strings.HasPrefix(source, "file://") {
		u, err := url.Parse(source)
		if err != nil || u.Scheme != "file" {
			return "", fmt.Errorf("%w: invalid file uri %q", ErrWASMSourceNotFound, source)
		}
		if u.Host != "" && u.Host != "localhost" {
			return "", fmt.Errorf("%w: remote file host %q not supported", ErrWASMSourceNotFound, u.Host)
		}
		if u.Path == "" {
			return "", fmt.Errorf("%w: empty file uri %q", ErrWASMSourceNotFound, source)
		}
		return u.Path, nil
	}
	if strings.Contains(source, "://") {
		return "", fmt.Errorf("%w: unsupported source scheme in %q (v0.1 supports file:// and plain local paths)", ErrWASMSourceNotFound, source)
	}
	if source == "" {
		return "", fmt.Errorf("%w: empty source", ErrWASMSourceNotFound)
	}
	return source, nil
}
