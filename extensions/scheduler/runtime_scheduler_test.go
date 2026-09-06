package scheduler_test

// Runtime integration tests: Scheduler as a Runtime-managed capability over the
// real Kernel (S15-S17). The Kernel is not modified.

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/scheduler"
	"dynamic-runtime/runtime"
)

var schedulerKey = runtime.NewKey[*scheduler.Scheduler]("scheduler")

func schedCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newSchedRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() { _ = rt.Close(schedCtx(t)) })
	return rt
}

func schedWaitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatalf("fiber %s state = %v, want %v", f.Name(), f.State(), want)
}

// schedProvider creates a Scheduler, registers Close as a reversible effect,
// and provides it as a stable capability.
type schedProvider struct {
	name    string
	ch      chan *scheduler.Scheduler
	applies *atomic.Int32
}

func (c *schedProvider) Name() string                 { return c.name }
func (c *schedProvider) Inject() []runtime.Dependency { return nil }
func (c *schedProvider) Provide() []runtime.Capability {
	return []runtime.Capability{schedulerKey.Capability()}
}
func (c *schedProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	s := scheduler.New()
	if c.ch != nil {
		c.ch <- s
	}
	// Runtime-managed cleanup: inverse closes the Scheduler on activation
	// unwind (waits for cooperative running tasks).
	if err := ctx.Effect(func() (func() error, error) {
		return s.Close, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, schedulerKey, s); err != nil {
		return nil, err
	}
	return nil, nil
}

// schedConsumer depends on the Scheduler capability (not on any Job).
type schedConsumer struct {
	name     string
	applies  *atomic.Int32
	inverses *atomic.Int32
	seen     chan *scheduler.Scheduler
}

func (c *schedConsumer) Name() string { return c.name }
func (c *schedConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(schedulerKey)}
}
func (c *schedConsumer) Provide() []runtime.Capability { return nil }
func (c *schedConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	s, err := runtime.Require(ctx, schedulerKey)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		select {
		case c.seen <- s:
		default:
		}
	}
	return func() error {
		if c.inverses != nil {
			c.inverses.Add(1)
		}
		return nil
	}, nil
}

// S15 — Runtime ownership: activation unload closes the Scheduler (future
// triggers stop) with no leak.
func TestS15SchedulerActivationDisposal(t *testing.T) {
	rt := newSchedRT(t)
	ch := make(chan *scheduler.Scheduler, 1)
	prov := &schedProvider{name: "sched-prov", ch: ch}
	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	if err := pf.Ready(schedCtx(t)); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	s := <-ch

	var runs atomic.Int32
	job := scheduler.Job{
		ID:       "tick",
		Schedule: scheduler.Interval{Every: 15 * time.Millisecond},
		Task: func(ctx context.Context) error {
			runs.Add(1)
			return nil
		},
	}
	if err := s.Add(job); err != nil {
		t.Fatalf("Add: %v", err)
	}
	deadline := time.Now().Add(3 * time.Second)
	for runs.Load() == 0 && time.Now().Before(deadline) {
		time.Sleep(2 * time.Millisecond)
	}
	if runs.Load() == 0 {
		t.Fatal("scheduled job never ran")
	}

	// Dispose the owning activation: the registered effect closes the
	// Scheduler; no future trigger may start.
	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := pf.Gone(schedCtx(t)); err != nil {
		t.Fatalf("provider Gone: %v", err)
	}
	if err := s.Add(scheduler.Job{ID: "x", Schedule: scheduler.Once{At: time.Now()}, Task: func(context.Context) error { return nil }}); !errors.Is(err, scheduler.ErrSchedulerClosed) {
		t.Fatalf("Add after disposal = %v, want ErrSchedulerClosed", err)
	}
	time.Sleep(50 * time.Millisecond)
	c1 := runs.Load()
	time.Sleep(150 * time.Millisecond)
	if got := runs.Load(); got != c1 {
		t.Fatalf("executions continued after scheduler disposal: %d -> %d", c1, got)
	}
}

