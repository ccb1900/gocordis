package event_test

import (
	"context"
	. "dynamic-runtime/extensions/event"
	"dynamic-runtime/runtime"
	"errors"
	"fmt"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"
)

// P1.4 Waterfall conformance (W-01..W-20 of the P1.4 Waterfall Event Runtime
// Specification v0.1).
//
// Waterfall is the fourth dispatch strategy over the SAME Kernel Event
// registry as Emit/Serial/Parallel: chain-aware handlers are installed with
// OnWaterfall (Effect-owned, §20) and receive a Next continuation; Waterfall
// then walks the registration-order snapshot as a synchronous ordered
// middleware chain (§4). Plain On handlers reached by a Waterfall dispatch run
// as transparent nodes (they hold no chain authority and cannot veto).
//
// Chain expectations below assert exact invocation order — Waterfall is fully
// synchronous and deterministic; there are no goroutines inside a chain.

var (
	p1WFKey2 = NewEventKey[int]("p1.waterfall.2")
	p1WFKey3 = NewEventKey[int]("p1.waterfall.3")
)

func wfRec(tag string, rec *p1Rec) func(context.Context, int, Next) error {
	return func(_ context.Context, p int, next Next) error {
		rec.add(tag)
		return next()
	}
}

// W-01: basic chain A -> next -> B -> next -> C runs every handler exactly
// once, in snapshot order.
func TestP1WaterfallBasicChain(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("A", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("C")
					return next()
				})
			},
		},
	})
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 7); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "A,B,C" {
		t.Fatalf("chain calls = %q, want A,B,C", got)
	}
}

// W-02: before/after around next — the core around-middleware shape:
//
//	A before → B before → C → B after → A after
func TestP1WaterfallBeforeAfter(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A-before")
					if err := next(); err != nil {
						return err
					}
					rec.add("A-after")
					return nil
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("B-before")
					if err := next(); err != nil {
						return err
					}
					rec.add("B-after")
					return nil
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("C")
					return nil
				})
			},
		},
	})
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "A-before,B-before,C,B-after,A-after" {
		t.Fatalf("chain calls = %q, want A-before,B-before,C,B-after,A-after", got)
	}
}

// W-03: chain order is the snapshot registration order (A B C registered ->
// A -> B -> C executed). Registering in the reverse order yields the reverse
// chain: the chain never depends on map/handler identity iteration.
func TestP1WaterfallRegistrationOrdering(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("A", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	})
	c := p1Ctx(r)
	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "A,B,C" {
		t.Fatalf("in-order chain = %q, want A,B,C", got)
	}

	// Reverse registration: C B A -> chain C, B, A.
	rt3 := p1Runtime(t)
	rec3 := &p1Rec{}
	r3 := p1Ready(t, rt3, &p1Registrar{
		name: "R3",
		rec:  rec3,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec3)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec3)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("A", rec3)) },
		},
	})
	c3 := p1Ctx(r3)
	if err := Waterfall(context.Background(), c3, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall (reversed): %v", err)
	}
	if got := p1Join(rec3.got()); got != "C,B,A" {
		t.Fatalf("reversed chain = %q, want C,B,A", got)
	}
}

// W-04/T-06: next() is synchronous. A's code after next() runs only after B
// fully completed — never "A before, A after, B" (an async pipeline would
// produce that order).
func TestP1WaterfallNextIsSynchronous(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A-before")
					if err := next(); err != nil {
						return err
					}
					rec.add("A-after")
					return nil
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("B")
					return nil
				})
			},
		},
	})
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "A-before,B,A-after" {
		t.Fatalf("chain calls = %q, want A-before,B,A-after", got)
	}
}

// W-05: short circuit. A does not call next(): B/C never run and the dispatch
// succeeds (short circuit is handler control flow, not an error).
func TestP1WaterfallShortCircuit(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A")
					return nil
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	})
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall (short-circuited) returned %v, want nil", err)
	}
	if got := p1Join(rec.got()); got != "A" {
		t.Fatalf("chain calls = %q, want A only", got)
	}
}

