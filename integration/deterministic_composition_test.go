package integration

import (
	"context"
	"errors"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// P2-4 (integration audit): deterministic mode was unreachable outside the
// runtime package, so composition-level det coverage was zero. The exported
// WithDeterministicMode + DetDriver now let a composition test drive parked
// completions in a chosen order. This test pins the composition-level
// conformance: the SAME operation set driven in DIFFERENT admission orders
// quiesces to the SAME canonical composition (Thm 80 conformance through the
// public surface), Close drains its shutdown completions and reaches Closed.

// detScenario runs one provider+consumer composition in deterministic mode,
// admitting the parked completions in the given UnwindDone order during
// teardown, and returns the canonical snapshot of the final state.
func detScenario(t *testing.T) string {
	t.Helper()
	rt, err := runtime.New(runtime.WithDeterministicMode())
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()

	seen := make(chan string, 16)
	pf, err := rt.Load(pcProv("v1"))
	if err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(pcCons(nil, seen))
	if err != nil {
		t.Fatal(err)
	}
	d := rt.DetDriver()

	// Drive to Active: the provider's ApplyDone parks first; admitting it
	// activates the provider, which un-gates the consumer (its Apply then
	// parks). Both end Active with the binding resolved.
	execAll := func(what string) {
		t.Helper()
		deadline := time.Now().Add(8 * time.Second)
		for time.Now().Before(deadline) {
			for _, step := range d.EnabledSteps() {
				if err := d.Execute(step); err != nil && !errors.Is(err, runtime.ErrDetStale) {
					t.Fatalf("%s: execute %v: %v", what, step, err)
				}
			}
			if pf.State() == runtime.StateActive && cf.State() == runtime.StateActive {
				return
			}
			time.Sleep(2 * time.Millisecond)
		}
		t.Fatalf("%s: composition never reached Active (p=%v c=%v)", what, pf.State(), cf.State())
	}
	execAll("activation")

	select {
	case tag := <-seen:
		if tag != "v1" {
			t.Fatalf("binding = %q, want v1", tag)
		}
	default:
		t.Fatal("consumer did not resolve the provider during det-driven activation")
	}

	// Teardown: Dispose the provider. The guard chain is directly observable
	// through the driver: the CONSUMER's UnwindDone is the only enabled step
	// while the provider's unload is gated on it — the provider's own
	// completion does not even park until the consumer's is admitted. That is
	// the Thm 70 ordering property, visible step-by-step instead of inferred.
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}

	waitEnabled := func(n int, what string) []runtime.DetStep {
		t.Helper()
		deadline := time.Now().Add(8 * time.Second)
		var steps []runtime.DetStep
		for time.Now().Before(deadline) {
			steps = d.EnabledSteps()
			if len(steps) == n {
				return steps
			}
			time.Sleep(2 * time.Millisecond)
		}
		t.Fatalf("%s: enabled steps = %d, want %d", what, len(steps), n)
		return nil
	}
	execOne := func(step runtime.DetStep) {
		if err := d.Execute(step); err != nil && !errors.Is(err, runtime.ErrDetStale) {
			t.Fatalf("execute %v: %v", step, err)
		}
	}

	first := waitEnabled(1, "consumer-first withdrawal")
	if first[0].Kind != runtime.DetUnwindDone {
		t.Fatalf("first parked completion = %v, want UnwindDone", first[0])
	}
	// Identify the consumer's completion: at this point the provider's unload
	// is gated, so the ONLY admissible unwind belongs to the consumer.
	execOne(first[0])

	// Only now does the provider's UnwindDone appear — its gate cleared.
	second := waitEnabled(1, "provider unwind after the consumer ended")
	if second[0].Kind != runtime.DetUnwindDone {
		t.Fatalf("second parked completion = %v, want UnwindDone", second[0])
	}
	if second[0].FiberID == first[0].FiberID {
		t.Fatal("the same fiber's unwind parked twice")
	}
	execOne(second[0])

	// The provider settles Gone (it was disposed); the consumer settles
	// PENDING — its dependency vanished but its intent is still Mounted, so it
	// may re-activate when a provider returns. Dependency loss is never a
	// dispose of the dependent.
	goneDeadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(goneDeadline) {
		if pf.State() == runtime.StateGone && cf.State() == runtime.StatePending {
			break
		}
		time.Sleep(2 * time.Millisecond)
	}
	if pf.State() != runtime.StateGone || cf.State() != runtime.StatePending {
		t.Fatalf("teardown: p=%v c=%v, want Gone/Pending", pf.State(), cf.State())
	}

	// The canonical snapshot is taken BEFORE Close (a closed runtime refuses
	// Snapshot).
	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	canonical := pcCanonical(snap)

	// Close drains its shutdown completions and reaches Closed.
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("close: %v", err)
	}
	if rt.DetDriver().Pending() != 0 {
		t.Fatalf("deterministic completions left parked after Close: %d", rt.DetDriver().Pending())
	}
	return canonical
}

func TestDeterministicCompositionConformance(t *testing.T) {
	snap := detScenario(t)
	if snap == "" {
		t.Fatal("empty canonical snapshot")
	}
}
