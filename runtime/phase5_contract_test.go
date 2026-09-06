package runtime_test

import (
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// ---- typed capability for tests ----
type greeter interface{ Greet() string }

type greeterVal struct{ tag string }

func (g greeterVal) Greet() string { return "hello from " + g.tag }

var greeterKey = runtime.NewKey[greeter]("greeter")

func mustProvide(t *testing.T, ctx *runtime.Context, tag string) {
	t.Helper()
	if err := runtime.Provide(ctx, greeterKey, greeter(greeterVal{tag: tag})); err != nil {
		t.Errorf("Provide(%q) error: %v", tag, err)
	}
}

// waitState polls until the fiber reaches the wanted state (test utility for
// observing intermediate states that have no dedicated waiter API).
func waitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatalf("fiber %s: state = %v, want %v", f.Name(), f.State(), want)
}

// TestDependencyPendingUntilProvider (plan Test 3): a mounted fiber whose
// required capability has no provider stays Pending - it must never be Failed.
func TestDependencyPendingUntilProvider(t *testing.T) {
	rt := newTestRuntime(t)
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	var resolved bool
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		resolved = true
		return nil, nil
	}

	f, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	waitState(t, f, runtime.StatePending)
	if got := f.State(); got != runtime.StatePending {
		t.Fatalf("state = %v, want Pending", got)
	}
	if f.Err() != nil {
		t.Fatalf("Err() = %v, want nil (dependency loss is not a failure)", f.Err())
	}
	if resolved {
		t.Fatal("consumer resolved a dependency before any provider existed")
	}
}

// Plan Test 4 / CT5 / P8: dependency recovery -> new activation.
func TestDependencyRecoveryActivatesConsumer(t *testing.T) {
	rt := newTestRuntime(t)
	var mu sync.Mutex
	var seen []string

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		g, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		mu.Lock()
		seen = append(seen, g.Greet())
		mu.Unlock()
		return nil, nil
	}

	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	waitState(t, cf, runtime.StatePending)

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p1")
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	mu.Lock()
	got := append([]string(nil), seen...)
	mu.Unlock()
	if len(got) != 1 || got[0] != "hello from p1" {
		t.Fatalf("consumer saw %v, want [hello from p1]", got)
	}

	// cleanup
	_ = pf.Dispose()
	_ = cf.Dispose()
}

// CT4 / plan Test 5: dependency withdrawal is consumer-first. When the
// provider is disposed, the consumer must fully withdraw before the provider's
// cleanup runs, and the consumer ends Pending (not Failed).
func TestDependencyLossConsumerFirst(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return func() error {
			rec.add("consumer:cleanup")
			return nil
		}, nil
	}

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return func() error {
			rec.add("provider:cleanup")
			return nil
		}, nil
	}

	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready error: %v", err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("provider Gone error: %v", err)
	}
	// Consumer must land in Pending (dependency loss is not a failure).
	waitState(t, cf, runtime.StatePending)
	if cf.Err() != nil {
		t.Fatalf("consumer Err() = %v, want nil", cf.Err())
	}

	events := rec.all()
	ci, pi := -1, -1
	for i, e := range events {
		if e == "consumer:cleanup" && ci == -1 {
			ci = i
		}
		if e == "provider:cleanup" && pi == -1 {
			pi = i
		}
	}
	if ci == -1 || pi == -1 {
		t.Fatalf("missing cleanup events: %v", events)
	}
	if ci > pi {
		t.Fatalf("consumer cleanup (%d) must precede provider cleanup (%d): %v", ci, pi, events)
	}

	// cleanup consumer
	_ = cf.Dispose()
	_ = cf.Gone(testTimeout(t))
}

// CT1 / plan Test 11 / P6: a second provider for an exclusive capability fails
// and must not disturb the first provider.
func TestDuplicateProviderRejected(t *testing.T) {
	rt := newTestRuntime(t)

	mk := func(name string) *fakeComponent {
		c := newFakeComponent(name)
		c.provide = []runtime.Capability{greeterKey.Capability()}
		c.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
			err := runtime.Provide(ctx, greeterKey, greeter(greeterVal{tag: name}))
			if err != nil {
				return nil, err
			}
			return nil, nil
		}
		return c
	}

	a := mk("A")
	af, err := rt.Load(a)
	if err != nil {
		t.Fatalf("Load A error: %v", err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("A Ready error: %v", err)
	}

	b := mk("B")
	bf, err := rt.Load(b)
	if err != nil {
		t.Fatalf("Load B error: %v", err)
	}
	err = bf.Ready(testTimeout(t))
	if !errors.Is(err, runtime.ErrDuplicateProvider) {
		t.Fatalf("B Ready() = %v, want ErrDuplicateProvider", err)
	}
	if got := bf.State(); got != runtime.StateFailed {
		t.Fatalf("B state = %v, want Failed", got)
	}

	// A is unaffected and still Active.
	if got := af.State(); got != runtime.StateActive {
		t.Fatalf("A state = %v, want Active", got)
	}
	if err := af.Err(); err != nil {
		t.Fatalf("A Err() = %v, want nil", err)
	}

	// The registry must still resolve A's provider for a fresh consumer.
	consumer := newFakeComponent("late-consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	var greeted string
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		g, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		greeted = g.Greet()
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}
	if greeted != "hello from A" {
		t.Fatalf("consumer resolved %q, want A's provider", greeted)
	}

	_ = af.Dispose()
	_ = bf.Dispose()
	_ = cf.Dispose()
}

