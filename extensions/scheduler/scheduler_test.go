package scheduler_test

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/scheduler"
)

// ---------------------------------------------------------------------------
// Deterministic Schedule math
// ---------------------------------------------------------------------------

func TestOnceNext(t *testing.T) {
	at := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)
	o := scheduler.Once{At: at}

	if next, ok := o.Next(at.Add(-time.Second)); !ok || !next.Equal(at) {
		t.Fatalf("Next(before At) = %v,%v; want At,true", next, ok)
	}
	if _, ok := o.Next(at); ok {
		t.Fatalf("Next(At) must be false (At is not after At)")
	}
	if _, ok := o.Next(at.Add(time.Second)); ok {
		t.Fatalf("Next(after At) must be false")
	}
}

func TestIntervalNextTheoretical(t *testing.T) {
	start := time.Date(2026, 1, 2, 3, 4, 5, 0, time.UTC)
	every := 10 * time.Millisecond
	in := scheduler.Interval{Start: start, Every: every}

	// First tick is Start + Every.
	if next, ok := in.Next(start); !ok || !next.Equal(start.Add(every)) {
		t.Fatalf("Next(Start) = %v,%v; want Start+Every", next, ok)
	}
	// Boundary: at an exact tick, the next is the following tick.
	if next, ok := in.Next(start.Add(every)); !ok || !next.Equal(start.Add(2*every)) {
		t.Fatalf("Next(Start+Every) = %v,%v; want Start+2Every", next, ok)
	}
	// Mid-tick.
	if next, ok := in.Next(start.Add(25 * time.Millisecond)); !ok || !next.Equal(start.Add(30*time.Millisecond)) {
		t.Fatalf("Next(+25ms) = %v,%v; want Start+30ms", next, ok)
	}
	// Far late -> skip missed ticks, jump to the first future tick.
	if next, ok := in.Next(start.Add(1234 * time.Millisecond)); !ok || !next.Equal(start.Add(1240*time.Millisecond)) {
		t.Fatalf("Next(+1234ms) = %v,%v; want Start+1240ms", next, ok)
	}

	// P3 — monotonic theoretical schedule: repeated Next never goes backwards.
	after := start
	prev := time.Time{}
	for i := 0; i < 100; i++ {
		next, ok := in.Next(after)
		if !ok {
			t.Fatal("interval ended unexpectedly")
		}
		if !next.After(prev) || !next.After(after) {
			t.Fatalf("non-monotonic: after=%v next=%v prev=%v", after, next, prev)
		}
		prev = next
		after = next
	}
}

func TestIntervalZeroStartIsUnanchored(t *testing.T) {
	in := scheduler.Interval{Every: time.Second}
	if _, ok := in.Next(time.Now()); ok {
		t.Fatal("an unanchored Interval must not produce a trigger before registration")
	}
}

// ---------------------------------------------------------------------------
// Helpers (real-time, tolerance based)
// ---------------------------------------------------------------------------

func newSched(t *testing.T) *scheduler.Scheduler {
	t.Helper()
	s := scheduler.New()
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
		defer cancel()
		_ = s.CloseContext(ctx)
	})
	return s
}

type startRecorder struct {
	mu     sync.Mutex
	starts []time.Time
	maxC   int32
	cur    int32
}

func (r *startRecorder) begin() {
	r.mu.Lock()
	r.starts = append(r.starts, time.Now())
	r.mu.Unlock()
	n := atomic.AddInt32(&r.cur, 1)
	for {
		m := atomic.LoadInt32(&r.maxC)
		if n <= m || atomic.CompareAndSwapInt32(&r.maxC, m, n) {
			break
		}
	}
}

func (r *startRecorder) end() { atomic.AddInt32(&r.cur, -1) }

func (r *startRecorder) count() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return len(r.starts)
}

func (r *startRecorder) snapshot() []time.Time {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]time.Time, len(r.starts))
	copy(out, r.starts)
	return out
}

