package runtime

// PB-06 (kernel side): stale async completion must never modify a newer
// activation. The public-side suite (p2_1_plugin_boundary_test.go) proves the
// Plugin Boundary through the exported API; this single check needs the
// deterministic driver (completion admission is an orchestrator-internal
// linearization point), so it lives in-package and reuses the C3 harness.

import (
	"testing"
)

func TestP21PB06StaleCompletionDoesNotTouchNewActivation(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("p2.1.stale").Capability()
	rec := c3NewRecorder(key)

	// Generation 1: P Active (activation A1).
	p := c3LoadFiber(t, rt, rec, &thm73Comp{name: "P", key: key, provide: true}, true)
	pAct1 := c3ActivateProvider(t, rt, rec, p)

	// Full withdrawal of generation 1.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P/A1)")
	c3Drain(t, rt, rec)
	if p.State() != StateGone {
		t.Fatalf("P state after gen-1 drain = %v, want Gone", p.State())
	}

	// Generation 2: P Loading with ApplyDone(A2) parked.
	if err := p.Load(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Load(P/A2)")
	c3WaitParked(t, rt, "P/A2 ApplyDone park")
	a2 := c3EnabledStep(t, rt, p.ID(), detStepApplyDone)
	pAct2 := a2.ActivationID
	before := len(rec.Events)

	// Injection point 1: stale ApplyDone(A1) while A2 is Loading. The
	// activation identity guard must reject it: P stays Loading with A2's id,
	// no event is produced, and the stale cleanup never runs.
	var staleFired bool
	if !rt.submit(&cmdApplyDone{fiberID: p.ID(), activationID: pAct1, cleanup: func() error {
		staleFired = true
		return nil
	}}) {
		t.Fatal("c3: stale ApplyDone submit rejected")
	}
	c3Settle(t, rt, rec, "stale ApplyDone(P/A1) while P/A2 Loading")
	if p.State() != StateLoading || ActivationID(actIDOf(p)) != pAct2 {
		t.Fatalf("PB-06: stale completion modified the Loading activation: state=%v act=%d, want Loading/%d",
			p.State(), actIDOf(p), pAct2)
	}
	if staleFired {
		t.Fatal("PB-06: stale completion cleanup was committed into the new activation")
	}
	if len(rec.Events) != before {
		t.Fatalf("PB-06: stale completion produced events: before=%d after=%d", before, len(rec.Events))
	}

	// Execute the real A2 ApplyDone: P Active, generation 2.
	c3ExecAndSettle(t, rt, rec, a2)
	if p.State() != StateActive || ActivationID(actIDOf(p)) != pAct2 {
		t.Fatalf("PB-06: P not Active after real A2 ApplyDone: state=%v act=%d", p.State(), actIDOf(p))
	}

	// Injection point 2: stale ApplyDone(A1) while A2 is Active — no
	// disturbance is allowed either.
	before = len(rec.Events)
	if !rt.submit(&cmdApplyDone{fiberID: p.ID(), activationID: pAct1, cleanup: func() error {
		staleFired = true
		return nil
	}}) {
		t.Fatal("c3: stale ApplyDone submit rejected")
	}
	c3Settle(t, rt, rec, "stale ApplyDone(P/A1) while P/A2 Active")
	if p.State() != StateActive || ActivationID(actIDOf(p)) != pAct2 || staleFired {
		t.Fatalf("PB-06: stale completion disturbed the Active activation: state=%v act=%d stale=%v",
			p.State(), actIDOf(p), staleFired)
	}
	if len(rec.Events) != before {
		t.Fatalf("PB-06: stale completion while Active produced events: before=%d after=%d", before, len(rec.Events))
	}

	c3Close(t, rt)
}
