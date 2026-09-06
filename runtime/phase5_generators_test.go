package runtime

import (
	"context"
	"fmt"
	"math/rand"
	"sort"
	"testing"
	"time"
)

// ---------------------------------------------------------------------------
// Phase 5 (remainder): recovery-exactness, ordering, and confluence generators.
// Assumptions (documented with each claim): finite effects/fibers, cooperative
// Apply/Cleanup, acyclic declared dependencies, Runtime-managed effects only.
// ---------------------------------------------------------------------------

// effRecComp installs n effects (each pushing its id) and optionally fails
// after k of them are committed.
type effRecComp struct {
	n       int
	failAt  int // -1 = no failure
	opens   *int
	closes  *int
	mu      *chanLock
	applies chan struct{}
}

type chanLock struct{ c chan struct{} }

func (l *chanLock) lock()   { l.c <- struct{}{} }
func (l *chanLock) unlock() { <-l.c }

func (c *effRecComp) Name() string          { return "recovery" }
func (c *effRecComp) Inject() []Dependency  { return nil }
func (c *effRecComp) Provide() []Capability { return nil }
func (c *effRecComp) Apply(ctx *Context) (Cleanup, error) {
	for i := 0; i < c.n; i++ {
		id := i
		if err := ctx.Effect(func() (func() error, error) {
			c.mu.lock()
			*c.opens++
			c.mu.unlock()
			return func() error {
				c.mu.lock()
				*c.closes++
				c.mu.unlock()
				return nil
			}, nil
		}); err != nil {
			return nil, err
		}
		if c.failAt >= 0 && id == c.failAt {
			return nil, fmt.Errorf("injected failure after %d effects", id+1)
		}
	}
	if c.applies != nil {
		c.applies <- struct{}{}
	}
	return nil, nil
}

func newCounters() (*int, *int, *chanLock) {
	o, cl := 0, 0
	l := &chanLock{c: make(chan struct{}, 1)}
	return &o, &cl, l
}

// P-REC-01 — for random stacks of committed effects, unwinding closes exactly
// the effects that were opened (S2 == S0) — including when Apply fails partway.
func TestPRecRecoveryExactnessRandomStacks(t *testing.T) {
	seeds := []int64{3, 11, 99}
	for _, seed := range seeds {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewSource(seed))
			for iter := 0; iter < 8; iter++ {
				n := 1 + rng.Intn(12)
				failAt := -1
				if rng.Intn(2) == 0 {
					failAt = rng.Intn(n)
				}
				opens, closes, lk := newCounters()
				comp := &effRecComp{n: n, failAt: failAt, opens: opens, closes: closes, mu: lk}
				rt, err := New()
				if err != nil {
					t.Fatal(err)
				}
				f, err := rt.Load(comp)
				if err != nil {
					t.Fatal(err)
				}
				// Converge to terminal state (Active or Failed).
				deadline := time.Now().Add(10 * time.Second)
				for (f.State() != StateActive && f.State() != StateFailed) && time.Now().Before(deadline) {
					time.Sleep(time.Millisecond)
				}
				if failAt >= 0 && f.State() != StateFailed {
					t.Fatalf("expected Failed after injected failure, got %v", f.State())
				}
				if failAt < 0 && f.State() != StateActive {
					t.Fatalf("expected Active, got %v", f.State())
				}
				_ = f.Dispose()
				_ = f.Gone(context.Background())
				expected := n
				if failAt >= 0 {
					expected = failAt + 1 // effects 0..failAt committed before failure
				}
				lk.lock()
				o, c := *opens, *closes
				lk.unlock()
				if o != expected || c != expected {
					t.Fatalf("iter %d (n=%d failAt=%d): opens=%d closes=%d, want %d/%d", iter, n, failAt, o, c, expected, expected)
				}
				_ = rt.Close(context.Background())
			}
		})
	}
}

// ---------------------------------------------------------------------------
// Ordering: concurrent provider/consumer mounting converges to a consistent
// snapshot (all active consumers resolve their declared provider).
// ---------------------------------------------------------------------------

type ordKey struct{ id int }

var ordKeys = func() []CapabilityKey {
	var ks []CapabilityKey
	for i := 0; i < 4; i++ {
		ks = append(ks, NewKey[ordKey](fmt.Sprintf("ord.%d", i)).Capability())
	}
	return ks
}()

type ordProv struct {
	key CapabilityKey
	tag int
}

func (c *ordProv) Name() string          { return fmt.Sprintf("ord-p%d", c.tag) }
func (c *ordProv) Inject() []Dependency  { return nil }
func (c *ordProv) Provide() []Capability { return []Capability{c.key} }
func (c *ordProv) Apply(ctx *Context) (Cleanup, error) {
	return nil, ctx.provideCap(c.key, ordKey{id: c.tag})
}

type ordCons struct {
	key CapabilityKey
}