func eventually(t *testing.T, timeout time.Duration, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(timeout)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("condition not met within %v: %s", timeout, what)
}

// sleepTask records a start and optionally sleeps.
func sleepTask(r *startRecorder, d time.Duration) scheduler.Task {
	return func(ctx context.Context) error {
		r.begin()
		defer r.end()
		if d > 0 {
			select {
			case <-time.After(d):
			case <-ctx.Done():
			}
		}
		return nil
	}
}

// ---------------------------------------------------------------------------
// S1 — Once: exactly one execution
// ---------------------------------------------------------------------------

func TestS1Once(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}

	if err := s.Add(scheduler.Job{ID: "once", Schedule: scheduler.Once{At: time.Now().Add(60 * time.Millisecond)}, Task: sleepTask(r, 0)}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 3*time.Second, "once job to fire", func() bool { return r.count() == 1 })
	time.Sleep(200 * time.Millisecond)
	if got := r.count(); got != 1 {
		t.Fatalf("Once fired %d times, want exactly 1", got)
	}
}

// A Once whose time is already past fires exactly once, promptly.
func TestS1OnceAlreadyPast(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "once-past", Schedule: scheduler.Once{At: time.Now().Add(-time.Second)}, Task: sleepTask(r, 0)}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 3*time.Second, "past once to fire", func() bool { return r.count() == 1 })
	time.Sleep(150 * time.Millisecond)
	if got := r.count(); got != 1 {
		t.Fatalf("past Once fired %d times, want exactly 1", got)
	}
}

// ---------------------------------------------------------------------------
// S2 — Interval: repeated execution
// ---------------------------------------------------------------------------

func TestS2Interval(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "iv", Schedule: scheduler.Interval{Every: 20 * time.Millisecond}, Task: sleepTask(r, 0)}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 3*time.Second, "at least 4 interval ticks", func() bool { return r.count() >= 4 })
}

// ---------------------------------------------------------------------------
// S3 — Interval theoretical timeline (not completion + Every)
// ---------------------------------------------------------------------------

func TestS3TheoreticalTimeline(t *testing.T) {
	// Task (30ms) is shorter than Every (150ms) so there is no overrun: the
	// next execution starts on the theoretical grid at Start+2*Every, i.e.
	// 150ms after the first start - NOT 30+150=180ms after completion-based
	// scheduling.
	s := newSched(t)
	r := &startRecorder{}
	if err := s.Add(scheduler.Job{
		ID:       "theo",
		Schedule: scheduler.Interval{Every: 150 * time.Millisecond},
		Task:     sleepTask(r, 30*time.Millisecond),
	}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 4*time.Second, "two executions", func() bool { return r.count() >= 2 })
	starts := r.snapshot()
	// The scheduler loop itself adds a small scheduling delta; keep the window
	// wide enough for the theoretical 150ms but exclude completion+Every 180ms.
	// (Actually with task < Every, the next start is on-grid: gap == Every.)
	if gap := starts[1].Sub(starts[0]); gap < 130*time.Millisecond || gap > 175*time.Millisecond {
		t.Fatalf("interval between starts = %v, want ~150ms (theoretical), not ~180ms (completion+Every)", gap)
	}
}

// ---------------------------------------------------------------------------
// S4 / P1 / P7 — overrun skip: same Job never overlaps; no catch-up storm
// ---------------------------------------------------------------------------

