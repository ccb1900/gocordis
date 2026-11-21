package runtime

import (
	"context"
	"testing"
	"time"
)

// R1 audit: the observation model must NOT merge two providers of the SAME key
// that live in DIFFERENT realms (paper Def 24 per-key realm ρ; ProviderView
// carries ScopeID). We build two real realms, each providing rehomeKey.

// realmScopedProv provides rehomeKey in whatever realm its Fiber was created in.
type realmScopedProv struct{ tag string }

func (c *realmScopedProv) Name() string          { return "realm-scoped-prov" }
func (c *realmScopedProv) Inject() []Dependency  { return nil }
func (c *realmScopedProv) Provide() []Capability { return []Capability{rehomeKey.Capability()} }
func (c *realmScopedProv) Apply(ctx *Context) (Cleanup, error) {
	return nil, Provide(ctx, rehomeKey, rehomeKeyVal{tag: c.tag})
}

// realmProvHost spawns one provider child in its OWN fresh realm (WithScope).
type realmProvHost struct {
	tag   string
	child chan *Fiber
}

func (c *realmProvHost) Name() string          { return "realm-prov-host" }
func (c *realmProvHost) Inject() []Dependency  { return nil }
func (c *realmProvHost) Provide() []Capability { return nil }
func (c *realmProvHost) Apply(ctx *Context) (Cleanup, error) {
	f, err := ctx.Child(&realmScopedProv{tag: c.tag}, WithScope())
	if err != nil {
		return nil, err
	}
	if c.child != nil {
		c.child <- f
	}
	return nil, nil
}

func obsWaitState(t *testing.T, f *Fiber, want FiberState) {
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

// TestObservationMultiRealmSameKeyDistinct — R1: two realms provide the same
// key. An observer resolving through realm A must see a change to A's provider;
// an observer resolving through realm B must NOT (it cannot see A).
func TestObservationMultiRealmSameKeyDistinct(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	hACh, hBCh := make(chan *Fiber, 1), make(chan *Fiber, 1)
	hostA, _ := rt.Load(&realmProvHost{tag: "A", child: hACh})
	obsWaitState(t, hostA, StateActive)
	hostB, _ := rt.Load(&realmProvHost{tag: "B", child: hBCh})
	obsWaitState(t, hostB, StateActive)
	provA, provB := <-hACh, <-hBCh
	obsWaitState(t, provA, StateActive)
	obsWaitState(t, provB, StateActive)

	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	// Verify the snapshot really has TWO records for rehomeKey in distinct realms.
	var scopeA, scopeB ScopeID
	var foundA, foundB bool
	for _, p := range snap.Providers {
		if p.Key != rehomeKey.Capability().String() {
			continue
		}
		if p.OwnerFiberID == provA.ID() {
			scopeA, foundA = p.ScopeID, true
		}
		if p.OwnerFiberID == provB.ID() {
			scopeB, foundB = p.ScopeID, true
		}
	}
	if !foundA || !foundB || scopeA == scopeB {
		t.Fatalf("two same-key providers must live in distinct realms: A=%v/%v B=%v/%v", foundA, scopeA, foundB, scopeB)
	}

	before := snap
	model := NewObservationModel() // default = all real operations observed
	scopeAOnly := map[CapabilityKey]ScopeID{rehomeKey.Capability(): scopeA}
	scopeBOnly := map[CapabilityKey]ScopeID{rehomeKey.Capability(): scopeB}

	// Both observers see the same key binding in their own realm — for now
	// equivalent. The critical assertion is that changing realm A's record is
	// observable ONLY through scopeA. We simulate by mutating A's ScopeID.
	mutated := RuntimeSnapshot{Providers: append([]ProviderView(nil), before.Providers...)}
	foundMut := false
	for i := range mutated.Providers {
		if mutated.Providers[i].OwnerFiberID == provA.ID() {
			// A's provider moved to a new scope — a change reachable from A only.
			mutated.Providers[i].ScopeID = scopeA + 1000
			foundMut = true
		}
	}
	if !foundMut {
		t.Fatal("could not mutate realm A provider")
	}

	// Observer in realm A: A's record changed -> NOT equivalent.
	if model.EquivalentAt(before, mutated, nil, scopeAOnly) {
		t.Fatal("R1 BUG: observer in realm A cannot see its own provider's namespace change")
	}
	// Observer in realm B: A's record changed but B's realm unchanged -> still equivalent.
	if !model.EquivalentAt(before, mutated, nil, scopeBOnly) {
		t.Fatal("observer in realm B should not be affected by realm A's change")
	}
}