// W-06: error propagation along the chain. C returns an error; B observes it
// through next() and returns it; A observes it through next() and returns it;
// the caller receives the C error.
func TestP1WaterfallErrorPropagation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	errC := errors.New("c failed")
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A-before")
					err := next()
					rec.add("A-after")
					return err
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("B-before")
					err := next()
					rec.add("B-after")
					return err
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("C")
					return errC
				})
			},
		},
	})
	c := p1Ctx(r)

	err := Waterfall(context.Background(), c, p1IntKey, 1)
	if !errors.Is(err, errC) {
		t.Fatalf("Waterfall error = %v, want errC", err)
	}
	if got := p1Join(rec.got()); got != "A-before,B-before,C,B-after,A-after" {
		t.Fatalf("chain calls = %q, want A-before,B-before,C,B-after,A-after", got)
	}
}

// W-07: a handler that returns an error without calling next() fails the
// dispatch; downstream C never starts.
func TestP1WaterfallErrorShortCircuit(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	errB := errors.New("b failed")
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A-before")
					err := next()
					rec.add("A-after")
					return err
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("B")
					return errB
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	})
	c := p1Ctx(r)

	err := Waterfall(context.Background(), c, p1IntKey, 1)
	if !errors.Is(err, errB) {
		t.Fatalf("Waterfall error = %v, want errB", err)
	}
	if got := p1Join(rec.got()); got != "A-before,B,A-after" {
		t.Fatalf("chain calls = %q, want A-before,B,A-after (C must not run)", got)
	}
}

// W-08: a handler may transform the downstream error before returning it.
func TestP1WaterfallErrorTransformation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	errC := errors.New("c failed")
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("A", rec)) },
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("B")
					if err := next(); err != nil {
						return fmt.Errorf("B wrapped: %w", err)
					}
					return nil
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("C")
					return errC
				})
			},
		},
	})
	c := p1Ctx(r)

	err := Waterfall(context.Background(), c, p1IntKey, 1)
	if !errors.Is(err, errC) {
		t.Fatalf("Waterfall error = %v, want wrapped errC", err)
	}
	if !strings.Contains(err.Error(), "B wrapped") {
		t.Fatalf("Waterfall error = %q, want B-wrap marker", err.Error())
	}
	if got := p1Join(rec.got()); got != "A,B,C" {
		t.Fatalf("chain calls = %q, want A,B,C", got)
	}
}

// W-09/T-07: a node's next() advances the chain at most once. The second call
// is a Handler contract violation: it never re-runs downstream and returns
// ErrWaterfallNextTwice.
func TestP1WaterfallDuplicateNext(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A-first")
					if err := next(); err != nil {
						return err
					}
					rec.add("A-second")
					return next() // duplicate: contract violation
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	})
	c := p1Ctx(r)

	err := Waterfall(context.Background(), c, p1IntKey, 1)
	if !errors.Is(err, ErrWaterfallNextTwice) {
		t.Fatalf("Waterfall error = %v, want ErrWaterfallNextTwice", err)
	}
	// Downstream B/C ran exactly once, inside the FIRST next() only.
	if got := p1Join(rec.got()); got != "A-first,B,C,A-second" {
		t.Fatalf("chain calls = %q, want A-first,B,C,A-second (single downstream run)", got)
	}

	// The violation surfaces even when the handler ignores the second error
	// (isolated runtime so no other registrations share the key).
	rt2 := p1Runtime(t)
	rec2 := &p1Rec{}
	r2 := p1Ready(t, rt2, &p1Registrar{
		name: "R2",
		rec:  rec2,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec2.add("A")
					_ = next()
					_ = next() // ignored violation
					return nil
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec2)) },
		},
	})
	c2 := p1Ctx(r2)
	err = Waterfall(context.Background(), c2, p1IntKey, 1)
	if !errors.Is(err, ErrWaterfallNextTwice) {
		t.Fatalf("ignored duplicate next: Waterfall error = %v, want ErrWaterfallNextTwice", err)
	}
	// Downstream ran exactly once, inside the first next().
	if got := p1Join(rec2.got()); got != "A,B" {
		t.Fatalf("isolated chain calls = %q, want A,B", got)
	}
}

