package runtime

import (
	"context"
	"testing"
	"time"
)

// R4 audit: independence must be observed on the REAL runtime, not on a
// hand-built snapshot model. These components provide keys and record the
// resolved provider identity + inverse execution, so commutativity and
// non-interference are checked against real Apply/unwind/graph behavior.

// independenceProv provides one key and records it.
type independenceProv struct {
	key Key[rehomeKeyVal]
	tag string
}

func (c *independenceProv) Name() string          { return "ind-prov-" + c.tag }
func (c *independenceProv) Inject() []Dependency  { return nil }
func (c *independenceProv) Provide() []Capability { return []Capability{c.key.Capability()} }
func (c *independenceProv) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, c.key, rehomeKeyVal{tag: c.tag}); err != nil {
		return nil, err
	}
	return nil, nil
}

// independenceConsumer requires one key, records the resolved tag, and installs
// a cleanup whose inverse is observable (for Thm 43 inverse-permutation checks).
type independenceConsumer struct {
	key     Key[rehomeKeyVal]
	seen    chan string
	inverse chan string
	tag     string
}

func (c *independenceConsumer) Name() string         { return "ind-cons-" + c.tag }
func (c *independenceConsumer) Inject() []Dependency { return []Dependency{Requires(c.key)} }
func (c *independenceConsumer) Provide() []Capability {
	return nil
}
func (c *independenceConsumer) Apply(ctx *Context) (Cleanup, error) {
	v, err := Require(ctx, c.key)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v.tag
	}
	return func() error {
		if c.inverse != nil {
			c.inverse <- c.tag
		}
		return nil
	}, nil
}

func indWait(t *testing.T, f *Fiber, want FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("%s stuck in %v, want %v", f.Name(), f.State(), want)
}

func indRecv(t *testing.T, ch chan string) string {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(8 * time.Second):
		t.Fatal("timeout")
		return ""
	}
}

// TestIndependenceRealRuntimeDistinctKeys — Thm 45 on the real runtime: two
// providers of DISTINCT keys, activated in both orders via deterministic
// channels, reach observationally equivalent states (same bindings).
func TestIndependenceRealRuntimeDistinctKeys(t *testing.T) {
	runOrder := func(provAFirst bool) RuntimeSnapshot {
		rt, err := New()
		if err != nil {
			t.Fatal(err)
		}
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		defer func() { _ = rt.Close(ctx) }()

		keyA := NewKey[rehomeKeyVal]("ind.a")
		keyB := NewKey[rehomeKeyVal]("ind.b")
		seenA, seenB := make(chan string, 2), make(chan string, 2)

		// Both providers must be Active before consumers start (dependency
		// gate); we load providers first, then consumers in chosen order.
		pa, _ := rt.Load(&independenceProv{key: keyA, tag: "a"})
		pb, _ := rt.Load(&independenceProv{key: keyB, tag: "b"})
		indWait(t, pa, StateActive)
		indWait(t, pb, StateActive)

		var cA, cB *Fiber
		if provAFirst {
			cA, _ = rt.Load(&independenceConsumer{key: keyA, seen: seenA, tag: "ca"})
			indWait(t, cA, StateActive)
			cB, _ = rt.Load(&independenceConsumer{key: keyB, seen: seenB, tag: "cb"})
			indWait(t, cB, StateActive)
		} else {
			cB, _ = rt.Load(&independenceConsumer{key: keyB, seen: seenB, tag: "cb"})
			indWait(t, cB, StateActive)
			cA, _ = rt.Load(&independenceConsumer{key: keyA, seen: seenA, tag: "ca"})
			indWait(t, cA, StateActive)
		}
		if got := indRecv(t, seenA); got != "a" {
			t.Fatalf("consumer A tag = %q", got)
		}
		if got := indRecv(t, seenB); got != "b" {
			t.Fatalf("consumer B tag = %q", got)
		}

		snap, err := rt.Snapshot(ctx)
		if err != nil {
			t.Fatal(err)
		}
		return snap
	}

	s1 := runOrder(true)
	s2 := runOrder(false)
	// Both orders must bind exactly the same (key, realm, provider) pairs.
	// Use the operation-derived model with bind-identity observed.
	model := NewObservationModel()
	if !model.EquivalentAt(s1, s2, nil, nil) {
		t.Fatalf("real-runtime distinct-key activation order changed observation:\nA=%+v\nB=%+v", s1.Providers, s2.Providers)
	}
}

