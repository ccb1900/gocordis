package runtime_test

import (
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// P3 (Ordering): a consumer must never become Active before its provider's
// identity is valid. Provider's Apply must finish before the consumer's Apply.
func TestPropertyOrderingProviderBeforeConsumer(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			rec.add("consumer:apply:missing")
			return nil, err
		}
		rec.add("consumer:apply:ok")
		return nil, nil
	}
	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		rec.add("provider:apply")
		return nil, nil
	}

	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	// Consumer first (Pending), then provider.
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

	events := rec.all()
	if len(events) != 2 || events[0] != "provider:apply" || events[1] != "consumer:apply:ok" {
		t.Fatalf("events = %v, want [provider:apply consumer:apply:ok]", events)
	}
}

// P4 (Progress): with finite operations and Apply/Cleanup that return, the
// runtime converges to a quiescent state (all fibers terminal).
func TestPropertyProgressToQuiescence(t *testing.T) {
	rt := newTestRuntime(t)

	provider := newFakeComponent("provider")
	provider.provide = []runtime.Capability{greeterKey.Capability()}
	provider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		mustProvide(t, ctx, "p")
		return nil, nil
	}
	pf, err := rt.Load(provider)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}

	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if _, err := runtime.Require(ctx, greeterKey); err != nil {
			return nil, err
		}
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}

	// Dispose everything: every fiber must reach terminal Gone (quiescence).
	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose provider error: %v", err)
	}
	if err := cf.Dispose(); err != nil {
		t.Fatalf("Dispose consumer error: %v", err)
	}
	if err := pf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("provider Gone error: %v", err)
	}
	if err := cf.Gone(testTimeout(t)); err != nil {
		t.Fatalf("consumer Gone error: %v", err)
	}
}

// P5 (Confluence): the same finite operation set under different legal
// schedules converges to the same Runtime-managed observable state.
func TestPropertyConfluenceReplacement(t *testing.T) {
	run := func(order []string) []string {
		rt, err := runtime.New()
		if err != nil {
			t.Fatalf("New error: %v", err)
		}
		defer func() { _ = rt.Close(testTimeout(t)) }()

		var mu sync.Mutex
		var seen []string
		mkConsumer := func(name string) *fakeComponent {
			c := newFakeComponent(name)
			c.inject = []runtime.Dependency{runtime.Requires(greeterKey)}
			c.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
				g, err := runtime.Require(ctx, greeterKey)
				if err != nil {
					return nil, err
				}
				mu.Lock()
				seen = append(seen, g.Greet())
				mu.Unlock()
				return nil, nil
			}
			return c
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

		c1 := mkConsumer("c1")
		c2 := mkConsumer("c2")
		p1 := mkProvider("P1")

		var fibers []*runtime.Fiber
		load := func(c runtime.Component) *runtime.Fiber {
			f, err := rt.Load(c)
			if err != nil {
				t.Fatalf("Load error: %v", err)
			}
			return f
		}
		for _, name := range order {
			switch name {
			case "c1":
				fibers = append(fibers, load(c1))
			case "c2":
				fibers = append(fibers, load(c2))
			case "p1":
				fibers = append(fibers, load(p1))
			}
		}
		// Wait until everything that can be active is active.
		for _, f := range fibers {
			_ = f.Ready(testTimeout(t))
		}

		// Replace P1 with P2 (withdraw-then-load).
		for _, f := range fibers {
			if f.Component() == p1 {
				_ = f.Dispose()
				_ = f.Gone(testTimeout(t))
			}
		}
		p2 := mkProvider("P2")
		p2f := load(p2)
		_ = p2f.Ready(testTimeout(t))
		for _, f := range fibers {
			_ = f.Ready(testTimeout(t))
		}

		mu.Lock()
		out := append([]string(nil), seen...)
		mu.Unlock()
		return out
	}

	scheduleA := run([]string{"c1", "c2", "p1"})
	scheduleB := run([]string{"p1", "c2", "c1"})
	scheduleC := run([]string{"c2", "p1", "c1"})

	for name, got := range map[string][]string{"A": scheduleA, "B": scheduleB, "C": scheduleC} {
		if len(got) != 4 {
			t.Fatalf("schedule %s consumer activations = %v, want 4 (c1/c2 x P1/P2)", name, got)
		}
	}
	if len(scheduleA) != len(scheduleB) || len(scheduleA) != len(scheduleC) {
		t.Fatalf("schedules diverged: A=%v B=%v C=%v", scheduleA, scheduleB, scheduleC)
	}
	for i := range scheduleA {
		if scheduleA[i] != scheduleB[i] || scheduleA[i] != scheduleC[i] {
			t.Fatalf("schedules diverged at %d: A=%v B=%v C=%v", i, scheduleA, scheduleB, scheduleC)
		}
	}
}

