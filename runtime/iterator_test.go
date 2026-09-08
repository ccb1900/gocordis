package runtime_test

import (
	"errors"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Conformance tests for the effect-iterator activation form (paper §3.1.3,
// §4.2.2 L-Iter/L-Divert, §4.4 Asynchrony/Failure):
//
//	Each yield is one iteration; the step's Cleanup is its inverse (LIFO on
//	the activation unwind stack). A divert falls only BETWEEN iterations
//	(inertial landing alternative): the activation unwinds what it accumulated
//	and the fiber is NOT Failed. A step error is a raise: unwind + Failed, no
//	automatic retry (the outcome blocks L-Begin re-entry).
//
// Theorem-facing properties:
//	Thm68 (recovery exactness): after a diverted/raised activation ends, every
//	managed resource is back to its S0 state — opened exactly what landed,
//	closed exactly once, LIFO.
//	Thm70 (ordering): a consumer whose dependency disappears mid-activation
//	diverts at the next boundary and never becomes Active against a gone
//	provider.
//
// Determinism: the component waits on a `proceed` signal before every yield
// boundary (after the first), so the test decides exactly when each boundary
// happens and no in-flight step races the divert.

// iterRes tracks one managed resource through the iterator tests.
type iterRes struct {
	mu     sync.Mutex
	opens  []int
	closes []int
}

func (r *iterRes) open(i int)  { r.mu.Lock(); r.opens = append(r.opens, i); r.mu.Unlock() }
func (r *iterRes) close(i int) { r.mu.Lock(); r.closes = append(r.closes, i); r.mu.Unlock() }
func (r *iterRes) snap() (opens []int, closes []int) {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]int(nil), r.opens...), append([]int(nil), r.closes...)
}

// iterComp activates in `steps` steps; step i opens resource i (and closes it
// via its inverse). Before every boundary except the first it waits on
// `proceed`. When failAt >= 0, step failAt fails BEFORE opening anything (a
// paper raise: nothing installed); failAt < 0 means no raise.
type iterComp struct {
	steps   int
	res     *iterRes
	deps    []runtime.Dependency
	landed  chan int      // receives the step index after each landed step
	proceed chan struct{} // test releases each boundary after the first
	failAt  int
}

func newIterComp(steps int, res *iterRes, landed chan int, proceed chan struct{}) *iterComp {
	return &iterComp{steps: steps, res: res, landed: landed, proceed: proceed, failAt: -1}
}

func (c *iterComp) Name() string                 { return "iter-comp" }
func (c *iterComp) Inject() []runtime.Dependency { return c.deps }
func (c *iterComp) Provide() []runtime.Capability {
	return nil
}

// Apply is never called when ApplyIter is present; panic proves precedence.
func (c *iterComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	panic("runtime_test: iterator component's Apply must not be called")
}

func (c *iterComp) ApplyIter(ctx *runtime.Context, yield func(func() (runtime.Cleanup, error)) error) error {
	for i := 0; i < c.steps; i++ {
		i := i
		if i > 0 && c.proceed != nil {
			<-c.proceed
		}
		err := yield(func() (runtime.Cleanup, error) {
			if c.failAt == i {
				// Paper raise: the iteration fails before installing anything.
				return nil, errors.New("iterator raise at step")
			}
			c.res.open(i)
			if c.landed != nil {
				c.landed <- i
			}
			return func() error { c.res.close(i); return nil }, nil
		})
		if err != nil {
			return err
		}
	}
	return nil
}

func iterLand(t *testing.T, landed chan int) int {
	t.Helper()
	select {
	case i := <-landed:
		return i
	case <-time.After(8 * time.Second):
		t.Fatal("timeout waiting for iterator step to land")
		return -1
	}
}

func iterWant(t *testing.T, want, got []int, what string) {
	t.Helper()
	if len(want) != len(got) {
		t.Fatalf("%s = %v, want %v", what, got, want)
	}
	for i := range want {
		if want[i] != got[i] {
			t.Fatalf("%s = %v, want %v", what, got, want)
		}
	}
}

// TestThm68IteratorLIFOExactlyOnceOnDivert — dispose between iterations: the
// divert falls at the released boundary; landed steps' inverses run LIFO,
// exactly once; the fiber ends Gone (dispose), never Failed; unstarted steps
// never open.
func TestThm68IteratorLIFOExactlyOnceOnDivert(t *testing.T) {
	rt := newTestRuntime(t)
	res := &iterRes{}
	landed := make(chan int, 8)
	proceed := make(chan struct{}, 8)
	f, err := rt.Load(newIterComp(4, res, landed, proceed))
	if err != nil {
		t.Fatal(err)
	}
	if got := iterLand(t, landed); got != 0 {
		t.Fatalf("landed step %d, want 0", got)
	}
	proceed <- struct{}{}
	if got := iterLand(t, landed); got != 1 {
		t.Fatalf("landed step %d, want 1", got)
	}
	// Dispose while the component waits at the boundary before step 2.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	proceed <- struct{}{}
	// The boundary probe diverts: no further step may land.
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if err := f.Err(); err != nil {
		t.Fatalf("diverted activation must not be Failed: %v", err)
	}
	opens2, closes2 := res.snap()
	iterWant(t, []int{0, 1}, opens2, "opens")
	iterWant(t, []int{1, 0}, closes2, "closes (LIFO, exactly once)")
}

// iterProv provides iterKey.
type iterProv struct{}

