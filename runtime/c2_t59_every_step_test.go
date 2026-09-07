package runtime

import (
	"context"
	"fmt"
	"testing"
	"time"
)

// C-2 (T59 every-step): registry well-formedness at every deterministic
// lifecycle step of a single independent fiber P.
//
//	Load(P) -> [ApplyDone parked] -> Execute(ApplyDone) -> Active ->
//	Dispose() -> [UnwindDone parked] -> Execute(UnwindDone) -> Gone -> Close
//
// After EVERY driver step the preservation oracle checkT59 must hold. checkT59
// is deliberately NOT a quiescence oracle: it must hold in the transitional
// Loading/Unloading states where semanticQuiescent reports non-quiescence, and
// it never requires the runtime to be idle. The single-fiber scenario
// exercises the registry well-formedness arms that exist for an isolated
// fiber: fiber registry identity, ownership links, and activation/state
// coherence. Provider/dependency arms join when C-2 grows consumer/provider
// and child scenarios. Runtime production code is untouched.

// checkT59 reports the first registry well-formedness violation. It reads only
// semantic runtime state under the established lock order (rt.mu -> f.mu),
// mirroring observe/t59CheckP, and is safe to call between driver steps.
//
// Activation/state coherence is constrained in the direction that is true at
// every driver-step boundary:
//   - a live activation requires state Loading/Active/Unloading;
//   - state Loading/Active requires a live activation.
//
// (State Unloading may transiently hold a nil activation while a finished
// activation is being finalized into Gone, so Unloading alone is not
// constrained.)
func checkT59(rt *Runtime) error {
	rt.mu.RLock()
	type entry struct {
		key FiberID
		f   *Fiber
	}
	entries := make([]entry, 0, len(rt.fibers))
	byID := make(map[FiberID]*Fiber, len(rt.fibers))
	for key, f := range rt.fibers {
		entries = append(entries, entry{key, f})
		byID[f.id] = f
	}
	rt.mu.RUnlock()

	for _, e := range entries {
		if e.f == nil {
			return fmt.Errorf("T59 registry: nil fiber under id %v", e.key)
		}
		if e.f.id != e.key {
			return fmt.Errorf("T59 registry: fiber %v registered under key %v", e.f.id, e.key)
		}
		f := e.f

		f.mu.RLock()
		state := f.state
		parent := f.parent
		realm := f.realm
		act := f.activation
		var actID ActivationID
		var actFiber *Fiber
		var actCtx *Context
		if act != nil {
			actID = act.id
			actFiber = act.fiber
			actCtx = act.ctx
		}
		childIDs := make([]FiberID, 0, len(f.children))
		for cid := range f.children {
			childIDs = append(childIDs, cid)
		}
		f.mu.RUnlock()

		if realm == nil {
			return fmt.Errorf("T59 fiber %d (%s): nil realm", f.id, f.Name())
		}
		if parent != nil && byID[parent.id] == nil {
			return fmt.Errorf("T59 fiber %d (%s): parent %d not registered", f.id, f.Name(), parent.id)
		}
		for _, cid := range childIDs {
			if byID[cid] == nil {
				return fmt.Errorf("T59 fiber %d (%s): child %d not registered", f.id, f.Name(), cid)
			}
		}

		switch {
		case act != nil:
			if state != StateLoading && state != StateActive && state != StateUnloading {
				return fmt.Errorf("T59 fiber %d (%s): live activation in state %v", f.id, f.Name(), state)
			}
			if actID == 0 {
				return fmt.Errorf("T59 fiber %d (%s): activation id 0", f.id, f.Name())
			}
			if actFiber != f {
				return fmt.Errorf("T59 fiber %d (%s): activation owner mismatch", f.id, f.Name())
			}
			if actCtx == nil {
				return fmt.Errorf("T59 fiber %d (%s): activation has nil context", f.id, f.Name())
			}
		case state == StateLoading || state == StateActive:
			return fmt.Errorf("T59 fiber %d (%s): state %v without live activation", f.id, f.Name(), state)
		}
	}
	return nil
}

