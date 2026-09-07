package runtime_test

// P2.1 Plugin Boundary Convergence (spec v0.2) — public-boundary conformance.
//
// This file intentionally lives in the EXTERNAL test package (runtime_test):
// it imports only dynamic-runtime/runtime and can therefore never touch
// orchestrator/registry/activation internals. Every Component here is
// therefore a proof of PB-08 ("an external Component implementor needs only
// the Runtime public API"), and each test maps to one PB-01..PB-10 boundary
// requirement of the convergence gate.
//
// The verdict this suite supports:
//
//	A1  Component = Plugin lifecycle entry      (PB-01)
//	A2  Context   = Plugin runtime boundary      (all tests: every mutation
//	                                               goes through Context API)
//	A3  Fiber/Activation = Plugin runtime identity (PB-05, PB-06)
//	A4  Effect    = side-effect ownership        (PB-04, PB-10)
//	A5  Dependency = Plugin readiness             (PB-02, PB-03)
//	A6  Reconciliation = activation/deactivation  (PB-02, PB-03, PB-09)
//	A7  Event     = Plugin communication          (PB-04, PB-08)
//	A8  Kernel does not depend on Plugin          (no Plugin type anywhere)
//	A9  No second Plugin lifecycle                (PB-04/PB-09/PB-10 unwind
//	                                               only through Runtime)
//	A10 External Component works on Public API     (this package)

import (
	"context"
	"errors"
	"testing"

	"dynamic-runtime/runtime"
)

var (
	p21EvKey  = runtime.NewEventKey[string]("p2.1.evt")
	p21WFKey  = runtime.NewEventKey[string]("p2.1.wf")
	p21SvcKey = runtime.NewKey[string]("p2.1.svc")
)

// p21Emitter is a live activation whose Context the test uses to dispatch
// Events. It stays Active for the whole test so handler-gone assertions are
// meaningful.
type p21Emitter struct {
	ctxCh chan *runtime.Context
}

func (c *p21Emitter) Name() string                  { return "p21-emitter" }
func (c *p21Emitter) Inject() []runtime.Dependency  { return nil }
func (c *p21Emitter) Provide() []runtime.Capability { return nil }
func (c *p21Emitter) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.ctxCh <- ctx
	return nil, nil
}

func p21LoadEmitter(t *testing.T, rt *runtime.Runtime) (*runtime.Fiber, *runtime.Context) {
	t.Helper()
	em := &p21Emitter{ctxCh: make(chan *runtime.Context, 1)}
	f, err := rt.Load(em)
	if err != nil {
		t.Fatalf("Load emitter: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("emitter Ready: %v", err)
	}
	return f, <-em.ctxCh
}

func p21Snapshot(t *testing.T, rt *runtime.Runtime) runtime.RuntimeSnapshot {
	t.Helper()
	snap, err := rt.Snapshot(testTimeout(t))
	if err != nil {
		t.Fatalf("Snapshot: %v", err)
	}
	return snap
}

func p21FiberSnap(t *testing.T, snap runtime.RuntimeSnapshot, id runtime.FiberID) (runtime.FiberSnapshot, bool) {
	t.Helper()
	for _, fs := range snap.Fibers {
		if fs.ID == id {
			return fs, true
		}
	}
	return runtime.FiberSnapshot{}, false
}

// PB-01: a minimal Component (Inject/Provide/Apply/Effect/Cleanup) enters
// Active through the Runtime and unwinds through Dispose/Gone.
func TestP21PB01ComponentAsPlugin(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	comp := newFakeComponent("plugin-min")
	comp.provide = []runtime.Capability{greeterKey.Capability()}
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "min")
		if err := installEffect(t, ctx, rec, "E", nil); err != nil {
			return nil, err
		}
		return func() error { rec.add("cleanup"); return nil }, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready: %v", err)
	}
	if got := f.State(); got != runtime.StateActive {
		t.Fatalf("state = %v, want Active", got)
	}
	snap := p21Snapshot(t, rt)
	fs, ok := p21FiberSnap(t, snap, f.ID())
	if !ok || fs.ActivationID == 0 {
		t.Fatalf("active plugin missing from snapshot: ok=%v fs=%+v", ok, fs)
	}
	if len(fs.Providers) != 1 || len(fs.Effects) < 2 {
		t.Fatalf("plugin activation should own 1 provider + effect slots, got %+v", fs)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	// Returned cleanup unwinds before effect inverses (LIFO).
	want := []string{"cleanup", "undo:E"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("unwind events = %v, want %v", got, want)
	}
	snap = p21Snapshot(t, rt)
	if _, ok := p21FiberSnap(t, snap, f.ID()); ok {
		t.Fatal("Gone fiber still present in the live snapshot")
	}
	if len(snap.Providers) != 0 {
		t.Fatalf("provider residue after unwind: %d", len(snap.Providers))
	}
}

