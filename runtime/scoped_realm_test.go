package runtime_test

import (
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

var scKey = runtime.NewKey[string]("scope.realm")

type scProvider struct {
	tag string
	out chan string
}

func (c *scProvider) Name() string                  { return "scope-provider:" + c.tag }
func (c *scProvider) Inject() []runtime.Dependency  { return nil }
func (c *scProvider) Provide() []runtime.Capability { return []runtime.Capability{scKey.Capability()} }
func (c *scProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, scKey, c.tag); err != nil {
		return nil, err
	}
	// Own a consumer that inherits this provider's realm.
	if _, err := ctx.Child(&scConsumer{out: c.out}); err != nil {
		return nil, err
	}
	return nil, nil
}

type scConsumer struct {
	out chan string
}

func (c *scConsumer) Name() string { return "scope-consumer" }
func (c *scConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(scKey)}
}
func (c *scConsumer) Provide() []runtime.Capability { return nil }
func (c *scConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, scKey)
	if err != nil {
		return nil, err
	}
	if c.out != nil {
		c.out <- v
	}
	return nil, nil
}

// scActivator creates two explicit-scope sibling providers (each owning a
// same-realm consumer) plus an unscoped cross consumer that must stay Pending.
type scActivator struct {
	hs chan *runtime.Fiber // spawned child handles in creation order
}

func (c *scActivator) Name() string                  { return "scope-activator" }
func (c *scActivator) Inject() []runtime.Dependency  { return nil }
func (c *scActivator) Provide() []runtime.Capability { return nil }
func (c *scActivator) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	a, err := ctx.Child(&scProvider{tag: "a", out: make(chan string, 1)}, runtime.WithScope())
	if err != nil {
		return nil, err
	}
	b, err := ctx.Child(&scProvider{tag: "b", out: make(chan string, 1)}, runtime.WithScope())
	if err != nil {
		return nil, err
	}
	x, err := ctx.Child(&scConsumer{}) // unscoped: root realm, must stay Pending
	if err != nil {
		return nil, err
	}
	for _, f := range []*runtime.Fiber{a, b, x} {
		c.hs <- f
	}
	return nil, nil
}

// TestScopedSiblingIsolationAndVisibility — sibling explicit realms provide the
// same key independently and are mutually invisible; an unscoped consumer in
// the parent (root) realm cannot resolve them.
func TestScopedSiblingIsolationAndVisibility(t *testing.T) {
	rt := newTestRuntime(t)
	act := &scActivator{hs: make(chan *runtime.Fiber, 8)}
	af, err := rt.Load(act)
	if err != nil {
		t.Fatal(err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("activator not active: %v", err)
	}
	pa := <-act.hs
	pb := <-act.hs
	cross := <-act.hs

	waitFiberState(t, pa, runtime.StateActive)
	waitFiberState(t, pb, runtime.StateActive)
	waitFiberState(t, cross, runtime.StatePending)

	// Cross-sibling invisibility: no root-realm provider exists, so cross stays
	// Pending even after both sibling providers are Active.
	if cross.State() != runtime.StatePending {
		t.Fatalf("cross consumer state = %v, want Pending", cross.State())
	}

	// Remove sibling subtree pa (with its same-realm consumer). pb must stay
	// Active (sibling isolation) — removing one scope never disturbs another.
	if err := pa.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pa.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	if pb.State() != runtime.StateActive {
		t.Fatalf("sibling pb disturbed by pa removal: %v", pb.State())
	}
	// The unscoped cross consumer is still Pending (nothing on its realm path).
	if cross.State() != runtime.StatePending {
		t.Fatalf("cross consumer changed after sibling removal: %v", cross.State())
	}

	_ = pb.Dispose()
	_ = pb.Gone(testTimeout(t))
	_ = af.Dispose()
	_ = af.Gone(testTimeout(t))
}

func waitFiberState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("fiber %s state = %v, want %v", f.Name(), f.State(), want)
}

// intReader is a consumer of the interception test key.
type intReader struct {
	key runtime.Key[int]
	out chan int
	err chan error
}

