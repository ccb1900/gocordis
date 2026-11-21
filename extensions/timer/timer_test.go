package timer_test

import (
	"context"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/timer"
	"dynamic-runtime/runtime"
)

// capture is a consumer that reads the provided service out of the Runtime.
// The service is obtained only through the declared capability, never through
// a package-level singleton or a back door into the component.
type capture struct {
	ch chan timer.Service
}

func (c *capture) Name() string { return "capture" }
func (c *capture) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(timer.ServiceKey)}
}
func (c *capture) Provide() []runtime.Capability { return nil }
func (c *capture) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	s, err := runtime.Require(ctx, timer.ServiceKey)
	if err != nil {
		return nil, err
	}
	c.ch <- s
	return nil, nil
}

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// loadService loads a real timer.Component plus a consumer, waits for both to
// become Active, and returns the service exactly as an application would see
// it through capability resolution.
func loadService(t *testing.T) (timer.Service, *runtime.Fiber) {
	t.Helper()
	ctx := testCtx(t)
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() { _ = rt.Close(testCtx(t)) })

	cap := &capture{ch: make(chan timer.Service, 1)}
	f, err := rt.Load(&timer.Component{})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	cf, err := rt.Load(cap)
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	if err := cf.Ready(ctx); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	select {
	case s := <-cap.ch:
		return s, f
	case <-ctx.Done():
		t.Fatal("service capability never delivered")
		return nil, nil
	}
}

func mustRecv[T any](t *testing.T, ch <-chan T, d time.Duration) T {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(d):
		t.Fatalf("timed out waiting for callback after %v", d)
		var zero T
		return zero
	}
}

func assertNoRecv[T any](t *testing.T, ch <-chan T, d time.Duration) {
	t.Helper()
	select {
	case v := <-ch:
		t.Fatalf("unexpected callback: %v", v)
	case <-time.After(d):
	}
}

func dispose(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
}

func TestTimeoutFiresOnceThenSelfRemoves(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)

	ch := make(chan struct{}, 4)
	var count atomic.Int32
	h := svc.Timeout(func() {
		count.Add(1)
		ch <- struct{}{}
	}, 20*time.Millisecond)

	mustRecv(t, ch, time.Second)
	assertNoRecv(t, ch, 80*time.Millisecond)
	if got := count.Load(); got != 1 {
		t.Fatalf("callback count = %d, want 1", got)
	}
	// A one-shot timer has already self-removed, so Stop reports false.
	if h.Stop() {
		t.Fatal("Stop on an already-fired Timeout should report false")
	}
	if h.Stop() {
		t.Fatal("Stop must be idempotent")
	}
}

func TestIntervalRepeatsUntilStopped(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)

	ch := make(chan int, 16)
	var n atomic.Int32
	h := svc.Interval(func() {
		ch <- int(n.Add(1))
	}, 20*time.Millisecond)

	for i := 1; i <= 3; i++ {
		if got := mustRecv(t, ch, time.Second); got != i {
			t.Fatalf("tick %d = %d", i, got)
		}
	}
	if !h.Stop() {
		t.Fatal("Stop on a live Interval should report true")
	}
	if h.Stop() {
		t.Fatal("Stop must be idempotent")
	}
	assertNoRecv(t, ch, 80*time.Millisecond)
}

func TestDebounceCoalescesBurst(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)

	const d = 60 * time.Millisecond
	ch := make(chan struct{}, 8)
	var count atomic.Int32
	debounced := svc.Debounce(func() {
		count.Add(1)
		ch <- struct{}{}
	}, d)

	// A burst of calls with no quiet period must collapse into a single fire.
	for i := 0; i < 6; i++ {
		debounced()
	}
	mustRecv(t, ch, time.Second)
	assertNoRecv(t, ch, 3*d/2)
	if got := count.Load(); got != 1 {
		t.Fatalf("debounced callback count = %d, want 1", got)
	}

	// A fresh burst after the quiet period is a fresh, single fire.
	for i := 0; i < 3; i++ {
		debounced()
	}
	mustRecv(t, ch, time.Second)
	if got := count.Load(); got != 2 {
		t.Fatalf("debounced callback count = %d, want 2", got)
	}
}

func TestThrottleLeadingAndTrailing(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)

	const d = 80 * time.Millisecond
	ch := make(chan struct{}, 8)
	var count atomic.Int32
	throttled := svc.Throttle(func() {
		count.Add(1)
		ch <- struct{}{}
	}, d)

	throttled() // leading edge: fires immediately
	mustRecv(t, ch, time.Second)
	// Calls inside the window are coalesced into a single trailing fire.
	throttled()
	throttled()
	throttled()
	mustRecv(t, ch, time.Second)
	assertNoRecv(t, ch, 3*d/2)
	if got := count.Load(); got != 2 {
		t.Fatalf("throttled callback count = %d, want 2 (leading + trailing)", got)
	}
}