// S16 — Provider stability: Job churn (Add/Remove/Add) never changes the
// Scheduler provider identity and never reactivates the Consumer.
func TestS16JobChurnProviderStable(t *testing.T) {
	rt := newSchedRT(t)

	var pApplies, cApplies, cInverses atomic.Int32
	ch := make(chan *scheduler.Scheduler, 1)
	prov := &schedProvider{name: "sched-prov", ch: ch, applies: &pApplies}
	cons := &schedConsumer{name: "sched-consumer", applies: &cApplies, inverses: &cInverses}

	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(schedCtx(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	s := <-ch

	noop := func(context.Context) error { return nil }
	for i := 0; i < 30; i++ {
		id := scheduler.JobID(string(rune('a'+i%26)) + string(rune('0'+i/26)))
		if err := s.Add(scheduler.Job{ID: id, Schedule: scheduler.Once{At: time.Now().Add(time.Hour)}, Task: noop}); err != nil {
			t.Fatalf("Add %s: %v", id, err)
		}
		if err := s.Remove(id); err != nil {
			t.Fatalf("Remove %s: %v", id, err)
		}
	}
	_ = s.Add(scheduler.Job{ID: "final", Schedule: scheduler.Once{At: time.Now().Add(time.Hour)}, Task: noop})

	if got := pApplies.Load(); got != 1 {
		t.Fatalf("scheduler provider Apply count = %d, want 1", got)
	}
	if got := cApplies.Load(); got != 1 {
		t.Fatalf("consumer Apply count = %d, want 1", got)
	}
	if got := cInverses.Load(); got != 0 {
		t.Fatalf("consumer inverse count = %d, want 0", got)
	}
	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %v, want Active", got)
	}

	_ = pf.Dispose()
	_ = cf.Dispose()
}

// S17 — Provider replacement follows Kernel identity replacement.
func TestS17ProviderReplacement(t *testing.T) {
	rt := newSchedRT(t)

	var cApplies atomic.Int32
	seen := make(chan *scheduler.Scheduler, 4)
	cons := &schedConsumer{name: "sched-consumer", applies: &cApplies, seen: seen}

	ch1 := make(chan *scheduler.Scheduler, 1)
	p1 := &schedProvider{name: "sched-p1", ch: ch1}
	p1f, err := rt.Load(p1)
	if err != nil {
		t.Fatalf("Load P1: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(schedCtx(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	s1 := <-ch1

	// Withdraw P1 fully, then load P2.
	if err := p1f.Dispose(); err != nil {
		t.Fatalf("Dispose P1: %v", err)
	}
	if err := p1f.Gone(schedCtx(t)); err != nil {
		t.Fatalf("P1 Gone: %v", err)
	}
	schedWaitState(t, cf, runtime.StatePending)

	ch2 := make(chan *scheduler.Scheduler, 1)
	p2 := &schedProvider{name: "sched-p2", ch: ch2}
	p2f, err := rt.Load(p2)
	if err != nil {
		t.Fatalf("Load P2: %v", err)
	}
	if err := cf.Ready(schedCtx(t)); err != nil {
		t.Fatalf("consumer Ready after replacement: %v", err)
	}
	s2 := <-ch2

	if got := cApplies.Load(); got != 2 {
		t.Fatalf("consumer Apply count = %d, want 2", got)
	}
	if s1 == s2 {
		t.Fatal("P1 and P2 must be distinct Scheduler instances")
	}
	seen1 := <-seen
	seen2 := <-seen
	if seen1 != s1 {
		t.Fatal("first activation did not resolve P1's scheduler")
	}
	if seen2 != s2 {
		t.Fatal("second activation did not resolve P2's scheduler")
	}
	// P1's scheduler was closed by its activation unload.
	if err := s1.Add(scheduler.Job{ID: "late", Schedule: scheduler.Once{At: time.Now()}, Task: func(context.Context) error { return nil }}); !errors.Is(err, scheduler.ErrSchedulerClosed) {
		t.Fatalf("Add on replaced scheduler = %v, want ErrSchedulerClosed", err)
	}

	_ = p2f.Dispose()
	_ = cf.Dispose()
}
