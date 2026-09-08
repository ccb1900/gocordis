package event_test

import (
	"context"
	. "dynamic-runtime/extensions/event"
	"dynamic-runtime/runtime"
	"errors"
	"strings"
	"sync"
	"testing"
	"time"
)

// P1.1 Emit conformance (E-01..E-13 of the P1.1 Emit Implementation
// Specification) plus the D4 emitter-path scope semantics frozen in the P1
// Event Decision Record v0.2.
//
// Test layout: components register handlers inside their own Apply; the test
// goroutine emits from a live activation Context (a Context captured while the
// emitting fiber is Active). Registrations therefore always happen inside the
// owner activation's Apply, exactly as frozen.

var (
	p1IntKey    = NewEventKey[int]("p1.int")
	p1SameInt   = NewEventKey[int]("p1.same")
	p1SameStr   = NewEventKey[string]("p1.same")
	p1CancelKey = NewEventKey[p1CancelPayload]("p1.cancel")
)

type p1CancelPayload struct {
	cancel func()
}

// p1Rec is a test-side, concurrency-safe invocation recorder.
type p1Rec struct {
	mu    sync.Mutex
	calls []string
}

func (r *p1Rec) add(s string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.calls = append(r.calls, s)
}

func (r *p1Rec) got() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.calls...)
}

func p1Join(got []string) string { return strings.Join(got, ",") }

// p1Registrar is a Component whose Apply installs a caller-supplied list of
// handler registrations and captures its activation Context for the test.
type p1Registrar struct {
	name    string
	rec     *p1Rec
	on      []func(*Context) error
	ctx     *Context
	selfCtx *Context
}

func (c *p1Registrar) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1Registrar) Name() string          { return c.name }
func (c *p1Registrar) Inject() []Dependency  { return nil }
func (c *p1Registrar) Provide() []Capability { return nil }
func (c *p1Registrar) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	c.ctx = ctx
	for _, f := range c.on {
		if err := f(ctx); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func p1Runtime(t *testing.T) *Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New(): %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

func p1Timeout(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func p1Ready(t *testing.T, rt *Runtime, comp Component) *Fiber {
	t.Helper()
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load(%s): %v", comp.Name(), err)
	}
	if err := f.Ready(p1Timeout(t)); err != nil {
		t.Fatalf("%s not ready: %v", f.Name(), err)
	}
	return f
}

// p1CtxHolder is implemented by every migrated fixture component: Apply
// publishes its own activation context (public surface — no kernel internals).
type p1CtxHolder interface{ p1PublishedCtx() *Context }

// p1Ctx returns the live activation context the fiber's component published
// during Apply. Replaces the old internal f.activation.ctx read.
func p1Ctx(f *Fiber) *Context {
	if h, ok := f.Component().(p1CtxHolder); ok {
		return h.p1PublishedCtx()
	}
	return nil
}

// p1Grab is a component whose Apply publishes its activation context on ch.
// Mounted where a test needs to emit "from" a realm.
type p1Grab struct {
	name    string
	out     chan *Context
	selfCtx *Context
}

func (c *p1Grab) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1Grab) Name() string          { return c.name }
func (c *p1Grab) Inject() []Dependency  { return nil }
func (c *p1Grab) Provide() []Capability { return nil }
func (c *p1Grab) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	c.out <- ctx
	return nil, nil
}

func p1WaitState(t *testing.T, f *Fiber, want FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("fiber %s state = %v, want %v", f.Name(), f.State(), want)
}

// E-01 + E-05: typed EventKey registration and exact matching. Event identity
// is (payload type, name): same name with a different payload type is a
// different Event; a different name never matches.
func TestP1EmitTypedIdentityAndMatching(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("int"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1SameInt, func(_ context.Context, p int) error { rec.add("same-int"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1SameStr, func(_ context.Context, s string) error { rec.add("same-str"); return nil })
			},
		},
	})
	c := p1Ctx(r)

	if err := Emit(c, p1IntKey, 7); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if got := p1Join(rec.got()); got != "int" {
		t.Fatalf("emit p1.int calls = %q, want %q", got, "int")
	}
	rec.calls = nil
	if err := Emit(c, p1SameInt, 1); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if got := p1Join(rec.got()); got != "same-int" {
		t.Fatalf("same name, int payload -> %q, want only the int handler", got)
	}
	rec.calls = nil
	if err := Emit(c, p1SameStr, "x"); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if got := p1Join(rec.got()); got != "same-str" {
		t.Fatalf("same name, string payload -> %q, want only the string handler", got)
	}
}

