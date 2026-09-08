package runtime

import (
	"context"
	"fmt"
	"testing"
	"time"
)

// C-1 (minimal): single-fiber Thm68 baseline under the deterministic driver.
//   baseline run: nothing loaded -> quiescent -> Observe(base)
//   subject run:  Load(P) -> driver -> Active -> Observe(A) -> Close ->
//                 Observe(post) ; assert post == base.
// No Runtime changes. Close uses a 1s diagnostic fuse (NOT theorem semantics).

func c1Wait(ms int, cond func() bool) bool {
	deadline := time.Now().Add(time.Duration(ms) * time.Millisecond)
	for time.Now().Before(deadline) {
		if cond() {
			return true
		}
		time.Sleep(time.Millisecond)
	}
	return false
}

func c1Step(t *testing.T, step int, action, fiber string, st FiberState, act uint64, pending int) {
	t.Logf("step=%d action=%s fiber=%s state=%v activation=%d pendingSteps=%d", step, action, fiber, st, act, pending)
}

func c1Activation(f *Fiber) uint64 {
	f.mu.RLock()
	defer f.mu.RUnlock()
	if f.activation == nil {
		return 0
	}
	return uint64(f.activation.id)
}

func TestC1Thm68MinimalBaseline(t *testing.T) {
	// baseline run
	rtB := detNew(t)
	if err := semanticQuiescent(rtB); err != nil {
		t.Fatalf("baseline not quiescent: %v", err)
	}
	base := observe(rtB)
	_ = rtB.Close(context.Background())

	// subject run
	rt := detNew(t)
	step := 0
	f, err := rt.Load(&thm73Comp{name: "P"}) // independent single fiber
	if err != nil {
		t.Fatal(err)
	}
	c1Step(t, step, "Load", "P", f.State(), c1Activation(f), rt.detPending())
	step++

	if !c1Wait(2000, func() bool { return rt.detPending() >= 1 }) {
		t.Fatalf("driver: ApplyDone never parked after Load; state=%v pending=%d", f.State(), rt.detPending())
	}
	c1Step(t, step, "ApplyDone-parked", "P", f.State(), c1Activation(f), rt.detPending())
	step++

	en := rt.detEnabledSteps()
	if len(en) != 1 || en[0].Kind != StepApplyDone || en[0].FiberID != f.ID() {
		t.Fatalf("enabled=%v, want [ApplyDone(P)]", en)
	}
	if err := rt.detExecute(en[0]); err != nil {
		t.Fatal(err)
	}
	c1Step(t, step, "Execute(ApplyDone)", "P", f.State(), c1Activation(f), rt.detPending())
	step++

	if !c1Wait(2000, func() bool { return f.State() == StateActive }) {
		t.Fatalf("P never Active; state=%v pending=%d", f.State(), rt.detPending())
	}
	if err := semanticQuiescent(rt); err != nil {
		t.Fatalf("not quiescent while Active: %v", err)
	}
	c1Step(t, step, "Active-quiescent", "P", f.State(), c1Activation(f), rt.detPending())
	step++
	active := observe(rt)
	if ObservationsEquivalent(base, active) {
		t.Fatalf(
			"active observation unexpectedly equals never-loaded baseline:\nbase %s\nactive %s",
			base.canonical(),
			active.canonical(),
		)
	}

	// Close with diagnostic fuse.
	t.Logf("Close begin")
	clctx, cancel := context.WithTimeout(context.Background(), 1*time.Second)
	err = rt.Close(clctx)
	cancel()
	if err == nil {
		t.Logf("Close end err=nil")
	} else {
		t.Logf("Close end err=%v", err)
	}
	if err != nil {
		t.Fatalf("Close did not complete in window: state=%v pending=%d orchDone=%v err=%v",
			f.State(), rt.detPending(), c1OrchDone(rt), err)
	}
	c1Step(t, step+1, "Close", "P", f.State(), c1Activation(f), rt.detPending())
	if f.State() != StateGone || rt.detPending() != 0 || !c1OrchDone(rt) {
		t.Fatalf("after Close: state=%v pending=%d orchDone=%v", f.State(), rt.detPending(), c1OrchDone(rt))
	}
	post := observe(rt)
	if !ObservationsEquivalent(base, post) {
		t.Fatalf("Thm68 post-close != never-loaded baseline:\nbase %s\npost %s", base.canonical(), post.canonical())
	}
	t.Logf("C-1 PASS: driver->Close boundary clean (steps=%d)", step+1)
}

func c1OrchDone(rt *Runtime) bool {
	select {
	case <-rt.orch.done:
		return true
	default:
		return false
	}
}

var _ = fmt.Sprintf
