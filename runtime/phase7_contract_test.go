package runtime_test

import (
	"sync"
	"sync/atomic"
	"testing"

	"dynamic-runtime/runtime"
)

// TestConcurrentLoadDisposeManyFibersCleanupOnce is the same stress but keeps
// fiber references to assert terminal state and per-fiber cleanup counts.
func TestConcurrentLoadDisposeManyFibersCleanupOnce(t *testing.T) {
	rt := newTestRuntime(t)

	const fibers = 6
	const cycles = 25

	type item struct {
		f        *runtime.Fiber
		applies  atomic.Int32
		cleanups atomic.Int32
	}
	items := make([]*item, fibers)
	var wg sync.WaitGroup

	for i := 0; i < fibers; i++ {
		it := &item{}
		items[i] = it
		comp := newFakeComponent("cycle")
		comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
			it.applies.Add(1)
			return func() error {
				it.cleanups.Add(1)
				return nil
			}, nil
		}
		f, err := rt.Load(comp)
		if err != nil {
			t.Fatalf("Load error: %v", err)
		}
		it.f = f
		wg.Add(1)
		go func(f *runtime.Fiber) {
			defer wg.Done()
			for j := 0; j < cycles; j++ {
				_ = f.Dispose()
				_ = f.Load()
			}
			_ = f.Dispose()
		}(f)
	}
	wg.Wait()

	totalApplies := int32(0)
	for _, it := range items {
		if err := it.f.Gone(testTimeout(t)); err != nil {
			t.Fatalf("Gone error: %v", err)
		}
		if got := it.f.State(); got != runtime.StateGone {
			t.Fatalf("state = %v, want Gone", got)
		}
		a := it.applies.Load()
		c := it.cleanups.Load()
		if a != c {
			t.Fatalf("applies=%d cleanups=%d: every activation's cleanup must run exactly once", a, c)
		}
		totalApplies += a
	}
	// Under last-writer-wins semantics a fast Dispose may beat a queued Load,
	// so individual fibers may never activate; the storm as a whole must have
	// exercised real activations.
	if totalApplies == 0 {
		t.Fatal("no activation happened at all during the storm")
	}
}

// Spec 34 / last-writer-wins: once a Dispose has actually taken effect on an
// in-flight Apply (cooperative cancellation observed), a subsequent Load must
// first let the current transition complete, then start a fresh activation.
func TestDisposeLoadDuringApplyRestarts(t *testing.T) {
	rt := newTestRuntime(t)
	applied := make(chan struct{})
	canceled := make(chan struct{})
	firstReturned := make(chan struct{})
	var applies atomic.Int32

	comp := newFakeComponent("restart")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		n := applies.Add(1)
		if n == 1 {
			close(applied)
			<-ctx.Done()
			close(canceled)
			close(firstReturned)
			// Cooperative cancellation observed; Apply finishes normally.
			return nil, nil
		}
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	<-applied

	// Dispose and wait until the in-flight Apply observes the cancellation.
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	<-canceled
	<-firstReturned

	// Now remount: the previous transition completes, then a fresh activation
	// starts.
	if err := f.Load(); err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error after reload: %v", err)
	}
	if n := applies.Load(); n != 2 {
		t.Fatalf("applies = %d, want 2 (a fresh activation after restart)", n)
	}
	if got := f.State(); got != runtime.StateActive {
		t.Fatalf("state = %v, want Active", got)
	}

	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// Concurrent Dispose from many goroutines while Load also races: final state
// must be consistent and cleanup exactly once.
func TestConcurrentDisposeLoadRaceConsistent(t *testing.T) {
	rt := newTestRuntime(t)
	var cleanups atomic.Int32
	var applies atomic.Int32

	comp := newFakeComponent("race")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		applies.Add(1)
		return func() error {
			cleanups.Add(1)
			return nil
		}, nil
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}

	var wg sync.WaitGroup
	for i := 0; i < 12; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := 0; j < 10; j++ {
				_ = f.Load()
				_ = f.Dispose()
			}
		}()
	}
	wg.Wait()

	// Force a deterministic terminal state.
	_ = f.Dispose()
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
	a, c := applies.Load(), cleanups.Load()
	if a != c {
		t.Fatalf("applies=%d cleanups=%d: cleanup must run exactly once per activation", a, c)
	}
}
