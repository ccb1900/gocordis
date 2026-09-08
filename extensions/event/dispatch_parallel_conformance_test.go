package event_test

import (
	"context"
	. "dynamic-runtime/extensions/event"
	"errors"
	"fmt"
	"sort"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

// P1.3 Parallel conformance (P-01..P-20 of the P1.3 Parallel Event Runtime
// Specification).
//
// Reuses the P1.1/P1.2 conformance helpers (p1Registrar, p1Rec, p1Killer,
// p1SerialHost, p1SerialX, p1Leaf, ...). Handlers run concurrently; tests
// therefore assert on membership sets, never on execution/completion order,
// except where the spec requires a deterministic property (P-19 error order).

var p1ParallelKey3 = NewEventKey[int]("p1.parallel.3")

// p1Set returns the sorted comma-joined membership of a call recorder.
func p1Set(got []string) string {
	cp := append([]string(nil), got...)
	sort.Strings(cp)
	return strings.Join(cp, ",")
}

// P-02 (core correctness) / P-01: handlers overlap in time. A barrier blocks
// every handler until ALL of them have started: a serial implementation can
// never satisfy this test.
func TestP1ParallelConcurrentBarrier(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	const n = 3
	entered := make(chan string, n)
	release := make(chan struct{})
	tags := []string{"a", "b", "c"}
	var on []func(*Context) error
	for _, tag := range tags {
		tag := tag
		on = append(on, func(ctx *Context) error {
			return On(ctx, p1IntKey, func(_ context.Context, p int) error {
				rec.add(tag)
				entered <- tag
				<-release
				rec.add(tag + "-done")
				return nil
			})
		})
	}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec, on: on})
	c := p1Ctx(r)

	done := make(chan error, 1)
	go func() { done <- Parallel(context.Background(), c, p1IntKey, 1) }()

	got := map[string]bool{}
	deadline := time.After(2 * time.Second)
	for len(got) < n {
		select {
		case tag := <-entered:
			got[tag] = true
		case <-deadline:
			t.Fatalf("only %d/%d handlers entered the barrier (not concurrent): %v", len(got), n, rec.got())
		}
	}
	close(release)
	if err := <-done; err != nil {
		t.Fatalf("Parallel: %v", err)
	}
	if want := "a,a-done,b,b-done,c,c-done"; p1Set(rec.got()) != want {
		t.Fatalf("calls = %s, want %s", p1Set(rec.got()), want)
	}
}

// P-03: Parallel returns only after every started handler has completed.
func TestP1ParallelWaitsForCompletion(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					time.Sleep(60 * time.Millisecond)
					rec.add("slow-done")
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					time.Sleep(5 * time.Millisecond)
					rec.add("fast-done")
					return nil
				})
			},
		},
	})
	c := p1Ctx(r)
	start := time.Now()
	if err := Parallel(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Parallel: %v", err)
	}
	elapsed := time.Since(start)
	if got := p1Set(rec.got()); got != "fast-done,slow-done" {
		t.Fatalf("calls = %s, want fast-done,slow-done", got)
	}
	if elapsed < 50*time.Millisecond {
		t.Fatalf("Parallel returned after %v; the 60ms handler must have completed first", elapsed)
	}
}

// P-04: snapshot membership is deterministic (registration sequence), while
// completion order is free.
func TestP1ParallelRegistrationOrderingIndependent(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	tags := []string{"a", "b", "c"}
	var on []func(*Context) error
	for _, tag := range tags {
		tag := tag
		on = append(on, func(ctx *Context) error {
			return On(ctx, p1IntKey, func(_ context.Context, p int) error {
				rec.add(tag)
				time.Sleep(time.Duration(1+int(p)%3) * time.Millisecond)
				return nil
			})
		})
	}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec, on: on})
	c := p1Ctx(r)
	for i := 0; i < 20; i++ {
		rec.calls = nil
		if err := Parallel(context.Background(), c, p1IntKey, i); err != nil {
			t.Fatalf("Parallel #%d: %v", i, err)
		}
		if got := p1Set(rec.got()); got != "a,b,c" {
			t.Fatalf("Parallel #%d membership = %s, want a,b,c", i, got)
		}
	}
}

