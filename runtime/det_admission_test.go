package runtime

import (
	"context"
	"errors"
	"testing"
	"time"
)

// Phase B — Deterministic completion admission. detExecute must never lose a
// parked completion to a failed admission: enqueue-before-delete guarantees a
// rejected admission leaves the completion parked for a later retry.

func bExecWaitParked(t *testing.T, rt *Runtime, f *Fiber) Step {
	t.Helper()
	if !waitB(t, rt, 3000, func() bool { return rt.detPending() == 1 && f.State() == StateLoading }) {
		t.Fatalf("ApplyDone never parked; state=%v pending=%d", f.State(), rt.detPending())
	}
	en := rt.detEnabledSteps()
	if len(en) != 1 || en[0].Kind != StepApplyDone || en[0].FiberID != f.ID() {
		t.Fatalf("enabled=%v, want [ApplyDone(%d)]", en, f.ID())
	}
	return en[0]
}

// T-B-EXEC-01: pending completion exists; admission fails; detExecute returns
// an admission error; the pending completion remains and is retryable.
func TestBExec01AdmissionFailureKeepsCompletion(t *testing.T) {
	rt := detNew(t)
	defer func() { rt.det.admitOverride = nil }()
	f, err := rt.Load(&detLeafComp{})
	if err != nil {
		t.Fatal(err)
	}
	step := bExecWaitParked(t, rt, f)

	// Stage an admission rejection (command queue full / orchestrator stopped).
	rt.det.admitOverride = func(command) bool { return false }

	if err := rt.detExecute(step); !errors.Is(err, errDetAdmission) {
		t.Fatalf("detExecute = %v, want errDetAdmission", err)
	}
	// Failure invariant: pending(step) remains present after a failed admission.
	if rt.detPending() != 1 {
		t.Fatalf("pending = %d after failed admission, want 1 (completion lost)", rt.detPending())
	}
	en := rt.detEnabledSteps()
	found := false
	for _, s := range en {
		if s == step {
			found = true
		}
	}
	if !found {
		t.Fatalf("parked completion missing from enabled after failed admission: %v", en)
	}
	if f.State() != StateLoading {
		t.Fatalf("fiber state = %v after failed admission, want Loading", f.State())
	}

	// Retry invariant: a later successful admission removes the completion and
	// drives the lifecycle forward (nothing was lost by the first failure).
	rt.det.admitOverride = nil
	if err := rt.detExecute(step); err != nil {
		t.Fatalf("retry detExecute: %v", err)
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after successful retry, want 0", rt.detPending())
	}
	if !waitB(t, rt, 3000, func() bool { return f.State() == StateActive }) {
		t.Fatalf("fiber never Active after retry; state=%v", f.State())
	}
}

// T-B-EXEC-02: first admission fails, second succeeds; the pending completion
// is removed and the command reaches the orchestrator exactly once.
func TestBExec02RetryAdmitsExactlyOnce(t *testing.T) {
	rt := detNew(t)
	defer func() { rt.det.admitOverride = nil }()
	f, err := rt.Load(&detLeafComp{})
	if err != nil {
		t.Fatal(err)
	}
	step := bExecWaitParked(t, rt, f)

	admitted := 0
	calls := 0
	rt.det.admitOverride = func(cmd command) bool {
		calls++
		if calls == 1 {
			return false // first admission rejected
		}
		admitted++
		return rt.enqueueNonBlocking(cmd)
	}

	if err := rt.detExecute(step); !errors.Is(err, errDetAdmission) {
		t.Fatalf("first detExecute = %v, want errDetAdmission", err)
	}
	if rt.detPending() != 1 {
		t.Fatalf("pending = %d after first rejected admission, want 1", rt.detPending())
	}

	if err := rt.detExecute(step); err != nil {
		t.Fatalf("second detExecute: %v", err)
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after successful second admission, want 0", rt.detPending())
	}
	if admitted != 1 {
		t.Fatalf("command reached orchestrator %d times, want exactly 1", admitted)
	}
	if !waitB(t, rt, 3000, func() bool { return f.State() == StateActive }) {
		t.Fatalf("fiber never Active after retried admission; state=%v", f.State())
	}
}

