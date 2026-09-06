package loader_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

func rtCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type chainEnv struct {
	rt   *runtime.Runtime
	reg  config.FactoryRegistry
	ctrl *config.Controller
}

func newChainEnv(t *testing.T) *chainEnv {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	e := &chainEnv{rt: rt, reg: reg, ctrl: ctrl}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(ctx)
	})
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return e
}

// markerFactory returns components whose Name identifies the factory.
type markerFactory struct{ tag string }

func (f *markerFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &testComponent{name: f.tag + ":" + cc.ID}, nil
}

func ownedFiber(t *testing.T, e *chainEnv, id string) *runtime.Fiber {
	t.Helper()
	for _, o := range e.ctrl.Owned() {
		if o.ID == id {
			return o.Fiber
		}
	}
	t.Fatalf("component %q not owned", id)
	return nil
}

// L15 — Factory can be explicitly registered into a Config FactoryRegistry.
func TestL15RegisterFactories(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://camera", func() config.Factory { return &markerFactory{tag: "cam"} })
	if _, err := l.Load(ctxT(t), art("camera-driver", "camera", "builtin://camera", "1.0")); err != nil {
		t.Fatalf("Load: %v", err)
	}

	cfgReg := config.NewFactoryRegistry()
	if err := l.RegisterFactories(cfgReg); err != nil {
		t.Fatalf("RegisterFactories: %v", err)
	}
	f, ok := cfgReg.Lookup("camera")
	if !ok {
		t.Fatal("factory type camera not registered")
	}
	comp, err := f.Create(config.ComponentConfig{ID: "cam-front", Type: "camera"})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if comp.Name() != "cam:cam-front" {
		t.Fatalf("created component name = %q", comp.Name())
	}
}

// L16 — Two modules sharing a Type cannot both register; the original stays.
// Module A is registered first; loading Module B (same Type) and re-registering
// fails with config.ErrFactoryExists while A's factory remains valid.
func TestL16FactoryDuplicate(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", func() config.Factory { return &markerFactory{tag: "A"} })
	regBuiltin(t, l, "builtin://b", func() config.Factory { return &markerFactory{tag: "B"} })
	if _, err := l.Load(ctxT(t), art("m-a", "camera", "builtin://a", "")); err != nil {
		t.Fatalf("load m-a: %v", err)
	}

	cfgReg := config.NewFactoryRegistry()
	if err := l.RegisterFactories(cfgReg); err != nil {
		t.Fatalf("first RegisterFactories: %v", err)
	}

	// Now a second module of the same Type is loaded; re-registering must fail
	// on the collision and keep the original factory.
	if _, err := l.Load(ctxT(t), art("m-b", "camera", "builtin://b", "")); err != nil {
		t.Fatalf("load m-b: %v", err)
	}
	err := l.RegisterFactories(cfgReg)
	if !errors.Is(err, config.ErrFactoryExists) {
		t.Fatalf("RegisterFactories = %v, want config.ErrFactoryExists", err)
	}
	f, ok := cfgReg.Lookup("camera")
	if !ok {
		t.Fatal("factory not registered")
	}
	comp, err := f.Create(config.ComponentConfig{ID: "x", Type: "camera"})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if comp.Name() != "A:x" {
		t.Fatalf("original factory was replaced: created %q, want A:x", comp.Name())
	}
}

// L18 + architecture acceptance — full chain:
// Artifact -> Loader -> Module -> Factory -> Config FactoryRegistry -> Config
// Reconcile -> Runtime Fiber. Module Load/Unload never changes Fiber lifecycle.
func TestL18ModuleFiberIndependenceChain(t *testing.T) {
	l := newLoader(t)
	e := newChainEnv(t)

	regBuiltin(t, l, "builtin://camera", func() config.Factory { return &markerFactory{tag: "cam"} })
	m, err := l.Load(ctxT(t), art("camera-driver", "camera", "builtin://camera", "1.0"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := l.RegisterFactories(e.reg); err != nil {
		t.Fatalf("RegisterFactories: %v", err)
	}

	// Acquire usage (upper-layer adapter responsibility).
	if err := l.Usage().Acquire(m.ID, "config-adapter"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}

	// Loading a module must NOT have created any fiber by itself.
	if len(e.ctrl.Owned()) != 0 {
		t.Fatal("module load created controller-owned components")
	}

	// Reconcile a desired component of Type camera (factory from the module).
	if err := e.ctrl.Reconcile(rtCtx(t), config.Config{
		Components: []config.ComponentConfig{{ID: "cam-front", Type: "camera"}},
	}); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	fiber := ownedFiber(t, e, "cam-front")
	if err := fiber.Ready(rtCtx(t)); err != nil {
		t.Fatalf("component fiber not Active: %v", err)
	}

	// The module is in use, so it cannot be unloaded.
	if err := l.Unload(ctxT(t), m.ID); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload while in use = %v, want ErrModuleInUse", err)
	}

	// Release usage and unload the module. The Fiber must remain Active:
	// Module lifecycle is independent of Fiber lifecycle.
	if err := l.Usage().Release(m.ID, "config-adapter"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if err := l.Unload(ctxT(t), m.ID); err != nil {
		t.Fatalf("Unload: %v", err)
	}
	if l.Has(m.ID) {
		t.Fatal("module still loaded")
	}
	if got := fiber.State(); got != runtime.StateActive {
		t.Fatalf("fiber state after module unload = %v, want Active (independent)", got)
	}
}

// L20 — Close cleans all unloadable modules and reports in-use ones truthfully.
func TestL20CloseCleanup(t *testing.T) {
	l := loader.NewBuiltinLoader() // manual: we assert Close semantics directly
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	if _, err := l.Load(ctxT(t), art("A", "a", "builtin://a", "")); err != nil {
		t.Fatalf("Load A: %v", err)
	}
	if _, err := l.Load(ctxT(t), art("B", "a", "builtin://a", "")); err != nil {
		t.Fatalf("Load B: %v", err)
	}
	if err := l.Usage().Acquire("B", "owner"); err != nil {
		t.Fatalf("Acquire B: %v", err)
	}

	// Close: A (unused) is unloaded; B (in use) remains and is reported.
	err := l.Close()
	if !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Close = %v, want ErrModuleInUse (B in use)", err)
	}
	if l.Has("A") {
		t.Fatal("unused module A not cleaned by Close")
	}
	if !l.Has("B") {
		t.Fatal("in-use module B must remain loaded after Close error")
	}

	// Release B; a later Close cleans it and returns nil.
	if err := l.Usage().Release("B", "owner"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if err := l.Close(); err != nil {
		t.Fatalf("second Close = %v, want nil", err)
	}
	if l.Has("B") {
		t.Fatal("module B not cleaned after becoming unused")
	}
	// Idempotent.
	if err := l.Close(); err != nil {
		t.Fatalf("third Close = %v, want nil", err)
	}
}
