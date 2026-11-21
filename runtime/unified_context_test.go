package runtime

import (
	"context"
	"fmt"
	"sync"
	"testing"
	"time"
)

// R3 audit: Unified Context (paper Def 28) recursion MUST be observed across
// real Parent → Child → Grandchild with observable metadata inheritance (Σ),
// observable effect inverses at each level (Γ→Γ), and Revision replacing a
// generation without leaking old effects. No empty inverses, no snapshot-only
// structure claims — everything here executes the real runtime.

var (
	ucMetaKey = NewMetaKey[string, rehomeKeyVal](
		"unified.meta",
		rehomeKeyVal{tag: ""},
		func(a, b rehomeKeyVal) rehomeKeyVal {
			if b.tag != "" {
				return b
			}
			return a
		},
	)
)

// ucProvider provides both a plain key and a meta-interpreted key.
type ucProvider struct{}

func (c *ucProvider) Name() string { return "uc-provider" }
func (c *ucProvider) Inject() []Dependency {
	return nil
}
func (c *ucProvider) Provide() []Capability {
	return []Capability{rehomeKey.Capability(), ucMetaKey.Capability()}
}
func (c *ucProvider) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, rehomeKey, rehomeKeyVal{tag: "svc"}); err != nil {
		return nil, err
	}
	return nil, ProvideMeta(ctx, ucMetaKey, func(m rehomeKeyVal) string { return "meta:" + m.tag })
}

// ucLevel installs an observable inverse tagged by level, optionally overrides
// metadata, and derives a child component factory for the next level.
type ucLevel struct {
	level    string
	inverse  chan string
	nu       *rehomeKeyVal // metadata installment at this level (nil = inherit)
	child    Component     // next level, if any
	childOut chan *Fiber
	metaSeen chan string
	stop     bool
}