func TestS4OverrunNoOverlapNoCatchUp(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	// Every 15ms, Task 60ms -> most ticks are skipped.
	if err := s.Add(scheduler.Job{
		ID:       "overrun",
		Schedule: scheduler.Interval{Every: 15 * time.Millisecond},
		Task:     sleepTask(r, 60*time.Millisecond),
	}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	time.Sleep(350 * time.Millisecond)

	// P1: no overlap for this Job.
	if max := atomic.LoadInt32(&r.maxC); max > 1 {
		t.Fatalf("max concurrent executions of one job = %d, want 1", max)
	}
	// P7: no catch-up storm - a naive per-tick schedule would have produced
	// ~23 executions in 350ms at 15ms cadence; with skip it is only a handful.
	n := r.count()
	if n < 2 || n > 8 {
		t.Fatalf("overrun job executed %d times in 350ms (every=15ms, task=60ms), want a small skipped count", n)
	}
}

// ---------------------------------------------------------------------------
// S5 / P2 — Job isolation
// ---------------------------------------------------------------------------

func TestS5JobIsolation(t *testing.T) {
	s := newSched(t)
	slow := &startRecorder{}
	fast := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "slow", Schedule: scheduler.Interval{Every: 30 * time.Millisecond}, Task: sleepTask(slow, 120*time.Millisecond)}); err != nil {
		t.Fatalf("Add slow: %v", err)
	}
	// Wait until the slow job is actually running (blocking the slow job's
	// own future ticks), then add a fast job.
	eventually(t, 3*time.Second, "slow job to start", func() bool { return slow.count() >= 1 })
	if err := s.Add(scheduler.Job{ID: "fast", Schedule: scheduler.Interval{Every: 10 * time.Millisecond}, Task: sleepTask(fast, 0)}); err != nil {
		t.Fatalf("Add fast: %v", err)
	}
	eventually(t, 3*time.Second, "fast job to execute while slow job is running", func() bool {
		return fast.count() >= 4 && atomic.LoadInt32(&slow.cur) == 1
	})
}

// ---------------------------------------------------------------------------
// S6 / S7 — Add semantics
// ---------------------------------------------------------------------------

func TestS6AddThenTrigger(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "a", Schedule: scheduler.Once{At: time.Now().Add(30 * time.Millisecond)}, Task: sleepTask(r, 0)}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 3*time.Second, "added job to trigger", func() bool { return r.count() == 1 })
}

func TestS7DuplicateAdd(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	job := scheduler.Job{ID: "dup", Schedule: scheduler.Interval{Every: 20 * time.Millisecond}, Task: sleepTask(r, 0)}
	if err := s.Add(job); err != nil {
		t.Fatalf("first Add: %v", err)
	}
	if err := s.Add(job); !errors.Is(err, scheduler.ErrJobExists) {
		t.Fatalf("duplicate Add = %v, want ErrJobExists", err)
	}
	// Original job unaffected.
	eventually(t, 3*time.Second, "original job to keep running", func() bool { return r.count() >= 2 })
}

// ---------------------------------------------------------------------------
// S8 / P4 — Remove stops future executions
// ---------------------------------------------------------------------------

func TestS8Remove(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "r", Schedule: scheduler.Interval{Every: 15 * time.Millisecond}, Task: sleepTask(r, 0)}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 3*time.Second, "job to run at least once", func() bool { return r.count() >= 1 })

	if err := s.Remove("r"); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	if err := s.Remove("r"); !errors.Is(err, scheduler.ErrJobNotFound) {
		t.Fatalf("second Remove = %v, want ErrJobNotFound", err)
	}
	time.Sleep(50 * time.Millisecond) // settle any start linearized before Remove
	c1 := r.count()
	time.Sleep(200 * time.Millisecond)
	if got := r.count(); got != c1 {
		t.Fatalf("executions continued after Remove: %d -> %d", c1, got)
	}
}

// ---------------------------------------------------------------------------
// S9 — Remove of a running job: context canceled, not force-killed
// ---------------------------------------------------------------------------