// W-10: cancellation. Once the dispatch context is canceled, next() must not
// start downstream B; the running handler is never force-terminated and the
// cancellation is reported as ctx.Err() — never conflated with a short
// circuit.
func TestP1WaterfallCancellation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	dctx, cancel := context.WithCancel(context.Background())
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A-before")
					cancel() // A triggers cancellation, then tries to continue
					err := next()
					rec.add("A-after")
					return err
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
		},
	})
	c := p1Ctx(r)

	// Case 1: A cancels the dispatch context, then calls next(): next() must
	// not start B; A itself is not force-terminated (it records A-after) and
	// the cancellation is reported as ctx.Err().
	err := Waterfall(dctx, c, p1IntKey, 1)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("canceled dispatch error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "A-before,A-after" {
		t.Fatalf("chain calls = %q, want A-before,A-after (B must not start)", got)
	}

	// Case 2: dispatch starts on an already-canceled context: no handler runs.
	rec.calls = nil
	canceled, cancel2 := context.WithCancel(context.Background())
	cancel2()
	if err := Waterfall(canceled, c, p1IntKey, 2); !errors.Is(err, context.Canceled) {
		t.Fatalf("pre-canceled dispatch error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("pre-canceled chain calls = %q, want none", got)
	}
}

// W-11/T-04: a registration made during a dispatch belongs to the NEXT
// snapshot only; the current chain keeps [A, B, C].
func TestP1WaterfallSnapshotRegistration(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(dctx context.Context, p int, next Next) error {
					rec.add("A")
					if p == 1 {
						// Register D mid-dispatch (same owner activation).
						if err := OnWaterfall(reg.ctx, p1IntKey, wfRec("D", rec)); err != nil {
							return err
						}
					}
					return next()
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall #1: %v", err)
	}
	if got := p1Join(rec.got()); got != "A,B,C" {
		t.Fatalf("dispatch #1 calls = %q, want A,B,C", got)
	}
	rec.calls = nil
	if err := Waterfall(context.Background(), c, p1IntKey, 2); err != nil {
		t.Fatalf("Waterfall #2: %v", err)
	}
	if got := p1Join(rec.got()); got != "A,B,C,D" {
		t.Fatalf("dispatch #2 calls = %q, want A,B,C,D", got)
	}
}

// W-12: unregistration during a dispatch never changes the current snapshot —
// B (owner disposed mid-chain) still executes — and B is gone from the next
// Waterfall.
func TestP1WaterfallSnapshotUnregistration(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	killer := &p1WFKiller{rec: rec}
	f2 := p1Ready(t, rt, killer) // registers "kill" first (registration seq 1)
	f3 := p1Ready(t, rt, &p1Registrar{
		name: "victim",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("victim", rec)) },
		},
	})
	killer.victim = f3
	c := p1Ctx(f2)

	// First Waterfall: kill disposes the victim fiber and waits for Gone
	// mid-dispatch; the victim handler is in the dispatch snapshot and still
	// executes.
	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall #1: %v", err)
	}
	if got := p1Join(rec.got()); got != "kill,victim" {
		t.Fatalf("dispatch #1 calls = %q, want kill,victim", got)
	}
	rec.calls = nil

	// Second Waterfall: the victim handler is gone.
	if err := Waterfall(context.Background(), c, p1IntKey, 2); err != nil {
		t.Fatalf("Waterfall #2: %v", err)
	}
	if got := p1Join(rec.got()); got != "kill" {
		t.Fatalf("dispatch #2 calls = %q, want kill only", got)
	}

	if err := f2.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f2.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("killer not gone: %v", err)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "wf-residue"), p1IntKey.ID(), p1WFKey2.ID(), p1WFKey3.ID())
}

