package config_test

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Harness + components
// ---------------------------------------------------------------------------

type testComponent struct {
	name    string
	inject  []runtime.Dependency
	provide []runtime.Capability
	applies *atomic.Int32
	applyFn func(*runtime.Context) error
	cleanup func() error
}

func (c *testComponent) Name() string                  { return c.name }
func (c *testComponent) Inject() []runtime.Dependency  { return c.inject }
func (c *testComponent) Provide() []runtime.Capability { return c.provide }
func (c *testComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	if c.applyFn != nil {
		if err := c.applyFn(ctx); err != nil {
			return nil, err
		}
	}
	return c.cleanup, nil
}

// factory builds one test Factory for a component kind.
type factory struct {
	calls *atomic.Int32
	build func(cc config.ComponentConfig) (runtime.Component, error)
}

func (f *factory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	if f.calls != nil {
		f.calls.Add(1)
	}
	if f.build != nil {
		return f.build(cc)
	}
	return nil, errors.New("unconfigured factory")
}

func simpleComp(name string, applies *atomic.Int32) *testComponent {
	return &testComponent{name: name, applies: applies}
}

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type env struct {
	rt   *runtime.Runtime
	reg  config.FactoryRegistry
	ctrl *config.Controller
}

func newEnv(t *testing.T) *env {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	e := &env{rt: rt, reg: reg, ctrl: ctrl}
	t.Cleanup(func() {
		cl, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(cl)
	})
	t.Cleanup(func() {
		cl, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(cl)
	})
	return e
}

func (e *env) registerSimple(t *testing.T, typ string, applies *atomic.Int32) *factory {
	t.Helper()
	f := &factory{calls: &atomic.Int32{}}
	f.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return simpleComp(cc.ID, applies), nil
	}
	if err := e.reg.Register(typ, f); err != nil {
		t.Fatalf("Register(%q): %v", typ, err)
	}
	return f
}

func ownedByID(t *testing.T, c *config.Controller, id string) (config.OwnedComponent, bool) {
	t.Helper()
	for _, o := range c.Owned() {
		if o.ID == id {
			return o, true
		}
	}
	return config.OwnedComponent{}, false
}

func ownIDs(c *config.Controller) map[string]config.OwnedComponent {
	out := map[string]config.OwnedComponent{}
	for _, o := range c.Owned() {
		out[o.ID] = o
	}
	return out
}

func cfg(comps ...config.ComponentConfig) config.Config {
	return config.Config{Components: comps}
}

func cc(id, typ string, kv ...any) config.ComponentConfig {
	m := map[string]any{}
	for i := 0; i+1 < len(kv); i += 2 {
		m[kv[i].(string)] = kv[i+1]
	}
	return config.ComponentConfig{ID: id, Type: typ, Config: m}
}

// ---------------------------------------------------------------------------
// C1 — Empty Reconcile
// ---------------------------------------------------------------------------

func TestC1EmptyReconcile(t *testing.T) {
	e := newEnv(t)
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{}); err != nil {
		t.Fatalf("Reconcile({}): %v", err)
	}
	if len(e.ctrl.Owned()) != 0 {
		t.Fatalf("owned %d components after empty reconcile, want 0", len(e.ctrl.Owned()))
	}
}

// ---------------------------------------------------------------------------
// C2 — Add Component
// ---------------------------------------------------------------------------

func TestC2AddComponent(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	o, ok := ownedByID(t, e.ctrl, "A")
	if !ok {
		t.Fatal("A not owned after add")
	}
	if o.Fiber == nil {
		t.Fatal("A has no fiber")
	}
	if err := o.Fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("A did not reach Active: %v", err)
	}
}

// ---------------------------------------------------------------------------
// C3 — Remove Component
// ---------------------------------------------------------------------------

