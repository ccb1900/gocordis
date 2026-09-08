package event_test

import (
	"context"
	. "dynamic-runtime/extensions/event"
	"errors"
	"fmt"
	"sort"
	"sync"
	"testing"
	"time"
)

// P1.2 Serial conformance (S-01..S-18 of the P1.2 Serial Event Runtime
// Specification).
//
// Reuses the P1.1 conformance helpers (p1Registrar, p1Rec, ...) from
// p1_emit_conformance_test.go. Handlers are context-aware (P1.2 extension of
// the P1.1 handler contract): Serial passes the caller-supplied dispatch
// context to every handler.

var p1SerialKey2 = NewEventKey[int]("p1.serial.2")

// S-01/S-02/S-17: basic ordered execution; ordering = registration sequence
// within the dispatch visibility set; deterministic across many dispatches.
func TestP1SerialOrderedDeterministic(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	tags := []string{"a", "b", "c", "d", "e"}
	var on []func(*Context) error
	for _, tag := range tags {
		tag := tag
		on = append(on, func(ctx *Context) error {
			return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add(tag); return nil })
		})
	}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec, on: on})
	c := p1Ctx(r)
	for i := 0; i < 100; i++ {
		rec.calls = nil
		if err := Serial(context.Background(), c, p1IntKey, i); err != nil {
			t.Fatalf("Serial #%d: %v", i, err)
		}
		if got := p1Join(rec.got()); got != "a,b,c,d,e" {
			t.Fatalf("Serial #%d order = %q, want a,b,c,d,e", i, got)
		}
	}
}

// S-03: Serial awaits the previous handler — the next handler does not start
// until the previous one has returned.
func TestP1SerialAwaitsPreviousHandler(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	started := make(chan struct{})
	release := make(chan struct{})
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("h1")
					close(started)
					<-release
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h2"); return nil })
			},
		},
	})
	c := p1Ctx(r)
	done := make(chan error, 1)
	go func() { done <- Serial(context.Background(), c, p1IntKey, 1) }()

	<-started
	// h1 is still blocked: h2 must not have started and Serial must not return.
	select {
	case err := <-done:
		t.Fatalf("Serial returned (err=%v) while h1 was still blocked", err)
	case <-time.After(50 * time.Millisecond):
	}
	if got := p1Join(rec.got()); got != "h1" {
		t.Fatalf("while h1 blocked, calls = %q, want h1 only", got)
	}
	close(release)
	if err := <-done; err != nil {
		t.Fatalf("Serial: %v", err)
	}
	if got := p1Join(rec.got()); got != "h1,h2" {
		t.Fatalf("after release, calls = %q, want h1,h2", got)
	}
}

// S-04/S-05: Serial is ordered execution, not a fail-fast chain. A handler
// error never stops later handlers, and multiple errors are all preserved.
func TestP1SerialErrorAggregationNotFailFast(t *testing.T) {
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
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("ok"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("e2"); return errB })
			},
		},
	})
	c := p1Ctx(r)
	err := Serial(context.Background(), c, p1IntKey, 1)
	if err == nil {
		t.Fatal("Serial returned nil, want aggregated errors")
	}
	if !errors.Is(err, errA) || !errors.Is(err, errB) {
		t.Fatalf("Serial error = %v, want both errA and errB preserved", err)
	}
	if got := p1Join(rec.got()); got != "e1,ok,e2" {
		t.Fatalf("calls = %q, want e1,ok,e2 (all handlers executed)", got)
	}
}

// S-06: cancellation prevents future handlers from starting. A pre-canceled
// dispatch runs no handler and returns context.Canceled.
func TestP1SerialCancellationStopsFutureHandlers(t *testing.T) {
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
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h3"); return nil })
			},
		},
	})
	c := p1Ctx(r)

	canceled, cancel := context.WithCancel(context.Background())
	cancel()
	if err := Serial(canceled, c, p1IntKey, 1); !errors.Is(err, context.Canceled) {
		t.Fatalf("pre-canceled Serial error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("pre-canceled calls = %q, want none", got)
	}
}

// TestP1SerialCancellationBetweenHandlers covers the between-handler
// cancellation raised by an earlier handler.
func TestP1SerialCancellationBetweenHandlers(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	dctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h1"); cancel(); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h2"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h3"); return nil })
			},
		},
	})
	c := p1Ctx(r)
	err := Serial(dctx, c, p1IntKey, 1)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("Serial error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "h1" {
		t.Fatalf("calls = %q, want h1 (h2/h3 must not start after cancellation)", got)
	}
}

