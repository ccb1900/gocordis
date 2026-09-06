package integration

import (
	"context"
	"errors"
	"path/filepath"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// WASM E2E environment: real Runtime + Loader (Builtin + WASM Backend) + Config
// Controller over one Factory Registry.
// ---------------------------------------------------------------------------

// wasmObs records instance materialization events (thread-safe).
type wasmObs struct {
	mu        sync.Mutex
	created   []wasmEvt
	destroyed []wasmEvt
}

type wasmEvt struct {
	module   string
	instance uint64
}

func (o *wasmObs) InstanceCreated(moduleID string, instanceID uint64) {
	o.mu.Lock()
	defer o.mu.Unlock()
	o.created = append(o.created, wasmEvt{module: moduleID, instance: instanceID})
}

func (o *wasmObs) InstanceDestroyed(moduleID string, instanceID uint64) {
	o.mu.Lock()
	defer o.mu.Unlock()
	o.destroyed = append(o.destroyed, wasmEvt{module: moduleID, instance: instanceID})
}

func (o *wasmObs) createdN(module string) int {
	o.mu.Lock()
	defer o.mu.Unlock()
	n := 0
	for _, e := range o.created {
		if e.module == module {
			n++
		}
	}
	return n
}

func (o *wasmObs) destroyedN(module string) int {
	o.mu.Lock()
	defer o.mu.Unlock()
	n := 0
	for _, e := range o.destroyed {
		if e.module == module {
			n++
		}
	}
	return n
}

type wasmEnv struct {
	rt   *runtime.Runtime
	ld   *loader.BuiltinLoader
	reg  config.FactoryRegistry
	ctrl *config.Controller
	wb   *wasm.Backend
	obs  *wasmObs
}

func newWASMEnv(t *testing.T) *wasmEnv {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	ld := loader.NewBuiltinLoader()
	obs := &wasmObs{}
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatalf("RegisterBackend: %v", err)
	}
	e := &wasmEnv{rt: rt, ld: ld, reg: reg, ctrl: ctrl, wb: wb, obs: obs}
	t.Cleanup(func() {
		_ = ctrl.CloseContext(shortCtx())
		_ = rt.Close(context.Background())
		clctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ld.CloseContext(clctx)
		_ = wb.Close()
	})
	return e
}

// wasmSrc builds a file:// URI into the wasm testdata directory.
func wasmSrc(t *testing.T, name string) string {
	t.Helper()
	abs, err := filepath.Abs(filepath.Join("..", "extensions", "loader", "wasm", "testdata", name))
	if err != nil {
		t.Fatalf("abs: %v", err)
	}
	return "file://" + abs
}

func (e *wasmEnv) loadWASM(t *testing.T, id, file string) *loader.Module {
	t.Helper()
	m, err := e.ld.Load(ctxT(t), loader.Artifact{ID: id, BackendType: loader.BackendWASM, Source: wasmSrc(t, file), Version: "1"})
	if err != nil {
		t.Fatalf("Load(%s): %v", id, err)
	}
	return m
}

func (e *wasmEnv) deployViaConfig(t *testing.T, m *loader.Module, componentID string) *runtime.Fiber {
	t.Helper()
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatalf("RegisterFactories: %v", err)
	}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: componentID, Type: m.Type}}}); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	for _, o := range e.ctrl.Owned() {
		if o.ID == componentID {
			return o.Fiber
		}
	}
	t.Fatalf("component %q not owned", componentID)
	return nil
}

// E2E-WASM-01 — WASM Artifact -> Module.
func TestE2EWASM01ArtifactToModule(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-01", "valid.wasm")
	if m.Type != "industrial.camera" || m.ID != "e2e-01" {
		t.Fatalf("module = %+v", m)
	}
	if !e.ld.Has("e2e-01") {
		t.Fatal("module not registered")
	}
	if e.obs.createdN("e2e-01") != 0 {
		t.Fatal("loading a module must not create a WASM instance")
	}
}