func TestS9RemoveRunningJobCancelsContext(t *testing.T) {
	s := newSched(t)
	started := make(chan struct{})
	canceled := make(chan struct{})
	released := make(chan struct{})
	var ran atomic.Bool

	task := func(ctx context.Context) error {
		ran.Store(true)
		close(started)
		select {
		case <-ctx.Done():
			close(canceled)
			return ctx.Err()
		case <-released:
			return nil
		}
	}
	if err := s.Add(scheduler.Job{ID: "run", Schedule: scheduler.Once{At: time.Now().Add(10 * time.Millisecond)}, Task: task}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	<-started

	if err := s.Remove("run"); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	<-canceled // the running task observed its context cancellation
	if !ran.Load() {
		t.Fatal("task never ran")
	}
}

func TestS9RemoveDoesNotForceKillStubbornTask(t *testing.T) {
	s := newSched(t)
	started := make(chan struct{})
	release := make(chan struct{})
	done := make(chan struct{})

	task := func(ctx context.Context) error {
		close(started)
		<-release // ignores ctx.Done entirely
		close(done)
		return nil
	}
	if err := s.Add(scheduler.Job{ID: "stub", Schedule: scheduler.Once{At: time.Now().Add(10 * time.Millisecond)}, Task: task}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	<-started

	// Remove must return without killing the running task.
	if err := s.Remove("stub"); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	select {
	case <-done:
		t.Fatal("stubborn task was killed by Remove")
	case <-time.After(50 * time.Millisecond):
		// still running, as expected
	}
	close(release)
	<-done
}

// ---------------------------------------------------------------------------
// S10 / S11 / P5 / P8 — Close
// ---------------------------------------------------------------------------

func TestS10Close(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	_ = s.Add(scheduler.Job{ID: "c", Schedule: scheduler.Interval{Every: 15 * time.Millisecond}, Task: sleepTask(r, 0)})
	eventually(t, 3*time.Second, "job to run before close", func() bool { return r.count() >= 1 })

	if err := s.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	time.Sleep(50 * time.Millisecond)
	c1 := r.count()
	time.Sleep(200 * time.Millisecond)
	if got := r.count(); got != c1 {
		t.Fatalf("executions continued after Close: %d -> %d", c1, got)
	}
	if err := s.Add(scheduler.Job{ID: "x", Schedule: scheduler.Once{At: time.Now().Add(time.Millisecond)}, Task: sleepTask(&startRecorder{}, 0)}); !errors.Is(err, scheduler.ErrSchedulerClosed) {
		t.Fatalf("Add after Close = %v, want ErrSchedulerClosed", err)
	}
	if err := s.Remove("c"); !errors.Is(err, scheduler.ErrSchedulerClosed) {
		t.Fatalf("Remove after Close = %v, want ErrSchedulerClosed", err)
	}
}

func TestS11CloseIdempotent(t *testing.T) {
	s := scheduler.New()
	for i := 0; i < 3; i++ {
		if err := s.Close(); err != nil {
			t.Fatalf("Close #%d: %v", i, err)
		}
	}
}

// ---------------------------------------------------------------------------
// S12 / P6 — Task panic isolation
// ---------------------------------------------------------------------------

func TestS12TaskPanicIsolation(t *testing.T) {
	s := newSched(t)
	good := &startRecorder{}

	panicky := func(context.Context) error {
		panic("boom")
	}
	if err := s.Add(scheduler.Job{ID: "panic", Schedule: scheduler.Interval{Every: 20 * time.Millisecond}, Task: panicky}); err != nil {
		t.Fatalf("Add panic job: %v", err)
	}
	if err := s.Add(scheduler.Job{ID: "good", Schedule: scheduler.Interval{Every: 15 * time.Millisecond}, Task: sleepTask(good, 0)}); err != nil {
		t.Fatalf("Add good job: %v", err)
	}

	// The scheduler must survive panics and keep scheduling the good job, and
	// still accept new jobs.
	eventually(t, 3*time.Second, "good job keeps running despite panics", func() bool { return good.count() >= 3 })
	extra := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "extra", Schedule: scheduler.Once{At: time.Now().Add(20 * time.Millisecond)}, Task: sleepTask(extra, 0)}); err != nil {
		t.Fatalf("Add after panics: %v", err)
	}
	eventually(t, 3*time.Second, "extra job runs after panics", func() bool { return extra.count() == 1 })
}