// S-07: a running handler observes cancellation through the dispatch context
// it was given; the Kernel does not force-terminate it, but later handlers do
// not start and errors from completed handlers are preserved.
func TestP1SerialHandlerObservesCancellation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	errA := errors.New("err-a")
	h2started := make(chan struct{})
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h1"); return errA })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(dctx context.Context, p int) error {
					rec.add("h2")
					close(h2started)
					<-dctx.Done()
					rec.add("h2-canceled")
					return dctx.Err()
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h3"); return nil })
			},
		},
	})
	c := p1Ctx(r)
	dctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- Serial(dctx, c, p1IntKey, 1) }()

	<-h2started
	cancel() // h2 observes cancellation; h3 must not start.
	err := <-done
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("Serial error = %v, want context.Canceled", err)
	}
	if !errors.Is(err, errA) {
		t.Fatalf("Serial error = %v, want completed-handler errA preserved", err)
	}
	got := rec.got()
	if p1Join(got) != "h1,h2,h2-canceled" {
		t.Fatalf("calls = %q, want h1,h2,h2-canceled (h3 must not start)", p1Join(got))
	}
}

// S-08: registration performed inside a handler does not change the current
// dispatch snapshot; it is visible from the next Serial.
func TestP1SerialSnapshotRegistration(t *testing.T) {
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
					return On(reg.ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h4"); return nil })
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h2"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h3"); return nil })
			},
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)

	if err := Serial(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Serial #1: %v", err)
	}
	if got := p1Join(rec.got()); got != "h1,h2,h3" {
		t.Fatalf("dispatch #1 calls = %q, want h1,h2,h3", got)
	}
	rec.calls = nil
	if err := Serial(context.Background(), c, p1IntKey, 2); err != nil {
		t.Fatalf("Serial #2: %v", err)
	}
	if got := p1Join(rec.got()); got != "h1,h2,h3,h4" {
		t.Fatalf("dispatch #2 calls = %q, want h1,h2,h3,h4", got)
	}
}

// S-09: unregistration during a dispatch (owner disposed mid-dispatch) never
// changes the current snapshot — the snapshot handler still runs — and the
// handler is gone from the next Serial.
func TestP1SerialSnapshotUnregistration(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	killer := &p1Killer{rec: rec}
	f2 := p1Ready(t, rt, killer) // registers "kill" first (registration seq 1)
	f3 := p1Ready(t, rt, &p1Registrar{
		name: "victim",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("victim"); return nil })
			},
		},
	})
	killer.victim = f3
	c := p1Ctx(f2)

	// First Serial: kill disposes the victim fiber and waits for its Gone
	// (registration physically removed mid-dispatch); the victim handler is in
	// the dispatch snapshot and still executes.
	if err := Serial(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Serial #1: %v", err)
	}
	if got := p1Join(rec.got()); got != "kill,victim" {
		t.Fatalf("dispatch #1 calls = %q, want kill,victim", got)
	}
	rec.calls = nil

	// Second Serial: the victim handler is gone (owner Gone + entry removed).
	if err := Serial(context.Background(), c, p1IntKey, 2); err != nil {
		t.Fatalf("Serial #2: %v", err)
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
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "serial-residue-1"), p1IntKey.ID(), p1SerialKey2.ID())
}

// p1Killer registers a handler that disposes the victim fiber mid-dispatch and
// waits for it to reach Gone.
type p1Killer struct {
	rec     *p1Rec
	victim  *Fiber
	selfCtx *Context
}

func (c *p1Killer) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1Killer) Name() string          { return "killer" }
func (c *p1Killer) Inject() []Dependency  { return nil }
func (c *p1Killer) Provide() []Capability { return nil }
func (c *p1Killer) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	return nil, On(ctx, p1IntKey, func(dctx context.Context, p int) error {
		c.rec.add("kill")
		if err := c.victim.Dispose(); err != nil {
			return err
		}
		return c.victim.Gone(dctx)
	})
}

