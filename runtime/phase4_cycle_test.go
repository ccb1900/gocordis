package runtime_test

import (
	"errors"
	"testing"

	"dynamic-runtime/runtime"
)

var (
	cycAKey = runtime.NewKey[string]("cycle.a")
	cycBKey = runtime.NewKey[string]("cycle.b")
)

// cycleComp declares it requires requireKey and provides provideKey.
type cycleComp struct {
	name        string
	requireKey  runtime.Key[string]
	provideKey  runtime.Key[string]
	skipProvide bool
}

func (c *cycleComp) Name() string { return c.name }
func (c *cycleComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(c.requireKey)}
}
func (c *cycleComp) Provide() []runtime.Capability {
	if c.skipProvide {
		return nil
	}
	return []runtime.Capability{c.provideKey.Capability()}
}
func (c *cycleComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, c.provideKey, c.name)
}

// TestPhase4LoadCycleRejected — a mutual declared dependency between two root
// Fibers is rejected at the Load boundary with ErrDependencyCycle and an
// actionable diagnostic; the first Fiber stays mounted (Pending).
func TestPhase4LoadCycleRejected(t *testing.T) {
	rt := newTestRuntime(t)
	a := &cycleComp{name: "A", requireKey: cycBKey, provideKey: cycAKey}
	b := &cycleComp{name: "B", requireKey: cycAKey, provideKey: cycBKey}

	fa, err := rt.Load(a)
	if err != nil {
		t.Fatalf("load A: %v", err)
	}
	if _, err := rt.Load(b); !errors.Is(err, runtime.ErrDependencyCycle) {
		t.Fatalf("Load(B) = %v, want ErrDependencyCycle", err)
	}
	if fa.State() != runtime.StatePending {
		t.Fatalf("A state = %v, want Pending", fa.State())
	}
	// Reverse order rejects too (deterministic at whichever side mounts second).
	rt2 := newTestRuntime(t)
	if _, err := rt2.Load(b); err != nil {
		t.Fatalf("load B first: %v", err)
	}
	if _, err := rt2.Load(a); !errors.Is(err, runtime.ErrDependencyCycle) {
		t.Fatalf("Load(A) second = %v, want ErrDependencyCycle", err)
	}
}

// mountCycler is a root activator that spawns two same-realm children which
// mutually require each other's provided key; the second Child must fail with
// ErrDependencyCycle, failing the activator's activation (no silent Pending).
type mountCycler struct {
	errCh chan error
}

func (c *mountCycler) Name() string                  { return "mount-cycler" }
func (c *mountCycler) Inject() []runtime.Dependency  { return nil }
func (c *mountCycler) Provide() []runtime.Capability { return nil }
func (c *mountCycler) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	child1 := &cycleComp{name: "child1", requireKey: cycBKey, provideKey: cycAKey}
	child2 := &cycleComp{name: "child2", requireKey: cycAKey, provideKey: cycBKey}
	if _, err := ctx.Child(child1); err != nil {
		return nil, err
	}
	_, err := ctx.Child(child2) // same realm: creates A->B->A declared cycle
	if err != nil {
		c.errCh <- err
		return nil, err
	}
	return nil, nil
}

// TestPhase4ChildBoundaryCycleRejected — a cycle formed by a second Child in
// the same realm is rejected at the Child boundary and fails the activator.
func TestPhase4ChildBoundaryCycleRejected(t *testing.T) {
	rt := newTestRuntime(t)
	act := &mountCycler{errCh: make(chan error, 1)}
	f, err := rt.Load(act)
	if err != nil {
		t.Fatalf("Load activator: %v", err)
	}
	if err := f.Ready(testTimeout(t)); !errors.Is(err, runtime.ErrDependencyCycle) {
		t.Fatalf("Ready = %v, want ErrDependencyCycle (child boundary)", err)
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("activator state = %v, want Failed", f.State())
	}
	select {
	case cerr := <-act.errCh:
		if !errors.Is(cerr, runtime.ErrDependencyCycle) {
			t.Fatalf("ctx.Child error = %v", cerr)
		}
	default:
		t.Fatal("child creation did not report the cycle")
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}
