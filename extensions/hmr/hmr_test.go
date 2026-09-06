package hmr_test

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// markerComponent is a controllable component tagged by implementation.
type markerComponent struct {
	id      string
	tag     string
	inject  []runtime.Dependency
	provide []runtime.Capability
	applyFn func(*runtime.Context) error
	applies *atomic.Int32
	cleanup func()
	rec     *recorder
}

func (c *markerComponent) Name() string                  { return c.tag + ":" + c.id }
func (c *markerComponent) Inject() []runtime.Dependency  { return c.inject }
func (c *markerComponent) Provide() []runtime.Capability { return c.provide }
func (c *markerComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	if c.rec != nil {
		c.rec.add("apply:" + c.tag)
	}
	if c.applyFn != nil {
		if err := c.applyFn(ctx); err != nil {
			return nil, err
		}
	}
	return func() error {
		if c.rec != nil {
			c.rec.add("cleanup:" + c.tag)
		}
		if c.cleanup != nil {
			c.cleanup()
		}
		return nil
	}, nil
}

type recorder struct {
	mu sync.Mutex
	ev []string
}

func (r *recorder) add(e string) {
	r.mu.Lock()
	r.ev = append(r.ev, e)
	r.mu.Unlock()
}
func (r *recorder) all() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := append([]string(nil), r.ev...)
	return out
}
func (r *recorder) index(e string) int {
	for i, x := range r.all() {
		if x == e {
			return i
		}
	}
	return -1
}

// markerFactory builds a config.Factory producing tagged components.
type markerFactory struct {
	tag     string
	provide []runtime.Capability
	applyFn func(*runtime.Context) error
	applies *atomic.Int32
	rec     *recorder
	fail    bool
}

func (f *markerFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	if f.fail {
		return nil, errors.New("factory refuses to create")
	}
	return &markerComponent{
		id:      cc.ID,
		tag:     f.tag,
		provide: f.provide,
		applyFn: f.applyFn,
		applies: f.applies,
		rec:     f.rec,
	}, nil
}

// depCap capability for provider tests.
type depCap struct{ tag string }

var depKey = runtime.NewKey[depCap]("dep")

type env struct {
	rt   *runtime.Runtime
	ld   *loader.BuiltinLoader
	ctrl *hmr.Controller
}

func newEnv(t *testing.T) *env {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	ld := loader.NewBuiltinLoader()
	ctrl := hmr.New(rt, ld, ld.Usage())
	e := &env{rt: rt, ld: ld, ctrl: ctrl}
	t.Cleanup(func() { _ = ctrl.CloseContext(ctxT(t)) })
	t.Cleanup(func() { _ = ld.CloseContext(ctxT(t)) })
	t.Cleanup(func() {
		cl, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(cl)
	})
	return e
}

// regFactory registers a builtin source producing markerFactory{tag}.
func (e *env) regFactory(t *testing.T, src, tag string, f *markerFactory) {
	t.Helper()
	if f == nil {
		f = &markerFactory{tag: tag}
	}
	if err := e.ld.RegisterBuiltin(src, func() config.Factory { return f }); err != nil {
		t.Fatalf("RegisterBuiltin(%q): %v", src, err)
	}
}

func artifact(id, typ, src, ver string) loader.Artifact {
	return loader.Artifact{ID: id, Type: typ, Source: src, Version: ver}
}

func (e *env) loadModule(t *testing.T, a loader.Artifact) *loader.Module {
	t.Helper()
	m, err := e.ld.Load(ctxT(t), a)
	if err != nil {
		t.Fatalf("loader.Load(%s): %v", a.ID, err)
	}
	return m
}

