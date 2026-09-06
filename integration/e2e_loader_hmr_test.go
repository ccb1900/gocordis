package integration

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

type hmrEnv struct {
	rt    *runtime.Runtime
	ld    *loader.BuiltinLoader
	usage loader.ModuleUsage
	h     *hmr.Controller
}

func newHMR(t *testing.T) *hmrEnv {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	e := &hmrEnv{rt: rt, ld: ld, usage: ld.Usage(), h: h}
	t.Cleanup(func() {
		_ = h.CloseContext(ctxT(t))
		_ = ld.CloseContext(ctxT(t))
		_ = rt.Close(context.Background())
	})
	return e
}

func (e *hmrEnv) register(t *testing.T, src, tag string, kind string, fail bool) {
	t.Helper()
	f := &hmrFactory{tag: tag, kind: kind, fail: fail}
	if err := e.ld.RegisterBuiltin(src, func() config.Factory { return f }); err != nil {
		t.Fatal(err)
	}
}

func artifact(id, typ, src, ver string) loader.Artifact {
	return loader.Artifact{ID: id, Type: typ, Source: src, Version: ver}
}

func (e *hmrEnv) load(t *testing.T, a loader.Artifact) *loader.Module {
	t.Helper()
	m, err := e.ld.Load(ctxT(t), a)
	if err != nil {
		t.Fatal(err)
	}
	return m
}

func (e *hmrEnv) install(t *testing.T, mod *loader.Module, componentID, targetID, targetArtifact string) *runtime.Fiber {
	t.Helper()
	comp, err := mod.Factory.Create(config.ComponentConfig{ID: componentID, Type: mod.Type})
	if err != nil {
		t.Fatal(err)
	}
	f, err := e.rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)
	if err := e.h.Register(hmr.Target{ID: targetID, ComponentID: componentID, Artifact: loader.Artifact{ID: targetArtifact, Type: mod.Type, Source: "builtin://x", Version: "x"}}); err != nil {
		t.Fatal(err)
	}
	if err := e.h.Bind(targetID, *mod, f); err != nil {
		t.Fatal(err)
	}
	return f
}

// hmrFactory builds test components for HMR modules.
type hmrFactory struct {
	tag  string
	kind string
	fail bool
}

func (f *hmrFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	if f.fail {
		return nil, errors.New("factory failure (injected)")
	}
	switch f.kind {
	case "independent":
		return independentComp(&kit{}, cc.ID, false), nil
	case "provider":
		return providerComp(&kit{}, cc.ID, f.tag, false), nil
	case "regowner":
		return regOwnerComp(&kit{}, cc.ID), nil
	}
	return nil, fmt.Errorf("unknown kind %s", f.kind)
}

// E2E-08 / P-01 / P-04 — HMR basic: new fiber Active, old Gone; module released
// only after old fiber Gone.
func TestE2E08HMRBasic(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-v1", "v1", "independent", false)
	e.register(t, "builtin://ind-v2", "v2", "independent", false)
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	f1 := e.install(t, m1, "comp", "t1", "ind-v2")

	if !e.usage.InUse("ind-v1") {
		t.Fatal("old module should be in use after Bind")
	}
	target := hmr.Target{ID: "t1", ComponentID: "comp", Artifact: artifact("ind-v2", "ind", "builtin://ind-v2", "2")}
	if err := e.h.Replace(ctxT(t), target); err != nil {
		t.Fatal(err)
	}
	b, ok := e.h.CurrentBinding("t1")
	if !ok {
		t.Fatal("no binding")
	}
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("fiber identity not changed")
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new fiber not Active")
	}
	if f1.State() != runtime.StateGone {
		t.Fatal("old fiber not Gone")
	}
	if !e.usage.InUse("ind-v2") {
		t.Fatal("new module usage not held")
	}
	if e.usage.InUse("ind-v1") {
		t.Fatal("old module usage leaked after old fiber Gone")
	}
	if e.ld.Has("ind-v1") {
		t.Fatal("old module still loaded after replacement")
	}
}