// E2E-WASM-02 — WASM Module -> Factory.
func TestE2EWASM02ModuleToFactory(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-02", "valid.wasm")
	if m.Factory == nil {
		t.Fatal("module factory is nil")
	}
	comp, err := m.Factory.Create(config.ComponentConfig{ID: "c", Type: m.Type})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if comp == nil {
		t.Fatal("nil component")
	}
}

// E2E-WASM-03 — WASM Factory -> Component.
func TestE2EWASM03FactoryToComponent(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-03", "valid.wasm")
	comp, err := m.Factory.Create(config.ComponentConfig{ID: "c3", Type: m.Type})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	var _ runtime.Component = comp
	if comp.Name() != "wasm:c3" {
		t.Fatalf("name = %q", comp.Name())
	}
	if len(comp.Inject()) != 0 || len(comp.Provide()) != 0 {
		t.Fatalf("wasm v0.1 component must be independent; inject=%v provide=%v", comp.Inject(), comp.Provide())
	}
}

// E2E-WASM-04 — Component -> Runtime Fiber.
func TestE2EWASM04ComponentToFiber(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-04", "valid.wasm")
	f := e.deployViaConfig(t, m, "c4")
	if f.ID() == 0 {
		t.Fatal("fiber has no id")
	}
	if f.Component() == nil {
		t.Fatal("fiber has no component")
	}
}

// E2E-WASM-05 — Fiber Active -> WASM Instance Active: mounting the fiber
// materializes exactly one fresh instance for its activation.
func TestE2EWASM05FiberActiveToInstanceActive(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-05", "valid.wasm")
	f := e.deployViaConfig(t, m, "c5")
	waitActive(t, f)
	if f.State() != runtime.StateActive {
		t.Fatalf("state = %v, want Active", f.State())
	}
	eventually(t, 5*time.Second, "instance created", func() bool { return e.obs.createdN("e2e-05") == 1 })
	if e.obs.destroyedN("e2e-05") != 0 {
		t.Fatal("instance destroyed while fiber Active")
	}
}

// E2E-WASM-06 — Fiber Dispose -> WASM Instance Destroy.
func TestE2EWASM06FiberDisposeToInstanceDestroy(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-06", "valid.wasm")
	f := e.deployViaConfig(t, m, "c6")
	waitActive(t, f)
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	eventually(t, 5*time.Second, "instance destroyed", func() bool { return e.obs.destroyedN("e2e-06") == 1 })
	if e.obs.createdN("e2e-06") != 1 {
		t.Fatalf("created = %d, want 1", e.obs.createdN("e2e-06"))
	}
}