func (c *intReader) Name() string { return "int-reader" }
func (c *intReader) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(c.key)}
}
func (c *intReader) Provide() []runtime.Capability { return nil }
func (c *intReader) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, c.key)
	if err != nil {
		c.err <- err
		return nil, nil
	}
	c.out <- v
	return nil, nil
}

type intProvider struct {
	key runtime.Key[int]
}

func (c *intProvider) Name() string                  { return "int-provider" }
func (c *intProvider) Inject() []runtime.Dependency  { return nil }
func (c *intProvider) Provide() []runtime.Capability { return []runtime.Capability{c.key.Capability()} }
func (c *intProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, c.key, 5)
}

// intActivator installs two interceptors (install order) then spawns an
// in-realm provider + reader to observe the deterministic chain.
type intActivator struct {
	key runtime.Key[int]
	rd  chan int
}

func (c *intActivator) Name() string                  { return "int-activator" }
func (c *intActivator) Inject() []runtime.Dependency  { return nil }
func (c *intActivator) Provide() []runtime.Capability { return nil }
func (c *intActivator) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Intercept(ctx, c.key, func(v int, _ runtime.Key[int]) (int, error) {
		return v + 1, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Intercept(ctx, c.key, func(v int, _ runtime.Key[int]) (int, error) {
		return v * 10, nil
	}); err != nil {
		return nil, err
	}
	if _, err := ctx.Child(&intProvider{key: c.key}); err != nil {
		return nil, err
	}
	if _, err := ctx.Child(&intReader{key: c.key, out: c.rd, err: make(chan error, 1)}); err != nil {
		return nil, err
	}
	return nil, nil
}

// TestInterceptChainOrder — interceptors apply in install order (ancestor ->
// this realm) on every Require: value 5 -> (+1)=6 -> (*10)=60.
func TestInterceptChainOrder(t *testing.T) {
	rt := newTestRuntime(t)
	key := runtime.NewKey[int]("intercept.order")
	act := &intActivator{key: key, rd: make(chan int, 1)}
	af, err := rt.Load(act)
	if err != nil {
		t.Fatal(err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("activator not active: %v", err)
	}
	select {
	case v := <-act.rd:
		if v != 60 {
			t.Fatalf("intercepted value = %d, want 60", v)
		}
	case <-time.After(8 * time.Second):
		t.Fatal("reader never observed the intercepted value")
	}
	_ = af.Dispose()
	_ = af.Gone(testTimeout(t))
}

// TestInterceptPanicContained — a panicking interceptor is contained and the
// reader receives an error (never a process crash / provider corruption).
func TestInterceptPanicContained(t *testing.T) {
	rt := newTestRuntime(t)
	key := runtime.NewKey[int]("intercept.panic")
	type panicActivator struct {
		key runtime.Key[int]
		rd  chan int
	}
	a := &panicActivator{key: key, rd: make(chan int, 1)}
	// Reuse intActivator-like flow via a local activator definition.
	_ = a
	// Implement inline component types are overkill; use intActivator with a
	// panicking variant via a dedicated component below.
	act := &panicInterceptActivator{key: key, errs: make(chan error, 1)}
	af, err := rt.Load(act)
	if err != nil {
		t.Fatal(err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("activator not active: %v", err)
	}
	select {
	case err := <-act.errs:
		if err == nil {
			t.Fatal("expected interceptor error")
		}
	case <-time.After(8 * time.Second):
		t.Fatal("reader never observed interceptor error")
	}
	_ = af.Dispose()
	_ = af.Gone(testTimeout(t))
}

type panicInterceptActivator struct {
	key  runtime.Key[int]
	errs chan error
}

func (c *panicInterceptActivator) Name() string                  { return "panic-activator" }
func (c *panicInterceptActivator) Inject() []runtime.Dependency  { return nil }
func (c *panicInterceptActivator) Provide() []runtime.Capability { return nil }
func (c *panicInterceptActivator) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Intercept(ctx, c.key, func(int, runtime.Key[int]) (int, error) {
		panic("boom in interceptor")
	}); err != nil {
		return nil, err
	}
	if _, err := ctx.Child(&intProvider{key: c.key}); err != nil {
		return nil, err
	}
	if _, err := ctx.Child(&intReader{key: c.key, out: make(chan int, 1), err: c.errs}); err != nil {
		return nil, err
	}
	return nil, nil
}
