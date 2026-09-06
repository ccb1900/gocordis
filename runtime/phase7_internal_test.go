package runtime

// White-box concurrency tests: they execute assertions on the orchestrator
// goroutine through a sync command so no sleep-based synchronization is needed.

import (
	"context"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

// syncCommand runs fn on the orchestrator goroutine and reports completion.
type syncCommand struct {
	fn   func(*orchestrator)
	done chan struct{}
}

func (c *syncCommand) apply(o *orchestrator) {
	defer close(c.done)
	if c.fn != nil {
		c.fn(o)
	}
}

func onOrchestrator(t *testing.T, rt *Runtime, fn func(*orchestrator)) {
	t.Helper()
	done := make(chan struct{})
	ok := rt.submit(&syncCommand{fn: fn, done: done})
	if !ok {
		t.Fatal("submit to orchestrator failed")
	}
	select {
	case <-done:
	case <-time.After(10 * time.Second):
		t.Fatal("orchestrator sync command timed out")
	}
}

type wbComponent struct {
	name string
}

func (c *wbComponent) Name() string          { return c.name }
func (c *wbComponent) Inject() []Dependency  { return nil }
func (c *wbComponent) Provide() []Capability { return nil }
func (c *wbComponent) Apply(ctx *Context) (Cleanup, error) {
	return nil, nil
}

func wbWaitState(t *testing.T, f *Fiber, want FiberState) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		f.mu.RLock()
		st := f.state
		f.mu.RUnlock()
		if st == want {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatalf("fiber state = %v, want %v", f.State(), want)
}

// Stale Apply completion (old activation ID) must be ignored: it must not
// transition the fiber and its cleanup must never run.
func TestStaleApplyDoneIgnored(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(&wbComponent{name: "x"})
	if err != nil {
		t.Fatal(err)
	}
	wbWaitState(t, f, StateActive)

	var ran atomic.Bool
	stale := &cmdApplyDone{
		fiberID:      f.id,
		activationID: f.currentActivationID() + 1, // does not match current activation
		cleanup: func() error {
			ran.Store(true)
			return nil
		},
		err: nil,
	}

	onOrchestrator(t, rt, func(o *orchestrator) {
		stale.apply(o)
	})

	if got := f.State(); got != StateActive {
		t.Fatalf("state after stale completion = %v, want Active", got)
	}

	// Dispose normally: the stale cleanup must not run during unwind.
	_ = f.Dispose()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	if ran.Load() {
		t.Fatal("stale apply completion's cleanup ran; it must be ignored")
	}
}

// A stale completion for a fiber with no current activation is ignored.
func TestStaleCompletionForGoneFiberIgnored(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(&wbComponent{name: "x"})
	if err != nil {
		t.Fatal(err)
	}
	_ = f.Dispose()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}

	var ran atomic.Bool
	onOrchestrator(t, rt, func(o *orchestrator) {
		(&cmdApplyDone{
			fiberID:      f.id,
			activationID: 1,
			cleanup: func() error {
				ran.Store(true)
				return nil
			},
		}).apply(o)
		(&cmdUnwindDone{fiberID: f.id, activationID: 1}).apply(o)
	})
	if ran.Load() {
		t.Fatal("stale completion cleanup ran on a Gone fiber")
	}
	if got := f.State(); got != StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
}

// Stale Unwind completion (old activation ID) must be ignored.
func TestStaleUnwindDoneIgnored(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(&wbComponent{name: "x"})
	if err != nil {
		t.Fatal(err)
	}
	wbWaitState(t, f, StateActive)

	onOrchestrator(t, rt, func(o *orchestrator) {
		(&cmdUnwindDone{fiberID: f.id, activationID: f.currentActivationID() + 1, err: nil}).apply(o)
	})
	if got := f.State(); got != StateActive {
		t.Fatalf("state after stale unwind = %v, want Active", got)
	}
}

func (f *Fiber) currentActivationID() ActivationID {
	f.mu.RLock()
	defer f.mu.RUnlock()
	if f.activation == nil {
		return 0
	}
	return f.activation.id
}