// W-13/T-03: Waterfall inherits the Realm scope model — ancestor + current
// visible, sibling invisible, chain order = registration order.
func TestP1WaterfallScopeVisibility(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	hs := make(chan *Fiber, 8)
	host := p1Ready(t, rt, &p1WFHost{rec: rec, hs: hs})
	p1WaitState(t, host, StateActive)

	handles := map[string]*Fiber{}
	deadline := time.After(8 * time.Second)
	for len(handles) < 4 {
		select {
		case f := <-hs:
			handles[f.Name()] = f
		case <-deadline:
			t.Fatalf("scope children not delivered: got %d/4", len(handles))
		}
	}
	a := handles["wf-leaf:a"]  // sibling scope A
	x := handles["wf-x"]       // sibling scope X
	b := handles["wf-leaf:b"]  // X member
	cc := handles["wf-leaf:c"] // X member
	for _, f := range []*Fiber{a, x, b, cc} {
		p1WaitState(t, f, StateActive)
	}
	ctxRoot := p1Ctx(host)
	ctxA := p1Ctx(a)
	ctxX := p1Ctx(x)

	rec.calls = nil
	if err := Waterfall(context.Background(), ctxRoot, p1IntKey, 1); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root" {
		t.Fatalf("root dispatch calls = %q, want root", got)
	}

	rec.calls = nil
	if err := Waterfall(context.Background(), ctxA, p1IntKey, 2); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root,a" {
		t.Fatalf("A dispatch calls = %q, want root,a", got)
	}

	rec.calls = nil
	if err := Waterfall(context.Background(), ctxX, p1IntKey, 3); err != nil {
		t.Fatal(err)
	}
	got := rec.got()
	if len(got) != 4 || got[0] != "root" || got[1] != "x" {
		t.Fatalf("scope dispatch calls = %q, want root,x,{b,c}", p1Join(got))
	}
	rest := append([]string(nil), got[2:]...)
	sort.Strings(rest)
	if p1Join(rest) != "b,c" {
		t.Fatalf("scope dispatch tail = %q, want b,c", p1Join(rest))
	}
}

// W-14/T-01/T-02: Effect disposal removes a Waterfall handler; the disposed
// handler never runs again and leaves no registry residue.
func TestP1WaterfallEffectDisposal(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	x := p1Ready(t, rt, &p1Registrar{name: "X", rec: rec}) // emitter, no handlers
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("h", rec)) },
		},
	})
	ctxX := p1Ctx(x)

	if err := Waterfall(context.Background(), ctxX, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "h" {
		t.Fatalf("pre-unwind calls = %q, want h", got)
	}
	p1AssertBindings(t, ctxX, p1IntKey.ID(), 1)
	rec.calls = nil

	if err := r.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := r.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("R not gone: %v", err)
	}
	if err := Waterfall(context.Background(), ctxX, p1IntKey, 2); err != nil {
		t.Fatalf("Waterfall after unwind: %v", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("post-unwind calls = %q, want none", got)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "wf-residue"), p1IntKey.ID(), p1WFKey2.ID(), p1WFKey3.ID())
}

// W-15/T-09: reentrant Waterfall. A handler dispatches a second event; the
// inner dispatch completes before the outer chain continues.
func TestP1WaterfallReentrant(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(dctx context.Context, p int, next Next) error {
					rec.add("A")
					if err := Waterfall(dctx, reg.ctx, p1WFKey2, 0); err != nil {
						return err
					}
					return next()
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1WFKey2, wfRec("X", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1WFKey2, wfRec("Y", rec)) },
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "A,X,Y,B" {
		t.Fatalf("reentrant chain calls = %q, want A,X,Y,B", got)
	}
}