func TestC3RemoveComponent(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile add: %v", err)
	}
	o, _ := ownedByID(t, e.ctrl, "A")

	if err := e.ctrl.Reconcile(ctxT(t), config.Config{}); err != nil {
		t.Fatalf("Reconcile remove: %v", err)
	}
	if _, ok := ownedByID(t, e.ctrl, "A"); ok {
		t.Fatal("A still owned after removal")
	}
	if err := o.Fiber.Gone(ctxT(t)); err != nil {
		t.Fatalf("A fiber not gone: %v", err)
	}
}

// ---------------------------------------------------------------------------
// C4 / P1 — Idempotence: second identical Reconcile creates nothing and keeps
// the same fiber (identity preserved).
// ---------------------------------------------------------------------------

func TestC4ReconcileIdempotent(t *testing.T) {
	e := newEnv(t)
	f := e.registerSimple(t, "a", nil)

	c := cfg(cc("A", "a", "x", 1, "nested", map[string]any{"k": []any{"a", "b"}}))
	if err := e.ctrl.Reconcile(ctxT(t), c); err != nil {
		t.Fatalf("first Reconcile: %v", err)
	}
	first, _ := ownedByID(t, e.ctrl, "A")
	if got := f.calls.Load(); got != 1 {
		t.Fatalf("factory calls after first reconcile = %d, want 1", got)
	}

	if err := e.ctrl.Reconcile(ctxT(t), c); err != nil {
		t.Fatalf("second Reconcile: %v", err)
	}
	second, _ := ownedByID(t, e.ctrl, "A")
	if got := f.calls.Load(); got != 1 {
		t.Fatalf("factory calls after second reconcile = %d, want 1 (no re-creation)", got)
	}
	if first.Fiber.ID() != second.Fiber.ID() {
		t.Fatal("fiber identity changed on idempotent reconcile")
	}
	if err := second.Fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("A not active: %v", err)
	}
}

// ---------------------------------------------------------------------------
// C5 / P3 — Duplicate ID: validation error, runtime unchanged
// ---------------------------------------------------------------------------

func TestC5DuplicateID(t *testing.T) {
	e := newEnv(t)
	f := e.registerSimple(t, "a", nil)
	// First apply A successfully.
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	before := ownIDs(e.ctrl)

	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("A", "a")))
	if !errors.Is(err, config.ErrDuplicateComponentID) {
		t.Fatalf("Reconcile(dup) = %v, want ErrDuplicateComponentID", err)
	}
	if got := f.calls.Load(); got != 1 {
		t.Fatalf("factory calls = %d, want 1 (no mutation)", got)
	}
	after := ownIDs(e.ctrl)
	if len(after) != len(before) || after["A"].Fiber.ID() != before["A"].Fiber.ID() {
		t.Fatal("runtime changed after invalid config")
	}
}

// ---------------------------------------------------------------------------
// C6 — Unknown Type: error, runtime unchanged
// ---------------------------------------------------------------------------

func TestC6UnknownType(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	before := ownIDs(e.ctrl)

	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("B", "no-such-type")))
	if !errors.Is(err, config.ErrUnknownComponentType) {
		t.Fatalf("Reconcile(unknown) = %v, want ErrUnknownComponentType", err)
	}
	after := ownIDs(e.ctrl)
	if len(after) != len(before) || after["A"].Fiber.ID() != before["A"].Fiber.ID() {
		t.Fatal("runtime changed after unknown type")
	}
}

// ---------------------------------------------------------------------------
// C7 / P3 — Atomic validation: valid A + invalid B => A not loaded either
// ---------------------------------------------------------------------------

func TestC7InvalidConfigAtomicValidation(t *testing.T) {
	e := newEnv(t)
	f := e.registerSimple(t, "a", nil)

	// B has an empty ID -> whole config invalid.
	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), config.ComponentConfig{Type: "a"}))
	if !errors.Is(err, config.ErrInvalidConfig) {
		t.Fatalf("Reconcile = %v, want ErrInvalidConfig", err)
	}
	if got := f.calls.Load(); got != 0 {
		t.Fatalf("factory calls = %d, want 0 (A must not be loaded)", got)
	}
	if len(e.ctrl.Owned()) != 0 {
		t.Fatal("nothing may be owned after an invalid config")
	}
}