// E2E-09 / P-02 — HMR provider replacement: consumer recovers via Kernel.
func TestE2E09HMRProviderReplacement(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))

	consKit := &kit{seen: make(chan string, 8)}
	ld.RegisterBuiltin("builtin://prov-v1", func() config.Factory {
		return &providerFactory{tag: "v1"}
	})
	ld.RegisterBuiltin("builtin://prov-v2", func() config.Factory {
		return &providerFactory{tag: "v2"}
	})
	m1, err := ld.Load(ctxT(t), artifact("prov-v1", "cam", "builtin://prov-v1", "1"))
	if err != nil {
		t.Fatal(err)
	}
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "cam", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	oldF, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, oldF)

	cons := consumerComp(consKit, "C")
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, cf)

	h.Register(hmr.Target{ID: "cam", ComponentID: "cam", Artifact: artifact("prov-v2", "cam", "builtin://prov-v2", "2")})
	h.Bind("cam", *m1, oldF)
	if err := h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: artifact("prov-v2", "cam", "builtin://prov-v2", "2")}); err != nil {
		t.Fatal(err)
	}
	b, _ := h.CurrentBinding("cam")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new provider not active")
	}
	// Consumer recovers onto the new provider.
	eventually(t, 8*time.Second, "consumer recovered", func() bool { return cf.State() == runtime.StateActive && consKit.applies.Load() >= 2 })
	got := false
	deadline := time.Now().Add(5 * time.Second)
	for !got && time.Now().Before(deadline) {
		select {
		case tag := <-consKit.seen:
			if tag == "v2" {
				got = true
			}
		case <-time.After(50 * time.Millisecond):
		}
	}
	if !got {
		t.Fatal("consumer never saw the new provider v2")
	}
}

type providerFactory struct{ tag string }

func (f *providerFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return providerComp(&kit{}, cc.ID, f.tag, false), nil
}

// E2E-10 — HMR failure preservation: old fiber stays Active and binding stays.
func TestE2E10HMRFailurePreservation(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-v1", "v1", "independent", false)
	e.register(t, "builtin://ind-bad", "bad", "independent", true) // factory fails
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	f1 := e.install(t, m1, "comp", "t1", "ind-bad")

	target := hmr.Target{ID: "t1", ComponentID: "comp", Artifact: artifact("ind-bad", "ind", "builtin://ind-bad", "9")}
	if err := e.h.Replace(ctxT(t), target); err == nil {
		t.Fatal("replacement should fail")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after failed replacement")
	}
	b, _ := e.h.CurrentBinding("t1")
	if b.Fiber != f1 {
		t.Fatal("binding changed after failed replacement")
	}
}

// E2E-11 — Config/HMR separation: each pipeline only affects its own fiber.
func TestE2E11ConfigHMRSeparation(t *testing.T) {
	e, _ := newCfgEnv(t)
	cc := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "cp", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc("v1")}}); err != nil {
		t.Fatal(err)
	}
	cfgFiber := fiberOf(t, e, "cp")
	waitActive(t, cfgFiber)

	he := newHMR(t)
	he.register(t, "builtin://ind-v1", "v1", "independent", false)
	he.register(t, "builtin://ind-v2", "v2", "independent", false)
	m1 := he.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	hf := he.install(t, m1, "hcomp", "h1", "ind-v2")
	_ = hf

	// Config change (reconcile) does not touch the HMR binding.
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc("v2")}}); err != nil {
		t.Fatal(err)
	}
	cfgFiber2 := fiberOf(t, e, "cp")
	if cfgFiber2.ID() == cfgFiber.ID() {
		t.Fatal("config fiber should have been replaced by config pipeline")
	}
	b, _ := he.h.CurrentBinding("h1")
	if b.Fiber != hf {
		t.Fatal("HMR binding changed by config reconcile")
	}

	// HMR replace does not touch the config-owned fiber.
	if err := he.h.Replace(ctxT(t), hmr.Target{ID: "h1", ComponentID: "hcomp", Artifact: artifact("ind-v2", "ind", "builtin://ind-v2", "2")}); err != nil {
		t.Fatal(err)
	}
	if fiberOf(t, e, "cp").State() != runtime.StateActive {
		t.Fatal("config fiber changed by HMR")
	}
}

// E2E-13 — Loader/Config separation: loader.Load only yields a Module; Config
// Reconcile is what creates the Fiber.
func TestE2E13LoaderConfigSeparation(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-v1", "v1", "independent", false)
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))

	// A config controller whose factory registry uses the module factory.
	reg := config.NewFactoryRegistry()
	if err := reg.Register("ind", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return m1.Factory.Create(cc)
	}}); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(e.rt, reg)
	defer ctrl.CloseContext(ctxT(t))

	if len(ctrl.Owned()) != 0 {
		t.Fatal("module load must not create components")
	}
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "c", Type: "ind"}}}); err != nil {
		t.Fatal(err)
	}
	if len(ctrl.Owned()) != 1 {
		t.Fatal("reconcile did not create the component")
	}
	waitActive(t, fiberOf(t, &env{ctrl: ctrl}, "c"))
}

