package runtime_test

import (
	"errors"
	"sync/atomic"
	"testing"

	"dynamic-runtime/runtime"
)

var (
	declAKey = runtime.NewKey[string]("decl.a")
	declBKey = runtime.NewKey[string]("decl.b")
)

// ---------------------------------------------------------------------------
// Declaration authority (Phase 2)
// ---------------------------------------------------------------------------

// D1 — Undeclared Require is rejected: the consumer cannot become Active.
func TestD1UndeclaredRequireRejected(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("bad-consumer")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, declAKey); err != nil {
			return nil, err
		}
		return nil, nil
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); !errors.Is(err, runtime.ErrUndeclaredRequire) {
		t.Fatalf("Ready = %v, want ErrUndeclaredRequire", err)
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// D2 — Undeclared Provide is rejected; previously committed effects unwind.
func TestD2UndeclaredProvideRejected(t *testing.T) {
	rt := newTestRuntime(t)
	var inverseRan atomic.Bool
	comp := newFakeComponent("bad-provider")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := ctx.Effect(func() (func() error, error) {
			return func() error { inverseRan.Store(true); return nil }, nil
		}); err != nil {
			return nil, err
		}
		if err := runtime.Provide(ctx, declAKey, "x"); err != nil {
			return nil, err
		}
		return nil, nil
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); !errors.Is(err, runtime.ErrUndeclaredProvide) {
		t.Fatalf("Ready = %v, want ErrUndeclaredProvide", err)
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", f.State())
	}
	if !inverseRan.Load() {
		t.Fatal("committed effect was not unwound after undeclared Provide failure")
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// D3 — Declared Provide is required only as an upper bound (subset allowed):
// a declared-but-unexercised provider surface still activates.
func TestD3DeclaredProvideSubsetAllowed(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("partial")
	comp.provide = []runtime.Capability{declAKey.Capability(), declBKey.Capability()}
	// This activation only provides declA; declB is declared but unused.
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		return nil, runtime.Provide(ctx, declAKey, "a")
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready = %v (upper-bound rule should allow subset)", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("state = %v, want Active", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// D4 — Duplicate declarations are tolerated (declaration is a set).
func TestD4DuplicateDeclarationTolerated(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("dup")
	comp.provide = []runtime.Capability{declAKey.Capability(), declAKey.Capability()}
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		return nil, runtime.Provide(ctx, declAKey, "a")
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready = %v", err)
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// D5 — Declared consumer/provider pair works end to end.
func TestD5DeclaredProviderConsumer(t *testing.T) {
	rt := newTestRuntime(t)
	p := newFakeComponent("provider")
	p.provide = []runtime.Capability{declAKey.Capability()}
	p.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		return nil, runtime.Provide(ctx, declAKey, "hello")
	}
	c := newFakeComponent("consumer")
	c.inject = []runtime.Dependency{runtime.Requires(declAKey)}
	gotCh := make(chan string, 1)
	c.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		v, err := runtime.Require(ctx, declAKey)
		if err != nil {
			return nil, err
		}
		gotCh <- v
		return nil, nil
	}
	cf, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	if v := <-gotCh; v != "hello" {
		t.Fatalf("consumer value = %q, want hello", v)
	}
}

// ---------------------------------------------------------------------------
// Panic containment (Phase 2)
// ---------------------------------------------------------------------------

// D6 — A panic inside Apply is contained: ErrComponentApplyPanic, committed
// effects unwind, Fiber lands in Failed.
func TestD6ApplyPanicContained(t *testing.T) {
	rt := newTestRuntime(t)
	var inverseRan atomic.Bool
	comp := newFakeComponent("panicker")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := ctx.Effect(func() (func() error, error) {
			return func() error { inverseRan.Store(true); return nil }, nil
		}); err != nil {
			return nil, err
		}
		panic("boom in apply")
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); !errors.Is(err, runtime.ErrComponentApplyPanic) {
		t.Fatalf("Ready = %v, want ErrComponentApplyPanic", err)
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", f.State())
	}
	if !inverseRan.Load() {
		t.Fatal("effect inverse did not run after Apply panic")
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// D7 — A panic inside an Effect install is contained as ErrEffectInstallPanic.
func TestD7EffectInstallPanicContained(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("install-panic")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		err := ctx.Effect(func() (func() error, error) {
			panic("boom in install")
		})
		return nil, err
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); !errors.Is(err, runtime.ErrEffectInstallPanic) {
		t.Fatalf("Ready = %v, want ErrEffectInstallPanic", err)
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(testTimeout(t))
}

// D8 — A panicking inverse during unwind is contained; LIFO unwind continues
// past it so remaining effects still run exactly once.
func TestD8InversePanicContinuesUnwind(t *testing.T) {
	rt := newTestRuntime(t)
	var firstRan atomic.Bool
	comp := newFakeComponent("inverse-panic")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		// Effect 1 (registered first, unwound last).
		if err := ctx.Effect(func() (func() error, error) {
			return func() error { firstRan.Store(true); return nil }, nil
		}); err != nil {
			return nil, err
		}
		// Effect 2 (registered last, unwound first) whose inverse panics.
		if err := ctx.Effect(func() (func() error, error) {
			return func() error { panic("boom in inverse") }, nil
		}); err != nil {
			return nil, err
		}
		return nil, nil
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready = %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(testTimeout(t)); !errors.Is(err, runtime.ErrInversePanic) {
		t.Fatalf("Gone = %v, want ErrInversePanic", err)
	}
	if f.State() != runtime.StateGone {
		t.Fatalf("state = %v, want Gone (fiber never stranded)", f.State())
	}
	if !firstRan.Load() {
		t.Fatal("LIFO unwind did not continue past the panicking inverse")
	}
}
