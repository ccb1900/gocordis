package runtime_test

import (
	"errors"
	"sync/atomic"
	"testing"

	"dynamic-runtime/runtime"
)

// F-05: once a Context is canceled (provider withdrawing, gated on a consumer),
// new Runtime-managed work (Effect / Child) must be rejected with
// ErrContextClosed. The normal Apply phase is unaffected (covered by the rest
// of the suite).
func TestContextRejectsWorkAfterCancelWhileWithdrawing(t *testing.T) {
	rt := newTestRuntime(t)
	pctxCh := make(chan *runtime.Context, 1)

	// Provider of greeter (captures its Context).
	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		pctxCh <- ctx
		mustProvide(t, ctx, "p")
		return nil, nil
	}

	// Consumer of greeter whose cleanup blocks, holding the provider in its
	// Active-but-withdrawing (gated) window.
	cleanupStarted := make(chan struct{})
	releaseCleanup := make(chan struct{})
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return func() error {
			close(cleanupStarted)
			<-releaseCleanup
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
	pctx := <-pctxCh

	// Dispose the provider; its withdrawal is gated on the consumer whose
	// cleanup is now blocked -> provider Context is canceled but the provider
	// activation is not yet unwinding.
	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose error: %v", err)
	}
	<-cleanupStarted

	select {
	case <-pctx.Done():
	default:
		t.Fatal("provider context was not canceled while withdrawing")
	}

	// Effect must be rejected and its install must never run.
	var installRan atomic.Bool
	err = pctx.Effect(func() (func() error, error) {
		installRan.Store(true)
		return func() error { return nil }, nil
	})
	if !errors.Is(err, runtime.ErrContextClosed) {
		t.Fatalf("Effect on canceled context = %v, want ErrContextClosed", err)
	}
	if installRan.Load() {
		t.Fatal("Effect install ran on a canceled context")
	}

	// Child must be rejected.
	if _, err := pctx.Child(newFakeComponent("late")); !errors.Is(err, runtime.ErrContextClosed) {
		t.Fatalf("Child on canceled context = %v, want ErrContextClosed", err)
	}

	// Let the consumer finish its cleanup: provider unloads, everything drains.
	close(releaseCleanup)
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("provider Gone error: %v", err)
	}
	waitState(t, cf, runtime.StatePending)
	_ = cf.Dispose()
	_ = cf.Gone(testTimeout(t))
}

// F-04 (public surface): after the Runtime is Closed every fiber is already
// unmounted, so Dispose() is an idempotent no-op (nil) and Load() reports
// ErrRuntimeClosed - deterministic, never racing a command channel.
func TestCommandBehaviorAfterClosedDeterministic(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("New error: %v", err)
	}
	f, err := rt.Load(noopComponent{name: "x"})
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("Close error: %v", err)
	}

	// Close unmounted the fiber: Dispose remains an idempotent no-op.
	for i := 0; i < 3; i++ {
		if err := f.Dispose(); err != nil {
			t.Fatalf("Dispose after Close (#%d) = %v, want nil (idempotent)", i, err)
		}
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
	// Load after Close is rejected.
	if err := f.Load(); !errors.Is(err, runtime.ErrRuntimeClosed) {
		t.Fatalf("Load after Close = %v, want ErrRuntimeClosed", err)
	}
}