// W-16: nested Waterfall one level deeper. Outer event1 A -> B with event2
// (X -> Y) nested inside A, and event3 (P -> Q) nested inside X. Inner
// dispatches complete fully before the outer chain resumes.
func TestP1WaterfallNested(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(dctx context.Context, p int, next Next) error {
					rec.add("A")
					if err := Waterfall(dctx, reg.ctx, p1WFKey2, 0); err != nil {
						return err
					}
					return next()
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1WFKey2, func(dctx context.Context, p int, next Next) error {
					rec.add("X")
					if err := Waterfall(dctx, reg.ctx, p1WFKey3, 0); err != nil {
						return err
					}
					return next()
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1WFKey2, wfRec("Y", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1WFKey3, wfRec("P", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1WFKey3, wfRec("Q", rec)) },
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "A,X,P,Q,Y,B" {
		t.Fatalf("nested chain calls = %q, want A,X,P,Q,Y,B", got)
	}
}

// W-17: a panicking handler is contained by the unified Event panic policy
// (ErrEventHandlerPanic) and the error follows chain propagation; Registry,
// Activation, and future dispatches survive.
func TestP1WaterfallPanicContained(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("A")
					if err := next(); err != nil {
						return err
					}
					rec.add("A-after")
					return nil
				})
			},
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("B")
					panic("boom")
				})
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	})
	c := p1Ctx(r)

	for i := 0; i < 3; i++ {
		rec.calls = nil
		err := Waterfall(context.Background(), c, p1IntKey, i)
		if !errors.Is(err, ErrEventHandlerPanic) {
			t.Fatalf("Waterfall #%d error = %v, want ErrEventHandlerPanic", i, err)
		}
		// Panic broke the chain at B: C never ran, and A observed the error
		// through next().
		if got := p1Join(rec.got()); got != "A,B" {
			t.Fatalf("Waterfall #%d calls = %q, want A,B (C must not run)", i, got)
		}
	}
	if st := r.State(); st != StateActive {
		t.Fatalf("fiber state after panics = %v, want Active", st)
	}
	p1AssertBindings(t, p1ProbeCtx(t, rt, "wf-panic-probe"), p1IntKey.ID(), 3)
}

// W-18/T-05: 1000 dispatches over the same snapshot produce the identical
// A -> B -> C chain every time.
func TestP1WaterfallDeterministicChain(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("A", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("B", rec)) },
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("C", rec)) },
		},
	})
	c := p1Ctx(r)

	for i := 0; i < 1000; i++ {
		rec.calls = nil
		if err := Waterfall(context.Background(), c, p1IntKey, i); err != nil {
			t.Fatalf("Waterfall #%d: %v", i, err)
		}
		if got := p1Join(rec.got()); got != "A,B,C" {
			t.Fatalf("Waterfall #%d calls = %q, want A,B,C", i, got)
		}
	}
}

// W-19: repeated Activate -> OnWaterfall -> Waterfall -> Dispose leaves no
// stale registration.
func TestP1WaterfallNoHandlerLeak(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	x := p1Ready(t, rt, &p1Registrar{name: "X", rec: rec}) // stable emitter
	ctxX := p1Ctx(x)

	for i := 0; i < 10; i++ {
		r := p1Ready(t, rt, &p1Registrar{
			name: fmt.Sprintf("R%02d", i),
			rec:  rec,
			on: []func(*Context) error{
				func(ctx *Context) error {
					return OnWaterfall(ctx, p1IntKey, wfRec(fmt.Sprintf("h%02d", i), rec))
				},
			},
		})
		rec.calls = nil
		if err := Waterfall(context.Background(), ctxX, p1IntKey, i); err != nil {
			t.Fatalf("Waterfall #%d: %v", i, err)
		}
		if got := p1Join(rec.got()); got != fmt.Sprintf("h%02d", i) {
			t.Fatalf("iteration #%d calls = %q, want h%02d", i, got, i)
		}
		if err := r.Dispose(); err != nil {
			t.Fatal(err)
		}
		if err := r.Gone(p1Timeout(t)); err != nil {
			t.Fatalf("R%02d not gone: %v", i, err)
		}
		p1AssertNoBindings(t, p1ProbeCtx(t, rt, "wf-cycle-probe"), p1IntKey.ID())
	}
}

// Plain On registrations reached by a Waterfall dispatch run as transparent
// nodes: they have no chain authority, so the chain auto-continues after a nil
// return; an error still fails the dispatch.
func TestP1WaterfallPlainHandlersTransparent(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h1"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h2"); return nil })
			},
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("h3", rec)) },
		},
	})
	c := p1Ctx(r)

	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Waterfall: %v", err)
	}
	if got := p1Join(rec.got()); got != "h1,h2,h3" {
		t.Fatalf("mixed chain calls = %q, want h1,h2,h3", got)
	}
}

