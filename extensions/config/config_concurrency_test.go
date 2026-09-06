package config_test

import (
	"context"
	"errors"
	"math/rand"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// assertOwnedConsistent: Applied ID <=> Controller ownership, no duplicates,
// no Gone fibers (an applied fiber has been handed to the Runtime).
func assertOwnedConsistent(t *testing.T, c *config.Controller) {
	t.Helper()
	seen := map[string]struct{}{}
	for _, o := range c.Owned() {
		if o.ID == "" {
			t.Fatal("owned component with empty ID")
		}
		if _, dup := seen[o.ID]; dup {
			t.Fatalf("duplicate owned ID %q", o.ID)
		}
		seen[o.ID] = struct{}{}
		if o.Fiber == nil {
			t.Fatalf("owned %q has no fiber", o.ID)
		}
		if st := o.Fiber.State(); st == runtime.StateGone {
			t.Fatalf("owned %q fiber is Gone", o.ID)
		}
	}
}

func cfgIDs(c *config.Controller) map[string]bool {
	out := map[string]bool{}
	for _, o := range c.Owned() {
		out[o.ID] = true
	}
	return out
}

func sameIDSet(a map[string]bool, want ...string) bool {
	if len(a) != len(want) {
		return false
	}
	for _, w := range want {
		if !a[w] {
			return false
		}
	}
	return true
}

// C11 — Concurrent Reconcile: no race, no ownership corruption; the final
// Applied state matches the last (linearized) reconcile.
func TestC11ConcurrentReconcile(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	e.registerSimple(t, "b", nil)

	configs := []config.Config{
		cfg(cc("A", "a")),
		cfg(cc("B", "b")),
		cfg(cc("A", "a"), cc("B", "b")),
		config.Config{},
	}

	var wg sync.WaitGroup
	for g := 0; g < 6; g++ {
		wg.Add(1)
		go func(seed int64) {
			defer wg.Done()
			r := rand.New(rand.NewSource(seed))
			for i := 0; i < 40; i++ {
				ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
				_ = e.ctrl.Reconcile(ctx, configs[r.Intn(len(configs))])
				cancel()
			}
		}(int64(g) + 1)
	}
	wg.Wait()
	assertOwnedConsistent(t, e.ctrl)

	// Converge to a known final state: Applied must equal it exactly.
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "b"))); err != nil {
		t.Fatalf("final Reconcile: %v", err)
	}
	ids := cfgIDs(e.ctrl)
	if !sameIDSet(ids, "A", "B") {
		t.Fatalf("final applied = %v, want {A B}", ids)
	}
	assertOwnedConsistent(t, e.ctrl)
}

// C12 — Reconcile x Close: no panic, no mutation after Close linearizes, no
// leak.
func TestC12ReconcileClose(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	defer func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	}()

	if err := reg.Register("a", &factory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return simpleComp(cc.ID, nil), nil
	}}); err != nil {
		t.Fatal(err)
	}

	stop := make(chan struct{})
	var wg sync.WaitGroup
	var reconciles atomic.Int32
	wg.Add(1)
	go func() {
		defer wg.Done()
		for {
			select {
			case <-stop:
				return
			default:
			}
			ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
			err := ctrl.Reconcile(ctx, cfg(cc("A", "a")))
			cancel()
			if errors.Is(err, config.ErrControllerClosed) {
				return
			}
			if err != nil {
				t.Errorf("unexpected reconcile error: %v", err)
				return
			}
			reconciles.Add(1)
		}
	}()

	// Let some reconciles happen, then close.
	deadline := time.Now().Add(5 * time.Second)
	for reconciles.Load() == 0 && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if err := ctrl.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	close(stop)
	wg.Wait()

	if len(ctrl.Owned()) != 0 {
		t.Fatal("controller owns components after Close")
	}
	if err := ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); !errors.Is(err, config.ErrControllerClosed) {
		t.Fatalf("Reconcile after Close = %v, want ErrControllerClosed", err)
	}
}

// P2 — Ownership conservation across add/remove steps.
func TestP2OwnershipConservation(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)

	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"), cc("B", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	assertOwnedConsistent(t, e.ctrl)
	aFiber := ownIDs(e.ctrl)["A"].Fiber

	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("B", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	ids := cfgIDs(e.ctrl)
	if !sameIDSet(ids, "B") {
		t.Fatalf("applied = %v, want {B}", ids)
	}
	if err := aFiber.Gone(ctxT(t)); err != nil {
		t.Fatalf("removed A fiber not gone: %v", err)
	}

	if err := e.ctrl.Reconcile(ctxT(t), config.Config{}); err != nil {
		t.Fatalf("Reconcile empty: %v", err)
	}
	if len(e.ctrl.Owned()) != 0 {
		t.Fatal("applied not empty")
	}
}

// P3 — Validation preservation: invalid configs never disturb applied state.
func TestP3ValidationPreservation(t *testing.T) {
	e := newEnv(t)
	f := e.registerSimple(t, "a", nil)
	if err := e.ctrl.Reconcile(ctxT(t), cfg(cc("A", "a"))); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	before := ownIDs(e.ctrl)
	origCalls := f.calls.Load()

	invalid := []config.Config{
		cfg(cc("A", "a"), cc("A", "a")),
		cfg(config.ComponentConfig{Type: "a"}), // empty ID
		cfg(cc("A", "")),                       // empty type
		cfg(cc("X", "not-registered")),         // unknown type
	}
	for i, c := range invalid {
		if err := e.ctrl.Reconcile(ctxT(t), c); err == nil {
			t.Fatalf("invalid config #%d accepted", i)
		}
	}
	after := ownIDs(e.ctrl)
	if len(after) != len(before) || after["A"].Fiber.ID() != before["A"].Fiber.ID() {
		t.Fatal("applied state changed by invalid configs")
	}
	if got := f.calls.Load(); got != origCalls {
		t.Fatalf("factory calls changed by invalid configs: %d -> %d", origCalls, got)
	}
}

// P6 — randomized concurrent reconcile (add/remove/replace) never corrupts
// ownership.
func TestP6ConcurrentReconcileSafety(t *testing.T) {
	e := newEnv(t)
	e.registerSimple(t, "a", nil)
	e.registerSimple(t, "b", nil)

	var wg sync.WaitGroup
	for g := 0; g < 6; g++ {
		wg.Add(1)
		go func(seed int64) {
			defer wg.Done()
			r := rand.New(rand.NewSource(seed))
			for i := 0; i < 60; i++ {
				var c config.Config
				switch r.Intn(5) {
				case 0:
					c = cfg(cc("A", "a"))
				case 1:
					c = cfg(cc("B", "b"))
				case 2:
					c = cfg(cc("A", "a", "v", r.Intn(5)))
				case 3:
					c = cfg(cc("A", "a"), cc("B", "b"))
				default:
					c = config.Config{}
				}
				ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
				_ = e.ctrl.Reconcile(ctx, c)
				cancel()
			}
		}(int64(g) + 100)
	}
	wg.Wait()
	assertOwnedConsistent(t, e.ctrl)
}