// E2E-WASM-07 — Module Unload is blocked by Usage.
func TestE2EWASM07ModuleUnloadBlockedByUsage(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-07", "valid.wasm")
	f := e.deployViaConfig(t, m, "c7")
	waitActive(t, f)

	if err := e.ld.Usage().Acquire(m.ID, "config-owner"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload = %v, want ErrModuleInUse", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("fiber disturbed: %v", f.State())
	}
	if err := e.ld.Usage().Release(m.ID, "config-owner"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); err != nil {
		t.Fatalf("Unload after release: %v", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("module unload disposed the fiber: %v", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// E2E-WASM-08 — WASM Failure Isolation: an invalid WASM artifact never disturbs
// other modules or fibers.
func TestE2EWASM08WASMFailureIsolation(t *testing.T) {
	e := newWASMEnv(t)
	ok := e.loadWASM(t, "e2e-08-ok", "valid.wasm")
	f := e.deployViaConfig(t, ok, "c8")
	waitActive(t, f)

	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "e2e-08-bad", BackendType: loader.BackendWASM, Source: wasmSrc(t, "invalid.wasm"), Version: "1"}); !errors.Is(err, wasm.ErrInvalidWASM) {
		t.Fatalf("Load(invalid) = %v, want ErrInvalidWASM", err)
	}
	if e.ld.Has("e2e-08-bad") {
		t.Fatal("invalid module entered registry")
	}
	// The healthy WASM module and its fiber are untouched.
	if f.State() != runtime.StateActive {
		t.Fatalf("healthy fiber disturbed: %v", f.State())
	}
	if got, ok := e.ld.Get("e2e-08-ok"); !ok || got.Factory == nil {
		t.Fatal("healthy module lost")
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// E2E-WASM-09 — Activation Identity Isolation: every activation owns a fresh,
// distinct instance; ending one activation never affects another.
func TestE2EWASM09ActivationIdentityIsolation(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "e2e-09", "valid.wasm")

	compA, err := m.Factory.Create(config.ComponentConfig{ID: "a", Type: m.Type})
	if err != nil {
		t.Fatal(err)
	}
	compB, err := m.Factory.Create(config.ComponentConfig{ID: "b", Type: m.Type})
	if err != nil {
		t.Fatal(err)
	}
	fa, err := e.rt.Load(compA)
	if err != nil {
		t.Fatal(err)
	}
	fb, err := e.rt.Load(compB)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, fa, fb)

	// Two independent active fibers: two distinct live instances.
	eventually(t, 5*time.Second, "two instances created", func() bool { return e.obs.createdN("e2e-09") == 2 })

	// Ending A must not touch B's instance: B stays Active and B's instance is
	// never destroyed while its activation is live.
	if err := fa.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := fa.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if e.obs.destroyedN("e2e-09") != 1 {
		t.Fatalf("destroyed after A ends = %d, want 1 (B unaffected)", e.obs.destroyedN("e2e-09"))
	}
	if fb.State() != runtime.StateActive {
		t.Fatalf("B fiber disturbed by A ending: %v", fb.State())
	}
	_ = fb.Dispose()
	_ = fb.Gone(ctxT(t))
	eventually(t, 5*time.Second, "both destroyed", func() bool { return e.obs.destroyedN("e2e-09") == 2 })
}

// E2E-WASM-10 — Builtin/WASM Backend Isolation: one Loader hosts both backends;
// failures on either side never pollute the other.
func TestE2EWASM10BuiltinWASMBackendIsolation(t *testing.T) {
	e := newWASMEnv(t)
	// Builtin module.
	if err := e.ld.RegisterBuiltin("builtin://cam", func() config.Factory {
		return &builtinWASMFactory{tag: "cam"}
	}); err != nil {
		t.Fatal(err)
	}
	bm, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "builtin-cam", Type: "cam", Source: "builtin://cam", Version: "1"})
	if err != nil {
		t.Fatalf("builtin load: %v", err)
	}
	// WASM module.
	wm := e.loadWASM(t, "e2e-10-wasm", "valid.wasm")

	// WASM-side failure leaves builtin usable.
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "x", BackendType: loader.BackendWASM, Source: wasmSrc(t, "missing-export.wasm"), Version: "1"}); !errors.Is(err, wasm.ErrWASMABI) {
		t.Fatalf("wasm ABI failure = %v, want ErrWASMABI", err)
	}
	if _, ok := e.ld.Get("builtin-cam"); !ok {
		t.Fatal("builtin module lost after wasm failure")
	}
	// Builtin-side failure leaves wasm usable.
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "y", Type: "cam", Source: "builtin://missing", Version: "1"}); !errors.Is(err, loader.ErrLoadFailed) {
		t.Fatalf("builtin failure = %v, want ErrLoadFailed", err)
	}
	if _, ok := e.ld.Get("e2e-10-wasm"); !ok {
		t.Fatal("wasm module lost after builtin failure")
	}
	// Both factories still create components.
	if _, err := bm.Factory.Create(config.ComponentConfig{ID: "b", Type: bm.Type}); err != nil {
		t.Fatalf("builtin factory: %v", err)
	}
	if _, err := wm.Factory.Create(config.ComponentConfig{ID: "w", Type: wm.Type}); err != nil {
		t.Fatalf("wasm factory: %v", err)
	}
}