// E-02/E-04/E-03: a registration is an Effect owned by (FiberID,
// ActivationID); unwinding removes the handler and leaves no registry residue.
func TestP1EmitEffectOwnedRegistrationAndUnwindRemoval(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("r"); return nil })
			},
		},
	})
	x := p1Ready(t, rt, &p1Registrar{name: "X", rec: rec}) // root-realm emitter, no handlers

	// E-02: the registration is a committed Event-kind Effect slot, observed
	// through the public snapshot.
	snap, err := rt.Snapshot(p1Timeout(t))
	if err != nil {
		t.Fatalf("snapshot: %v", err)
	}
	var eventSlots int
	for _, fr := range snap.Fibers {
		if fr.ID != r.ID() {
			continue
		}
		for _, ev := range fr.Effects {
			if ev.Kind == runtime.EffectKindEvent && ev.State == "Committed" {
				eventSlots++
			}
		}
	}
	if eventSlots != 1 {
		t.Fatalf("Event-kind committed effects = %d, want 1", eventSlots)
	}

	// E-04: the registry entry traces to the owner activation, observed
	// through the public read model.
	ctxX := p1Ctx(x)
	bindings := ctxX.EventBindings(p1IntKey.ID())
	if len(bindings) != 1 {
		t.Fatalf("registry entries = %d, want 1", len(bindings))
	}
	if bindings[0].Owner.FiberID != r.ID() {
		t.Fatalf("owner fiber = %d, want %d", bindings[0].Owner.FiberID, r.ID())
	}

	// While R is Active its handler is visible from X's realm path (root realm).
	if err := Emit(ctxX, p1IntKey, 1); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if got := p1Join(rec.got()); got != "r" {
		t.Fatalf("pre-unwind emit calls = %q, want %q", got, "r")
	}
	rec.calls = nil

	// E-03: after R unwinds the handler is gone and the registry has no residue.
	if err := r.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := r.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("R not gone: %v", err)
	}
	if err := Emit(ctxX, p1IntKey, 2); err != nil {
		t.Fatalf("Emit after unwind: %v", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("post-unwind emit calls = %q, want none", got)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "emit-residue-1"), p1IntKey.ID())
}

// E-06: handler execution order is deterministic and registration-ordered,
// independent of map iteration.
func TestP1EmitDeterministicOrdering(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	tags := []string{"h1", "h2", "h3", "h4", "h5"}
	var on []func(*Context) error
	for _, tag := range tags {
		tag := tag
		on = append(on, func(ctx *Context) error {
			return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add(tag); return nil })
		})
	}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec, on: on})
	c := p1Ctx(r)
	for i := 0; i < 3; i++ {
		rec.calls = nil
		if err := Emit(c, p1IntKey, i); err != nil {
			t.Fatalf("Emit #%d: %v", i, err)
		}
		if got := p1Join(rec.got()); got != "h1,h2,h3,h4,h5" {
			t.Fatalf("emit #%d order = %q, want h1,h2,h3,h4,h5", i, got)
		}
	}
}

// E-09: Emit dispatches a stable snapshot; registrations performed inside a
// handler never change the current batch and are visible from the next Emit.
func TestP1EmitDispatchSnapshotIsolation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("h1")
					return On(reg.ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h3"); return nil })
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h2"); return nil })
			},
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)

	if err := Emit(c, p1IntKey, 1); err != nil {
		t.Fatalf("Emit #1: %v", err)
	}
	if got := p1Join(rec.got()); got != "h1,h2" {
		t.Fatalf("dispatch #1 calls = %q, want h1,h2 (h3 registered mid-dispatch must not join)", got)
	}
	rec.calls = nil

	if err := Emit(c, p1IntKey, 2); err != nil {
		t.Fatalf("Emit #2: %v", err)
	}
	if got := p1Join(rec.got()); got != "h1,h2,h3" {
		t.Fatalf("dispatch #2 calls = %q, want h1,h2,h3", got)
	}
}

// E-10: one handler's error (or panic) never stops later handlers; errors are
// aggregated with errors.Join; a handler panic is contained as a handler error.
func TestP1EmitErrorAggregationAndPanicIsolation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	errA := errors.New("err-a")
	errB := errors.New("err-b")
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("e1"); return errA })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("panic")
					panic("boom")
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("ok"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("e2"); return errB })
			},
		},
	})
	c := p1Ctx(r)
	err := Emit(c, p1IntKey, 1)
	if err == nil {
		t.Fatal("Emit returned nil, want joined handler errors")
	}
	if !errors.Is(err, errA) || !errors.Is(err, errB) {
		t.Fatalf("Emit error = %v, want aggregation of errA and errB", err)
	}
	if !errors.Is(err, ErrEventHandlerPanic) {
		t.Fatalf("Emit error = %v, want contained ErrEventHandlerPanic", err)
	}
	if got := p1Join(rec.got()); got != "e1,panic,ok,e2" {
		t.Fatalf("dispatch calls = %q, want all four handlers in order", got)
	}
}