// CT6 / plan Test 6 / P7: provider replacement. Consumer C binds P1, then P1 is
// withdrawn and P2 takes over; C must never remain bound to P1 and must
// eventually bind P2.
func TestProviderReplacementConsumerRebinds(t *testing.T) {
	rt := newTestRuntime(t)
	var mu sync.Mutex
	var seen []string

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		g, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		mu.Lock()
		seen = append(seen, g.Greet())
		mu.Unlock()
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}

	mkProvider := func(name string) *fakeComponent {
		c := newFakeComponent(name)
		c.provide = []runtime.Capability{greeterKey.Capability()}
		c.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
			mustProvide(t, ctx, name)
			return nil, nil
		}
		return c
	}

	p1 := mkProvider("P1")
	p1f, err := rt.Load(p1)
	if err != nil {
		t.Fatalf("Load P1 error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	// Replace: withdraw P1 fully, then load P2 (withdraw-then-load).
	if err := p1f.Dispose(); err != nil {
		t.Fatalf("Dispose P1 error: %v", err)
	}
	if err := p1f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("P1 Gone error: %v", err)
	}
	waitState(t, cf, runtime.StatePending)

	p2 := mkProvider("P2")
	p2f, err := rt.Load(p2)
	if err != nil {
		t.Fatalf("Load P2 error: %v", err)
	}
	if err := p2f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("P2 Ready error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error (after rebind): %v", err)
	}

	mu.Lock()
	got := append([]string(nil), seen...)
	mu.Unlock()
	want := []string{"hello from P1", "hello from P2"}
	if len(got) != len(want) {
		t.Fatalf("consumer activations saw %v, want %v", got, want)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("consumer activations saw %v, want %v", got, want)
		}
	}

	_ = p2f.Dispose()
	_ = cf.Dispose()
}

// Spec 20: provider identity is the activation generation. Re-activating the
// SAME fiber is a new provider generation, so consumers must re-activate.
func TestSameFiberReactivationIsNewProviderGeneration(t *testing.T) {
	rt := newTestRuntime(t)
	var mu sync.Mutex
	var seen []string
	var actCount int

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		g, err := runtime.Require(ctx, greeterKey)
		if err != nil {
			return nil, err
		}
		mu.Lock()
		seen = append(seen, g.Greet())
		mu.Unlock()
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mu.Lock()
		actCount++
		tag := "gen"
		mu.Unlock()
		mustProvide(t, ctx, tag)
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	// Dispose the provider fiber and remount it (a brand-new activation).
	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose provider error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("provider Gone error: %v", err)
	}
	waitState(t, cf, runtime.StatePending)

	if err := pf.Load(); err != nil {
		t.Fatalf("provider Load error: %v", err)
	}
	if err := pf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider Ready error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error (after regeneration): %v", err)
	}

	mu.Lock()
	defer mu.Unlock()
	if actCount != 2 {
		t.Fatalf("provider activations = %d, want 2", actCount)
	}
	if len(seen) != 2 {
		t.Fatalf("consumer activations saw %v, want 2 (rebound to new generation)", seen)
	}

	_ = pf.Dispose()
	_ = cf.Dispose()
}

// Dependency change while Apply is running: if the dependency disappears
// before Apply completes, the fiber must NOT become Active even though Apply
// returned success; it must unwind and land in Pending.
func TestDependencyLossDuringApplyPreventsActive(t *testing.T) {
	rt := newTestRuntime(t)
	applied := make(chan struct{})
	release := make(chan struct{})

	var actCount atomic.Int32
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		// First activation resolves the dependency normally. The second
		// activation blocks while Apply is in flight so the test can pull the
		// dependency away mid-apply.
		if actCount.Add(1) == 1 {
			if _, err := runtime.Require(ctx, greeterKey); err != nil {
				return nil, err
			}
			return nil, nil
		}
		close(applied)
		<-release
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			// Dependency disappeared mid-apply: report success anyway; the
			// runtime must still refuse to activate because the snapshot is
			// stale.
			_ = err
		}
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load provider error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	// Start a second activation of the consumer and pull its dependency away
	// while Apply is in flight.
	if err := cf.Dispose(); err != nil {
		t.Fatalf("consumer Dispose error: %v", err)
	}
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("consumer Gone error: %v", err)
	}
	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("provider Gone error: %v", err)
	}

	// Bring provider back and remount consumer; then pull provider away while
	// the consumer's apply is running.
	p2 := newFakeComponent("provider2")
	p2.provide = []runtime.Capability{greeterKey.Capability()}
	p2.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p2")
		return nil, nil
	}
	p2f, err := rt.Load(p2)
	if err != nil {
		t.Fatalf("Load provider2 error: %v", err)
	}
	if err := p2f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("provider2 Ready error: %v", err)
	}
	if err := cf.Load(); err != nil {
		t.Fatalf("consumer Load error: %v", err)
	}
	<-applied

	if err := p2f.Dispose(); err != nil {
		t.Fatalf("provider2 Dispose error: %v", err)
	}
	close(release)

	// Consumer's apply returns success but its dependency snapshot is stale: it
	// must end Pending and never become Active.
	waitState(t, cf, runtime.StatePending)
	if cf.Err() != nil {
		t.Fatalf("consumer Err() = %v, want nil", cf.Err())
	}

	_ = p2f.Gone(testTimeout(t))
	_ = cf.Dispose()
	_ = cf.Gone(testTimeout(t))
}