func (c *iterProv) Name() string                 { return "iter-prov" }
func (c *iterProv) Inject() []runtime.Dependency { return nil }
func (c *iterProv) Provide() []runtime.Capability {
	return []runtime.Capability{iterKey.Capability()}
}
func (c *iterProv) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, iterKey, "P"); err != nil {
		return nil, err
	}
	return nil, nil
}

var iterKey = runtime.NewKey[string]("iter.dep")

func iterDeps() []runtime.Dependency { return []runtime.Dependency{runtime.Requires(iterKey)} }

// TestThm70IteratorDivertOnDependencyLoss — Thm70 at iteration granularity: a
// consumer mid-activation whose provider withdraws diverts at the next
// boundary, unwinds with accumulated inverses, ends Pending (dependency loss,
// not failure, Err()==nil). Recovery is a fresh full iterator once the
// provider returns.
func TestThm70IteratorDivertOnDependencyLoss(t *testing.T) {
	rt := newTestRuntime(t)
	pf, err := rt.Load(&iterProv{})
	if err != nil {
		t.Fatal(err)
	}
	waitStateT(t, pf, runtime.StateActive)

	res := &iterRes{}
	landed := make(chan int, 8)
	proceed := make(chan struct{}, 8)
	cf, err := rt.Load(&iterComp{steps: 4, res: res, landed: landed, proceed: proceed, deps: iterDeps(), failAt: -1})
	if err != nil {
		t.Fatal(err)
	}
	// Steps 0 and 1 land against P.
	if got := iterLand(t, landed); got != 0 {
		t.Fatalf("landed step %d, want 0", got)
	}
	proceed <- struct{}{}
	if got := iterLand(t, landed); got != 1 {
		t.Fatalf("landed step %d, want 1", got)
	}
	// Withdraw P, then release the boundary: the probe must divert (the
	// consumer's activation must end BEFORE the provider can finish its own
	// unload — consumer-first withdrawal gates P's Gone on cf's end).
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	proceed <- struct{}{}
	if err := cf.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("consumer did not divert: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	if err := cf.Err(); err != nil {
		t.Fatalf("dependency-loss divert must not be a failure: %v", err)
	}
	waitStateT(t, cf, runtime.StatePending)
	opens2, closes2 := res.snap()
	iterWant(t, []int{0, 1}, opens2, "opens")
	iterWant(t, []int{1, 0}, closes2, "closes")

	// Recovery: P returns; the consumer re-activates with a FRESH iterator
	// (step 0 opens again — the new activation starts from the beginning).
	pf2, err := rt.Load(&iterProv{})
	if err != nil {
		t.Fatal(err)
	}
	waitStateT(t, pf2, runtime.StateActive)
	if got := iterLand(t, landed); got != 0 {
		t.Fatalf("fresh activation landed step %d, want 0 (iterator restarts)", got)
	}
	// Let the remaining steps run to completion.
	proceed <- struct{}{}
	if got := iterLand(t, landed); got != 1 {
		t.Fatalf("landed step %d, want 1", got)
	}
	proceed <- struct{}{}
	if got := iterLand(t, landed); got != 2 {
		t.Fatalf("landed step %d, want 2", got)
	}
	proceed <- struct{}{}
	if got := iterLand(t, landed); got != 3 {
		t.Fatalf("landed step %d, want 3", got)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer did not reach Active after full iterator: %v", err)
	}
	if err := cf.Dispose(); err != nil {
		t.Fatalf("dispose after completion: %v", err)
	}
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone after completion: %v", err)
	}
	if err := cf.Err(); err != nil {
		t.Fatalf("completed activation must not be Failed: %v", err)
	}
	opens3, closes3 := res.snap()
	// First (diverted) activation: [0 1]; second (complete): [0 1 2 3].
	iterWant(t, []int{0, 1, 0, 1, 2, 3}, opens3, "opens across activations")
	iterWant(t, []int{1, 0, 3, 2, 1, 0}, closes3, "closes across activations (LIFO per activation)")
}

// TestThm68IteratorRaiseMidActivation — a step error is a paper raise: the
// activation unwinds landed inverses exactly once (LIFO), the fiber is Failed
// with the raise as its outcome, and the outcome blocks re-entry (no
// automatic retry).
func TestThm68IteratorRaiseMidActivation(t *testing.T) {
	rt := newTestRuntime(t)
	res := &iterRes{}
	landed := make(chan int, 8)
	proceed := make(chan struct{}, 8)
	f, err := rt.Load(&iterComp{steps: 4, res: res, landed: landed, proceed: proceed, failAt: 2})
	if err != nil {
		t.Fatal(err)
	}
	if got := iterLand(t, landed); got != 0 {
		t.Fatalf("landed step %d, want 0", got)
	}
	proceed <- struct{}{}
	if got := iterLand(t, landed); got != 1 {
		t.Fatalf("landed step %d, want 1", got)
	}
	proceed <- struct{}{} // boundary before the raising step 2
	waitStateT(t, f, runtime.StateFailed)
	if err := f.Err(); err == nil {
		t.Fatal("raise must surface as the fiber outcome")
	}
	opens2, closes2 := res.snap()
	iterWant(t, []int{0, 1}, opens2, "opens")
	iterWant(t, []int{1, 0}, closes2, "closes (exactly once, LIFO)")
	// Terminal: the outcome withholds re-entry; disposal (not a retry) ends it.
	if err := f.Dispose(); err != nil {
		t.Fatalf("dispose after raise: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone after raise: %v", err)
	}
	if st := f.State(); st != runtime.StateGone {
		t.Fatalf("state after Gone = %v, want Gone (no auto re-entry)", st)
	}
}

func waitStateT(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting %s -> %v (state %v, err %v)", f.Name(), want, f.State(), f.Err())
}
