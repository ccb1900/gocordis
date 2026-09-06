package runtime

import (
	"context"
	"testing"
)

// Phase A (theorem-infra v0.2): Semantic Observation + Quiescence Oracle +
// Precondition Checker.
//
// Observation is a canonical, semantic projection of the runtime's paper-level
// state. It contains ONLY semantic entities (fibers/providers/dependencies/
// effects/children) with stable numeric/string identities — no mutexes,
// goroutines, channel/pointer addresses or internal queue state.

import (
	"fmt"
	"sort"
	"strings"
)

// ---------------------------------------------------------------------------
// Semantic Observation
// ---------------------------------------------------------------------------

type FiberObservation struct {
	ID         FiberID
	Name       string
	State      FiberState
	Activation uint64
	Parent     uint64 // 0 = root
}

type ProviderObservation struct {
	Capability   string
	OwnerFiberID uint64
	ActivationID uint64
	Retiring     bool
}

type DependencyObservation struct {
	ConsumerFiberID uint64
	Capability      string
	ProviderFiberID uint64
	ProviderActID   uint64
}

type EffectObservation struct {
	OwnerFiberID uint64
	ActivationID uint64
	Committed    int
	Undone       int
}

type ChildObservation struct {
	ParentID uint64
	ChildID  uint64
}

type Observation struct {
	Fibers       []FiberObservation
	Providers    []ProviderObservation
	Dependencies []DependencyObservation
	Effects      []EffectObservation
	Children     []ChildObservation
}

// observe builds a canonical semantic observation. It is intended to run at a
// quiescent checkpoint (no in-flight lifecycle transition), where a direct
// locked read is consistent; no orchestrator probe is needed.
func observe(rt *Runtime) Observation {
	rt.mu.RLock()
	fs := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		fs = append(fs, f)
	}
	rt.mu.RUnlock()

	obs := Observation{}
	for _, f := range fs {
		f.mu.RLock()
		st := f.state
		act := f.activation
		var actID uint64
		var deps []DependencySnapshot
		effCommitted, effUndone := 0, 0
		if act != nil {
			actID = uint64(act.id)
			deps = append([]DependencySnapshot(nil), act.deps...)
			act.ctx.mu.Lock()
			for _, slot := range act.ctx.effects {
				if slot.state == effectCommitted {
					effCommitted++
				}
				if slot.state == effectUndone {
					effUndone++
				}
			}
			act.ctx.mu.Unlock()
		}
		parent := uint64(0)
		if f.parent != nil {
			parent = uint64(f.parent.id)
		}
		var childIDs []FiberID
		for cid := range f.children {
			childIDs = append(childIDs, cid)
		}
		f.mu.RUnlock()

		// Terminal Gone fibers carry no paper-level observable state (no
		// activation/resources), so they are excluded from the semantic
		// observation.
		if st == StateGone {
			continue
		}
		obs.Fibers = append(obs.Fibers, FiberObservation{ID: f.id, Name: f.Name(), State: st, Activation: actID, Parent: parent})
		for _, cid := range childIDs {
			obs.Children = append(obs.Children, ChildObservation{ParentID: uint64(f.id), ChildID: uint64(cid)})
		}
		for _, d := range deps {
			obs.Dependencies = append(obs.Dependencies, DependencyObservation{
				ConsumerFiberID: uint64(f.id), Capability: d.Key.String(),
				ProviderFiberID: uint64(d.Provider.FiberID), ProviderActID: uint64(d.Provider.ActivationID),
			})
		}
		if actID != 0 {
			obs.Effects = append(obs.Effects, EffectObservation{OwnerFiberID: uint64(f.id), ActivationID: actID, Committed: effCommitted, Undone: effUndone})
		}
	}

	// Providers across realm tree (root today; child realms added with scoping).
	root := rt.rootRealm
	var walk func(r *realm)
	walk = func(r *realm) {
		if r == nil {
			return
		}
		r.mu.RLock()
		for _, rec := range r.own {
			obs.Providers = append(obs.Providers, ProviderObservation{
				Capability: rec.key.String(), OwnerFiberID: uint64(rec.identity.FiberID),
				ActivationID: uint64(rec.identity.ActivationID), Retiring: rec.retiring,
			})
		}
		r.mu.RUnlock()
	}
	walk(root)

	sort.Slice(obs.Fibers, func(i, j int) bool { return obs.Fibers[i].ID < obs.Fibers[j].ID })
	sort.Slice(obs.Providers, func(i, j int) bool {
		if obs.Providers[i].Capability != obs.Providers[j].Capability {
			return obs.Providers[i].Capability < obs.Providers[j].Capability
		}
		return obs.Providers[i].OwnerFiberID < obs.Providers[j].OwnerFiberID
	})
	sort.Slice(obs.Dependencies, func(i, j int) bool {
		a, b := obs.Dependencies[i], obs.Dependencies[j]
		if a.ConsumerFiberID != b.ConsumerFiberID {
			return a.ConsumerFiberID < b.ConsumerFiberID
		}
		return a.Capability < b.Capability
	})
	sort.Slice(obs.Effects, func(i, j int) bool {
		a, b := obs.Effects[i], obs.Effects[j]
		if a.OwnerFiberID != b.OwnerFiberID {
			return a.OwnerFiberID < b.OwnerFiberID
		}
		return a.ActivationID < b.ActivationID
	})
	sort.Slice(obs.Children, func(i, j int) bool {
		a, b := obs.Children[i], obs.Children[j]
		if a.ParentID != b.ParentID {
			return a.ParentID < b.ParentID
		}
		return a.ChildID < b.ChildID
	})
	return obs
}