// install creates a component from module mod and loads its fiber (Active),
// then registers+binds an HMR target for it.
func (e *env) install(t *testing.T, targetID, componentID string, mod *loader.Module, targetArtifact loader.Artifact) (*runtime.Fiber, *markerComponent) {
	t.Helper()
	compIf, err := mod.Factory.Create(config.ComponentConfig{ID: componentID, Type: mod.Type})
	if err != nil {
		t.Fatalf("create: %v", err)
	}
	fiber, err := e.rt.Load(compIf)
	if err != nil {
		t.Fatalf("rt.Load: %v", err)
	}
	if err := fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("fiber Ready: %v", err)
	}
	if err := e.ctrl.Register(hmr.Target{ID: targetID, ComponentID: componentID, Artifact: targetArtifact}); err != nil {
		t.Fatalf("Register: %v", err)
	}
	if err := e.ctrl.Bind(targetID, *mod, fiber); err != nil {
		t.Fatalf("Bind: %v", err)
	}
	return fiber, compIf.(*markerComponent)
}

func target(id string, a loader.Artifact) hmr.Target {
	return hmr.Target{ID: id, ComponentID: "camera", Artifact: a}
}

// H1 — Target registration succeeds.
func TestH1Register(t *testing.T) {
	e := newEnv(t)
	if err := e.ctrl.Register(hmr.Target{ID: "cam", ComponentID: "camera", Artifact: artifact("camera-v1", "camera", "builtin://camera-v1", "1")}); err != nil {
		t.Fatalf("Register: %v", err)
	}
	if !e.ctrl.Has("cam") {
		t.Fatal("target not registered")
	}
}

// H2 — Duplicate target registration fails and keeps the original.
func TestH2DuplicateTarget(t *testing.T) {
	e := newEnv(t)
	tg := hmr.Target{ID: "cam", ComponentID: "camera", Artifact: artifact("a", "camera", "builtin://x", "1")}
	if err := e.ctrl.Register(tg); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Register(tg); !errors.Is(err, hmr.ErrTargetExists) {
		t.Fatalf("duplicate Register = %v, want ErrTargetExists", err)
	}
	got, ok := e.ctrl.Get("cam")
	if !ok || got.ComponentID != "camera" {
		t.Fatal("original target changed")
	}
}

// H3/H4 — Unregister / missing target.
func TestH3H4Unregister(t *testing.T) {
	e := newEnv(t)
	if err := e.ctrl.Unregister("nope"); !errors.Is(err, hmr.ErrTargetNotFound) {
		t.Fatalf("Unregister missing = %v, want ErrTargetNotFound", err)
	}
	if err := e.ctrl.Register(hmr.Target{ID: "a", ComponentID: "camera", Artifact: artifact("x", "camera", "builtin://s", "1")}); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Unregister("a"); err != nil {
		t.Fatalf("Unregister: %v", err)
	}
	if e.ctrl.Has("a") {
		t.Fatal("target still present after Unregister")
	}
}

// H5 — New module load failure: old stays Active.
func TestH5NewModuleLoadFailure(t *testing.T) {
	e := newEnv(t)
	f1 := &markerFactory{tag: "v1"}
	e.regFactory(t, "builtin://cam-v1", "v1", f1)
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))

	err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://missing-v2", "2")))
	if !errors.Is(err, loader.ErrLoadFailed) {
		t.Fatalf("Replace = %v, want load failure", err)
	}
	if oldFiber.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after failed new load")
	}
	b, _ := e.ctrl.CurrentBinding("cam")
	if b.Fiber != oldFiber {
		t.Fatal("binding changed after failed load")
	}
}

// H6 — Incompatible module type: old stays Active.
func TestH6IncompatibleModule(t *testing.T) {
	e := newEnv(t)
	f1 := &markerFactory{tag: "v1"}
	e.regFactory(t, "builtin://cam-v1", "v1", f1)
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "database", "builtin://db-v2", "2"))
	_ = oldFiber

	// Provide a "database"-typed module source.
	e.regFactory(t, "builtin://db-v2", "db", &markerFactory{tag: "db"})
	err := e.ctrl.Replace(ctxT(t), target("cam", artifact("db-v2", "database", "builtin://db-v2", "2")))
	if !errors.Is(err, hmr.ErrIncompatibleModule) {
		t.Fatalf("Replace = %v, want ErrIncompatibleModule", err)
	}
	if oldFiber.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after incompatible module")
	}
}