// PB-02: dependency-driven activation. A consumer with an unsatisfied
// declared dependency stays Pending (never Failed); loading the provider
// drives Loading -> Active.
func TestP21PB02DependencyDrivenActivation(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		v, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		select {
		case out <- v.Greet():
		default:
		}
		return nil, nil
	}

	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	waitState(t, cf, runtime.StatePending)
	if cf.Err() != nil {
		t.Fatalf("consumer Err = %v, want nil (unsatisfied dependency is Pending, not Failed)", cf.Err())
	}
	if len(out) != 0 {
		t.Fatal("consumer resolved before a provider existed")
	}

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %v, want Active", got)
	}
	if v := <-out; v != "hello from p" {
		t.Fatalf("consumer resolved %q, want hello from p", v)
	}
	_ = pf.Dispose()
	_ = cf.Dispose()
}

// PB-03: dependency withdrawal is Runtime-managed: Active -> Pending when the
// provider disappears (never a crash), and reloading the provider re-activates
// the consumer through a fresh activation.
func TestP21PB03DependencyWithdrawal(t *testing.T) {
	rt := newTestRuntime(t)
	out := make(chan string, 4)
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		v, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		select {
		case out <- v.Greet():
		default:
		}
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p1")
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	if v := <-out; v != "hello from p1" {
		t.Fatalf("consumer resolved %q, want hello from p1", v)
	}

	// Withdrawal: consumer must fully unwind to Pending (not Failed).
	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose: %v", err)
	}
	if err := cf.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("consumer did not end its activation: %v", err)
	}
	waitState(t, cf, runtime.StatePending)
	if cf.Err() != nil {
		t.Fatalf("consumer Err after withdrawal = %v, want nil", cf.Err())
	}
	snap := p21Snapshot(t, rt)
	if fs, ok := p21FiberSnap(t, snap, cf.ID()); !ok || fs.ActivationID != 0 {
		t.Fatalf("withdrawn consumer should have no live activation: ok=%v fs=%+v", ok, fs)
	}

	// Recovery: provider returns -> fresh consumer activation.
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p2")
		return nil, nil
	}
	if err := pf.Load(); err != nil {
		t.Fatalf("provider reload: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready after reload: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready after reload: %v", err)
	}
	if v := <-out; v != "hello from p2" {
		t.Fatalf("consumer re-resolved %q, want hello from p2", v)
	}
	_ = pf.Dispose()
	_ = cf.Dispose()
}

// PB-04: everything a Component registers (Event handler, Provider, Effect)
// is owned by its activation and fully unwound by Dispose — handler stops
// firing, provider disappears, inverses run LIFO.
func TestP21PB04EffectOwnership(t *testing.T) {
	rt := newTestRuntime(t)
	evRec := &eventRecorder{}
	undoRec := &eventRecorder{}
	_, hostCtx := p21LoadEmitter(t, rt)

	plugin := newFakeComponent("plugin")
	plugin.provide = []runtime.Capability{p21SvcKey.Capability()}
	plugin.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := runtime.Provide(ctx, p21SvcKey, "svc-v1"); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, undoRec, "E1", nil); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, undoRec, "E2", nil); err != nil {
			return nil, err
		}
		if err := runtime.On(ctx, p21EvKey, func(_ context.Context, p string) error {
			evRec.add("evt:" + p)
			return nil
		}); err != nil {
			return nil, err
		}
		return nil, nil
	}
	f, err := rt.Load(plugin)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready: %v", err)
	}

	// Positive control: while Active the handler fires and the provider exists.
	if err := runtime.Emit(hostCtx, p21EvKey, "a"); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if got := evRec.all(); !equalStrings(got, []string{"evt:a"}) {
		t.Fatalf("handler calls while active = %v, want [evt:a]", got)
	}
	snap := p21Snapshot(t, rt)
	if fs, ok := p21FiberSnap(t, snap, f.ID()); !ok || len(fs.Providers) != 1 {
		t.Fatalf("plugin provider missing while active: ok=%v", ok)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}

	wantUndo := []string{"undo:E2", "undo:E1"}
	if got := undoRec.all(); !equalStrings(got, wantUndo) {
		t.Fatalf("inverse order = %v, want %v", got, wantUndo)
	}
	// Handler MUST NOT remain registered after activation unwind.
	if err := runtime.Emit(hostCtx, p21EvKey, "b"); err != nil {
		t.Fatalf("Emit after dispose: %v", err)
	}
	if got := evRec.all(); !equalStrings(got, []string{"evt:a"}) {
		t.Fatalf("handler fired after unwind: %v", got)
	}
	snap = p21Snapshot(t, rt)
	for _, pv := range snap.Providers {
		if pv.OwnerFiberID == f.ID() {
			t.Fatalf("provider residue after unwind: %+v", pv)
		}
	}
}