func c2Wait(ms int, cond func() bool) bool {
	deadline := time.Now().Add(time.Duration(ms) * time.Millisecond)
	for time.Now().Before(deadline) {
		if cond() {
			return true
		}
		time.Sleep(time.Millisecond)
	}
	return false
}

func c2Step(t *testing.T, step int, action string, f *Fiber, rt *Runtime) {
	t.Helper()
	t.Logf("c2 step=%d action=%s fiber=%s state=%v activation=%d pending=%d",
		step, action, f.Name(), f.State(), actIDOf(f), rt.detPending())
}

func c2T59Check(t *testing.T, rt *Runtime, when string) {
	t.Helper()
	if err := checkT59(rt); err != nil {
		t.Fatalf("T59 every-step violated at %s: %v", when, err)
	}
}

// TestC2T59EveryStepSingleFiber drives the full single-fiber lifecycle under
// the deterministic driver and runs checkT59 after every step, covering both
// StepApplyDone and StepUnwindDone.
func TestC2T59EveryStepSingleFiber(t *testing.T) {
	rt := detNew(t)
	c2T59Check(t, rt, "initial(empty registry)")

	step := 0
	f, err := rt.Load(&t66Comp{name: "P"}) // independent single fiber
	if err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && f.State() == StateLoading }) {
		t.Fatalf("ApplyDone never parked after Load; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Load", f, rt)
	en := rt.detEnabledSteps()
	if len(en) != 1 || en[0].Kind != StepApplyDone || en[0].FiberID != f.ID() {
		t.Fatalf("enabled=%v, want [ApplyDone(P)]", en)
	}
	actID := en[0].ActivationID
	step++

	// Transitional Loading: checkT59 holds while quiescence does not.
	c2T59Check(t, rt, "Loading(ApplyDone parked)")
	if err := semanticQuiescent(rt); err == nil {
		t.Fatal("semanticQuiescent should report transitional Loading (checkT59 is not a quiescence oracle)")
	}

	if err := rt.detExecute(en[0]); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return f.State() == StateActive }) {
		t.Fatalf("P never Active; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Execute(ApplyDone)", f, rt)
	step++
	c2T59Check(t, rt, "Active")
	if err := semanticQuiescent(rt); err != nil {
		t.Fatalf("not quiescent while Active: %v", err)
	}

	// Unmount the fiber (Intent = Unmounted, same decision Close would make)
	// so the UnwindDone completion parks under the deterministic driver.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && f.State() == StateUnloading }) {
		t.Fatalf("UnwindDone never parked after Dispose; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Dispose", f, rt)
	en2 := rt.detEnabledSteps()
	if len(en2) != 1 || en2[0].Kind != StepUnwindDone || en2[0].FiberID != f.ID() {
		t.Fatalf("enabled=%v, want [UnwindDone(P)]", en2)
	}
	if en2[0].ActivationID != actID {
		t.Fatalf("UnwindDone activation %v != ApplyDone activation %v", en2[0].ActivationID, actID)
	}
	step++

	// Transitional Unloading: checkT59 holds while quiescence does not.
	c2T59Check(t, rt, "Unloading(UnwindDone parked)")
	if err := semanticQuiescent(rt); err == nil {
		t.Fatal("semanticQuiescent should report transitional Unloading (checkT59 is not a quiescence oracle)")
	}

	if err := rt.detExecute(en2[0]); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return f.State() == StateGone && rt.detPending() == 0 }) {
		t.Fatalf("P never Gone; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Execute(UnwindDone)", f, rt)
	step++
	c2T59Check(t, rt, "Gone")
	if err := semanticQuiescent(rt); err != nil {
		t.Fatalf("not quiescent after Gone: %v", err)
	}

	clctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	err = rt.Close(clctx)
	cancel()
	if err != nil {
		t.Fatalf("Close did not complete: state=%v pending=%d err=%v", f.State(), rt.detPending(), err)
	}
	select {
	case <-rt.orch.done:
	default:
		t.Fatalf("orchestrator not done after Close; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Close", f, rt)
	c2T59Check(t, rt, "after Close")
	t.Logf("C-2 PASS: checkT59 held at every driver step (ApplyDone + UnwindDone; steps=%d)", step)
}