// H7 — New component creation failure: old stays Active.
func TestH7CreateFailure(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))

	e.regFactory(t, "builtin://cam-v2", "v2", &markerFactory{tag: "v2", fail: true})
	err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2")))
	if err == nil {
		t.Fatal("expected replacement failure")
	}
	if oldFiber.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after create failure")
	}
	// No module usage leak (H14).
	if e.ld.Has("cam-v2") {
		t.Fatal("failed new module left loaded")
	}
	if e.ld.Usage().InUse("cam-v2") {
		t.Fatal("module usage leaked after create failure")
	}
}

// H8 — New fiber fails to reach Active: old stays Active.
func TestH8NewFiberFailure(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))

	bad := &markerFactory{tag: "v2"}
	bad.applyFn = func(ctx *runtime.Context) error { return errors.New("apply boom") }
	e.regFactory(t, "builtin://cam-v2", "v2", bad)
	err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2")))
	if err == nil {
		t.Fatal("expected replacement failure")
	}
	if oldFiber.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after new fiber failure")
	}
	if e.ld.Usage().InUse("cam-v2") || e.ld.Has("cam-v2") {
		t.Fatal("failed replacement leaked module/usage")
	}
}

// H9/H10/H11/H12/H13 — warm replacement success.
func TestH9WarmReplacement(t *testing.T) {
	e := newEnv(t)
	rec := &recorder{}
	f1 := &markerFactory{tag: "v1", rec: rec}
	e.regFactory(t, "builtin://cam-v1", "v1", f1)
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))

	if !e.ld.Usage().InUse("cam-v1") {
		t.Fatal("old module should be in use after Bind")
	}

	f2 := &markerFactory{tag: "v2", rec: rec}
	e.regFactory(t, "builtin://cam-v2", "v2", f2)
	if err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2"))); err != nil {
		t.Fatalf("Replace: %v", err)
	}

	b, ok := e.ctrl.CurrentBinding("cam")
	if !ok {
		t.Fatal("no binding after replace")
	}
	// H10: fiber identity changed.
	if b.Fiber.ID() == oldFiber.ID() {
		t.Fatal("fiber identity did not change")
	}
	// H9: new Active, old Gone.
	if b.Fiber.State() != runtime.StateActive {
		t.Fatalf("new fiber state = %v, want Active", b.Fiber.State())
	}
	if oldFiber.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %v, want Gone", oldFiber.State())
	}
	if b.Module.ID != "cam-v2" {
		t.Fatalf("binding module = %q, want cam-v2", b.Module.ID)
	}
	// Warm ordering: new fiber applied before old cleanup.
	if a := rec.index("apply:v2"); a < 0 {
		t.Fatal("new apply never recorded")
	}
	if c := rec.index("cleanup:v1"); c < 0 {
		t.Fatal("old cleanup never recorded")
	}
	if rec.index("apply:v2") > rec.index("cleanup:v1") {
		t.Fatal("warm replacement violated: new apply must precede old cleanup")
	}
	// H12/H13: new usage acquired; old usage released only after old Gone.
	if !e.ld.Usage().InUse("cam-v2") {
		t.Fatal("new module usage not held after replacement")
	}
	if e.ld.Usage().InUse("cam-v1") {
		t.Fatal("old module usage still held after old fiber Gone")
	}
	// Old module unloaded (no other usage).
	if e.ld.Has("cam-v1") {
		t.Fatal("old module still loaded after replacement")
	}
}

// H15 — concurrent Replace of the same target is serialized (no overlap; both
// may complete in sequence when both artifacts are valid).
func TestH15ConcurrentReplace(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	_, _ = e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))
	e.regFactory(t, "builtin://cam-v2", "v2", &markerFactory{tag: "v2"})
	e.regFactory(t, "builtin://cam-v3", "v3", &markerFactory{tag: "v3"})

	var wg sync.WaitGroup
	errs := make([]error, 2)
	wg.Add(2)
	go func() {
		defer wg.Done()
		errs[0] = e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2")))
	}()
	go func() {
		defer wg.Done()
		errs[1] = e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v3", "camera", "builtin://cam-v3", "3")))
	}()
	wg.Wait()
	if errs[0] != nil || errs[1] != nil {
		t.Fatalf("replace errors: %v %v", errs[0], errs[1])
	}
	b, _ := e.ctrl.CurrentBinding("cam")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("final fiber not Active after serialized concurrent replaces")
	}
	if b.Module.ID != "cam-v2" && b.Module.ID != "cam-v3" {
		t.Fatalf("final module = %q, want cam-v2 or cam-v3", b.Module.ID)
	}
}