// T-B-EXEC-03: existing shutdown regressions keep passing — C-01 Active Fiber
// Close, C-02 Normal Runtime Close, C-03 Loading Consumer Close, C-04
// Ownership Cascade Close (each exercised end-to-end below; they also run as
// part of go test ./runtime).
func TestBExec03ShutdownRegressions(t *testing.T) {
	t.Run("C-01 Active Fiber Close", func(t *testing.T) { testBExecCloseActive(t) })
	t.Run("C-02 Normal Runtime Close", func(t *testing.T) { testBExecCloseNormal(t) })
	t.Run("C-03 Loading Consumer Close", func(t *testing.T) { testBExecCloseLoadingConsumer(t) })
	t.Run("C-04 Ownership Cascade Close", func(t *testing.T) { testBExecCloseOwnership(t) })
}

// C-01 flow: deterministic P Active -> Close drains ApplyDone+UnwindDone and
// returns nil with pending 0 and the orchestrator done.
func testBExecCloseActive(t *testing.T) {
	rt := detNew(t)
	key := thm73Key()
	pf, err := rt.Load(&thm73Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	admitOneApply(t, rt, pf)
	if !waitB(t, rt, 3000, func() bool { return pf.State() == StateActive }) {
		t.Fatalf("P not Active")
	}
	if err := rt.Close(ctxBExec(t)); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if pf.State() != StateGone {
		t.Fatalf("P state = %v, want Gone", pf.State())
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after Close", rt.detPending())
	}
	select {
	case <-rt.orch.done:
	default:
		t.Fatal("orchestrator not done after Close")
	}
}

// C-02 flow: normal-mode Close unchanged.
func testBExecCloseNormal(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	key := thm73Key()
	pf, err := rt.Load(&thm73Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(ctxBExec(t)); err != nil {
		t.Fatal(err)
	}
	if err := rt.Close(ctxBExec(t)); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if pf.State() != StateGone {
		t.Fatalf("P state = %v, want Gone", pf.State())
	}
}

// C-03 flow: a Loading consumer is driven to Gone by Close without ever
// becoming Active and without a new activation being started.
func testBExecCloseLoadingConsumer(t *testing.T) {
	rt := detNew(t)
	key := thm73Key()
	pf, err := rt.Load(&thm73Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	admitOneApply(t, rt, pf)
	if !waitB(t, rt, 3000, func() bool { return pf.State() == StateActive }) {
		t.Fatalf("P not Active")
	}
	xf, err := rt.Load(&thm73Comp{name: "X", key: key, consumer: true})
	if err != nil {
		t.Fatal(err)
	}
	if !waitB(t, rt, 3000, func() bool { return rt.detPending() >= 1 && xf.State() == StateLoading }) {
		t.Fatalf("X not Loading with parked Apply; X=%v", xf.State())
	}
	actBefore := rt.nextActivationID.Load()
	if err := rt.Close(ctxBExec(t)); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if xf.State() != StateGone {
		t.Fatalf("X state = %v, want Gone", xf.State())
	}
	if rt.nextActivationID.Load() != actBefore {
		t.Fatalf("shutdown drain started a new activation (%d -> %d)", actBefore, rt.nextActivationID.Load())
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after Close", rt.detPending())
	}
}

// C-04 flow: ownership cascade — parent and owned child both reach Gone.
func testBExecCloseOwnership(t *testing.T) {
	rt := detNew(t)
	childCh := make(chan *Fiber, 1)
	pf, err := rt.Load(&detParentComp{childCh: childCh})
	if err != nil {
		t.Fatal(err)
	}
	admit := func(f *Fiber) {
		for _, s := range rt.detEnabledSteps() {
			if s.FiberID == f.ID() && s.Kind == StepApplyDone {
				if err := rt.detExecute(s); err != nil {
					t.Fatalf("detExecute(%v): %v", s, err)
				}
			}
		}
	}
	var child *Fiber
	for i := 0; i < 60; i++ {
		admit(pf)
		if child != nil {
			admit(child)
		} else {
			select {
			case child = <-childCh:
			default:
			}
		}
		if pf.State() == StateActive && child != nil && child.State() == StateActive {
			break
		}
		if !waitB(t, rt, 200, func() bool {
			return pf.State() == StateActive && (child == nil || child.State() == StateActive)
		}) {
			// keep looping; state transitions are async
		}
	}
	if pf.State() != StateActive {
		t.Fatalf("parent not Active: %v", pf.State())
	}
	if child == nil {
		t.Fatal("parent did not report child handle")
	}
	if child.State() != StateActive {
		t.Fatalf("child not Active: %v", child.State())
	}
	if err := rt.Close(ctxBExec(t)); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if child.State() != StateGone || pf.State() != StateGone {
		t.Fatalf("after Close child=%v parent=%v", child.State(), pf.State())
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after Close", rt.detPending())
	}
}

func ctxBExec(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}