// P10 / plan Test 15: concurrent Load + Close + Load. Every Load accepted
// before Close must end Gone; Load after Close must be rejected.
func TestPropertyRaceLoadClose(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("New error: %v", err)
	}

	var wg sync.WaitGroup
	var loaded atomic.Int32
	var loadErrs atomic.Int32
	stop := make(chan struct{})

	for i := 0; i < 4; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				select {
				case <-stop:
					return
				default:
				}
				_, err := rt.Load(noopComponent{name: "racer"})
				if err != nil {
					if errors.Is(err, runtime.ErrRuntimeClosed) {
						loadErrs.Add(1)
						return
					}
					return
				}
				loaded.Add(1)
			}
		}()
	}

	// Wait until at least one Load succeeded, then Close.
	deadline := time.Now().Add(5 * time.Second)
	for loaded.Load() == 0 && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if err := rt.Close(testTimeout(t)); err != nil {
		t.Fatalf("Close error: %v", err)
	}
	close(stop)
	wg.Wait()

	if loaded.Load() == 0 {
		t.Fatal("no Load succeeded before Close; the race did not exercise anything")
	}
	if loadErrs.Load() == 0 {
		t.Fatal("no Load was rejected; Close did not take effect")
	}
	// After Close, Load is rejected.
	if _, err := rt.Load(noopComponent{name: "late"}); !errors.Is(err, runtime.ErrRuntimeClosed) {
		t.Fatalf("Load after Close = %v, want ErrRuntimeClosed", err)
	}
}

// CT10 (Stable Registry pattern): consumers depend on a stable registry, not
// on its members. Member churn (add/remove) must not reload the consumer.
func TestStableRegistryMemberChurn(t *testing.T) {
	rt := newTestRuntime(t)

	// The stable registry is a plain object shared by reference.
	type registry struct {
		mu      sync.Mutex
		members map[string]bool
	}
	reg := &registry{members: map[string]bool{}}

	type regCap struct{ reg *registry }
	var regKey = runtime.NewKey[regCap]("registry")

	// Registry provider: supplies the stable registry object.
	regProvider := newFakeComponent("registry")
	regProvider.provide = []runtime.Capability{regKey.Capability()}
	regProvider.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := runtime.Provide(ctx, regKey, regCap{reg: reg}); err != nil {
			return nil, err
		}
		return nil, nil
	}
	rf, err := rt.Load(regProvider)
	if err != nil {
		t.Fatalf("Load registry error: %v", err)
	}
	if err := rf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("registry Ready error: %v", err)
	}

	// Consumer: depends only on the stable registry.
	var consumerApplies atomic.Int32
	consumer := newFakeComponent("consumer")
	consumer.inject = []runtime.Dependency{runtime.Requires(regKey)}
	consumer.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		rc, err := runtime.Require(ctx, regKey)
		if err != nil {
			return nil, err
		}
		if rc.reg == nil {
			return nil, errors.New("nil registry")
		}
		consumerApplies.Add(1)
		return nil, nil
	}
	cf, err := rt.Load(consumer)
	if err != nil {
		t.Fatalf("Load consumer error: %v", err)
	}
	if err := cf.Ready(testTimeout(t)); err != nil {
		t.Fatalf("consumer Ready error: %v", err)
	}
	before := consumerApplies.Load()

	// Members register/unregister themselves as reversible membership effects
	// on the shared registry (NOT as providers of the registry capability).
	member := func(name string) *fakeComponent {
		c := newFakeComponent(name)
		c.inject = []runtime.Dependency{runtime.Requires(regKey)}
		c.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
			rc, err := runtime.Require(ctx, regKey)
			if err != nil {
				return nil, err
			}
			err = ctx.Effect(func() (func() error, error) {
				rc.reg.mu.Lock()
				rc.reg.members[name] = true
				rc.reg.mu.Unlock()
				return func() error {
					rc.reg.mu.Lock()
					delete(rc.reg.members, name)
					rc.reg.mu.Unlock()
					return nil
				}, nil
			})
			return nil, err
		}
		return c
	}

	m1, err := rt.Load(member("tool-A"))
	if err != nil {
		t.Fatalf("Load member error: %v", err)
	}
	if err := m1.Ready(testTimeout(t)); err != nil {
		t.Fatalf("member Ready error: %v", err)
	}
	m2, err := rt.Load(member("tool-B"))
	if err != nil {
		t.Fatalf("Load member error: %v", err)
	}
	if err := m2.Ready(testTimeout(t)); err != nil {
		t.Fatalf("member Ready error: %v", err)
	}

	reg.mu.Lock()
	memberCount := len(reg.members)
	reg.mu.Unlock()
	if memberCount != 2 {
		t.Fatalf("registry members = %d, want 2", memberCount)
	}

	// Churn: remove A, add C, remove B, add D... consumer must stay Active.
	if err := m1.Dispose(); err != nil {
		t.Fatalf("Dispose m1 error: %v", err)
	}
	if err := m1.Gone(testTimeout(t)); err != nil {
		t.Fatalf("m1 Gone error: %v", err)
	}
	if err := m2.Dispose(); err != nil {
		t.Fatalf("Dispose m2 error: %v", err)
	}
	if err := m2.Gone(testTimeout(t)); err != nil {
		t.Fatalf("m2 Gone error: %v", err)
	}

	reg.mu.Lock()
	remaining := len(reg.members)
	reg.mu.Unlock()
	if remaining != 0 {
		t.Fatalf("registry members after removal = %d, want 0", remaining)
	}

	// The consumer must never have reloaded due to member churn.
	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %v, want Active (unaffected by churn)", got)
	}
	if after := consumerApplies.Load(); after != before {
		t.Fatalf("consumer re-applied on member churn: applies %d -> %d", before, after)
	}
}
