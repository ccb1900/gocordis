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

	nextInstance atomic.Uint64
}

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
		observer: o.observer,
		rt:       wazero.NewRuntime(context.Background()),
	}
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
	if err := ctx.Err(); err != nil {
		return loader.Module{}, err
	}

	ref, err := b.loadModule(ctx, artifact, data)
	if err != nil {
		return loader.Module{}, err
	}
	return loader.Module{
		ID:      artifact.ID,
		Type:    ref.moduleType,
		Version: artifact.Version,
		Factory: &wasmFactory{backend: b, ref: ref},
	}, nil
}

// loadModule compiles (full wazero validation) the wasm bytes and checks the
// minimal ABI surface. Compilation failures map to ErrInvalidWASM; ABI
// violations to ErrWASMABI. Context cancellation is honored before and after
// the (non-preemptible) compile step.
func (b *Backend) loadModule(ctx context.Context, artifact loader.Artifact, data []byte) (*moduleRef, error) {
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
	return &moduleRef{id: artifact.ID, version: artifact.Version, moduleType: moduleType, compiled: compiled}, nil
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