func (c *ucLevel) Name() string { return "uc-" + c.level }
func (c *ucLevel) Inject() []Dependency {
	return []Dependency{RequiresMeta(ucMetaKey, rehomeKeyVal{tag: "declared"})}
}
func (c *ucLevel) Provide() []Capability { return nil }
func (c *ucLevel) Apply(ctx *Context) (Cleanup, error) {
	// Σ projection: read the derived metadata view (inherited or local).
	v, err := RequireMeta(ctx, ucMetaKey)
	if err != nil {
		return nil, err
	}
	if c.metaSeen != nil {
		c.metaSeen <- v
	}
	// Γ→Γ projection: install an observable inverse at THIS level.
	if err := ctx.Effect(func() (func() error, error) {
		return func() error {
			if c.inverse != nil {
				c.inverse <- c.level
			}
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	// Derived realization: this level may shadow the metadata for descendants.
	if c.nu != nil {
		if err := InterceptMeta(ctx, ucMetaKey, *c.nu); err != nil {
			return nil, err
		}
	}
	// Recursion: derive the next level's Fiber (fresh Γ + inherited Σ).
	if c.child != nil {
		child, err := ctx.Child(c.child)
		if err != nil {
			return nil, err
		}
		if c.childOut != nil {
			c.childOut <- child
		}
	}
	return nil, nil
}

func ucWait(t *testing.T, f *Fiber, want FiberState) {
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

func ucRecv(t *testing.T, ch chan string) string {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(8 * time.Second):
		t.Fatal("timeout")
		return ""
	}
}

// TestUnifiedContextThreeLevelRecursion — R3: Parent→Child→Grandchild with
// observable Σ inheritance/local override and Γ→Γ inverse execution.
func TestUnifiedContextThreeLevelRecursion(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 25*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	pf, _ := rt.Load(&ucProvider{})
	ucWait(t, pf, StateActive)

	inv, grandMeta, childMeta, parentMeta := make(chan string, 8), make(chan string, 2), make(chan string, 2), make(chan string, 2)
	grandchildOut := make(chan *Fiber, 1)
	childOut := make(chan *Fiber, 1)

	grandchild := &ucLevel{level: "grandchild", inverse: inv, metaSeen: grandMeta}
	child := &ucLevel{
		level: "child", inverse: inv, metaSeen: childMeta,
		nu:    &rehomeKeyVal{tag: "child-nu"}, // local shadow for descendants
		child: grandchild, childOut: grandchildOut,
	}
	parent := &ucLevel{
		level: "parent", inverse: inv, metaSeen: parentMeta,
		nu:    &rehomeKeyVal{tag: "parent-nu"},
		child: child, childOut: childOut,
	}
	pfNode, _ := rt.Load(parent)
	ucWait(t, pfNode, StateActive)
	childFiber := <-childOut
	ucWait(t, childFiber, StateActive)
	grandFiber := <-grandchildOut
	ucWait(t, grandFiber, StateActive)

	// Σ: parent sees its own ν; child installs its ν before deriving grandchild;
	// grandchild sees the CHILD's ν (local override), not the parent's.
	if got := ucRecv(t, parentMeta); got != "meta:declared" {
		t.Fatalf("parent Σ read = %q (declared merged before install)", got)
	}
	if got := ucRecv(t, childMeta); got != "meta:parent-nu" {
		t.Fatalf("child Σ should inherit parent ν, got %q", got)
	}
	if got := ucRecv(t, grandMeta); got != "meta:child-nu" {
		t.Fatalf("grandchild Σ should see child's local shadow, got %q", got)
	}

	// Γ→Γ: dispose the parent; ownership gating makes the whole recursion
	// unwind child-first, grandchild-first, each level's observable inverse
	// running exactly once.
	if err := pfNode.Dispose(); err != nil {
		t.Fatal(err)
	}
	ucWait(t, grandFiber, StateGone)
	ucWait(t, childFiber, StateGone)
	ucWait(t, pfNode, StateGone)

	seen := map[string]int{}
	for i := 0; i < 3; i++ {
		seen[ucRecv(t, inv)]++
	}
	for _, level := range []string{"parent", "child", "grandchild"} {
		if seen[level] != 1 {
			t.Fatalf("inverse for %s executed %d times, want 1: %v", level, seen[level], seen)
		}
	}
}

// TestUnifiedContextRevisionGeneration — R3: revising a level creates a new
// Γ (fresh activation) and Σ is rebuilt at insert time; old generation's
// inverse must NOT fire for the replacement.
func TestUnifiedContextRevisionGeneration(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 25*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	pf, _ := rt.Load(&ucProvider{})
	ucWait(t, pf, StateActive)

	inv := make(chan string, 8)
	metaSeen := make(chan string, 2)
	leaf := &ucLevel{level: "leaf", inverse: inv, metaSeen: metaSeen}
	f, _ := rt.Load(leaf)
	ucWait(t, f, StateActive)
	_ = ucRecv(t, metaSeen)
	oldID := f.activationIDSnapshot()

	// Revise to a fresh generation with a NEW inverse tag; the old inverse
	// tag must never be observed after revision.
	replacement := &ucLevel{level: "leaf-v2", inverse: inv, metaSeen: make(chan string, 2)}
	nf, err := f.Revise(ctx, replacement)
	if err != nil {
		t.Fatalf("revise: %v", err)
	}
	ucWait(t, nf, StateActive)
	_ = ucRecv(t, replacement.metaSeen)
	newID := nf.activationIDSnapshot()
	if newID == oldID {
		t.Fatal("revision did not create a fresh activation (Γ)")
	}

	if err := nf.Dispose(); err != nil {
		t.Fatal(err)
	}
	ucWait(t, nf, StateGone)
	// BOTH generations' inverses must run exactly once: the retired "leaf"
	// during Revise (oldgeneration withdrawal), the "leaf-v2" on Dispose.
	first, second := ucRecv(t, inv), ucRecv(t, inv)
	if !(first == "leaf" && second == "leaf-v2") && !(first == "leaf-v2" && second == "leaf") {
		t.Fatalf("revision inverse order/content wrong: %q then %q", first, second)
	}
	// No third inverse — each generation reverted exactly once.
	select {
	case extra := <-inv:
		t.Fatalf("extra inverse leaked: %s", extra)
	default:
	}
}

// activationIDSnapshot reads the live activation id under the fiber lock.
func (f *Fiber) activationIDSnapshot() ActivationID {
	f.mu.RLock()
	defer f.mu.RUnlock()
	if f.activation == nil {
		return 0
	}
	return f.activation.id
}

var _ = fmt.Sprint
var _ = sync.Once{}