// PB-05: each activation of the same Component gets a NEW Activation and a NEW
// Context; effects of the old activation never surface in the new one.
func TestP21PB05ActivationIsolation(t *testing.T) {
	rt := newTestRuntime(t)
	undoRec := &eventRecorder{}
	ctxCh := make(chan *runtime.Context, 4)
	comp := newFakeComponent("remount")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		ctxCh <- ctx
		if err := installEffect(t, ctx, undoRec, "X", nil); err != nil {
			return nil, err
		}
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready A: %v", err)
	}
	ctxA := <-ctxCh
	snapA := p21Snapshot(t, rt)
	fsA, _ := p21FiberSnap(t, snapA, f.ID())
	if fsA.ActivationID == 0 {
		t.Fatal("activation A has no ActivationID")
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose A: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone A: %v", err)
	}
	if got := undoRec.all(); !equalStrings(got, []string{"undo:X"}) {
		t.Fatalf("activation A unwind events = %v, want [undo:X]", got)
	}

	// Second activation of the same Component instance.
	if err := f.Load(); err != nil {
		t.Fatalf("Load B: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready B: %v", err)
	}
	ctxB := <-ctxCh
	snapB := p21Snapshot(t, rt)
	fsB, _ := p21FiberSnap(t, snapB, f.ID())
	if ctxA == ctxB {
		t.Fatal("activation B reused activation A's Context")
	}
	if fsB.ActivationID == fsA.ActivationID || fsB.ActivationID == 0 {
		t.Fatalf("activation ids not distinct/nonzero: A=%d B=%d", fsA.ActivationID, fsB.ActivationID)
	}
	// Activation A's effect must not be visible in B: exactly one live effect
	// slot, owned by B.
	if len(fsB.Effects) != 1 || fsB.Effects[0].ActivationID != fsB.ActivationID {
		t.Fatalf("activation B effect ownership = %+v, want single B-owned slot", fsB.Effects)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose B: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone B: %v", err)
	}
	if got := undoRec.all(); !equalStrings(got, []string{"undo:X", "undo:X"}) {
		t.Fatalf("inverse events = %v, want exactly one inverse per activation", got)
	}
}

// PB-07: Realm scope isolation. A consumer inside sibling scope B must never
// resolve the capability provided by sibling scope A (no cross-realm reads).
type p21ScopeProv struct{ tag string }

func (c *p21ScopeProv) Name() string                 { return "p21-prov:" + c.tag }
func (c *p21ScopeProv) Inject() []runtime.Dependency { return nil }
func (c *p21ScopeProv) Provide() []runtime.Capability {
	return []runtime.Capability{greeterKey.Capability()}
}
func (c *p21ScopeProv) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, greeterKey, greeter(greeterVal{tag: c.tag}))
}

type p21ScopeCons struct{ out chan string }

func (c *p21ScopeCons) Name() string { return "p21-cons" }
func (c *p21ScopeCons) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(greeterKey)}
}
func (c *p21ScopeCons) Provide() []runtime.Capability { return nil }
func (c *p21ScopeCons) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, greeterKey)
	if err != nil {
		return nil, err
	}
	c.out <- v.Greet()
	return nil, nil
}

// p21ScopeA mounts provider+consumer inside one explicit scope and hands out
// the fiber handles; p21ScopeB mounts a bare consumer in a sibling explicit
// scope.
type p21ScopeA struct {
	hs  chan *runtime.Fiber
	out chan string
}