func (c *ordCons) Name() string          { return "ord-c" }
func (c *ordCons) Inject() []Dependency  { return []Dependency{{Key: c.key}} }
func (c *ordCons) Provide() []Capability { return nil }
func (c *ordCons) Apply(ctx *Context) (Cleanup, error) {
	_, ok := ctx.realm.lookup(c.key)
	if !ok {
		return nil, ErrDependencyMissing
	}
	return nil, nil
}

// P-ORD-01 — concurrent mounts on shared keys converge: exactly the providers
// that won are Active, every Active consumer resolves, invariants hold.
func TestPOrdConcurrentMountOrdering(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(contextOf(t))
	key := ordKeys[0]
	fs := make([]*Fiber, 0, 16)
	var chans []chan struct{}
	for i := 0; i < 8; i++ {
		p, err := rt.Load(&ordProv{key: key, tag: i})
		if err != nil {
			t.Fatal(err)
		}
		fs = append(fs, p)
	}
	for i := 0; i < 8; i++ {
		c, err := rt.Load(&ordCons{key: key})
		if err != nil {
			t.Fatal(err)
		}
		fs = append(fs, c)
	}
	_ = chans
	rt.waitQuiesce()
	// Exactly one provider Active per realm; every Active consumer resolved.
	onOrchestrator(t, rt, func(o *orchestrator) {
		activeProv := 0
		for _, f := range rt.fibersSnapshot() {
			if f.State() == StateActive && len(f.provide) > 0 {
				activeProv++
			}
			if f.State() == StateActive && len(f.inject) > 0 {
				if _, ok := o.resolveDependency(f.realm, key); !ok {
					t.Fatalf("Active consumer %d unresolvable", f.id)
				}
			}
		}
		if activeProv != 1 {
			t.Fatalf("active providers = %d, want 1", activeProv)
		}
	})
	if err := rt.checkOnOrch(t); err != nil {
		t.Fatalf("invariant: %v", err)
	}
	for _, f := range fs {
		_ = f.Dispose()
	}
	for _, f := range fs {
		waitGoneList(t, f)
	}
}

// ---------------------------------------------------------------------------
// Confluence: two independent orderings of the SAME operation multiset reach
// the same canonical observable at quiescence.
// ---------------------------------------------------------------------------

type opKind int

const (
	opLoadProv opKind = iota
	opLoadCons
	opDispose
)

type conOp struct {
	kind opKind
	key  CapabilityKey
}

// runConfluence executes ops serially and returns a canonical snapshot:
// sorted "name:state" of mounted fibers after quiescence.
func runConfluence(t *testing.T, ops []conOp) []string {
	t.Helper()
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(contextOf(t))
	for _, op := range ops {
		switch op.kind {
		case opLoadProv:
			// Fixed tag so the canonical observable is order-independent.
			_, _ = rt.Load(&ordProv{key: op.key, tag: 0})
		case opLoadCons:
			_, _ = rt.Load(&ordCons{key: op.key})
		case opDispose:
			// Intentionally unused in confluence schedules (see comment below).
		}
	}
	rt.waitQuiesce()
	var snap []string
	for _, f := range rt.fibersSnapshot() {
		snap = append(snap, fmt.Sprintf("%s:%v", f.Name(), f.State()))
	}
	sort.Strings(snap)
	return snap
}

// P-CONF-01 — a fixed mount-only op multiset (including a duplicate-provider
// conflict that is deterministic per realm) converges to the same canonical
// observable across generated orderings. (Disposal-based schedules are not
// confluent under arbitrary interleavings because disposal targets specific
// identities; that belongs to identity-aware scheduling, out of v0.1 scope.)
func TestPConfConfluenceGeneratedSchedules(t *testing.T) {
	base := []conOp{
		{kind: opLoadProv, key: ordKeys[1]},
		{kind: opLoadProv, key: ordKeys[1]}, // duplicate: exactly one Active per realm
		{kind: opLoadProv, key: ordKeys[2]},
		{kind: opLoadCons, key: ordKeys[1]},
		{kind: opLoadCons, key: ordKeys[2]},
	}

	snapA := runConfluence(t, base)
	snapB := runConfluence(t, shuffleOps(base))
	snapC := runConfluence(t, shuffleOps(base))
	if len(snapA) != len(snapB) || len(snapA) != len(snapC) {
		t.Fatalf("snapshot sizes differ: %d %d %d", len(snapA), len(snapB), len(snapC))
	}
	for i := range snapA {
		if snapA[i] != snapB[i] || snapA[i] != snapC[i] {
			t.Fatalf("confluence violated: %v vs %v vs %v", snapA, snapB, snapC)
		}
	}
}

func shuffleOps(in []conOp) []conOp {
	rng := rand.New(rand.NewSource(time.Now().UnixNano()))
	out := append([]conOp(nil), in...)
	rng.Shuffle(len(out), func(i, j int) { out[i], out[j] = out[j], out[i] })
	return out
}