// E-07/E-08: Emit is synchronous and never runs handlers concurrently: when
// Emit returns every handler it invoked has completed, and two sleeping
// handlers cannot overlap.
func TestP1EmitSynchronousSequentialDispatch(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					time.Sleep(40 * time.Millisecond)
					rec.add("s1")
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					time.Sleep(40 * time.Millisecond)
					rec.add("s2")
					return nil
				})
			},
		},
	})
	c := p1Ctx(r)
	start := time.Now()
	if err := Emit(c, p1IntKey, 1); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	elapsed := time.Since(start)
	if got := p1Join(rec.got()); got != "s1,s2" {
		t.Fatalf("dispatch calls = %q, want s1,s2", got)
	}
	if elapsed < 65*time.Millisecond {
		t.Fatalf("Emit returned in %v; two 40ms handlers must run sequentially (>= 65ms)", elapsed)
	}
}

// E-11: emitter cancellation is cooperative. A canceled emitter stops further
// dispatch before the next handler and Emit returns ctx.Err(); a handler that
// is already running is never force-terminated.
func TestP1EmitCancellationStopsFurtherDispatch(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	x := p1Ready(t, rt, &p1Registrar{name: "X", rec: rec}) // emitter fiber
	ctxX := p1Ctx(x)
	p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1CancelKey, func(_ context.Context, p p1CancelPayload) error {
					p.cancel()
					rec.add("c1")
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1CancelKey, func(_ context.Context, p p1CancelPayload) error { rec.add("c2"); return nil })
			},
		},
	})

	// c1 runs first and cancels the emitter mid-dispatch; c2 must be skipped.
	err := Emit(ctxX, p1CancelKey, p1CancelPayload{cancel: func() { ctxX.Cancel() }})
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("Emit error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "c1" {
		t.Fatalf("dispatch calls = %q, want c1 (c2 skipped after cancellation)", got)
	}

	// A pre-canceled emitter dispatches nothing.
	rec.calls = nil
	if err := Emit(ctxX, p1CancelKey, p1CancelPayload{}); !errors.Is(err, context.Canceled) {
		t.Fatalf("pre-canceled Emit error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("pre-canceled dispatch calls = %q, want none", got)
	}
}

// E-12: Emit never changes Runtime lifecycle / dependency / provider semantics
// and never emits canonical Runtime events of its own.
func TestP1EmitNoLifecycleAuthorityLeak(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("r"); return nil })
			},
		},
	})
	c := p1Ctx(r)
	before, err := rt.Snapshot(p1Timeout(t))
	if err != nil {
		t.Fatalf("before snapshot: %v", err)
	}

	if err := Emit(c, p1IntKey, 1); err != nil {
		t.Fatalf("Emit: %v", err)
	}

	after, err := rt.Snapshot(p1Timeout(t))
	if err != nil {
		t.Fatalf("after snapshot: %v", err)
	}
	if after.EventSequence != before.EventSequence {
		t.Fatalf("Emit emitted canonical event(s); event sequence %d -> %d", before.EventSequence, after.EventSequence)
	}
	if len(after.Fibers) != len(before.Fibers) {
		t.Fatalf("Emit changed fiber count %d -> %d", len(before.Fibers), len(after.Fibers))
	}
	beforeStates := map[FiberID]string{}
	for _, f := range before.Fibers {
		beforeStates[f.ID] = string(f.State)
	}
	for _, f := range after.Fibers {
		if st := string(f.State); st != beforeStates[f.ID] {
			t.Fatalf("fiber %v state changed by Emit: %v -> %v", f.ID, beforeStates[f.ID], st)
		}
	}
	if len(after.Providers) != len(before.Providers) || len(after.Dependencies) != len(before.Dependencies) {
		t.Fatalf("Emit changed provider/dependency rows")
	}
}

// E-01 guard rails: nil Context / nil handler / zero-value EventKey are
// rejected and never create registry entries.
func TestP1EmitAndOnGuardRails(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec})
	c := p1Ctx(r)
	var zeroKey runtime.EventKey[int]

	if err := On[int](nil, p1IntKey, func(_ context.Context, p int) error { return nil }); err == nil {
		t.Fatal("On(nil ctx) returned nil")
	}
	if err := On(c, zeroKey, func(_ context.Context, p int) error { return nil }); err == nil {
		t.Fatal("On(zero key) returned nil")
	}
	if err := On[int](c, p1IntKey, nil); err == nil {
		t.Fatal("On(nil handler) returned nil")
	}
	if err := Emit[int](nil, p1IntKey, 1); err == nil {
		t.Fatal("Emit(nil ctx) returned nil")
	}
	if err := Emit(c, zeroKey, 1); err == nil {
		t.Fatal("Emit(zero key) returned nil")
	}
	p1AssertNoBindings(t, c, p1IntKey.ID())
}