// ---------------------------------------------------------------------------
// C8 / P5 — Replacement creates a NEW fiber
// ---------------------------------------------------------------------------

func TestC8ReplacementNewFiber(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a", "v", 1))); err != nil {
		t.Fatalf("Reconcile v1: %v", err)
	}
	old, _ := ownedByID(t, e.ctrl, "A")

	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a", "v", 2))); err != nil {
		t.Fatalf("Reconcile v2: %v", err)
	}
	new_, _ := ownedByID(t, e.ctrl, "A")
	if old.Fiber.ID() == new_.Fiber.ID() {
		t.Fatal("replacement must create a NEW fiber")
	}
	if err := old.Fiber.Gone(ctxT(t)); err != nil {
		t.Fatalf("old fiber not gone: %v", err)
	}
	if err := new_.Fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("new fiber not active: %v", err)
	}
}

// ---------------------------------------------------------------------------
// C9 / P7 — Partial failure: successes recorded, failure not faked
// ---------------------------------------------------------------------------

func TestC9PartialFailure(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	e.registerSimple(t, "c", nil)
	boom := errors.New("create b failed")
	bf := &factory{}
	bf.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return nil, boom
	}
	if err := e.reg.Register("b", bf); err != nil {
		t.Fatal(err)
	}

	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "b"), cc("C", "c")))
	if !errors.Is(err, boom) {
		t.Fatalf("Reconcile = %v, want create failure", err)
	}
	owned := ownIDs(e.ctrl)
	if _, ok := owned["A"]; !ok {
		t.Fatal("A should be applied")
	}
	if _, ok := owned["C"]; !ok {
		t.Fatal("C should be applied")
	}
	if _, ok := owned["B"]; ok {
		t.Fatal("B must NOT be applied (create failed)")
	}
}

// ---------------------------------------------------------------------------
// C10 / P4 — Ownership isolation: only controller-owned components removed
// ---------------------------------------------------------------------------

func TestC10OwnershipIsolation(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}

	// A foreign component loaded directly on the Runtime, not by the
	// controller.
	foreign, err := e.rt.Load(simpleComp("foreign", nil))
	if err != nil {
		t.Fatalf("Load foreign: %v", err)
	}
	if err := foreign.Ready(ctxT(t)); err != nil {
		t.Fatalf("foreign Ready: %v", err)
	}

	if err := e.ctrl.Reconcile(ctxT(t), config.Config{}); err != nil {
		t.Fatalf("Reconcile({}): %v", err)
	}
	if len(e.ctrl.Owned()) != 0 {
		t.Fatal("controller still owns components")
	}
	if got := foreign.State(); got != runtime.StateActive {
		t.Fatalf("foreign component state = %v, want Active (untouched)", got)
	}
	_ = foreign.Dispose()
}

// ---------------------------------------------------------------------------
// C13 — Factory duplicate
// ---------------------------------------------------------------------------

func TestC13FactoryDuplicate(t *testing.T) {
	e := newEnv(t)
	f1 := e.registerSimple(t, "a", nil)
	other := &factory{}
	if err := e.reg.Register("a", other); !errors.Is(err, config.ErrFactoryExists) {
		t.Fatalf("duplicate Register = %v, want ErrFactoryExists", err)
	}
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	if got := f1.calls.Load(); got != 1 {
		t.Fatalf("original factory not used: calls=%d", got)
	}
}

// ---------------------------------------------------------------------------
// C14 — Factory panic isolation
// ---------------------------------------------------------------------------