func (c *p21ScopeA) Name() string                  { return "p21-scope-a" }
func (c *p21ScopeA) Inject() []runtime.Dependency  { return nil }
func (c *p21ScopeA) Provide() []runtime.Capability { return nil }
func (c *p21ScopeA) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	p, err := ctx.Child(&p21ScopeProv{tag: "A"})
	if err != nil {
		return nil, err
	}
	x, err := ctx.Child(&p21ScopeCons{out: c.out})
	if err != nil {
		return nil, err
	}
	c.hs <- p
	c.hs <- x
	return nil, nil
}

type p21ScopeB struct {
	hs  chan *runtime.Fiber
	out chan string
}

func (c *p21ScopeB) Name() string                  { return "p21-scope-b" }
func (c *p21ScopeB) Inject() []runtime.Dependency  { return nil }
func (c *p21ScopeB) Provide() []runtime.Capability { return nil }
func (c *p21ScopeB) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	x, err := ctx.Child(&p21ScopeCons{out: c.out})
	if err != nil {
		return nil, err
	}
	c.hs <- x
	return nil, nil
}

func TestP21PB07ScopeIsolation(t *testing.T) {
	rt := newTestRuntime(t)
	ha := make(chan *runtime.Fiber, 4)
	hb := make(chan *runtime.Fiber, 2)
	outA := make(chan string, 4)
	outB := make(chan string, 4)

	host := newFakeComponent("host")
	host.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := ctx.Child(&p21ScopeA{hs: ha, out: outA}, runtime.WithScope()); err != nil {
			return nil, err
		}
		if _, err := ctx.Child(&p21ScopeB{hs: hb, out: outB}, runtime.WithScope()); err != nil {
			return nil, err
		}
		return nil, nil
	}
	af, err := rt.Load(host)
	if err != nil {
		t.Fatalf("Load host: %v", err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("host Ready: %v", err)
	}
	_, consA := <-ha, <-ha
	consB := <-hb

	waitState(t, consA, runtime.StateActive)
	if v := <-outA; v != "hello from A" {
		t.Fatalf("scope A consumer resolved %q, want hello from A", v)
	}
	// Sibling scope B must stay Pending: scope A's capability is not readable.
	waitState(t, consB, runtime.StatePending)
	if consB.Err() != nil {
		t.Fatalf("scope B consumer Err = %v, want nil (isolation is Pending, not failure)", consB.Err())
	}
	select {
	case v := <-outB:
		t.Fatalf("scope B consumer wrongly resolved sibling A capability: %q", v)
	default:
	}
	_ = af.Dispose()
	_ = af.Gone(testTimeout(t))
}

type p21SvcCons struct{ out chan string }

func (c *p21SvcCons) Name() string { return "p21-svc-cons" }
func (c *p21SvcCons) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(p21SvcKey)}
}
func (c *p21SvcCons) Provide() []runtime.Capability { return nil }
func (c *p21SvcCons) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, p21SvcKey)
	if err != nil {
		return nil, err
	}
	c.out <- v
	return nil, nil
}