// Chain-aware handlers stay fully usable by the other dispatch modes (they
// share one registry): Emit/Serial/Parallel invoke them as terminal nodes
// whose next() is trivially satisfied.
func TestP1WaterfallHandlerSharedAcrossDispatchModes(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return OnWaterfall(ctx, p1IntKey, func(_ context.Context, p int, next Next) error {
					rec.add("w")
					if err := next(); err != nil {
						return err
					}
					rec.add("w-after")
					return nil
				})
			},
		},
	})
	c := p1Ctx(r)

	if err := Emit(c, p1IntKey, 1); err != nil {
		t.Fatalf("Emit over chain-aware handler: %v", err)
	}
	if got := p1Join(rec.got()); got != "w,w-after" {
		t.Fatalf("Emit calls = %q, want w,w-after", got)
	}
}

// Guard rails: OnWaterfall/Waterfall reject nil context, zero-value keys, and
// nil handlers without touching the registry.
func TestP1WaterfallGuardRails(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec})
	c := p1Ctx(r)
	var zeroKey runtime.EventKey[int]

	if err := OnWaterfall[int](nil, p1IntKey, wfRec("x", rec)); err == nil {
		t.Fatal("OnWaterfall(nil ctx) returned nil")
	}
	if err := OnWaterfall(c, zeroKey, wfRec("x", rec)); err == nil {
		t.Fatal("OnWaterfall(zero key) returned nil")
	}
	if err := OnWaterfall[int](c, p1IntKey, nil); err == nil {
		t.Fatal("OnWaterfall(nil handler) returned nil")
	}
	if err := Waterfall[int](nil, nil, p1IntKey, 1); err == nil {
		t.Fatal("Waterfall(nil context) returned nil")
	}
	if err := Waterfall[int](context.Background(), c, zeroKey, 1); err == nil {
		t.Fatal("Waterfall(zero key) returned nil")
	}
	p1AssertNoBindings(t, c, p1IntKey.ID())
}

// W-20 (local component): concurrent chain-aware registration while Waterfall
// dispatchers run is race-free; every registration is eventually dispatched
// exactly once.
func TestP1WaterfallConcurrentRegistrationSafe(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("h0", rec)) },
		},
	})
	c := p1Ctx(r)

	stop := make(chan struct{})
	var dispWG sync.WaitGroup
	for g := 0; g < 2; g++ {
		dispWG.Add(1)
		go func() {
			defer dispWG.Done()
			for i := 0; i < 500; i++ {
				select {
				case <-stop:
					return
				default:
				}
				_ = Waterfall(context.Background(), c, p1IntKey, 1)
			}
		}()
	}

	const n = 24
	var regWG sync.WaitGroup
	for i := 0; i < n; i++ {
		regWG.Add(1)
		go func(i int) {
			defer regWG.Done()
			tag := fmt.Sprintf("c%02d", i)
			if err := OnWaterfall(c, p1IntKey, wfRec(tag, rec)); err != nil {
				t.Errorf("OnWaterfall: %v", err)
			}
		}(i)
	}
	regWG.Wait()
	close(stop)
	dispWG.Wait()

	if got := len(c.EventBindings(p1IntKey.ID())); got != n+1 {
		t.Fatalf("visible bindings = %d, want %d", got, n+1)
	}
	rec.calls = nil
	if err := Waterfall(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("final Waterfall: %v", err)
	}
	want := make([]string, 0, n+1)
	want = append(want, "h0")
	for i := 0; i < n; i++ {
		want = append(want, fmt.Sprintf("c%02d", i))
	}
	if got := p1Set(rec.got()); got != p1Set(want) {
		t.Fatalf("final membership mismatch:\ngot  %s\nwant %s", got, p1Set(want))
	}
}

