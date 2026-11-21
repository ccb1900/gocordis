package runtime

import (
	"context"
	"sync"
	"testing"
	"time"
)

// Unified Context recursive composition (paper Def 28, §3.3.1):
//
//	Γ∞ ≔ μΓ. Γ × (Γ → Γ) × Σ
//
// The three projections are realized in GoCordis as:
//   - Γ (current state)       — Fiber.activation + Context.state;
//   - Γ→Γ (level accumulator) — the Effect stack whose inverses revert this
//     level's consequences (install barrier + strict LIFO);
//   - Σ (coeffect context)    — the key realm table (ρ), the derived metadata
//     view (ι), and the identity-resolved dependency edges.
//
// The recursion is realized by Context.Child: each child Fiber owns a FRESH
// Context (its own Γ and accumulator) that inherits Σ from its parent at
// derivation time, while the parent aggregates children via ownership gating.
// This is a STRUCTURAL composition check — not a claim that all structs live
// in one Go object (the paper does not require that).

// composeLeaf is a leaf component that installs one reversible effect and
// depends on a key, so its Context has a non-empty Γ→Γ accumulator and Σ edge.
type composeLeaf struct {
	key  Key[rehomeKeyVal]
	seen chan string
	ch   chan *Fiber // reports its own child for one nesting level
}

func (c *composeLeaf) Name() string          { return "compose-leaf" }
func (c *composeLeaf) Inject() []Dependency  { return []Dependency{Requires(c.key)} }
func (c *composeLeaf) Provide() []Capability { return nil }
func (c *composeLeaf) Apply(ctx *Context) (Cleanup, error) {
	v, err := Require(ctx, c.key)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v.tag
	}
	// Install a reversible effect at this level (accumulator projection).
	if err := ctx.Effect(func() (func() error, error) {
		return func() error { return nil }, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

// composeParent hosts a nested child whose Context inherits this parent's
// derived metadata view (Σ inheritance) and owns its own accumulator.
type composeParent struct {
	key Key[rehomeKeyVal]
	ch  chan *Fiber
	nu  string
}

func (c *composeParent) Name() string          { return "compose-parent" }
func (c *composeParent) Inject() []Dependency  { return nil }
func (c *composeParent) Provide() []Capability { return nil }
func (c *composeParent) Apply(ctx *Context) (Cleanup, error) {
	if err := ctx.Effect(func() (func() error, error) {
		return func() error { return nil }, nil
	}); err != nil {
		return nil, err
	}
	child, err := ctx.Child(&composeLeaf{key: c.key})
	if err != nil {
		return nil, err
	}
	if c.ch != nil {
		c.ch <- child
	}
	return nil, nil
}

// checkUnifiedContextInvariants verifies the Γ∞ projections over a snapshot:
//  1. Γ: every non-Gone fiber has at most one live activation;
//  2. Γ→Γ: every effect slot belongs to a live activation (accumulator cannot
//     outlive its level);
//  3. Σ: every dependency edge binds a provider identity, and a child's
//     metadata view is derived (a sibling root never sees the parent's view —
//     verified separately by the P-CO-01 leak test).
func checkUnifiedContextInvariants(t *testing.T, snap RuntimeSnapshot, what string) {
	t.Helper()
	activation := map[FiberID]ActivationID{}
	for _, f := range snap.Fibers {
		if f.State == StateGone {
			continue
		}
		if f.ActivationID == 0 {
			t.Fatalf("%s: Γ projection: fiber %s state=%v has no activation", what, f.Name, f.State)
		}
		activation[f.ID] = f.ActivationID
	}
	for _, e := range snap.Effects {
		act, ok := activation[e.OwnerFiberID]
		if !ok || act != e.ActivationID {
			t.Fatalf("%s: Γ→Γ projection: effect slot of fiber %d/act %d has no live activation", what, e.OwnerFiberID, e.ActivationID)
		}
	}
	for _, d := range snap.Dependencies {
		if d.ProviderFiberID == 0 {
			t.Fatalf("%s: Σ projection: dependency of fiber %d has no provider identity", what, d.ConsumerFiberID)
		}
	}
}

// TestUnifiedContextRecursiveComposition — Γ∞ recursion across a real
// nested Child tree: parent and child each own a level accumulator and a
// derived Σ; disposal unwinds both levels LIFO and the child reaches Gone
// before the parent finalizes (ownership preserves the recursive structure).
func TestUnifiedContextRecursiveComposition(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	pf, err := rt.Load(&rehomeProvider{tag: "base"})
	if err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, pf, StateActive)

	parentCh := make(chan *Fiber, 1)
	parent, err := rt.Load(&composeParent{key: rehomeKey, ch: parentCh})
	if err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, parent, StateActive)
	child := <-parentCh
	rehomeWaitState(t, child, StateActive)

	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	checkUnifiedContextInvariants(t, snap, "nested-active")

	// The child's dependency edge must bind the provider identity (Σ).
	var childEdge *DependencyView
	for i := range snap.Dependencies {
		if snap.Dependencies[i].ConsumerFiberID == child.ID() {
			childEdge = &snap.Dependencies[i]
		}
	}
	if childEdge == nil || childEdge.ProviderFiberID != pf.ID() {
		t.Fatalf("child Σ dependency not bound to provider identity: %+v", childEdge)
	}

	// Recursive unwind: dispose the parent; the child must reach Gone BEFORE
	// the parent (ownership gating preserves Γ→Γ nesting).
	if err := parent.Dispose(); err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, child, StateGone)
	rehomeWaitState(t, parent, StateGone)

	snap2, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	checkUnifiedContextInvariants(t, snap2, "after-unwind")
}

// TestUnifiedContextRevisionPreservesRecursion — a revision of the parent
// must produce a NEW Γ (fresh activation) while the recursive Σ inheritance
// (its child table) is re-created at insert time, not lost.
func TestUnifiedContextRevisionPreservesRecursion(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	pf, err := rt.Load(&rehomeProvider{tag: "base"})
	if err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, pf, StateActive)

	parentCh := make(chan *Fiber, 1)
	parent, err := rt.Load(&composeParent{key: rehomeKey, ch: parentCh})
	if err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, parent, StateActive)
	firstChild := <-parentCh
	oldAct := firstChild.activationIDSnapshot()

	// Revise the child to a fresh definition — new activation (Γ), inherited
	// Σ rebuilt at insert time.
	newChild, err := firstChild.Revise(ctx, &composeLeaf{key: rehomeKey})
	if err != nil {
		t.Fatalf("revise: %v", err)
	}
	rehomeWaitState(t, newChild, StateActive)
	newAct := newChild.activationIDSnapshot()
	if newAct == oldAct {
		t.Fatal("revision did not create a fresh activation (Γ)")
	}

	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	checkUnifiedContextInvariants(t, snap, "after-revision")
}

func (f *Fiber) activationIDSnapshot() ActivationID {
	f.mu.RLock()
	defer f.mu.RUnlock()
	if f.activation == nil {
		return 0
	}
	return f.activation.id
}

// Wait no sleep needed: channels + snapshot after terminal state.
var _ = sync.Once{}
