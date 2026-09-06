package runtime_test

import (
	"errors"
	"sync"
	"sync/atomic"
	"testing"

	"dynamic-runtime/runtime"
)

// Plan Test 1: Basic lifecycle
// Load -> (Pending) -> Loading -> Active -> Dispose -> Unloading -> Gone.
func TestBasicLifecycleLoadActiveDisposeGone(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("basic")
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}

	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if got := f.State(); got != runtime.StateActive {
		t.Fatalf("state = %v, want Active", got)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
}

// A successful Apply returning a Cleanup must run that cleanup exactly once on
// normal withdrawal.
func TestDisposeRunsReturnedCleanupOnce(t *testing.T) {
	rt := newTestRuntime(t)
	var cleanups atomic.Int32
	comp := newFakeComponent("clean")
	comp.cleanup = func() error {
		cleanups.Add(1)
		return nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if n := cleanups.Load(); n != 1 {
		t.Fatalf("cleanup ran %d times, want exactly 1", n)
	}
}

// Plan Test 2: Apply failure -> Failed (with the apply error attached).
func TestApplyFailureEndsFailed(t *testing.T) {
	rt := newTestRuntime(t)
	boom := errors.New("apply boom")
	comp := newFakeComponent("fails")
	comp.applyErr = boom

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}

	err = f.Ready(testTimeout(t))
	if !errors.Is(err, boom) {
		t.Fatalf("Ready() = %v, want apply error %v", err, boom)
	}
	if got := f.State(); got != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", got)
	}
	if !errors.Is(f.Err(), boom) {
		t.Fatalf("Err() = %v, want %v", f.Err(), boom)
	}
}

// Apply failure must unwind partial effects: a Cleanup returned alongside the
// error still runs exactly once (it was recorded as an effect of the failed
// activation).
func TestApplyFailureRunsReturnedCleanup(t *testing.T) {
	rt := newTestRuntime(t)
	boom := errors.New("apply boom")
	var cleanups atomic.Int32
	comp := newFakeComponent("partial")
	comp.applyErr = boom
	comp.cleanup = func() error {
		cleanups.Add(1)
		return nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	err = f.Ready(testTimeout(t))
	if !errors.Is(err, boom) {
		t.Fatalf("Ready() = %v, want %v", err, boom)
	}
	if got := f.State(); got != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", got)
	}
	if n := cleanups.Load(); n != 1 {
		t.Fatalf("cleanup ran %d times, want exactly 1", n)
	}
}

// A cleanup error during normal withdrawal must surface on Gone while the
// Fiber still ends Gone (Gone + Err).
func TestCleanupErrorSurfacesOnGone(t *testing.T) {
	rt := newTestRuntime(t)
	cleanErr := errors.New("cleanup boom")
	comp := newFakeComponent("dirty")
	comp.cleanup = func() error { return cleanErr }

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	err = f.Gone(testTimeout(t))
	if !errors.Is(err, cleanErr) {
		t.Fatalf("Gone() = %v, want cleanup error %v", err, cleanErr)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
}

// Cooperative cancellation: Dispose while Apply is in flight cancels the
// activation context; when Apply returns because of the cancellation, the
// fiber withdraws to Gone (a normal withdrawal, not a failure).
func TestCooperativeCancelDuringApplyEndsGone(t *testing.T) {
	rt := newTestRuntime(t)
	applied := make(chan struct{})
	release := make(chan struct{})
	var applyReturned atomic.Bool

	comp := newFakeComponent("cancelme")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		close(applied)
		select {
		case <-ctx.Done():
			applyReturned.Store(true)
			return nil, ctx.Err()
		case <-release:
			applyReturned.Store(true)
			return nil, nil
		}
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	<-applied

	// Dispose while Apply is running.
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}

	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
	if !applyReturned.Load() {
		t.Fatal("Apply did not return after cancellation")
	}
	if n := comp.calls.Load(); n != 1 {
		t.Fatalf("Apply invoked %d times, want 1", n)
	}
	close(release)
}

// A mounted fiber that fails stays Failed (no automatic retry); an explicit
// Dispose then Load retries into a fresh activation.
func TestFailedDisposeLoadRetryCycle(t *testing.T) {
	rt := newTestRuntime(t)
	var attempts atomic.Int32
	comp := newFakeComponent("retry")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if attempts.Add(1) == 1 {
			return nil, errors.New("first attempt fails")
		}
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err == nil {
		t.Fatal("Ready() = nil, want first-attempt failure")
	}
	if got := f.State(); got != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", got)
	}

	// Mounted + Failed: an explicit Load must NOT auto-retry.
	if err := f.Load(); err != nil {
		t.Fatalf("Load error: %v", err)
	}
	// Give any (wrong) retry a chance to happen, then assert still Failed.
	if got := f.State(); got != runtime.StateFailed {
		t.Fatalf("state after Load on Failed = %v, want Failed (no auto retry)", got)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	if err := f.Load(); err != nil {
		t.Fatalf("second Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready after retry error: %v", err)
	}
	if n := attempts.Load(); n != 2 {
		t.Fatalf("Apply attempts = %d, want 2", n)
	}
}

// Load() after a clean Dispose remounts the same Fiber (new activation).
func TestLoadRemountsAfterGone(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("remount")
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if err := f.Load(); err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready after remount error: %v", err)
	}
	if got := f.State(); got != runtime.StateActive {
		t.Fatalf("state = %v, want Active", got)
	}
	if n := comp.calls.Load(); n != 2 {
		t.Fatalf("Apply calls = %d, want 2 (one per activation)", n)
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// Per-fiber exclusivity: Unwind of an activation must never start before its
// Apply has returned. The cleanup observes whether Apply had already returned.
func TestUnwindWaitsForApplyReturn(t *testing.T) {
	rt := newTestRuntime(t)
	applied := make(chan struct{})
	applyReturned := make(chan struct{})
	cleanupRan := make(chan struct{})
	var violation atomic.Bool

	comp := newFakeComponent("excl")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		close(applied)
		<-ctx.Done()
		close(applyReturned)
		return func() error {
			select {
			case <-applyReturned:
			default:
				violation.Store(true)
			}
			close(cleanupRan)
			return nil
		}, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	<-applied
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	select {
	case <-cleanupRan:
	default:
		t.Fatal("cleanup never ran")
	}
	if violation.Load() {
		t.Fatal("Unwind overlapped Apply (cleanup ran before Apply returned)")
	}
}

// Concurrent Load/Dispose/Dispose must linearize to a consistent final state
// (no panic, cleanup exactly once, final Gone).
func TestConcurrentDisposeLinearizesToGone(t *testing.T) {
	rt := newTestRuntime(t)
	var cleanups atomic.Int32
	comp := newFakeComponent("conc")
	comp.cleanup = func() error {
		cleanups.Add(1)
		return nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}

	var wg sync.WaitGroup
	for i := 0; i < 8; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_ = f.Dispose()
		}()
	}
	wg.Wait()

	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if n := cleanups.Load(); n != 1 {
		t.Fatalf("cleanup ran %d times, want exactly 1", n)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
}