// S-10/S-11: scope visibility and sibling isolation for Serial dispatches.
// Tree: root host (root realm) mounts sibling explicit scopes A and X;
// X mounts unscoped members B and C (realm X). Dispatch inside X sees the
// root handler, X's own handler, and B/C; it never sees A (sibling scope).
func TestP1SerialScopeVisibilityAndSiblingIsolation(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	hs := make(chan *Fiber, 8)
	host := p1Ready(t, rt, &p1SerialHost{rec: rec, hs: hs})
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
	a := handles["leaf:a"]   // sibling scope A
	x := handles["serial-x"] // sibling scope X
	b := handles["leaf:b"]   // X member
	cc := handles["leaf:c"]  // X member
	p1WaitState(t, a, StateActive)
	p1WaitState(t, x, StateActive)
	p1WaitState(t, b, StateActive)
	p1WaitState(t, cc, StateActive)

	ctxRoot := p1Ctx(host)
	ctxA := p1Ctx(a)
	ctxX := p1Ctx(x)
	ctxB := p1Ctx(b)

	// Root dispatch: only the root-realm handler is visible (children scopes
	// are not on the root realm path).
	rec.calls = nil
	if err := Serial(context.Background(), ctxRoot, p1IntKey, 1); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root" {
		t.Fatalf("root dispatch calls = %q, want root", got)
	}

	// A dispatch: root handler + A's own; X/B/C (sibling scope) invisible.
	rec.calls = nil
	if err := Serial(context.Background(), ctxA, p1IntKey, 2); err != nil {
		t.Fatal(err)
	}
	if got := p1Join(rec.got()); got != "root,a" {
		t.Fatalf("A dispatch calls = %q, want root,a", got)
	}

	// X and B dispatches: root + x + b + c (same realm members); never a.
	for _, emitCtx := range []*Context{ctxX, ctxB} {
		rec.calls = nil
		if err := Serial(context.Background(), emitCtx, p1IntKey, 3); err != nil {
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
}

// p1SerialX is an explicit scope fiber that registers its own handler and
// mounts two unscoped member children (which inherit realm X).
type p1SerialX struct {
	rec     *p1Rec
	hs      chan *Fiber
	selfCtx *Context
}

func (c *p1SerialX) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1SerialX) Name() string          { return "serial-x" }
func (c *p1SerialX) Inject() []Dependency  { return nil }
func (c *p1SerialX) Provide() []Capability { return nil }
func (c *p1SerialX) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := On(ctx, p1IntKey, func(_ context.Context, p int) error { c.rec.add("x"); return nil }); err != nil {
		return nil, err
	}
	b, err := ctx.Child(&p1Leaf{tag: "b", rec: c.rec})
	if err != nil {
		return nil, err
	}
	mc, err := ctx.Child(&p1Leaf{tag: "c", rec: c.rec})
	if err != nil {
		return nil, err
	}
	c.hs <- b
	c.hs <- mc
	return nil, nil
}

// p1SerialHost registers the root-realm handler and mounts sibling explicit
// scopes A and X.
type p1SerialHost struct {
	rec     *p1Rec
	hs      chan *Fiber
	selfCtx *Context
}

func (c *p1SerialHost) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1SerialHost) Name() string          { return "serial-host" }
func (c *p1SerialHost) Inject() []Dependency  { return nil }
func (c *p1SerialHost) Provide() []Capability { return nil }
func (c *p1SerialHost) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := On(ctx, p1IntKey, func(_ context.Context, p int) error { c.rec.add("root"); return nil }); err != nil {
		return nil, err
	}
	a, err := ctx.Child(&p1Leaf{tag: "a", rec: c.rec}, WithScope())
	if err != nil {
		return nil, err
	}
	x, err := ctx.Child(&p1SerialX{rec: c.rec, hs: c.hs}, WithScope())
	if err != nil {
		return nil, err
	}
	c.hs <- a
	c.hs <- x
	return nil, nil
}

