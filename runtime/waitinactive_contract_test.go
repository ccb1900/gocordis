package runtime_test

import (
	"sync"
	"testing"

	"dynamic-runtime/runtime"
)

// Test 1 — WaitInactive on dependency loss: the consumer's current activation
// cycle ends when the provider is disposed; WaitInactive returns and the fiber
// rests at Pending (NOT Gone).
func TestWaitInactiveOnDependencyLoss(t *testing.T) {
	rt := newTestRuntime(t)

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return nil, nil
	}
	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return nil, nil
	}

	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose error: %v", err)
	}

	if err := cf.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("consumer WaitInactive error: %v", err)
	}
	// WaitInactive returns as soon as the activation cycle's cleanup completes;
	// the fiber may still be settling to its resting state Pending.
	waitState(t, cf, runtime.StatePending)

	_ = cf.Dispose()
	_ = cf.Gone(testTimeout(t))
	_ = pf.Gone(testTimeout(t))
}

// Test 2 — WaitInactive on Dispose: a plain Active fiber's cycle ends when it
// is disposed; WaitInactive returns and the fiber reaches Gone.
func TestWaitInactiveOnDispose(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("plain")
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
	if err := f.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("WaitInactive error: %v", err)
	}
	// The fiber settles to its terminal state Gone right after the activation
	// cycle ends.
	waitState(t, f, runtime.StateGone)
}

// Test 3 — WaitInactive when already inactive returns immediately, whether the
// fiber rests at Pending (waiting for a missing dependency) or is Gone.
func TestWaitInactiveWhenAlreadyInactive(t *testing.T) {
	rt := newTestRuntime(t)

	// Pending (mounted, dependency missing).
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	waitState(t, cf, runtime.StatePending)
	if err := cf.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("WaitInactive on Pending fiber error: %v", err)
	}

	// Gone (disposed before any activation).
	plain := newFakeComponent("plain")
	pf, err := rt.Load(plain)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if err := pf.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("WaitInactive on Gone fiber error: %v", err)
	}

	_ = cf.Dispose()
}

// Test 5 — Concurrent waiters: every WaitInactive waiter returns once the
// activation cycle ends.
func TestWaitInactiveConcurrentWaiters(t *testing.T) {
	rt := newTestRuntime(t)

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return nil, nil
	}
	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return nil, nil
	}

	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose error: %v", err)
	}

	const n = 8
	var wg sync.WaitGroup
	errs := make([]error, n)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			errs[i] = cf.WaitInactive(testTimeout(t))
		}(i)
	}
	wg.Wait()
	for i, e := range errs {
		if e != nil {
			t.Fatalf("waiter %d error: %v", i, e)
		}
	}

	_ = cf.Dispose()
	_ = cf.Gone(testTimeout(t))
	_ = pf.Gone(testTimeout(t))
}