// E2E-14 — Module in-use: unload rejected while a fiber uses the module; after
// fiber Gone and Release, unload succeeds.
func TestE2E14ModuleInUse(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-v1", "v1", "independent", false)
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "c", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f, err := e.rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)
	if err := e.usage.Acquire(m1.ID, "owner"); err != nil {
		t.Fatal(err)
	}
	if err := e.ld.Unload(ctxT(t), m1.ID); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload while in use = %v, want ErrModuleInUse", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatal("fiber affected by rejected unload")
	}
	_ = f.Dispose()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if err := e.usage.Release(m1.ID, "owner"); err != nil {
		t.Fatal(err)
	}
	if err := e.ld.Unload(ctxT(t), m1.ID); err != nil {
		t.Fatalf("Unload after release = %v", err)
	}
}

// E2E-18 / P-11 — concurrent HMR on the same target is serialized.
func TestE2E18ConcurrentHMR(t *testing.T) {
	e := newHMR(t)
	for _, v := range []string{"v1", "v2", "v3", "v4"} {
		e.register(t, "builtin://ind-"+v, v, "independent", false)
	}
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	_ = e.install(t, m1, "comp", "t", "ind-v4")

	var wg sync.WaitGroup
	errs := make([]error, 3)
	targets := []string{"ind-v2", "ind-v3", "ind-v4"}
	for i := 0; i < 3; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			errs[i] = e.h.Replace(ctxT(t), hmr.Target{ID: "t", ComponentID: "comp", Artifact: artifact(targets[i], "ind", "builtin://"+targets[i], "2")})
		}(i)
	}
	wg.Wait()
	for i, err := range errs {
		if err != nil {
			t.Fatalf("replace %d failed: %v", i, err)
		}
	}
	b, _ := e.h.CurrentBinding("t")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("final binding not active")
	}
	if b.Module.ID != "ind-v2" && b.Module.ID != "ind-v3" && b.Module.ID != "ind-v4" {
		t.Fatalf("unexpected final module %q", b.Module.ID)
	}
}

// E2E-19 — different targets run independently; A failure doesn't affect B.
func TestE2E19DifferentTargetConcurrency(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-a1", "a1", "independent", false)
	e.register(t, "builtin://ind-a2", "a2", "independent", true)
	e.register(t, "builtin://ind-b1", "b1", "independent", false)
	e.register(t, "builtin://ind-b2", "b2", "independent", false)

	mA := e.load(t, artifact("ind-a1", "ind", "builtin://ind-a1", "1"))
	fA := e.install(t, mA, "A", "tA", "ind-a2")
	mB := e.load(t, artifact("ind-b1", "ind", "builtin://ind-b1", "1"))
	fB := e.install(t, mB, "B", "tB", "ind-b2")

	var wg sync.WaitGroup
	var errA, errB error
	wg.Add(2)
	go func() {
		defer wg.Done()
		errA = e.h.Replace(ctxT(t), hmr.Target{ID: "tA", ComponentID: "A", Artifact: artifact("ind-a2", "ind", "builtin://ind-a2", "2")})
	}()
	go func() {
		defer wg.Done()
		errB = e.h.Replace(ctxT(t), hmr.Target{ID: "tB", ComponentID: "B", Artifact: artifact("ind-b2", "ind", "builtin://ind-b2", "2")})
	}()
	wg.Wait()

	if errA == nil {
		t.Fatal("A replacement should fail")
	}
	if errB != nil {
		t.Fatalf("B replacement failed: %v", errB)
	}
	if fA.State() != runtime.StateActive {
		t.Fatal("A old fiber not active after A failure")
	}
	bB, _ := e.h.CurrentBinding("tB")
	if bB.Fiber.State() != runtime.StateActive {
		t.Fatal("B not replaced successfully")
	}
	_ = fB
}

