package runtime_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// CT8: Load after Close is rejected; a fresh runtime with no fibers closes
// cleanly.
func TestLoadAfterCloseRejected(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("New error: %v", err)
	}
	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("Close error: %v", err)
	}
	if _, err := rt.Load(noopComponent{name: "late"}); !errors.Is(err, runtime.ErrRuntimeClosed) {
		t.Fatalf("Load after Close = %v, want ErrRuntimeClosed", err)
	}
}

// Close is idempotent.
func TestCloseIdempotent(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("New error: %v", err)
	}
	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("first Close error: %v", err)
	}
	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("second Close error: %v", err)
	}
}

// Close drains active fibers (including providers, consumers, and owned
// children) to Gone, and Load is rejected afterwards.
func TestCloseDrainsActiveFibers(t *testing.T) {
	rt := newTestRuntime(t)

	child := newFakeComponent("child")
	parent := newFakeComponent("parent")
	var childFiber *runtime.Fiber
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		cf, err := ctx.Child(child)
		if err != nil {
			return nil, err
		}
		childFiber = cf
		return nil, nil
	}

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

	parentF, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent error: %v", err)
	}
	if err := parentF.Ready(testTimeout(t)); err != nil {
		t.Fatalf("parent Ready error: %v", err)
	}
	if childFiber == nil {
		t.Fatal("child fiber was not created")
	}
	consumerF, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	providerF, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := consumerF.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("Close error: %v", err)
	}

	for name, f := range map[string]*runtime.Fiber{
		"parent":   parentF,
		"child":    childFiber,
		"consumer": consumerF,
		"provider": providerF,
	} {
		if got := f.State(); got != runtime.StateGone {
			t.Fatalf("%s state = %v after Close, want Gone", name, got)
		}
	}
	if _, err := rt.Load(noopComponent{name: "after"}); !errors.Is(err, runtime.ErrRuntimeClosed) {
		t.Fatalf("Load after Close = %v, want ErrRuntimeClosed", err)
	}
}

// Close waits for a blocked-but-cooperative component and completes once it
// honors cancellation.
func TestCloseCancelsAndDrainsBlockedApply(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("New error: %v", err)
	}

	applied := make(chan struct{})
	comp := newFakeComponent("blocked")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		close(applied)
		<-ctx.Done()
		return nil, ctx.Err()
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	<-applied

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("Close error: %v", err)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("fiber state = %v after Close, want Gone", got)
	}
}

// A component that ignores cancellation makes Close time out; the Runtime must
// remain Closing (not Closed) and Load must be rejected. Once the component
// finally returns, the Runtime finishes Closing on its own.
func TestCloseTimeoutWhenComponentStubborn(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("New error: %v", err)
	}

	applied := make(chan struct{})
	release := make(chan struct{})
	comp := newFakeComponent("stubborn")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		close(applied)
		<-release // ignores ctx.Done
		return nil, nil
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	<-applied

	shortCtx, cancel := context.WithTimeout(context.Background(), 150*time.Millisecond)
	defer cancel()
	err = rt.Close(shortCtx)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("Close() = %v, want context deadline exceeded", err)
	}

	// While Closing, Load must already be rejected.
	if _, err := rt.Load(noopComponent{name: "x"}); !errors.Is(err, runtime.ErrRuntimeClosed) {
		t.Fatalf("Load while Closing = %v, want ErrRuntimeClosed", err)
	}

	// Release the stubborn component: the runtime should then complete the
	// shutdown on its own and reach Closed.
	close(release)
	done := make(chan struct{})
	go func() {
		defer close(done)
		_ = rt.Close(context.Background())
	}()
	select {
	case <-done:
	case <-time.After(10 * time.Second):
		t.Fatal("runtime did not finish closing after the component returned")
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("fiber state = %v, want Gone", got)
	}
}

// Close on a runtime with dependency loss leaves the consumer Pending first;
// Close still drives everything to terminal Gone (provider, then consumer).
func TestCloseDrainsDependencyChain(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return func() error {
			rec.add("consumer:cleanup")
			return nil
		}, nil
	}
	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return func() error {
			rec.add("provider:cleanup")
			return nil
		}, nil
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

	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("Close error: %v", err)
	}
	if got := cf.State(); got != runtime.StateGone {
		t.Fatalf("consumer state = %v, want Gone", got)
	}
	if got := pf.State(); got != runtime.StateGone {
		t.Fatalf("provider state = %v, want Gone", got)
	}

	events := rec.all()
	ci, pi := -1, -1
	for i, e := range events {
		if e == "consumer:cleanup" && ci == -1 {
			ci = i
		}
		if e == "provider:cleanup" && pi == -1 {
			pi = i
		}
	}
	if ci == -1 || pi == -1 {
		t.Fatalf("missing cleanups: %v", events)
	}
	if ci > pi {
		t.Fatalf("consumer cleanup must precede provider cleanup during Close: %v", events)
	}
}