// ---------------------------------------------------------------------------
// S13 — Concurrent Add/Remove
// ---------------------------------------------------------------------------

func TestS13ConcurrentAddRemove(t *testing.T) {
	s := newSched(t)
	const workers = 16
	const perWorker = 20
	var wg sync.WaitGroup
	for w := 0; w < workers; w++ {
		wg.Add(1)
		go func(w int) {
			defer wg.Done()
			for i := 0; i < perWorker; i++ {
				id := scheduler.JobID(fmt.Sprintf("w%d-j%d", w, i))
				job := scheduler.Job{ID: id, Schedule: scheduler.Once{At: time.Now().Add(time.Hour)}, Task: func(context.Context) error { return nil }}
				if err := s.Add(job); err != nil {
					t.Errorf("Add %s: %v", id, err)
					return
				}
				if err := s.Remove(id); err != nil {
					t.Errorf("Remove %s: %v", id, err)
					return
				}
			}
		}(w)
	}
	wg.Wait()
	// Everything removed: a final Remove must report not found for each.
	for w := 0; w < workers; w++ {
		for i := 0; i < perWorker; i++ {
			id := scheduler.JobID(fmt.Sprintf("w%d-j%d", w, i))
			if err := s.Remove(id); !errors.Is(err, scheduler.ErrJobNotFound) {
				t.Fatalf("Remove %s after cleanup = %v, want ErrJobNotFound", id, err)
			}
		}
	}
}

// ---------------------------------------------------------------------------
// S14 / P4 — Trigger/Remove race: no new execution after Remove
// ---------------------------------------------------------------------------

func TestS14TriggerRemoveRace(t *testing.T) {
	s := newSched(t)
	r := &startRecorder{}
	if err := s.Add(scheduler.Job{ID: "race", Schedule: scheduler.Interval{Every: 10 * time.Millisecond}, Task: sleepTask(r, 0)}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	eventually(t, 3*time.Second, "job running", func() bool { return r.count() >= 2 })
	if err := s.Remove("race"); err != nil {
		t.Fatalf("Remove: %v", err)
	}
	time.Sleep(60 * time.Millisecond) // settle starts linearized before Remove
	c1 := r.count()
	time.Sleep(250 * time.Millisecond)
	if got := r.count(); got != c1 {
		t.Fatalf("execution started after Remove linearized: %d -> %d", c1, got)
	}
}

// ---------------------------------------------------------------------------
// S18 — CloseContext timeout: stubborn task
// ---------------------------------------------------------------------------

func TestS18CloseContextTimeout(t *testing.T) {
	s := scheduler.New()
	started := make(chan struct{})
	release := make(chan struct{})
	task := func(ctx context.Context) error {
		close(started)
		<-release // ignores ctx.Done
		return nil
	}
	if err := s.Add(scheduler.Job{ID: "stub", Schedule: scheduler.Once{At: time.Now().Add(5 * time.Millisecond)}, Task: task}); err != nil {
		t.Fatalf("Add: %v", err)
	}
	<-started

	short, cancel := context.WithTimeout(context.Background(), 80*time.Millisecond)
	defer cancel()
	if err := s.CloseContext(short); !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("CloseContext = %v, want deadline exceeded", err)
	}
	// Even after timeout the scheduler rejects new jobs and further Remove.
	if err := s.Add(scheduler.Job{ID: "x", Schedule: scheduler.Once{At: time.Now()}, Task: task}); !errors.Is(err, scheduler.ErrSchedulerClosed) {
		t.Fatalf("Add after timed-out Close = %v, want ErrSchedulerClosed", err)
	}

	// Once the stubborn task returns, the scheduler drains and Close succeeds.
	close(release)
	eventually(t, 3*time.Second, "scheduler drained", func() bool {
		return s.CloseContext(context.Background()) == nil
	})
}