func TestP21PB08PublicApiBoundary(t *testing.T) {
	rt := newTestRuntime(t)
	evRec := &eventRecorder{}
	undoRec := &eventRecorder{}
	childH := make(chan *runtime.Fiber, 1)
	plugCtxCh := make(chan *runtime.Context, 1)
	svcOut := make(chan string, 2)
	_, hostCtx := p21LoadEmitter(t, rt)

	root := newFakeComponent("root")
	root.provide = []runtime.Capability{greeterKey.Capability()}
	root.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "root")
		return nil, nil
	}
	rf, err := rt.Load(root)
	if err != nil {
		t.Fatalf("Load root: %v", err)
	}
	if err := rf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("root Ready: %v", err)
	}

	child := newFakeComponent("child")
	child.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) { return nil, nil }

	plugin := newFakeComponent("plugin")
	plugin.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	plugin.provide = []runtime.Capability{p21SvcKey.Capability()}
	plugin.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		plugCtxCh <- ctx
		g, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		if err := runtime.Provide(ctx, p21SvcKey, g.Greet()+"+svc"); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, undoRec, "M", nil); err != nil {
			return nil, err
		}
		if err := runtime.On(ctx, p21EvKey, func(_ context.Context, p string) error {
			evRec.add("evt:" + p)
			return nil
		}); err != nil {
			return nil, err
		}
		if err := runtime.OnWaterfall(ctx, p21WFKey, func(_ context.Context, p string, next runtime.Next) error {
			evRec.add("wf:" + p)
			return next()
		}); err != nil {
			return nil, err
		}
		cf, err := ctx.Child(child)
		if err != nil {
			return nil, err
		}
		childH <- cf
		return func() error { undoRec.add("cleanup"); return nil }, nil
	}
	pf, err := rt.Load(plugin)
	if err != nil {
		t.Fatalf("Load plugin: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("plugin Ready: %v", err)
	}
	if got := pf.State(); got != runtime.StateActive {
		t.Fatalf("plugin state = %v, want Active", got)
	}
	plugCtx := <-plugCtxCh
	if plugCtx == nil {
		t.Fatal("plugin activation produced no Context")
	}

	// Consumer resolves the plugin-provided capability (dependency on plugin).
	cons := newFakeComponent("svc-cons")
	cons.inject = []runtime.Dependency{runtime.Requires(p21SvcKey)}
	cons.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		v, err := runtime.Require(ctx, p21SvcKey)
		if err != nil {
			return nil, err
		}
		svcOut <- v
		return nil, nil
	}
	consF, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load svc consumer: %v", err)
	}
	if err := consF.Ready(testTimeout(t)); err != nil {
		t.Fatalf("svc consumer Ready: %v", err)
	}
	if v := <-svcOut; v != "hello from root+svc" {
		t.Fatalf("svc consumer resolved %q, want hello from root+svc", v)
	}

	// All four dispatch strategies reach the plugin's registrations.
	if err := runtime.Emit(hostCtx, p21EvKey, "e"); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if err := runtime.Serial(context.Background(), hostCtx, p21EvKey, "s"); err != nil {
		t.Fatalf("Serial: %v", err)
	}
	if err := runtime.Waterfall(context.Background(), hostCtx, p21WFKey, "w"); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	wantEv := []string{"evt:e", "evt:s", "wf:w"}
	if got := evRec.all(); !equalStrings(got, wantEv) {
		t.Fatalf("event calls = %v, want %v", got, wantEv)
	}

	// Unload the plugin: child must be owned-Gone, provider gone, consumer
	// Pending, event registrations gone, inverses run.
	if err := pf.Dispose(); err != nil {
		t.Fatalf("plugin Dispose: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("plugin Gone: %v", err)
	}
	cf := <-childH
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("child not Gone: %v", err)
	}
	waitState(t, consF, runtime.StatePending)
	if err := runtime.Emit(hostCtx, p21EvKey, "x"); err != nil {
		t.Fatalf("Emit after dispose: %v", err)
	}
	if got := evRec.all(); !equalStrings(got, wantEv) {
		t.Fatalf("handler fired after plugin unwind: %v", got)
	}
	wantUndo := []string{"cleanup", "undo:M"}
	if got := undoRec.all(); !equalStrings(got, wantUndo) {
		t.Fatalf("plugin unwind events = %v, want %v", got, wantUndo)
	}
	snap := p21Snapshot(t, rt)
	for _, pv := range snap.Providers {
		if pv.OwnerFiberID == pf.ID() {
			t.Fatalf("plugin provider residue: %+v", pv)
		}
	}
	_ = rf.Dispose()
	_ = rf.Gone(testTimeout(t))
}

// PB-09: Child ownership. Parent withdrawal must eventually take every owned
// child to Gone.
func TestP21PB09ChildOwnership(t *testing.T) {
	rt := newTestRuntime(t)
	childH := make(chan *runtime.Fiber, 1)
	parent := newFakeComponent("parent")
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		cf, err := ctx.Child(noopComponent{name: "child"})
		if err != nil {
			return nil, err
		}
		childH <- cf
		return nil, nil
	}
	pf, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("parent Ready: %v", err)
	}
	cf := <-childH
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("child Ready: %v", err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("parent Dispose: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("parent Gone: %v", err)
	}
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("owned child not Gone after parent withdrawal: %v", err)
	}
	if got := cf.State(); got != runtime.StateGone {
		t.Fatalf("child state = %v, want Gone", got)
	}
}