// H16 — Target isolation: a failing replacement on A leaves B untouched.
func TestH16TargetIsolation(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	e.regFactory(t, "builtin://other-v1", "o1", &markerFactory{tag: "o1"})
	modA := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	fiberA, _ := e.install(t, "A", "camera", modA, artifact("cam-bad", "camera", "builtin://cam-bad", "9"))
	modB := e.loadModule(t, artifact("other-v1", "camera", "builtin://other-v1", "1"))
	fiberB, _ := e.install(t, "B", "camera", modB, artifact("other-v2", "camera", "builtin://other-v2", "2"))

	e.regFactory(t, "builtin://cam-bad", "bad", &markerFactory{tag: "bad", applyFn: func(*runtime.Context) error { return errors.New("bad") }})
	e.regFactory(t, "builtin://other-v2", "o2", &markerFactory{tag: "o2"})

	if err := e.ctrl.Replace(ctxT(t), target("A", artifact("cam-bad", "camera", "builtin://cam-bad", "9"))); err == nil {
		t.Fatal("A replace should fail")
	}
	if fiberA.State() != runtime.StateActive {
		t.Fatal("A old fiber not Active after A failure")
	}
	// B unaffected and can still replace successfully.
	if fiberB.State() != runtime.StateActive {
		t.Fatal("B affected by A failure")
	}
	if err := e.ctrl.Replace(ctxT(t), target("B", artifact("other-v2", "camera", "builtin://other-v2", "2"))); err != nil {
		t.Fatalf("B replace failed: %v", err)
	}
}

// H17 — Context cancellation before the destructive phase keeps Old Active.
func TestH17ContextCancellation(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))

	// New component blocks in Apply until its context is done.
	block := make(chan struct{})
	released := make(chan struct{})
	slow := &markerFactory{tag: "v2"}
	slow.applyFn = func(ctx *runtime.Context) error {
		close(block)
		select {
		case <-ctx.Done():
			return ctx.Err()
		case <-released:
			return nil
		}
	}
	e.regFactory(t, "builtin://cam-v2", "v2", slow)

	replCtx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() {
		done <- e.ctrl.Replace(replCtx, target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2")))
	}()
	<-block  // new fiber is Loading (not Active) -> pre-destructive
	cancel() // cancel before destructive phase
	err := <-done
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("Replace = %v, want context.Canceled", err)
	}
	if oldFiber.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after pre-destructive cancel")
	}
	b, _ := e.ctrl.CurrentBinding("cam")
	if b.Fiber != oldFiber {
		t.Fatal("binding changed after canceled replacement")
	}
	close(released)
	_ = oldFiber
}