func TestC14FactoryPanic(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "ok", nil)
	panicFactory := &factory{}
	panicFactory.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		panic("factory exploded")
	}
	if err := e.reg.Register("boom", panicFactory); err != nil {
		t.Fatal(err)
	}

	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "boom"), cc("B", "ok")))
	if !errors.Is(err, config.ErrComponentCreatePanic) {
		t.Fatalf("Reconcile = %v, want ErrComponentCreatePanic", err)
	}
	if _, ok := ownedByID(t, e.ctrl, "B"); !ok {
		t.Fatal("B should still be applied after A's factory panic")
	}
	if _, ok := ownedByID(t, e.ctrl, "A"); ok {
		t.Fatal("A must not be applied after factory panic")
	}
	// Controller continues to work.
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("B", "ok"))); err != nil {
		t.Fatalf("controller did not survive factory panic: %v", err)
	}
}

// ---------------------------------------------------------------------------
// C15 — Component create failure: applied unchanged, no leak
// ---------------------------------------------------------------------------

func TestC15CreateFailure(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	before := ownIDs(e.ctrl)

	bad := &factory{}
	bad.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return nil, errors.New("cannot create")
	}
	if err := e.reg.Register("bad", bad); err != nil {
		t.Fatal(err)
	}
	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "bad")))
	if err == nil {
		t.Fatal("expected create failure")
	}
	after := ownIDs(e.ctrl)
	if len(after) != len(before) || after["A"].Fiber.ID() != before["A"].Fiber.ID() {
		t.Fatal("existing applied state changed after create failure")
	}
}

// ---------------------------------------------------------------------------
// C16 — Component load failure (factory returns a nil component)
// ---------------------------------------------------------------------------

func TestC16LoadFailure(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	before := ownIDs(e.ctrl)

	nilFactory := &factory{}
	nilFactory.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return nil, nil // component cannot be loaded
	}
	if err := e.reg.Register("nil", nilFactory); err != nil {
		t.Fatal(err)
	}
	err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "nil")))
	if err == nil {
		t.Fatal("expected load failure")
	}
	after := ownIDs(e.ctrl)
	if len(after) != len(before) {
		t.Fatalf("applied changed after load failure: %v", ownIDs(e.ctrl))
	}
	if _, ok := after["B"]; ok {
		t.Fatal("B must not be applied")
	}
}

// ---------------------------------------------------------------------------
// C17 — Runtime dependency handled by the Kernel, not the controller
// ---------------------------------------------------------------------------

type depCap struct{ name string }

var depKey = runtime.NewKey[depCap]("dep")

func TestC17RuntimeDependency(t *testing.T) {
	e := newEnv(t)

	// Type "b" provides depKey.
	bFac := &factory{}
	bFac.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return &testComponent{
			name:    cc.ID,
			provide: []runtime.Capability{depKey.Capability()},
			applyFn: func(ctx *runtime.Context) error {
				return runtime.Provide(ctx, depKey, depCap{name: cc.ID})
			},
		}, nil
	}
	if err := e.reg.Register("b", bFac); err != nil {
		t.Fatal(err)
	}
	// Type "a" requires depKey.
	aFac := &factory{}
	aFac.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return &testComponent{
			name:   cc.ID,
			inject: []runtime.Dependency{runtime.Requires(depKey)},
		}, nil
	}
	if err := e.reg.Register("a", aFac); err != nil {
		t.Fatal(err)
	}

	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "b"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	a, _ := ownedByID(t, e.ctrl, "A")
	b, _ := ownedByID(t, e.ctrl, "B")
	// The Kernel resolves the dependency: both eventually become Active without
	// any controller involvement.
	if err := b.Fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("B not active: %v", err)
	}
	if err := a.Fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("A not active (kernel must handle dependency): %v", err)
	}
}

// ---------------------------------------------------------------------------
// C18 — Provider identity: unchanged on no-op, new on replacement
// ---------------------------------------------------------------------------

