package config_test

import (
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// Plugin-switch conformance (paper §5.2.1 / §4.4 Configuration: the
// declarative layer may "disable and later re-enable" a component; the entry
// is the surviving identity, the fiber the identity of one enablement).
//
//	ComponentConfig.Enabled = nil/true  -> reconciled to a live fiber (Load)
//	ComponentConfig.Enabled = false     -> declaration applied, NO fiber exists
//	flip false->true                    -> enablement (Load)
//	flip true->false                    -> withdrawal (Dispose); entry survives
//	restart-equivalent                  -> re-reconcile of the same declaration
//	                                       reproduces the same states (the switch
//	                                       state lives in the DECLARATION store,
//	                                       never in runtime-internal state)

func boolPtr(b bool) *bool { return &b }

func enabledCC(id, typ string, enabled *bool, kv ...any) config.ComponentConfig {
	c := cc(id, typ, kv...)
	c.Enabled = enabled
	return c
}

// TestSwitchDisabledAtBootNoFiber — a disabled declaration is applied (owned,
// definition validated by the factory) but the Runtime holds no fiber for it.
func TestSwitchDisabledAtBootNoFiber(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "svc", nil)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false)))); err != nil {
		t.Fatal(err)
	}
	own, ok := ownedByID(t, e.ctrl, "p1")
	if !ok {
		t.Fatal("disabled entry must remain owned (entry identity survives)")
	}
	if own.Fiber != nil {
		t.Fatalf("disabled entry owns fiber %v, want nil", own.Fiber)
	}
	snap, err := e.rt.Snapshot(ctxT(t))
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Fibers) != 0 {
		t.Fatalf("runtime fibers = %d, want 0 (no live object for a disabled entry)", len(snap.Fibers))
	}
}

// TestSwitchFlipToEnabledLoads — false -> true is an enablement: the fiber is
// created and reaches Active; the entry identity persists across the flip.
func TestSwitchFlipToEnabledLoads(t *testing.T) {
	e := newEnv(t)
	applies := newCounter()
	e.registerSimple(t, "svc", applies)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false)))); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(true)))); err != nil {
		t.Fatal(err)
	}
	own, ok := ownedByID(t, e.ctrl, "p1")
	if !ok || own.Fiber == nil {
		t.Fatal("enabled entry must own a live fiber")
	}
	waitActiveT(t, own.Fiber)
	if got := applies.Load(); got != 1 {
		t.Fatalf("applies = %d, want 1 (single enablement)", got)
	}
}

// TestSwitchFlipToDisabledUnloadsEntrySurvives — true -> false is a
// withdrawal: the fiber reaches Gone and the entry stays owned (definition
// intact), so a later flip re-enables from the same declaration.
func TestSwitchFlipToDisabledUnloadsEntrySurvives(t *testing.T) {
	e := newEnv(t)
	applies := newCounter()
	e.registerSimple(t, "svc", applies)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(true)))); err != nil {
		t.Fatal(err)
	}
	own, _ := ownedByID(t, e.ctrl, "p1")
	waitActiveT(t, own.Fiber)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false)))); err != nil {
		t.Fatal(err)
	}
	if err := own.Fiber.Gone(ctxT(t)); err != nil {
		t.Fatalf("disabled fiber did not reach Gone: %v", err)
	}
	own2, ok := ownedByID(t, e.ctrl, "p1")
	if !ok {
		t.Fatal("disabled entry must survive the withdrawal")
	}
	if own2.Fiber != nil {
		t.Fatalf("entry still owns a fiber after disable: %v", own2.Fiber)
	}
	if got := applies.Load(); got != 1 {
		t.Fatalf("applies = %d, want 1 (no re-apply on disable)", got)
	}
}

// TestSwitchRestartEquivalent — the switch state lives in the declaration:
// a fresh Controller applying the same disabled declaration reproduces the
// same down state, and an enabled one the same up state. No runtime-internal
// state is persisted anywhere.
func TestSwitchRestartEquivalent(t *testing.T) {
	declared := cfg(
		enabledCC("on", "svc", boolPtr(true)),
		enabledCC("off", "svc", boolPtr(false)),
	)
	for round := 0; round < 2; round++ {
		e := newEnv(t)
		e.registerSimple(t, "svc", nil)
		if err := e.ctrl.Reconcile(ctxT(t), declared); err != nil {
			t.Fatal(err)
		}
		snap, err := e.rt.Snapshot(ctxT(t))
		if err != nil {
			t.Fatal(err)
		}
		if len(snap.Fibers) != 1 {
			t.Fatalf("round %d: live fibers = %d, want exactly the enabled one", round, len(snap.Fibers))
		}
		if snap.Fibers[0].Name != "on" {
			t.Fatalf("round %d: live fiber = %q, want the enabled entry", round, snap.Fibers[0].Name)
		}
		own, ok := ownedByID(t, e.ctrl, "off")
		if !ok || own.Fiber != nil {
			t.Fatalf("round %d: disabled entry missing or fiberful", round)
		}
		_ = e.ctrl.CloseContext(ctxT(t))
	}
}

// TestSwitchIdempotentReconcileNoChurn — reconciling the same declaration
// twice performs no fiber-level work (idempotent no-op), for both switch
// states.
func TestSwitchIdempotentReconcileNoChurn(t *testing.T) {
	e := newEnv(t)
	applies := newCounter()
	f := e.registerSimple(t, "svc", applies)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(true)))); err != nil {
		t.Fatal(err)
	}
	own, _ := ownedByID(t, e.ctrl, "p1")
	waitActiveT(t, own.Fiber)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(true)))); err != nil {
		t.Fatal(err)
	}
	f1 := own.Fiber
	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false)))); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false)))); err != nil {
		t.Fatal(err)
	}
	if got := applies.Load(); got != 1 {
		t.Fatalf("applies = %d, want 1 (no churn)", got)
	}
	// Factory calls: 1 for the enablement, 1 for the flip to disabled (the
	// replace composite re-applies the entry to re-validate its definition),
	// and NOTHING for the idempotent re-reconcile of the same declaration.
	if got := f.calls.Load(); got != 2 {
		t.Fatalf("factory calls = %d, want 2 (enable + flip; idempotent adds none)", got)
	}
	_ = f1
}

// TestSwitchDisabledDefinitionChangeNoFiber — revising a disabled entry's
// definition swaps the declaration without ever creating a fiber; enabling
// afterwards loads the NEW definition.
func TestSwitchDisabledDefinitionChangeNoFiber(t *testing.T) {
	e := newEnv(t)
	applies := newCounter()
	e.registerSimple(t, "svc", applies)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false), "v", "1"))); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(false), "v", "2"))); err != nil {
		t.Fatal(err)
	}
	snap, err := e.rt.Snapshot(ctxT(t))
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Fibers) != 0 {
		t.Fatalf("definition change while disabled created fibers: %d", len(snap.Fibers))
	}
	if err := e.ctrl.Reconcile(ctxT(t), cfg(enabledCC("p1", "svc", boolPtr(true), "v", "2"))); err != nil {
		t.Fatal(err)
	}
	own, ok := ownedByID(t, e.ctrl, "p1")
	if !ok || own.Fiber == nil {
		t.Fatal("enabled entry must own a live fiber")
	}
	waitActiveT(t, own.Fiber)
	if got := applies.Load(); got != 1 {
		t.Fatalf("applies = %d, want 1 (enabled loads the new definition once)", got)
	}
}

func newCounter() *atomic.Int32 { return &atomic.Int32{} }

func waitActiveT(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == runtime.StateActive {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting %s Active (state %v err %v)", f.Name(), f.State(), f.Err())
}