// P-05/P-06/P-19: errors do not stop other handlers; multiple errors are all
// preserved; the aggregated error is normalized to snapshot registration order
// and therefore identical across runs despite nondeterministic completion.
func TestP1ParallelErrorsAndDeterministicOrder(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	errA := errors.New("err-a")
	errB := errors.New("err-b")
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error { // slow error: usually finishes last
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("a")
					time.Sleep(30 * time.Millisecond)
					return errA
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("b"); return nil })
			},
			func(ctx *Context) error { // fast error: usually finishes first
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("c")
					return errB
				})
			},
		},
	})
	c := p1Ctx(r)
	for i := 0; i < 10; i++ {
		rec.calls = nil
		err := Parallel(context.Background(), c, p1IntKey, i)
		if err == nil {
			t.Fatal("Parallel returned nil, want aggregated errA+errB")
		}
		if !errors.Is(err, errA) || !errors.Is(err, errB) {
			t.Fatalf("Parallel #%d error = %v, want errA and errB", i, err)
		}
		// Registration-order normalization: errA (registered first) precedes
		// errB regardless of completion order.
		if err.Error() != "err-a\nerr-b" {
			t.Fatalf("Parallel #%d error order = %q, want err-a then err-b", i, err.Error())
		}
		if got := p1Set(rec.got()); got != "a,b,c" {
			t.Fatalf("Parallel #%d calls = %s, want a,b,c", i, got)
		}
	}
}

// P-07: one handler's panic is contained; the other handlers complete; the
// Runtime keeps working.
func TestP1ParallelPanicIsolation(t *testing.T) {
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
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					time.Sleep(5 * time.Millisecond)
					rec.add("ok1")
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("ok2"); return nil })
			},
		},
	})
	c := p1Ctx(r)
	for i := 0; i < 3; i++ {
		rec.calls = nil
		err := Parallel(context.Background(), c, p1IntKey, i)
		if !errors.Is(err, ErrEventHandlerPanic) {
			t.Fatalf("Parallel #%d error = %v, want ErrEventHandlerPanic", i, err)
		}
		if got := p1Set(rec.got()); got != "ok1,ok2,panic" {
			t.Fatalf("Parallel #%d calls = %s, want ok1,ok2,panic", i, got)
		}
	}
	if st := r.State(); st != StateActive {
		t.Fatalf("fiber state after panics = %v, want Active", st)
	}
	p1AssertBindings(t, p1ProbeCtx(t, rt, "parallel-panic-probe"), p1IntKey.ID(), 3)
}

// P-08: cancellation before dispatch starts prevents every handler from
// starting.
func TestP1ParallelCancellationBeforeStart(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("a"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("b"); return nil })
			},
		},
	})
	c := p1Ctx(r)
	canceled, cancel := context.WithCancel(context.Background())
	cancel()
	if err := Parallel(canceled, c, p1IntKey, 1); !errors.Is(err, context.Canceled) {
		t.Fatalf("pre-canceled Parallel error = %v, want context.Canceled", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("pre-canceled calls = %q, want none", got)
	}
}

// P-09: cancellation after handlers started never force-terminates them; every
// started handler completes.
func TestP1ParallelCancellationAfterStart(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	const n = 3
	started := make(chan struct{}, n)
	release := make(chan struct{})
	tags := []string{"a", "b", "c"}
	var on []func(*Context) error
	for _, tag := range tags {
		tag := tag
		on = append(on, func(ctx *Context) error {
			return On(ctx, p1IntKey, func(_ context.Context, p int) error {
				rec.add(tag)
				started <- struct{}{}
				<-release
				rec.add(tag + "-done")
				return nil
			})
		})
	}
	r := p1Ready(t, rt, &p1Registrar{name: "R", rec: rec, on: on})
	c := p1Ctx(r)

	dctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- Parallel(dctx, c, p1IntKey, 1) }()

	// Wait until at least two handlers are mid-flight, then cancel.
	for i := 0; i < 2; i++ {
		select {
		case <-started:
		case <-time.After(2 * time.Second):
			t.Fatalf("handlers did not start (only %d/%d)", i, 2)
		}
	}
	cancel()
	close(release)
	if err := <-done; err != nil && !errors.Is(err, context.Canceled) {
		t.Fatalf("Parallel error = %v, want nil or context.Canceled", err)
	}
	// Every handler that started must have completed (not force-terminated).
	got := rec.got()
	for _, tag := range []string{"a", "b"} {
		if !strings.Contains(p1Join(got), tag+"-done") {
			t.Fatalf("started handler %s did not complete; calls = %s", tag, p1Set(got))
		}
	}
}