func TestC18ProviderIdentity(t *testing.T) {
	e := newEnv(t)
	pFac := &factory{}
	pFac.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return &testComponent{
			name:    cc.ID,
			provide: []runtime.Capability{depKey.Capability()},
			applyFn: func(ctx *runtime.Context) error {
				return runtime.Provide(ctx, depKey, depCap{name: cc.ID})
			},
		}, nil
	}
	if err := e.reg.Register("p", pFac); err != nil {
		t.Fatal(err)
	}

	c1 := cfg(cc("P", "p", "v", 1))
	if err := e.ctrl.Reconcile(ctxT(t), c1); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	id1, _ := ownedByID(t, e.ctrl, "P")

	// Idempotent reconcile: provider identity unchanged.
	if err := e.ctrl.Reconcile(ctxT(t), c1); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	id2, _ := ownedByID(t, e.ctrl, "P")
	if id1.Fiber.ID() != id2.Fiber.ID() {
		t.Fatal("provider identity changed on no-op reconcile")
	}

	// Replacement: new fiber -> Kernel produces a new provider generation.
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("P", "p", "v", 2))); err != nil {
		t.Fatalf("Reconcile replacement: %v", err)
	}
	id3, _ := ownedByID(t, e.ctrl, "P")
	if id1.Fiber.ID() == id3.Fiber.ID() {
		t.Fatal("replacement must change provider identity")
	}
}

// ---------------------------------------------------------------------------
// C19 / P8 — Close cleans owned components; idempotent
// ---------------------------------------------------------------------------

func TestC19CloseCleanup(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	owned := ownIDs(e.ctrl)
	if len(owned) != 2 {
		t.Fatalf("owned = %d, want 2", len(owned))
	}

	if err := e.ctrl.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if err := e.ctrl.Close(); err != nil {
		t.Fatalf("second Close: %v", err)
	}
	if len(e.ctrl.Owned()) != 0 {
		t.Fatal("controller still owns components after Close")
	}
	for _, o := range owned {
		if err := o.Fiber.Gone(ctxT(t)); err != nil {
			t.Fatalf("owned fiber %s not gone after Close: %v", o.ID, err)
		}
	}
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); !errors.Is(err, config.ErrControllerClosed) {
		t.Fatalf("Reconcile after Close = %v, want ErrControllerClosed", err)
	}
}

// ---------------------------------------------------------------------------
// C20 — CloseContext timeout with a stubborn component cleanup
// ---------------------------------------------------------------------------

func TestC20CloseContextTimeout(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	}()

	release := make(chan struct{})
	stubbornFac := &factory{}
	stubbornFac.build = func(cc config.ComponentConfig) (runtime.Component, error) {
		return &testComponent{
			name: cc.ID,
			cleanup: func() error {
				<-release // blocks, ignoring cancellation
				return nil
			},
		}, nil
	}
	if err := reg.Register("stubborn", stubbornFac); err != nil {
		t.Fatal(err)
	}

	if err := ctrl.Reconcile(ctxT(t), cfg(cc("S", "stubborn"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	s, _ := ownedByID(t, ctrl, "S")
	if err := s.Fiber.Ready(ctxT(t)); err != nil {
		t.Fatalf("S not active: %v", err)
	}

	short, cancel := context.WithTimeout(context.Background(), 100*time.Millisecond)
	err = ctrl.CloseContext(short)
	cancel()
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("CloseContext = %v, want deadline exceeded", err)
	}
	// The stubborn fiber must NOT have been falsely reported gone.
	if s.Fiber.State() == runtime.StateGone {
		t.Fatal("stubborn fiber reported Gone while cleanup is blocked")
	}
	// Controller is closed for new work.
	if err := ctrl.Reconcile(ctxT(t), cfg(cc("X", "stubborn"))); !errors.Is(err, config.ErrControllerClosed) {
		t.Fatalf("Reconcile after timed-out Close = %v, want ErrControllerClosed", err)
	}

	// Release the cleanup: the controller drains and a later Close succeeds.
	close(release)
	done := make(chan error, 1)
	go func() { done <- ctrl.CloseContext(context.Background()) }()
	select {
	case err := <-done:
		if err != nil {
			t.Fatalf("CloseContext after release: %v", err)
		}
	case <-time.After(10 * time.Second):
		t.Fatal("controller did not finish closing after stubborn cleanup returned")
	}
}