// D4 scope semantics: emitter-path matching. Handlers registered in an
// explicit scope are visible to that scope and its descendants only; root
// handlers are global; siblings are isolated; matching is additive (no
// shadowing); execution order is the global registration order.
func TestP1EmitScopeMatching(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	hs := make(chan *Fiber, 8)
	host := p1Ready(t, rt, &p1ScopeHost{rec: rec, hs: hs})
	p1WaitState(t, host, StateActive)

	// Child Apply goroutines push handles concurrently, so arrival order is not
	// deterministic; classify by Component name instead of receive order.
	handles := map[string]*Fiber{}
	deadline := time.After(8 * time.Second)
	for len(handles) < 3 {
		select {
		case f := <-hs:
			handles[f.Name()] = f
		case <-deadline:
			t.Fatalf("scope children not delivered: got %d/3", len(handles))
		}
	}
	a := handles["scope-a"]  // scoped child A (realm RA): registers "a" and mounts GA
	b := handles["leaf:b"]   // scoped sibling B (realm RB): registers "b"
	ga := handles["leaf:ga"] // A's scoped child (realm RA1 below RA): registers "ga"
	p1WaitState(t, a, StateActive)
	p1WaitState(t, b, StateActive)
	p1WaitState(t, ga, StateActive)

	ctxRoot := p1Ctx(host)
	ctxA := p1Ctx(a)
	ctxB := p1Ctx(b)
	ctxGA := p1Ctx(ga)

	// Emit from the host (root realm): only the root handler is visible.
	rec.calls = nil
	if err := Emit(ctxRoot, p1IntKey, 1); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root" {
		t.Fatalf("root emit calls = %q, want root", got)
	}

	// Emit from A: root handler (global) + A's own handler; root registered
	// before A, so order is root,a (no shadowing, additive).
	rec.calls = nil
	if err := Emit(ctxA, p1IntKey, 2); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root,a" {
		t.Fatalf("A emit calls = %q, want root,a", got)
	}

	// Sibling B is isolated from A (and vice versa), but sees the root handler.
	rec.calls = nil
	if err := Emit(ctxB, p1IntKey, 3); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root,b" {
		t.Fatalf("B emit calls = %q, want root,b", got)
	}

	// Emit from GA (descendant of RA): ancestor A handler and root handler both
	// participate; global registration order root,a,ga.
	rec.calls = nil
	if err := Emit(ctxGA, p1IntKey, 4); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root,a,ga" {
		t.Fatalf("GA emit calls = %q, want root,a,ga", got)
	}
}

// p1Leaf registers one named handler in its own Apply.
type p1Leaf struct {
	tag     string
	rec     *p1Rec
	selfCtx *Context
}

func (c *p1Leaf) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1Leaf) Name() string          { return "leaf:" + c.tag }
func (c *p1Leaf) Inject() []Dependency  { return nil }
func (c *p1Leaf) Provide() []Capability { return nil }
func (c *p1Leaf) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	return nil, On(ctx, p1IntKey, func(_ context.Context, p int) error { c.rec.add(c.tag); return nil })
}

// p1ScopeA registers handler "a" (realm RA) and mounts a scoped child GA.
type p1ScopeA struct {
	rec     *p1Rec
	hs      chan *Fiber
	selfCtx *Context
}

func (c *p1ScopeA) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1ScopeA) Name() string          { return "scope-a" }
func (c *p1ScopeA) Inject() []Dependency  { return nil }
func (c *p1ScopeA) Provide() []Capability { return nil }
func (c *p1ScopeA) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := On(ctx, p1IntKey, func(_ context.Context, p int) error { c.rec.add("a"); return nil }); err != nil {
		return nil, err
	}
	g, err := ctx.Child(&p1Leaf{tag: "ga", rec: c.rec}, WithScope())
	if err != nil {
		return nil, err
	}
	c.hs <- g
	return nil, nil
}

// p1ScopeHost registers the global (root realm) handler and mounts two
// explicit-scope siblings A and B.
type p1ScopeHost struct {
	rec     *p1Rec
	hs      chan *Fiber
	selfCtx *Context
}

func (c *p1ScopeHost) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1ScopeHost) Name() string          { return "scope-host" }
func (c *p1ScopeHost) Inject() []Dependency  { return nil }
func (c *p1ScopeHost) Provide() []Capability { return nil }
func (c *p1ScopeHost) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := On(ctx, p1IntKey, func(_ context.Context, p int) error { c.rec.add("root"); return nil }); err != nil {
		return nil, err
	}
	a, err := ctx.Child(&p1ScopeA{rec: c.rec, hs: c.hs}, WithScope())
	if err != nil {
		return nil, err
	}
	b, err := ctx.Child(&p1Leaf{tag: "b", rec: c.rec}, WithScope())
	if err != nil {
		return nil, err
	}
	c.hs <- a
	c.hs <- b
	return nil, nil
}