// H18 — exclusive provider replacement: fallback withdraw-then-load via Kernel;
// consumers follow Kernel lifecycle.
func TestH18ProviderReplacement(t *testing.T) {
	e := newEnv(t)
	consumerApplies := &atomic.Int32{}
	seen := make(chan string, 8)

	// Old provider module (provides depKey with tag v1).
	p1 := &markerFactory{tag: "p1", provide: []runtime.Capability{depKey.Capability()}}
	p1.applyFn = func(ctx *runtime.Context) error {
		return runtime.Provide(ctx, depKey, depCap{tag: "p1"})
	}
	e.regFactory(t, "builtin://prov-v1", "p1", p1)
	oldMod := e.loadModule(t, artifact("prov-v1", "camera", "builtin://prov-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("prov-v2", "camera", "builtin://prov-v2", "2"))

	// Consumer requires depKey.
	consumer := &markerComponent{id: "consumer", inject: []runtime.Dependency{runtime.Requires(depKey)}, applies: consumerApplies}
	consumer.applyFn = func(ctx *runtime.Context) error {
		v, err := runtime.Require(ctx, depKey)
		if err != nil {
			return err
		}
		seen <- v.tag
		return nil
	}
	cf, err := e.rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}

	// New provider module (same type, provides depKey tag p2).
	p2 := &markerFactory{tag: "p2", provide: []runtime.Capability{depKey.Capability()}}
	p2.applyFn = func(ctx *runtime.Context) error {
		return runtime.Provide(ctx, depKey, depCap{tag: "p2"})
	}
	e.regFactory(t, "builtin://prov-v2", "p2", p2)

	if err := e.ctrl.Replace(ctxT(t), target("cam", artifact("prov-v2", "camera", "builtin://prov-v2", "2"))); err != nil {
		t.Fatalf("provider Replace: %v", err)
	}
	b, _ := e.ctrl.CurrentBinding("cam")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new provider fiber not Active")
	}
	if oldFiber.State() != runtime.StateGone {
		t.Fatal("old provider fiber not Gone")
	}
	// Consumer re-activates onto the new provider (kernel-driven): its first
	// activation saw p1; a later activation must see p2.
	eventually(t, func() bool { return consumerApplies.Load() >= 2 && cf.State() == runtime.StateActive })
	gotP2 := false
	deadline := time.Now().Add(5 * time.Second)
	for !gotP2 && time.Now().Before(deadline) {
		select {
		case tag := <-seen:
			if tag == "p2" {
				gotP2 = true
			}
		case <-time.After(50 * time.Millisecond):
		}
	}
	if !gotP2 {
		t.Fatal("consumer never observed the new provider identity p2")
	}
}

func eventually(t *testing.T, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatal("condition not met")
}

// H19 — Close rejects new work and releases HMR-owned usages.
func TestH19Close(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	modA := e.loadModule(t, artifact("a-v1", "camera", "builtin://cam-v1", "1"))
	_, _ = e.install(t, "A", "camera", modA, artifact("a-v2", "camera", "builtin://cam-v1", "2"))
	modB := e.loadModule(t, artifact("b-v1", "camera", "builtin://cam-v1", "1"))
	_, _ = e.install(t, "B", "camera", modB, artifact("b-v2", "camera", "builtin://cam-v1", "2"))

	if !e.ld.Usage().InUse("a-v1") || !e.ld.Usage().InUse("b-v1") {
		t.Fatal("expected HMR usage on bound modules")
	}
	if err := e.ctrl.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if err := e.ctrl.Close(); err != nil {
		t.Fatalf("second Close: %v", err)
	}
	if e.ld.Usage().InUse("a-v1") || e.ld.Usage().InUse("b-v1") {
		t.Fatal("HMR-owned usage not released on Close")
	}
	if err := e.ctrl.Register(hmr.Target{ID: "x", ComponentID: "camera", Artifact: artifact("x", "camera", "builtin://s", "1")}); !errors.Is(err, hmr.ErrHMRClosed) {
		t.Fatalf("Register after Close = %v, want ErrHMRClosed", err)
	}
	if err := e.ctrl.Replace(ctxT(t), target("A", artifact("a-v2", "camera", "builtin://cam-v1", "2"))); !errors.Is(err, hmr.ErrHMRClosed) {
		t.Fatalf("Replace after Close = %v, want ErrHMRClosed", err)
	}
}

// H20 — HMR does not second-guess kernel fiber state (structural check):
// replacement only reports success when the new fiber is Active; binding fiber
// state is never mutated by HMR itself.
func TestH20Independence(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))
	e.regFactory(t, "builtin://cam-v2", "v2", &markerFactory{tag: "v2"})
	if err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2"))); err != nil {
		t.Fatalf("Replace: %v", err)
	}
	b, _ := e.ctrl.CurrentBinding("cam")
	// Success means Active; the state is owned by the Kernel, never forced by
	// HMR.
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("replacement reported success but fiber not Active")
	}
	_ = oldFiber
}