// Provider removal is guarded by identity: removing an old activation's record
// must never delete a newer activation's provider.
func TestProviderRemovalIdentityGuard(t *testing.T) {
	reg := newRealm(nil) // single root realm (no scoping)
	key := NewKey[int]("k").Capability()

	id1 := ProviderIdentity{FiberID: 1, ActivationID: 1}
	id2 := ProviderIdentity{FiberID: 1, ActivationID: 2}

	if err := reg.registerOwn(key, id1, 111); err != nil {
		t.Fatal(err)
	}
	// A stale removal (old identity that does not match) must be a no-op.
	reg.removeOwn(key, ProviderIdentity{FiberID: 1, ActivationID: 999})
	rec, ok := reg.lookup(key)
	if !ok || rec.identity != id1 || rec.value != 111 {
		t.Fatalf("stale removal deleted the provider: %+v", rec)
	}

	// Removing with the matching identity removes the record.
	reg.removeOwn(key, id1)
	if _, ok := reg.lookup(key); ok {
		t.Fatal("provider still present after matching removal")
	}

	// Simulate: activation 2 registers while activation 1's late inverse
	// arrives; the late inverse must not delete activation 2's provider.
	_ = reg.registerOwn(key, id2, 222)
	reg.removeOwn(key, id1) // late inverse from act1
	rec, ok = reg.lookup(key)
	if !ok || rec.identity != id2 {
		t.Fatal("late inverse from an old activation deleted the new provider")
	}
}

// Concurrency churn: many fibers Load/Dispose concurrently while providers and
// consumers come and go; afterwards every fiber is disposed to Gone and the
// registry must be empty (no leaked providers, no orphan records).
func TestConcurrentChurnLeavesNoLeakedProviders(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	key := NewKey[string]("churn").Capability()

	var mu sync.Mutex
	var fibers []*Fiber
	var wg sync.WaitGroup

	spawn := func(c Component) *Fiber {
		f, err := rt.Load(c)
		if err != nil {
			t.Errorf("Load error: %v", err)
			return nil
		}
		mu.Lock()
		fibers = append(fibers, f)
		mu.Unlock()
		return f
	}

	for i := 0; i < 4; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := 0; j < 25; j++ {
				prov := &churnProvider{key: key}
				f := spawn(prov)
				if f == nil {
					return
				}
				_ = f.Dispose()
				cons := &churnConsumer{key: key}
				c := spawn(cons)
				if c == nil {
					return
				}
				_ = c.Dispose()
			}
		}()
	}
	wg.Wait()

	// Dispose every remaining fiber and wait for terminal Gone.
	mu.Lock()
	fs := append([]*Fiber(nil), fibers...)
	mu.Unlock()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	for _, f := range fs {
		_ = f.Dispose()
	}
	for _, f := range fs {
		_ = f.Gone(ctx)
	}

	onOrchestrator(t, rt, func(o *orchestrator) {
		if n := o.rt.rootRealm.count(); n != 0 {
			t.Fatalf("registry leaked %d provider records after all fibers Gone", n)
		}
	})
}

type churnProvider struct {
	key CapabilityKey
}

func (c *churnProvider) Name() string          { return "churn-provider" }
func (c *churnProvider) Inject() []Dependency  { return nil }
func (c *churnProvider) Provide() []Capability { return []Capability{c.key} }
func (c *churnProvider) Apply(ctx *Context) (Cleanup, error) {
	rec, _ := ctx.realm.lookup(c.key)
	_ = rec
	if err := ctx.provideCap(c.key, "v"); err != nil {
		return nil, err
	}
	return nil, nil
}

type churnConsumer struct {
	key CapabilityKey
}

func (c *churnConsumer) Name() string         { return "churn-consumer" }
func (c *churnConsumer) Inject() []Dependency { return []Dependency{{Key: c.key}} }
func (c *churnConsumer) Provide() []Capability {
	return nil
}
func (c *churnConsumer) Apply(ctx *Context) (Cleanup, error) {
	_, err := ctx.realm.lookup(c.key)
	_ = err
	return nil, nil
}