// TestDisposeJoinsRunningCallback proves the service's effect inverse waits
// for a callback already in flight before the fiber can reach Gone.
func TestDisposeJoinsRunningCallback(t *testing.T) {
	svc, f := loadService(t)

	started := make(chan struct{}, 1)
	release := make(chan struct{})
	var after atomic.Int32
	svc.Interval(func() {
		select {
		case started <- struct{}{}:
		default:
		}
		<-release // deliberately long-running callback
		after.Add(1)
	}, 5*time.Millisecond)

	mustRecv(t, started, time.Second)
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}

	// While the callback is blocked, Gone must NOT complete: disposal joins it.
	short, cancel := context.WithTimeout(context.Background(), 120*time.Millisecond)
	defer cancel()
	if err := f.Gone(short); err == nil {
		t.Fatal("Gone completed while a callback was still running: callbacks not joined")
	}

	close(release)
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatalf("Gone after release: %v", err)
	}
	if got := after.Load(); got != 1 {
		t.Fatalf("in-flight callback completion count = %d, want 1", got)
	}
}

// TestCallbacksDoNotFireAfterDispose checks every timer kind against a
// disposed service: no post-dispose fire, and all calls remain safe.
func TestCallbacksDoNotFireAfterDispose(t *testing.T) {
	svc, f := loadService(t)
	dispose(t, f)

	ch := make(chan struct{}, 8)
	fn := func() { ch <- struct{}{} }
	h1 := svc.Timeout(fn, 5*time.Millisecond)
	h2 := svc.Interval(fn, 5*time.Millisecond)
	debounced := svc.Debounce(fn, 5*time.Millisecond)
	throttled := svc.Throttle(fn, 5*time.Millisecond)
	debounced()
	throttled()
	_ = h1
	_ = h2
	assertNoRecv(t, ch, 60*time.Millisecond)
	if h1.Stop() || h2.Stop() {
		t.Fatal("Stop on a disposed service should report false")
	}
}

func TestConcurrentStopsAreSafe(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)

	h := svc.Interval(func() {}, time.Hour)
	const goroutines = 16
	done := make(chan struct{}, goroutines)
	var stopped atomic.Int32
	for i := 0; i < goroutines; i++ {
		go func() {
			if h.Stop() {
				stopped.Add(1)
			}
			done <- struct{}{}
		}()
	}
	for i := 0; i < goroutines; i++ {
		mustRecv(t, done, time.Second)
	}
	if got := stopped.Load(); got != 1 {
		t.Fatalf("exactly one Stop should win, got %d", got)
	}
}

func TestThrottleNoTrailing(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)

	const d = 80 * time.Millisecond
	ch := make(chan struct{}, 8)
	var count atomic.Int32
	throttled := svc.ThrottleNoTrailing(func() {
		count.Add(1)
		ch <- struct{}{}
	}, d)

	throttled() // leading edge fires
	mustRecv(t, ch, time.Second)
	throttled()
	throttled()
	throttled() // all inside the window: dropped, no trailing fire
	assertNoRecv(t, ch, 2*d)
	if got := count.Load(); got != 1 {
		t.Fatalf("no-trailing callback count = %d, want 1", got)
	}

	// The window has expired, so the next call is a fresh leading fire.
	throttled()
	mustRecv(t, ch, time.Second)
	if got := count.Load(); got != 2 {
		t.Fatalf("no-trailing callback count = %d, want 2", got)
	}
}

// owningConsumer installs a debounce handle as its OWN effect, so the timer
// must unwind with the consumer rather than with the timer service.
type owningConsumer struct {
	ch    chan func()
	calls *atomic.Int32
	d     time.Duration
}

func (c *owningConsumer) Name() string {
	return "owner"
}
func (c *owningConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(timer.ServiceKey)}
}
func (c *owningConsumer) Provide() []runtime.Capability { return nil }
func (c *owningConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, timer.ServiceKey)
	if err != nil {
		return nil, err
	}
	trigger, stop := svc.DebounceHandle(func() { c.calls.Add(1) }, c.d)
	if err := timer.Owned(ctx, trigger, stop); err != nil {
		_ = stop()
		return nil, err
	}
	c.ch <- trigger
	return nil, nil
}

func TestOwnedDebounceStopsWithConsumer(t *testing.T) {
	ctx := testCtx(t)
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() { _ = rt.Close(testCtx(t)) })

	if _, err := rt.Load(&timer.Component{}); err != nil {
		t.Fatalf("load provider: %v", err)
	}
	owner := &owningConsumer{
		ch:    make(chan func(), 1),
		calls: &atomic.Int32{},
		d:     400 * time.Millisecond,
	}
	cf, err := rt.Load(owner)
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := cf.Ready(ctx); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	trigger := mustRecv(t, owner.ch, time.Second)

	trigger() // pending fire, well in the future
	if err := cf.Dispose(); err != nil {
		t.Fatalf("dispose consumer: %v", err)
	}
	if err := cf.Gone(testCtx(t)); err != nil {
		t.Fatalf("consumer Gone: %v", err)
	}

	trigger() // post-Gone: must be inert
	<-time.After(2 * owner.d)
	if got := owner.calls.Load(); got != 0 {
		t.Fatalf("owned debounce fired %d times after consumer disposal, want 0", got)
	}
}