// W-20 (local component): concurrent Waterfall dispatches racing with owner
// disposal are race-free and leave no stale registration.
func TestP1WaterfallConcurrentDisposalSafe(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { return OnWaterfall(ctx, p1IntKey, wfRec("h", rec)) },
		},
	})
	c := p1Ctx(r)

	stop := make(chan struct{})
	errCh := make(chan error, 1)
	var wg sync.WaitGroup
	for g := 0; g < 4; g++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				select {
				case <-stop:
					return
				default:
				}
				err := Waterfall(context.Background(), c, p1IntKey, 1)
				if err != nil && !errors.Is(err, context.Canceled) {
					select {
					case errCh <- err:
					default:
					}
					return
				}
			}
		}()
	}
	time.Sleep(5 * time.Millisecond)
	if err := r.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := r.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("R not gone: %v", err)
	}
	close(stop)
	wg.Wait()
	select {
	case err := <-errCh:
		t.Fatalf("unexpected dispatch error during disposal: %v", err)
	default:
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "wf-residue"), p1IntKey.ID(), p1WFKey2.ID(), p1WFKey3.ID())
}

// p1WFKiller is the Waterfall analog of p1Killer: a chain-aware node that
// disposes its victim mid-chain and waits for Gone before continuing.
type p1WFKiller struct {
	rec     *p1Rec
	victim  *Fiber
	selfCtx *Context
}

func (c *p1WFKiller) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1WFKiller) Name() string          { return "wf-killer" }
func (c *p1WFKiller) Inject() []Dependency  { return nil }
func (c *p1WFKiller) Provide() []Capability { return nil }
func (c *p1WFKiller) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	err := OnWaterfall(ctx, p1IntKey, func(dctx context.Context, p int, next Next) error {
		c.rec.add("kill")
		if err := c.victim.Dispose(); err != nil {
			return err
		}
		if err := c.victim.Gone(dctx); err != nil {
			return err
		}
		return next()
	})
	return nil, err
}

// p1WFLeaf is a chain-aware leaf: it registers tag and continues the chain.
type p1WFLeaf struct {
	tag     string
	rec     *p1Rec
	selfCtx *Context
}

func (c *p1WFLeaf) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1WFLeaf) Name() string          { return "wf-leaf:" + c.tag }
func (c *p1WFLeaf) Inject() []Dependency  { return nil }
func (c *p1WFLeaf) Provide() []Capability { return nil }
func (c *p1WFLeaf) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	return nil, OnWaterfall(ctx, p1IntKey, wfRec(c.tag, c.rec))
}

// p1WFX is an explicit scope fiber that registers a chain-aware handler and
// mounts two unscoped member children (which inherit realm X).
type p1WFX struct {
	rec     *p1Rec
	hs      chan *Fiber
	selfCtx *Context
}

func (c *p1WFX) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1WFX) Name() string          { return "wf-x" }
func (c *p1WFX) Inject() []Dependency  { return nil }
func (c *p1WFX) Provide() []Capability { return nil }
func (c *p1WFX) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := OnWaterfall(ctx, p1IntKey, wfRec("x", c.rec)); err != nil {
		return nil, err
	}
	b, err := ctx.Child(&p1WFLeaf{tag: "b", rec: c.rec})
	if err != nil {
		return nil, err
	}
	mc, err := ctx.Child(&p1WFLeaf{tag: "c", rec: c.rec})
	if err != nil {
		return nil, err
	}
	c.hs <- b
	c.hs <- mc
	return nil, nil
}

// p1WFHost registers the root-realm chain-aware handler and mounts sibling
// explicit scopes A and X.
type p1WFHost struct {
	rec     *p1Rec
	hs      chan *Fiber
	selfCtx *Context
}

func (c *p1WFHost) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1WFHost) Name() string          { return "wf-host" }
func (c *p1WFHost) Inject() []Dependency  { return nil }
func (c *p1WFHost) Provide() []Capability { return nil }
func (c *p1WFHost) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := OnWaterfall(ctx, p1IntKey, wfRec("root", c.rec)); err != nil {
		return nil, err
	}
	a, err := ctx.Child(&p1WFLeaf{tag: "a", rec: c.rec}, WithScope())
	if err != nil {
		return nil, err
	}
	x, err := ctx.Child(&p1WFX{rec: c.rec, hs: c.hs}, WithScope())
	if err != nil {
		return nil, err
	}
	c.hs <- a
	c.hs <- x
	return nil, nil
}
