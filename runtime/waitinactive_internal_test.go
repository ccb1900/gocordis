package runtime

// White-box tests for WaitInactive activation-generation semantics.

import (
	"context"
	"errors"
	"testing"
	"time"
)

// Test 4 — Activation generation: a waiter that starts while Activation 2 is
// live must NOT be released by Activation 1's historical end (or by a stale
// Activation 1 completion). It may only return once Activation 2 ends.
func TestWaitInactiveActivationGeneration(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	defer func() { _ = rt.Close(context.Background()) }()

	f, err := rt.Load(&wbComponent{name: "gen"})
	if err != nil {
		t.Fatal(err)
	}
	wbWaitState(t, f, StateActive)
	act1 := f.currentActivationID()
	if act1 == 0 {
		t.Fatal("activation 1 id is invalid")
	}

	// End activation 1 (unmount) and start activation 2 (remount).
	_ = f.Dispose()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	cancel()
	if err := f.Load(); err != nil {
		t.Fatalf("Load: %v", err)
	}
	wbWaitState(t, f, StateActive)
	act2 := f.currentActivationID()
	if act2 == act1 {
		t.Fatal("activation id did not advance across remount")
	}

	// A stale completion from activation 1 must be ignored (no state change).
	var staleRan bool
	onOrchestrator(t, rt, func(o *orchestrator) {
		(&cmdApplyDone{
			fiberID:      f.id,
			activationID: act1,
			cleanup: func() error {
				staleRan = true
				return nil
			},
		}).apply(o)
	})
	if staleRan {
		t.Fatal("stale activation-1 completion cleanup ran")
	}
	if got := f.State(); got != StateActive {
		t.Fatalf("state after stale completion = %v, want Active", got)
	}

	// A waiter for activation 2 must NOT be satisfied by activation 1's past
	// end or by the stale completion: it must block until activation 2 ends.
	short, shortCancel := context.WithTimeout(context.Background(), 150*time.Millisecond)
	defer shortCancel()
	if err := f.WaitInactive(short); !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("WaitInactive returned %v while activation 2 is live; want deadline exceeded", err)
	}

	// Ending activation 2 releases the waiter.
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.WaitInactive(ctx2(t)); err != nil {
		t.Fatalf("WaitInactive after disposing activation 2: %v", err)
	}
	wbWaitState(t, f, StateGone)
}

func ctx2(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// F-04 (primitive): once the orchestrator has stopped, submit() must
// deterministically return false instead of winning a race against the
// buffered command channel.
func TestSubmitAfterCloseReturnsFalse(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	if err := rt.Close(context.Background()); err != nil {
		t.Fatalf("Close: %v", err)
	}
	done := make(chan struct{})
	if ok := rt.submit(&syncCommand{done: done}); ok {
		t.Fatal("submit after Close returned true; want false")
	}
}