// TestIndependenceRealRuntimeInverseDoesNotDisturb — Def 42(2): disposing one
// component's provider must NOT disturb another component's active binding.
func TestIndependenceRealRuntimeInverseDoesNotDisturb(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	keyA := NewKey[rehomeKeyVal]("ind.disturb.a")
	keyB := NewKey[rehomeKeyVal]("ind.disturb.b")
	seenA, seenB := make(chan string, 2), make(chan string, 2)

	pa, _ := rt.Load(&independenceProv{key: keyA, tag: "a"})
	pb, _ := rt.Load(&independenceProv{key: keyB, tag: "b"})
	indWait(t, pa, StateActive)
	indWait(t, pb, StateActive)
	ca, _ := rt.Load(&independenceConsumer{key: keyA, seen: seenA, tag: "ca"})
	cb, _ := rt.Load(&independenceConsumer{key: keyB, seen: seenB, tag: "cb"})
	indWait(t, ca, StateActive)
	indWait(t, cb, StateActive)
	_ = indRecv(t, seenA)
	_ = indRecv(t, seenB)

	// Dispose provider A and its consumer; B's binding must remain intact.
	if err := ca.Dispose(); err != nil {
		t.Fatal(err)
	}
	indWait(t, ca, StateGone)
	if err := pa.Dispose(); err != nil {
		t.Fatal(err)
	}
	indWait(t, pa, StateGone)

	if cb.State() != StateActive {
		t.Fatalf("disposing A disturbed B's consumer: %v", cb.State())
	}
	// B's provider record still present and live.
	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	foundB := false
	for _, p := range snap.Providers {
		if p.OwnerFiberID == pb.ID() && p.State == StateActive && !p.Retiring {
			foundB = true
		}
	}
	if !foundB {
		t.Fatal("B's provider binding was disturbed by A's withdrawal")
	}
}

// TestIndependenceRealRuntimeInversePermutation — C: TWO INDEPENDENT
// operations on DISTINCT keys, both forward then reverted in two different
// orders. Both runs must reach observationally EQUIVALENT base states, and
// each operation's inverse must run exactly once. No empty branch, no same-key
// witness shortcut (paper §3.4 Thm 43/45).
func TestIndependenceRealRuntimeInversePermutation(t *testing.T) {
	run := func(reverse bool) (RuntimeSnapshot, []string) {
		rt, err := New()
		if err != nil {
			t.Fatal(err)
		}
		ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
		defer cancel()
		defer func() { _ = rt.Close(ctx) }()

		keyA := NewKey[rehomeKeyVal]("ind.perm.a")
		keyB := NewKey[rehomeKeyVal]("ind.perm.b")
		inv := make(chan string, 8)
		pa, _ := rt.Load(&independenceProv{key: keyA, tag: "a"})
		pb, _ := rt.Load(&independenceProv{key: keyB, tag: "b"})
		indWait(t, pa, StateActive)
		indWait(t, pb, StateActive)
		ca, _ := rt.Load(&independenceConsumer{key: keyA, inverse: inv, tag: "ca"})
		cb, _ := rt.Load(&independenceConsumer{key: keyB, inverse: inv, tag: "cb"})
		indWait(t, ca, StateActive)
		indWait(t, cb, StateActive)

		// Two independent operations: dispose consumer A and consumer B in
		// chosen order; each inverse runs exactly once.
		order := []*Fiber{ca, cb}
		if reverse {
			order = []*Fiber{cb, ca}
		}
		for _, f := range order {
			_ = f.Dispose()
			indWait(t, f, StateGone)
		}
		got := []string{indRecv(t, inv), indRecv(t, inv)}
		snap, err := rt.Snapshot(ctx)
		if err != nil {
			t.Fatal(err)
		}
		return snap, got
	}

	forward, invF := run(false)
	reversed, invR := run(true)
	// Both inverse orders executed each independent operation's inverse once.
	for _, label := range []string{"ca", "cb"} {
		countF, countR := 0, 0
		for _, x := range invF {
			if x == label {
				countF++
			}
		}
		for _, x := range invR {
			if x == label {
				countR++
			}
		}
		if countF != 1 || countR != 1 {
			t.Fatalf("inverse %s counts %d/%d, want 1/1", label, countF, countR)
		}
	}
	// Final states must be observationally equivalent (both back to base:
	// providers still Active and bound, consumers gone).
	model := NewObservationModel()
	if !model.EquivalentAt(forward, reversed, nil, nil) {
		t.Fatalf("inverse-order permutations reached non-equivalent states:\nA=%+v\nB=%+v", forward.Providers, reversed.Providers)
	}
}