// P-WASM-01 — Module/Fiber Orthogonality: Module lifecycle is independent of
// Fiber lifecycle in both directions.
func TestPWASM01ModuleFiberOrthogonality(t *testing.T) {
	e := newWASMEnv(t)
	// Load the module with no fiber at all: module is Loaded, fiber count 0.
	m := e.loadWASM(t, "p01", "valid.wasm")
	if e.obs.createdN("p01") != 0 {
		t.Fatal("module load created an instance (module lifecycle leaked into activation)")
	}
	// Fiber active does not pin the module registry state beyond usage: usage is
	// explicit (never inferred by the Loader scanning Fibers). While a holder
	// exists, Unload is rejected; after release, Unload succeeds yet the Fiber
	// stays Active — Module lifecycle and Fiber lifecycle are orthogonal.
	f := e.deployViaConfig(t, m, "p01")
	waitActive(t, f)
	if err := e.ld.Usage().Acquire(m.ID, "owner"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload(in use) = %v, want ErrModuleInUse", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("fiber disturbed while module in use: %v", f.State())
	}
	if err := e.ld.Usage().Release(m.ID, "owner"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); err != nil {
		t.Fatalf("Unload after release: %v", err)
	}
	if e.ld.Has(m.ID) {
		t.Fatal("module still present after Unload")
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("module unload disposed the fiber: %v", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// P-WASM-02 — Runtime Authority: the WASM Backend never changes Fiber state;
// instances exist only through Kernel-controlled activations.
func TestPWASM02RuntimeAuthority(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "p02", "valid.wasm")
	comp, err := m.Factory.Create(config.ComponentConfig{ID: "p02", Type: m.Type})
	if err != nil {
		t.Fatal(err)
	}
	f, err := e.rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if f.State() != runtime.StatePending && f.State() != runtime.StateLoading && f.State() != runtime.StateActive {
		t.Fatalf("unexpected initial state %v", f.State())
	}
	waitActive(t, f)
	// The Backend only observed an activation; it never set the state itself.
	created := e.obs.createdN("p02")
	if created != 1 {
		t.Fatalf("created = %d, want 1 (activation-owned instance)", created)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("state = %v", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// P-WASM-03 — Activation Ownership: each instance belongs to exactly one
// activation and dies with it.
func TestPWASM03ActivationOwnership(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "p03", "valid.wasm")
	f := e.deployViaConfig(t, m, "p03")
	waitActive(t, f)
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if err := f.Load(); err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if e.obs.createdN("p03") != 2 || e.obs.destroyedN("p03") != 1 {
		t.Fatalf("created/destroyed = %d/%d, want 2/1 across two activations", e.obs.createdN("p03"), e.obs.destroyedN("p03"))
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
	if e.obs.destroyedN("p03") != 2 {
		t.Fatalf("destroyed = %d, want 2 after second activation ends", e.obs.destroyedN("p03"))
	}
}

// P-WASM-04 — Recovery: after Unloading, the activation's instance is
// destroyed (no orphan) and the Fiber is reusable.
func TestPWASM04Recovery(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "p04", "valid.wasm")
	f := e.deployViaConfig(t, m, "p04")
	waitActive(t, f)
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.WaitInactive(ctxT(t)); err != nil {
		t.Fatalf("WaitInactive: %v", err)
	}
	eventually(t, 5*time.Second, "instance destroyed on recovery", func() bool { return e.obs.destroyedN("p04") == 1 })
	// Fiber reaches its terminal Gone (intent unmounted).
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
}

// P-WASM-05 — Failure Conservation: every WASM load failure leaves the Module
// Registry unchanged.
func TestPWASM05FailureConservation(t *testing.T) {
	e := newWASMEnv(t)
	e.loadWASM(t, "p05-ok", "valid.wasm")
	before := len(e.ld.Snapshot())

	failures := map[string]error{
		"invalid":        wasm.ErrInvalidWASM,
		"missing-export": wasm.ErrWASMABI,
		"not-there":      wasm.ErrWASMSourceNotFound,
		"wrong-type":     loader.ErrInvalidArtifact,
	}
	for name, want := range failures {
		var a loader.Artifact
		switch name {
		case "invalid":
			a = loader.Artifact{ID: "p05-" + name, BackendType: loader.BackendWASM, Source: wasmSrc(t, "invalid.wasm"), Version: "1"}
		case "missing-export":
			a = loader.Artifact{ID: "p05-" + name, BackendType: loader.BackendWASM, Source: wasmSrc(t, "missing-export.wasm"), Version: "1"}
		case "not-there":
			a = loader.Artifact{ID: "p05-" + name, BackendType: loader.BackendWASM, Source: "file:///nonexistent/module.wasm", Version: "1"}
		case "wrong-type":
			// The Backend's own guard: only BackendType "wasm" is accepted.
			if _, err := e.wb.Load(ctxT(t), loader.Artifact{ID: "p05-wrong", BackendType: "cam", Source: wasmSrc(t, "valid.wasm"), Version: "1"}); !errors.Is(err, loader.ErrInvalidArtifact) {
				t.Fatalf("backend type guard = %v, want ErrInvalidArtifact", err)
			}
			continue
		}
		if _, err := e.ld.Load(ctxT(t), a); !errors.Is(err, want) {
			t.Fatalf("%s: Load = %v, want %v", name, err, want)
		}
		if e.ld.Has("p05-" + name) {
			t.Fatalf("%s: module entered registry", name)
		}
	}
	if got := len(e.ld.Snapshot()); got != before {
		t.Fatalf("registry changed after failures: %d -> %d", before, got)
	}
	if _, ok := e.ld.Get("p05-ok"); !ok {
		t.Fatal("healthy module lost")
	}
}

// P-WASM-06 — Backend Isolation: a Backend failure never pollutes Loader state
// or other Backends (Builtin and WASM coexist after failures).
func TestPWASM06BackendIsolation(t *testing.T) {
	e := newWASMEnv(t)
	if err := e.ld.RegisterBuiltin("builtin://ok", func() config.Factory {
		return &builtinWASMFactory{tag: "ok"}
	}); err != nil {
		t.Fatal(err)
	}
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "b", Type: "cam", Source: "builtin://ok", Version: "1"}); err != nil {
		t.Fatalf("builtin load: %v", err)
	}
	wm := e.loadWASM(t, "p06", "valid.wasm")

	// Close the WASM Backend: only WASM loads fail afterwards; the Builtin
	// Backend and previously loaded modules keep working.
	if err := e.wb.Close(); err != nil {
		t.Fatal(err)
	}
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "late", BackendType: loader.BackendWASM, Source: wasmSrc(t, "valid.wasm"), Version: "1"}); !errors.Is(err, wasm.ErrWASMBackendClosed) {
		t.Fatalf("wasm load after backend close = %v, want ErrWASMBackendClosed", err)
	}
	if _, ok := e.ld.Get("b"); !ok {
		t.Fatal("builtin module lost after wasm backend close")
	}
	if _, ok := e.ld.Get("p06"); !ok {
		t.Fatal("loaded wasm module lost after backend close")
	}
	if _, err := wm.Factory.Create(config.ComponentConfig{ID: "w", Type: wm.Type}); err != nil {
		t.Fatalf("wasm factory unusable after backend close: %v", err)
	}
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "b2", Type: "cam", Source: "builtin://ok", Version: "2"}); err != nil {
		t.Fatalf("builtin load after wasm backend close: %v", err)
	}
}

// builtinWASMFactory is a minimal Config factory for the Builtin side of the
// WASM E2E isolation tests.
type builtinWASMFactory struct{ tag string }

func (f *builtinWASMFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &builtinWASMComp{name: "builtin:" + f.tag + ":" + cc.ID}, nil
}

type builtinWASMComp struct{ name string }

func (c *builtinWASMComp) Name() string                  { return c.name }
func (c *builtinWASMComp) Inject() []runtime.Dependency  { return nil }
func (c *builtinWASMComp) Provide() []runtime.Capability { return nil }
func (c *builtinWASMComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}
