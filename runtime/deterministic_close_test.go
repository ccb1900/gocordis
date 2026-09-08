package runtime

import (
	"context"
	"testing"
	"time"
)

func waitB(t *testing.T, rt *Runtime, ms int, cond func() bool) bool {
	t.Helper()
	deadline := time.Now().Add(time.Duration(ms) * time.Millisecond)
	for time.Now().Before(deadline) {
		if cond() {
			return true
		}
		time.Sleep(time.Millisecond)
	}
	return false
}

func detNew(t *testing.T) *Runtime {
	t.Helper()
	rt, err := New(withRuntimeMode(runtimeDeterministic))
	if err != nil {
		t.Fatal(err)
	}
	return rt
}

func admitOneApply(t *testing.T, rt *Runtime, f *Fiber) {
	t.Helper()
	if !waitB(t, rt, 3000, func() bool { return rt.detPending() >= 1 }) {
		t.Fatalf("no parked completion")
	}
	en := rt.detEnabledSteps()
	for _, s := range en {
		if s.FiberID == f.ID() && s.Kind == detStepApplyDone {
			if err := rt.detExecute(s); err != nil {
				t.Fatal(err)
			}
			return
		}
	}
	t.Fatalf("expected ApplyDone(%d), enabled=%v", f.ID(), en)
}

// C-01 — Deterministic: P Active -> Close returns nil; P Gone; pending 0.
func TestC01DeterministicCloseAfterActive(t *testing.T) {
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
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if pf.State() != StateGone {
		t.Fatalf("P state = %v, want Gone", pf.State())
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d, want 0 after Close", rt.detPending())
	}
	select {
	case <-rt.orch.done:
	default:
		t.Fatal("orchestrator not done after Close")
	}
}

// C-02 — Normal mode unchanged: P Active -> Close returns nil.
func TestC02NormalCloseUnchanged(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	key := thm73Key()
	pf, err := rt.Load(&thm73Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(ctxBG(t)); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if pf.State() != StateGone {
		t.Fatalf("P state = %v, want Gone", pf.State())
	}
}

// C-03 — Shutdown drain is not a general scheduler: a Loading consumer (apply
// parked) is driven to Gone WITHOUT ever reaching Active during Close, and no
// new activation is started by the drain.
func TestC03ShutdownDrainNotGeneralScheduler(t *testing.T) {
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
	// X becomes Loading (provider active) and its Apply parks.
	if !waitB(t, rt, 3000, func() bool { return rt.detPending() >= 1 && xf.State() == StateLoading }) {
		t.Fatalf("X not Loading with parked Apply; X=%v", xf.State())
	}
	actBefore := rt.nextActivationID.Load()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
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

// C-04 — Ownership: parent with owned child; Close drives both Gone.
func TestC04OwnershipClose(t *testing.T) {
	rt := detNew(t)
	childCh := make(chan *Fiber, 1)
	pf, err := rt.Load(&detParentComp{childCh: childCh})
	if err != nil {
		t.Fatal(err)
	}
	admit := func(f *Fiber) {
		en := rt.detEnabledSteps()
		for _, s := range en {
			if s.FiberID == f.ID() && s.Kind == detStepApplyDone {
				_ = rt.detExecute(s)
			}
		}
	}
	// Drive ApplyDone admissions until both parent and its reported child are
	// Active (whichever order they park).
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
		both := pf.State() == StateActive && child != nil && child.State() == StateActive
		if both {
			break
		}
		if !waitB(t, rt, 200, func() bool {
			return (pf.State() == StateActive) && (child == nil || child.State() == StateActive)
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

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if child.State() != StateGone || pf.State() != StateGone {
		t.Fatalf("after Close child=%v parent=%v", child.State(), pf.State())
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after Close", rt.detPending())
	}
}

type detLeafComp struct{}

func (c *detLeafComp) Name() string          { return "leaf" }
func (c *detLeafComp) Inject() []Dependency  { return nil }
func (c *detLeafComp) Provide() []Capability { return nil }
func (c *detLeafComp) Apply(ctx *Context) (Cleanup, error) {
	return nil, nil
}

type detParentComp struct {
	childCh chan *Fiber
}

func (c *detParentComp) Name() string          { return "parent" }
func (c *detParentComp) Inject() []Dependency  { return nil }
func (c *detParentComp) Provide() []Capability { return nil }
func (c *detParentComp) Apply(ctx *Context) (Cleanup, error) {
	f, err := ctx.Child(&detLeafComp{})
	if err != nil {
		return nil, err
	}
	select {
	case c.childCh <- f:
	default:
	}
	return nil, nil
}

func ctxBG(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}