// P-10: registration during a handler execution never changes the current
// Parallel snapshot; it is visible from the next Parallel.
func TestP1ParallelSnapshotRegistration(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	var registered atomic.Bool
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error {
					rec.add("a")
					if registered.CompareAndSwap(false, true) {
						return On(reg.ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("d"); return nil })
					}
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("b"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("c"); return nil })
			},
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)

	if err := Parallel(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Parallel #1: %v", err)
	}
	if got := p1Set(rec.got()); got != "a,b,c" {
		t.Fatalf("dispatch #1 membership = %s, want a,b,c (d registered mid-dispatch must not join)", got)
	}
	rec.calls = nil
	if err := Parallel(context.Background(), c, p1IntKey, 2); err != nil {
		t.Fatalf("Parallel #2: %v", err)
	}
	if got := p1Set(rec.got()); got != "a,b,c,d" {
		t.Fatalf("dispatch #2 membership = %s, want a,b,c,d", got)
	}
}

// P-11: unregistration during a dispatch (owner disposed mid-dispatch) never
// changes the current snapshot; the handler is gone from the next Parallel.
func TestP1ParallelSnapshotUnregistration(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	killer := &p1Killer{rec: rec}
	f2 := p1Ready(t, rt, killer)
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

	// Parallel #1: kill disposes the victim fiber concurrently; the victim
	// handler is in the snapshot (already started) and still completes.
	if err := Parallel(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("Parallel #1: %v", err)
	}
	if got := p1Set(rec.got()); got != "kill,victim" {
		t.Fatalf("dispatch #1 membership = %s, want kill,victim", got)
	}
	rec.calls = nil
	if err := Parallel(context.Background(), c, p1IntKey, 2); err != nil {
		t.Fatalf("Parallel #2: %v", err)
	}
	if got := p1Set(rec.got()); got != "kill" {
		t.Fatalf("dispatch #2 membership = %s, want kill", got)
	}

	if err := f2.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f2.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("killer not gone: %v", err)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "parallel-residue-1"), p1IntKey.ID(), p1ParallelKey3.ID())
}

// P-12: scope visibility — ancestor/current visible, sibling invisible.
func TestP1ParallelScopeVisibility(t *testing.T) {
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
	a := handles["leaf:a"]
	x := handles["serial-x"]
	b := handles["leaf:b"]
	cc := handles["leaf:c"]
	p1WaitState(t, a, StateActive)
	p1WaitState(t, x, StateActive)
	p1WaitState(t, b, StateActive)
	p1WaitState(t, cc, StateActive)

	cases := []struct {
		name string
		ctx  *Context
		want string
	}{
		{"root", p1Ctx(host), "root"},
		{"scope-a", p1Ctx(a), "a,root"},
		{"scope-x", p1Ctx(x), "b,c,root,x"},
		{"member-b", p1Ctx(b), "b,c,root,x"},
	}
	for _, tc := range cases {
		rec.calls = nil
		if err := Parallel(context.Background(), tc.ctx, p1IntKey, 1); err != nil {
			t.Fatalf("%s Parallel: %v", tc.name, err)
		}
		if got := p1Set(rec.got()); got != tc.want {
			t.Fatalf("%s dispatch membership = %s, want %s", tc.name, got, tc.want)
		}
	}
}

// P-13/P-20: Effect disposal removes the handler; repeated activate/On/
// Parallel/dispose cycles leave no stale registrations.
func TestP1ParallelEffectDisposalNoHandlerLeak(t *testing.T) {
	rt := p1Runtime(t)
	for cycle := 1; cycle <= 3; cycle++ {
		rec := &p1Rec{}
		x := p1Ready(t, rt, &p1Registrar{name: fmt.Sprintf("X%d", cycle), rec: rec})
		tag := fmt.Sprintf("h%d", cycle)
		r := p1Ready(t, rt, &p1Registrar{
			name: tag,
			rec:  rec,
			on: []func(*Context) error{
				func(ctx *Context) error {
					return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add(tag); return nil })
				},
			},
		})
		ctxX := p1Ctx(x)
		if err := Parallel(context.Background(), ctxX, p1IntKey, 1); err != nil {
			t.Fatalf("cycle %d Parallel: %v", cycle, err)
		}
		if got := p1Set(rec.got()); got != tag {
			t.Fatalf("cycle %d calls = %s, want %s", cycle, got, tag)
		}
		if err := r.Dispose(); err != nil {
			t.Fatal(err)
		}
		if err := r.Gone(p1Timeout(t)); err != nil {
			t.Fatalf("cycle %d fiber not gone: %v", cycle, err)
		}
		p1AssertNoBindings(t, p1ProbeCtx(t, rt, "parallel-cycle-probe"), p1IntKey.ID())
	}
}

