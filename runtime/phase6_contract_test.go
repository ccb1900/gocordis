package runtime_test

import (
	"errors"
	"testing"

	"dynamic-runtime/runtime"
)

// CT9 / plan Test 10: when a parent is disposed, owned children are withdrawn
// first; children reach Gone before the parent, and their cleanups run first.
func TestOwnedChildCascade(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	childCh := make(chan *runtime.Fiber, 1)

	child := newFakeComponent("child")
	child.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		return func() error {
			rec.add("child:cleanup")
			return nil
		}, nil
	}

	parent := newFakeComponent("parent")
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		cf, err := ctx.Child(child)
		if err != nil {
			return nil, err
		}
		childCh <- cf
		return func() error {
			rec.add("parent:cleanup")
			return nil
		}, nil
	}

	pf, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("parent Ready error: %v", err)
	}
	cf := <-childCh
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("child Ready error: %v", err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("parent Dispose error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("parent Gone error: %v", err)
	}

	// Child must be Gone before parent completed exit.
	if got := cf.State(); got != runtime.StateGone {
		t.Fatalf("child state = %v after parent Gone, want Gone", got)
	}

	events := rec.all()
	ci, pi := -1, -1
	for i, e := range events {
		if e == "child:cleanup" && ci == -1 {
			ci = i
		}
		if e == "parent:cleanup" && pi == -1 {
			pi = i
		}
	}
	if ci == -1 || pi == -1 {
		t.Fatalf("missing cleanup events: %v", events)
	}
	if ci > pi {
		t.Fatalf("child cleanup (%d) must precede parent cleanup (%d): %v", ci, pi, events)
	}
}

// A three-level ownership tree unwinds grandchild -> child -> parent.
func TestOwnedGrandchildCascade(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	grandCh := make(chan *runtime.Fiber, 1)
	childCh := make(chan *runtime.Fiber, 1)

	grand := newFakeComponent("grand")
	grand.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		return func() error {
			rec.add("grand:cleanup")
			return nil
		}, nil
	}

	child := newFakeComponent("child")
	child.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		gf, err := ctx.Child(grand)
		if err != nil {
			return nil, err
		}
		grandCh <- gf
		return func() error {
			rec.add("child:cleanup")
			return nil
		}, nil
	}

	parent := newFakeComponent("parent")
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		cf, err := ctx.Child(child)
		if err != nil {
			return nil, err
		}
		childCh <- cf
		return func() error {
			rec.add("parent:cleanup")
			return nil
		}, nil
	}

	pf, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("parent Ready error: %v", err)
	}
	cf := <-childCh
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("child Ready error: %v", err)
	}
	gf := <-grandCh
	if err := gf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("grandchild Ready error: %v", err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("parent Dispose error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("parent Gone error: %v", err)
	}
	if got := cf.State(); got != runtime.StateGone {
		t.Fatalf("child state = %v, want Gone", got)
	}
	if got := gf.State(); got != runtime.StateGone {
		t.Fatalf("grandchild state = %v, want Gone", got)
	}

	events := rec.all()
	order := map[string]int{}
	for i, e := range events {
		if _, seen := order[e]; !seen {
			order[e] = i
		}
	}
	for _, name := range []string{"grand:cleanup", "child:cleanup", "parent:cleanup"} {
		if _, ok := order[name]; !ok {
			t.Fatalf("missing cleanup %q in %v", name, events)
		}
	}
	if !(order["grand:cleanup"] < order["child:cleanup"] && order["child:cleanup"] < order["parent:cleanup"]) {
		t.Fatalf("cleanup order = %v, want grand < child < parent", events)
	}
}

// Children of a failed parent activation are still disposed (no orphans).
func TestChildrenDisposedWhenParentApplyFails(t *testing.T) {
	rt := newTestRuntime(t)
	childCh := make(chan *runtime.Fiber, 1)

	child := newFakeComponent("child")
	child.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		return nil, nil
	}

	boom := errors.New("parent boom")
	parent := newFakeComponent("parent")
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		cf, err := ctx.Child(child)
		if err != nil {
			return nil, err
		}
		childCh <- cf
		return nil, boom
	}

	pf, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); !errors.Is(err, boom) {
		t.Fatalf("parent Ready() = %v, want %v", err, boom)
	}
	cf := <-childCh

	// Child must be disposed even though the parent never became Active.
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("child Gone error: %v", err)
	}
	if got := cf.State(); got != runtime.StateGone {
		t.Fatalf("child state = %v, want Gone", got)
	}
	if got := pf.State(); got != runtime.StateFailed {
		t.Fatalf("parent state = %v, want Failed", got)
	}

	_ = pf.Dispose()
	_ = pf.Gone(testTimeout(t))
}

// Ownership is not dependency: disposing an owner disposes the child even when
// the child's dependencies come from an unrelated provider that stays alive.
func TestOwnershipIndependentFromDependency(t *testing.T) {
	rt := newTestRuntime(t)
	childCh := make(chan *runtime.Fiber, 1)

	// An unrelated provider of greeter that outlives the owner.
	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "shared")
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready error: %v", err)
	}

	child := newFakeComponent("child")
	child.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	child.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return nil, nil
	}

	parent := newFakeComponent("parent")
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		cf, err := ctx.Child(child)
		if err != nil {
			return nil, err
		}
		childCh <- cf
		return nil, nil
	}

	parf, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent error: %v", err)
	}
	if err := parf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("parent Ready error: %v", err)
	}
	cf := <-childCh
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("child Ready error: %v", err)
	}

	if err := parf.Dispose(); err != nil {
		t.Fatalf("parent Dispose error: %v", err)
	}
	if err := parf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("parent Gone error: %v", err)
	}
	if got := cf.State(); got != runtime.StateGone {
		t.Fatalf("child state = %v, want Gone (owned child dies with owner)", got)
	}
	// The unrelated provider is unaffected.
	if got := pf.State(); got != runtime.StateActive {
		t.Fatalf("provider state = %v, want Active", got)
	}

	_ = pf.Dispose()
	_ = pf.Gone(testTimeout(t))
}

// A Context of an ended activation cannot create children.
func TestChildRejectedOnEndedActivationContext(t *testing.T) {
	rt := newTestRuntime(t)
	ctxCh := make(chan *runtime.Context, 1)
	parent := newFakeComponent("parent")
	parent.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		ctxCh <- ctx
		return nil, nil
	}
	pf, err := rt.Load(parent)
	if err != nil {
		t.Fatalf("Load parent error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("parent Ready error: %v", err)
	}
	ctx := <-ctxCh
	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	if _, err := ctx.Child(newFakeComponent("late")); !errors.Is(err, runtime.ErrContextClosed) {
		t.Fatalf("Child on ended context = %v, want ErrContextClosed", err)
	}
}