func actIDOf(f *Fiber) uint64 {
	f.mu.RLock()
	defer f.mu.RUnlock()
	if f.activation == nil {
		return 0
	}
	return uint64(f.activation.id)
}

func (o Observation) canonical() string {
	var b strings.Builder
	for _, f := range o.Fibers {
		fmt.Fprintf(&b, "fiber:%d:%s:%s:%d:%d;", f.ID, f.Name, f.State, f.Activation, f.Parent)
	}
	for _, p := range o.Providers {
		fmt.Fprintf(&b, "prov:%s:%d:%d:%v;", p.Capability, p.OwnerFiberID, p.ActivationID, p.Retiring)
	}
	for _, d := range o.Dependencies {
		fmt.Fprintf(&b, "dep:%d:%s:%d:%d;", d.ConsumerFiberID, d.Capability, d.ProviderFiberID, d.ProviderActID)
	}
	for _, e := range o.Effects {
		fmt.Fprintf(&b, "fx:%d:%d:%d:%d;", e.OwnerFiberID, e.ActivationID, e.Committed, e.Undone)
	}
	for _, c := range o.Children {
		fmt.Fprintf(&b, "child:%d:%d;", c.ParentID, c.ChildID)
	}
	return b.String()
}

func ObservationsEquivalent(a, b Observation) bool { return a.canonical() == b.canonical() }

// ---------------------------------------------------------------------------
// Precondition Checker
// ---------------------------------------------------------------------------

type Preconditions struct {
	AcyclicPrecedence   bool
	ProviderUnique      bool
	PairwiseIndependent bool
	ComponentTotal      bool
}

func (p Preconditions) All() bool {
	return p.AcyclicPrecedence && p.ProviderUnique && p.PairwiseIndependent && p.ComponentTotal
}

func (p Preconditions) String() string {
	return fmt.Sprintf("acyclic=%v unique=%v independent=%v total=%v",
		p.AcyclicPrecedence, p.ProviderUnique, p.PairwiseIndependent, p.ComponentTotal)
}

// semanticQuiescent is a pure (non-time-based) quiescence oracle over the
// semantic state: no transitional fiber, no withdrawal-in-progress with
// pending gates, and no Pending fiber whose dependencies are satisfied.
func semanticQuiescent(rt *Runtime) error {
	rt.mu.RLock()
	fs := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		fs = append(fs, f)
	}
	rt.mu.RUnlock()
	for _, f := range fs {
		f.mu.RLock()
		st := f.state
		withdrawing := f.withdrawing
		gates := len(f.waitGates)
		f.mu.RUnlock()
		if st == StateLoading || st == StateUnloading {
			return fmt.Errorf("transitional fiber %s (%s)", f.Name(), st)
		}
		if withdrawing && gates > 0 {
			return fmt.Errorf("unresolved withdrawal gate on %s", f.Name())
		}
		if st == StatePending {
			for _, dep := range f.inject {
				if _, ok := resolveFor(rt, f, dep.Key); ok {
					return fmt.Errorf("unresolved dependency transition on %s", f.Name())
				}
			}
		}
	}
	return nil
}

func TestObserveCanonicalStable(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	key := NewKey[string]("obs.stable").Capability()
	pf, err := rt.Load(&t66Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(&t66Comp{name: "C", key: key, consumer: true})
	if err != nil {
		t.Fatal(err)
	}
	if _, err := t66Drain(rt); err != nil {
		t.Fatal(err)
	}
	if err := semanticQuiescent(rt); err != nil {
		t.Fatalf("expected semantic quiescence with Active fibers: %v", err)
	}
	if pf.State() != StateActive || cf.State() != StateActive {
		t.Fatalf("P/C not Active")
	}
	o1 := observe(rt)
	o2 := observe(rt)
	if !ObservationsEquivalent(o1, o2) {
		t.Fatal("observation not canonical/deterministic")
	}
	if len(o1.Fibers) != 2 || len(o1.Providers) != 1 || len(o1.Dependencies) != 1 {
		t.Fatalf("unexpected observation: %s", o1.canonical())
	}
	_ = pf.Dispose()
	_ = cf.Dispose()
	if _, err := t66Drain(rt); err != nil {
		t.Fatal(err)
	}
	o3 := observe(rt)
	if len(o3.Providers) != 0 || len(o3.Dependencies) != 0 {
		t.Fatalf("dispose left semantic residue: %s", o3.canonical())
	}
	_ = rt.Close(context.Background())
}

func TestPreconditionsAll(t *testing.T) {
	ok := Preconditions{AcyclicPrecedence: true, ProviderUnique: true, PairwiseIndependent: true, ComponentTotal: true}
	if !ok.All() {
		t.Fatal("all preconditions should hold")
	}
	bad := Preconditions{AcyclicPrecedence: false, ProviderUnique: true, PairwiseIndependent: true, ComponentTotal: true}
	if bad.All() {
		t.Fatal("missing acyclic precedence must fail precondition")
	}
}