// E2E-20 — Registry + HMR: replacing a registry-owner component replaces the
// provider; the consumer follows; member churn is not provider replacement.
func TestE2E20RegistryHMR(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))

	regFactory := func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return regOwnerComp(&kit{}, cc.ID), nil
		}}
	}
	ld.RegisterBuiltin("builtin://reg-v1", regFactory)
	ld.RegisterBuiltin("builtin://reg-v2", regFactory)
	m1, err := ld.Load(ctxT(t), artifact("reg-v1", "reg", "builtin://reg-v1", "1"))
	if err != nil {
		t.Fatal(err)
	}
	comp1, err := m1.Factory.Create(config.ComponentConfig{ID: "R", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	owner1, err := rt.Load(comp1)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, owner1)
	reg1 := comp1.(*testComp).reg

	consKit := &kit{}
	cf, err := rt.Load(regConsumerComp(consKit, "C"))
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, cf)

	h.Register(hmr.Target{ID: "R", ComponentID: "R", Artifact: artifact("reg-v2", "reg", "builtin://reg-v2", "2")})
	h.Bind("R", *m1, owner1)
	// Member churn is not provider replacement: consumer stays.
	reg1.Add("m", "1")
	reg1.Remove("m")
	if consKit.applies.Load() != 1 {
		t.Fatalf("consumer re-applied on member churn: %d", consKit.applies.Load())
	}

	// Replace the registry owner.
	if err := h.Replace(ctxT(t), hmr.Target{ID: "R", ComponentID: "R", Artifact: artifact("reg-v2", "reg", "builtin://reg-v2", "2")}); err != nil {
		t.Fatal(err)
	}
	b, _ := h.CurrentBinding("R")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new registry owner not active")
	}
	eventually(t, 8*time.Second, "consumer recovered", func() bool {
		return cf.State() == runtime.StateActive && consKit.applies.Load() >= 2
	})
	newComp, ok := b.Fiber.Component().(*testComp)
	if !ok || newComp.reg == reg1 {
		t.Fatal("registry provider not actually replaced")
	}
}

// E2E-22 — Close during HMR: no panic, no leak, later Replace rejected.
func TestE2E22CloseDuringHMR(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-v1", "v1", "independent", false)
	e.register(t, "builtin://ind-v2", "v2", "independent", false)
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	_ = e.install(t, m1, "comp", "t", "ind-v2")

	var wg sync.WaitGroup
	var okReplace, closedReplace atomic.Int32
	for g := 0; g < 4; g++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				err := e.h.Replace(ctxT(t), hmr.Target{ID: "t", ComponentID: "comp", Artifact: artifact("ind-v2", "ind", "builtin://ind-v2", "2")})
				if err == nil {
					okReplace.Add(1)
					continue
				}
				if errors.Is(err, hmr.ErrHMRClosed) {
					closedReplace.Add(1)
					return
				}
				if errors.Is(err, loader.ErrModuleExists) {
					continue
				}
				t.Errorf("unexpected error: %v", err)
				return
			}
		}()
	}
	time.Sleep(5 * time.Millisecond)
	if err := e.h.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	wg.Wait()
	if okReplace.Load() == 0 || closedReplace.Load() == 0 {
		t.Fatalf("ok=%d closed=%d", okReplace.Load(), closedReplace.Load())
	}
	if e.usage.InUse("ind-v1") {
		t.Fatal("usage leaked after HMR close")
	}
}

// E2E-26 — Activation identity: WaitInactive for the old activation returns
// once it ends and is not mis-satisfied by a later activation.
func TestE2E26ActivationIdentity(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	f, err := rt.Load(independentComp(&kit{}, "x", false))
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)

	waitCh := make(chan error, 1)
	go func() { waitCh <- f.WaitInactive(context.Background()) }()
	_ = f.Dispose()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	select {
	case err := <-waitCh:
		if err != nil {
			t.Fatalf("WaitInactive: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("WaitInactive never returned for the ended activation")
	}
	// Remount = a new activation; it must not affect the completed waiter.
	if err := f.Load(); err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)
}

// E2E-27 / P-10 — stale async completion isolation is guaranteed by the Kernel;
// this composes it with a real replacement (old completion cannot touch new).
func TestE2E27StaleCompletionIsolation(t *testing.T) {
	e := newHMR(t)
	e.register(t, "builtin://ind-v1", "v1", "independent", false)
	e.register(t, "builtin://ind-v2", "v2", "independent", false)
	m1 := e.load(t, artifact("ind-v1", "ind", "builtin://ind-v1", "1"))
	oldF := e.install(t, m1, "comp", "t", "ind-v2")
	if err := e.h.Replace(ctxT(t), hmr.Target{ID: "t", ComponentID: "comp", Artifact: artifact("ind-v2", "ind", "builtin://ind-v2", "2")}); err != nil {
		t.Fatal(err)
	}
	b, _ := e.h.CurrentBinding("t")
	if oldF.State() != runtime.StateGone || b.Fiber.State() != runtime.StateActive {
		t.Fatal("replacement state incorrect")
	}
	// The old fiber stays Gone; the new fiber's lifecycle is untouched.
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	if err := oldF.Ready(ctx); !errors.Is(err, runtime.ErrFiberGone) {
		t.Fatalf("old fiber Ready = %v, want ErrFiberGone", err)
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new fiber corrupted")
	}
}