// S-12/S-18: Effect disposal removes the handler (no handler leak) and a
// Serial dispatch never registers anything by itself.
func TestP1SerialEffectDisposalRemovesHandler(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	x := p1Ready(t, rt, &p1Registrar{name: "X", rec: rec}) // emitter, no handlers
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h"); return nil })
			},
		},
	})
	ctxX := p1Ctx(x)

	if err := Serial(context.Background(), ctxX, p1IntKey, 1); err != nil {
		t.Fatalf("Serial: %v", err)
	}
	if got := p1Join(rec.got()); got != "h" {
		t.Fatalf("pre-unwind calls = %q, want h", got)
	}
	p1AssertBindings(t, ctxX, p1IntKey.ID(), 1)
	rec.calls = nil

	// Repeated dispatches do not grow the registry.
	for i := 0; i < 5; i++ {
		if err := Serial(context.Background(), ctxX, p1IntKey, i); err != nil {
			t.Fatalf("Serial: %v", err)
		}
	}
	p1AssertBindings(t, ctxX, p1IntKey.ID(), 1)
	rec.calls = nil

	if err := r.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := r.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("R not gone: %v", err)
	}
	if err := Serial(context.Background(), ctxX, p1IntKey, 2); err != nil {
		t.Fatalf("Serial after unwind: %v", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("post-unwind calls = %q, want none", got)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "serial-residue-2"), p1IntKey.ID())
}

// S-13: concurrent registration is safe (no data race, no duplicate
// invocation, no corruption); all registered handlers are dispatched exactly
// once.
func TestP1SerialConcurrentRegistrationSafe(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec})
	c := p1Ctx(r)

	const n = 24
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			tag := fmt.Sprintf("h%02d", i)
			if err := On(c, p1IntKey, func(_ context.Context, p int) error { rec.add(tag); return nil }); err != nil {
				t.Errorf("On: %v", err)
			}
		}(i)
	}
	wg.Wait()

	if got := len(c.EventBindings(p1IntKey.ID())); got != n {
		t.Fatalf("visible bindings = %d, want %d", got, n)
	}
	if err := Serial(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Serial: %v", err)
	}
	got := rec.got()
	if len(got) != n {
		t.Fatalf("dispatch calls = %d, want %d", len(got), n)
	}
	want := make([]string, n)
	for i := 0; i < n; i++ {
		want[i] = fmt.Sprintf("h%02d", i)
	}
	sort.Strings(got)
	sort.Strings(want)
	if p1Join(got) != p1Join(want) {
		t.Fatalf("dispatch set mismatch:\ngot  %s\nwant %s", p1Join(got), p1Join(want))
	}
}

// S-14: concurrent disposal during in-flight dispatches is safe: no data
// race, no registry corruption, and every handler is gone once the owner
// reached Gone.
func TestP1SerialConcurrentDisposalSafe(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h"); return nil })
			},
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
				err := Serial(context.Background(), c, p1IntKey, 1)
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
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "serial-residue-3"), p1IntKey.ID(), p1SerialKey2.ID())
}

// S-15: a handler may trigger another dispatch (reentrancy); the nested
// dispatch uses a new snapshot and cannot deadlock on the registry lock.
func TestP1SerialReentrantDispatch(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(dctx context.Context, p int) error {
					rec.add("a1")
					return Serial(dctx, reg.ctx, p1SerialKey2, 0)
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("a2"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1SerialKey2, func(_ context.Context, p int) error { rec.add("b1"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1SerialKey2, func(_ context.Context, p int) error { rec.add("b2"); return nil })
			},
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)
	if err := Serial(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Serial: %v", err)
	}
	if got := p1Join(rec.got()); got != "a1,b1,b2,a2" {
		t.Fatalf("reentrant dispatch calls = %q, want a1,b1,b2,a2", got)
	}
}

// S-16: a panicking handler is contained as a handler error; remaining
// handlers run, the registry stays intact, and the Runtime keeps working.
func TestP1SerialPanicContained(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("panic")
					panic("boom")
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("ok"); return nil })
			},
		},
	})
	c := p1Ctx(r)

	for i := 0; i < 3; i++ {
		rec.calls = nil
		err := Serial(context.Background(), c, p1IntKey, i)
		if !errors.Is(err, ErrEventHandlerPanic) {
			t.Fatalf("Serial #%d error = %v, want ErrEventHandlerPanic", i, err)
		}
		if got := p1Join(rec.got()); got != "panic,ok" {
			t.Fatalf("Serial #%d calls = %q, want panic,ok", i, got)
		}
	}
	if st := r.State(); st != StateActive {
		t.Fatalf("fiber state after panics = %v, want Active", st)
	}
	p1AssertBindings(t, p1ProbeCtx(t, rt, "serial-panic-probe"), p1IntKey.ID(), 2)
}