// PB-10: failure unwind. Apply error/panic must not leave Effects, Providers,
// Event handlers, or Children behind.
func TestP21PB10FailureUnwind(t *testing.T) {
	t.Run("apply-error", func(t *testing.T) {
		rt := newTestRuntime(t)
		evRec := &eventRecorder{}
		undoRec := &eventRecorder{}
		childH := make(chan *runtime.Fiber, 1)
		_, hostCtx := p21LoadEmitter(t, rt)
		boom := errors.New("apply boom")

		comp := newFakeComponent("failing")
		comp.provide = []runtime.Capability{p21SvcKey.Capability()}
		comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if err := runtime.Provide(ctx, p21SvcKey, "ghost"); err != nil {
				return nil, err
			}
			if err := installEffect(t, ctx, undoRec, "A", nil); err != nil {
				return nil, err
			}
			if err := installEffect(t, ctx, undoRec, "B", nil); err != nil {
				return nil, err
			}
			if err := runtime.On(ctx, p21EvKey, func(_ context.Context, p string) error {
				evRec.add("evt")
				return nil
			}); err != nil {
				return nil, err
			}
			cf, err := ctx.Child(noopComponent{name: "child"})
			if err != nil {
				return nil, err
			}
			childH <- cf
			return nil, boom
		}
		f, err := rt.Load(comp)
		if err != nil {
			t.Fatalf("Load: %v", err)
		}
		if err := f.Ready(testTimeout(t)); !errors.Is(err, boom) {
			t.Fatalf("Ready = %v, want %v", err, boom)
		}
		if got := f.State(); got != runtime.StateFailed {
			t.Fatalf("state = %v, want Failed", got)
		}
		// LIFO unwind of the effects committed before the failure.
		if got := undoRec.all(); !equalStrings(got, []string{"undo:B", "undo:A"}) {
			t.Fatalf("inverse events = %v, want [undo:B undo:A]", got)
		}
		cf := <-childH
		if err := cf.Gone(testTimeout(t)); err != nil {
			t.Fatalf("child not Gone after failed parent: %v", err)
		}
		if err := runtime.Emit(hostCtx, p21EvKey, "x"); err != nil {
			t.Fatalf("Emit: %v", err)
		}
		if got := evRec.all(); len(got) != 0 {
			t.Fatalf("handler fired after failed apply: %v", got)
		}
		snap := p21Snapshot(t, rt)
		for _, pv := range snap.Providers {
			if pv.OwnerFiberID == f.ID() {
				t.Fatalf("provider residue after failed apply: %+v", pv)
			}
		}
	})

	t.Run("apply-panic", func(t *testing.T) {
		rt := newTestRuntime(t)
		undoRec := &eventRecorder{}
		childH := make(chan *runtime.Fiber, 1)
		_, hostCtx := p21LoadEmitter(t, rt)

		comp := newFakeComponent("panicking")
		comp.provide = []runtime.Capability{p21SvcKey.Capability()}
		comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if err := runtime.Provide(ctx, p21SvcKey, "ghost"); err != nil {
				return nil, err
			}
			if err := installEffect(t, ctx, undoRec, "P", nil); err != nil {
				return nil, err
			}
			if err := runtime.On(ctx, p21EvKey, func(_ context.Context, p string) error { return nil }); err != nil {
				return nil, err
			}
			if _, err := ctx.Child(noopComponent{name: "child"}); err != nil {
				return nil, err
			}
			childH <- nil
			panic("boom in apply")
		}
		f, err := rt.Load(comp)
		if err != nil {
			t.Fatalf("Load: %v", err)
		}
		if err := f.Ready(testTimeout(t)); !errors.Is(err, runtime.ErrComponentApplyPanic) {
			t.Fatalf("Ready = %v, want ErrComponentApplyPanic", err)
		}
		if got := f.State(); got != runtime.StateFailed {
			t.Fatalf("state = %v, want Failed", got)
		}
		if got := undoRec.all(); !equalStrings(got, []string{"undo:P"}) {
			t.Fatalf("inverse events = %v, want [undo:P]", got)
		}
		if err := runtime.Emit(hostCtx, p21EvKey, "x"); err != nil {
			t.Fatalf("Emit: %v", err)
		}
		snap := p21Snapshot(t, rt)
		for _, pv := range snap.Providers {
			if pv.OwnerFiberID == f.ID() {
				t.Fatalf("provider residue after apply panic: %+v", pv)
			}
		}
	})
}