// P-14: concurrent registration and Parallel dispatch are race-free and never
// corrupt the registry; every registered handler is eventually dispatched.
func TestP1ParallelConcurrentRegistrationSafe(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("h0"); return nil })
			},
		},
	})
	c := p1Ctx(r)

	// Dispatchers run Parallel while registrations happen concurrently.
	stop := make(chan struct{})
	var dispWG sync.WaitGroup
	for g := 0; g < 2; g++ {
		dispWG.Add(1)
		go func() {
			defer dispWG.Done()
			for i := 0; ; i++ {
				select {
				case <-stop:
					return
				default:
				}
				if i >= 500 {
					return
				}
				_ = Parallel(context.Background(), c, p1IntKey, 1)
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
			if err := On(c, p1IntKey, func(_ context.Context, p int) error { rec.add(tag); return nil }); err != nil {
				t.Errorf("On: %v", err)
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
	if err := Parallel(context.Background(), c, p1IntKey, 1); err != nil {
		t.Fatalf("final Parallel: %v", err)
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

// P-15: concurrent disposal during in-flight Parallel dispatches is race-free;
// no stale registration remains.
func TestP1ParallelConcurrentDisposalSafe(t *testing.T) {
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
				err := Parallel(context.Background(), c, p1IntKey, 1)
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
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "parallel-residue-2"), p1IntKey.ID())
}

// P-16/P-17: a handler may trigger nested Parallel dispatches (reentrancy);
// each dispatch has its own snapshot; no deadlock; everything completes.
func TestP1ParallelReentrantAndNested(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	var reg *p1Registrar
	reg = &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			// P-16: a1 re-enters Parallel on a second event key.
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(dctx context.Context, p int) error {
					rec.add("a1")
					return Parallel(dctx, reg.ctx, p1SerialKey2, 0)
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1IntKey, func(_ context.Context, p int) error { rec.add("a2"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1SerialKey2, func(dctx context.Context, p int) error {
					rec.add("b1")
					// P-17: nested one level deeper.
					return Parallel(dctx, reg.ctx, p1ParallelKey3, 0)
				})
			},
			func(ctx *Context) error {
				return On(ctx, p1SerialKey2, func(_ context.Context, p int) error { rec.add("b2"); return nil })
			},
			func(ctx *Context) error {
				return On(ctx, p1ParallelKey3, func(_ context.Context, p int) error { rec.add("c1"); return nil })
			},
		},
	}
	r := p1Ready(t, rt, reg)
	c := p1Ctx(r)
	done := make(chan error, 1)
	go func() { done <- Parallel(context.Background(), c, p1IntKey, 1) }()
	select {
	case err := <-done:
		if err != nil {
			t.Fatalf("nested Parallel: %v", err)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("nested Parallel deadlocked")
	}
	if want := "a1,a2,b1,b2,c1"; p1Set(rec.got()) != want {
		t.Fatalf("nested dispatch membership = %s, want %s", p1Set(rec.got()), want)
	}
}

// P-18: Parallel passes the payload through without copying; every handler
// observes the same payload identity.
func TestP1ParallelPayloadIdentity(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	key := NewEventKey[*int]("p1.payload")
	box := 42
	sent := &box
	r := p1Ready(t, rt, &p1Registrar{
		name: "R",
		rec:  rec,
		on: []func(*Context) error{
			func(ctx *Context) error {
				return On(ctx, key, func(_ context.Context, p *int) error {
					if p != sent {
						rec.add("h1-mismatch")
					} else {
						rec.add("h1-same")
					}
					return nil
				})
			},
			func(ctx *Context) error {
				return On(ctx, key, func(_ context.Context, p *int) error {
					if p != sent {
						rec.add("h2-mismatch")
					} else {
						rec.add("h2-same")
					}
					return nil
				})
			},
		},
	})
	c := p1Ctx(r)
	if err := Parallel(context.Background(), c, key, sent); err != nil {
		t.Fatalf("Parallel: %v", err)
	}
	if got := p1Set(rec.got()); got != "h1-same,h2-same" {
		t.Fatalf("payload identity calls = %s, want h1-same,h2-same (no copy)", got)
	}
}
