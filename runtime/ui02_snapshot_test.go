package runtime

import (
	"context"
	"errors"
	"fmt"
	"reflect"
	"regexp"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"
)

// UI-02 acceptance tests: Observation model + orchestrator-linearized
// Snapshot + canonical event sequence foundation.

var u2Key = NewKey[string]("ui02.db")

func u2New(t *testing.T) *Runtime {
	t.Helper()
	rt, err := New()
	if err != nil {
		t.Fatalf("New: %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

func u2Snap(t *testing.T, rt *Runtime) RuntimeSnapshot {
	t.Helper()
	s, err := rt.Snapshot(context.Background())
	if err != nil {
		t.Fatalf("Snapshot: %v", err)
	}
	return s
}

func u2Wait(t *testing.T, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting for %s", what)
}

// --- components -------------------------------------------------------------

type u2Noop struct{ name string }

func (c *u2Noop) Name() string                        { return c.name }
func (c *u2Noop) Inject() []Dependency                { return nil }
func (c *u2Noop) Provide() []Capability               { return nil }
func (c *u2Noop) Apply(ctx *Context) (Cleanup, error) { return nil, nil }

type u2P struct {
	name string
	key  Key[string]
	tag  string
}

func (c *u2P) Name() string          { return c.name }
func (c *u2P) Inject() []Dependency  { return nil }
func (c *u2P) Provide() []Capability { return []Capability{c.key.Capability()} }
func (c *u2P) Apply(ctx *Context) (Cleanup, error) {
	return nil, Provide(ctx, c.key, c.tag)
}

type u2C struct {
	name string
	key  Key[string]
}

func (c *u2C) Name() string          { return c.name }
func (c *u2C) Inject() []Dependency  { return []Dependency{Requires(c.key)} }
func (c *u2C) Provide() []Capability { return nil }
func (c *u2C) Apply(ctx *Context) (Cleanup, error) {
	_, err := Require(ctx, c.key)
	return nil, err
}

// u2Staged gates Apply and Cleanup so tests can hold Loading / Active /
// Unloading states for observation.
type u2Staged struct {
	name           string
	applyEntered   chan struct{}
	blockApply     chan struct{} // close to release Apply
	cleanupEntered chan struct{}
	blockCleanup   chan struct{} // close to release Cleanup
}

func (c *u2Staged) Name() string          { return c.name }
func (c *u2Staged) Inject() []Dependency  { return nil }
func (c *u2Staged) Provide() []Capability { return nil }
func (c *u2Staged) Apply(ctx *Context) (Cleanup, error) {
	select {
	case c.applyEntered <- struct{}{}:
	default:
	}
	if c.blockApply != nil {
		<-c.blockApply
	}
	return func() error {
		select {
		case c.cleanupEntered <- struct{}{}:
		default:
		}
		if c.blockCleanup != nil {
			<-c.blockCleanup
		}
		return nil
	}, nil
}

// u2EffectComp provides a capability and returns a Cleanup, producing one
// Provider effect and one Cleanup effect per activation.
type u2EffectComp struct {
	name string
	key  Key[string]
}

func (c *u2EffectComp) Name() string          { return c.name }
func (c *u2EffectComp) Inject() []Dependency  { return nil }
func (c *u2EffectComp) Provide() []Capability { return []Capability{c.key.Capability()} }
func (c *u2EffectComp) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, c.key, "v"); err != nil {
		return nil, err
	}
	return func() error { return nil }, nil
}

// u2FailAfterProvide registers a provider and then fails Apply (the record is
// removed by the unwind inverse WITHOUT prior retirement).
type u2FailAfterProvide struct {
	key Key[string]
}

func (c *u2FailAfterProvide) Name() string          { return "u2-fail-provide" }
func (c *u2FailAfterProvide) Inject() []Dependency  { return nil }
func (c *u2FailAfterProvide) Provide() []Capability { return []Capability{c.key.Capability()} }
func (c *u2FailAfterProvide) Apply(ctx *Context) (Cleanup, error) {
	_ = Provide(ctx, c.key, "x")
	return nil, errors.New("boom")
}

// --- canonical rendering ----------------------------------------------------

func u2Canonical(s RuntimeSnapshot) string {
	var b strings.Builder
	fmt.Fprintf(&b, "rt=%s state=%s seq=%d\n", s.RuntimeID, s.State, s.EventSequence)
	for _, f := range s.Fibers {
		fmt.Fprintf(&b, "fiber %d %s state=%s intent=%s act=%d scope=%d parent=%d children=%v err=%q\n",
			f.ID, f.Name, f.State, f.Intent, f.ActivationID, f.ScopeID, f.ParentFiberID, f.ChildFiberIDs, f.Err)
	}
	for _, sc := range s.Scopes {
		fmt.Fprintf(&b, "scope %d parent=%d keys=%v fibers=%v\n", sc.ID, sc.ParentID, sc.ProviderKeys, sc.FiberIDs)
	}
	for _, p := range s.Providers {
		fmt.Fprintf(&b, "prov %s scope=%d owner=%d/%d state=%s retiring=%v\n",
			p.Key, p.ScopeID, p.OwnerFiberID, p.OwnerActivationID, p.State, p.Retiring)
	}
	for _, d := range s.Dependencies {
		fmt.Fprintf(&b, "dep %s consumer=%d/%d provider=%d/%d status=%s reason=%q\n",
			d.Key, d.ConsumerFiberID, d.ConsumerActivationID, d.ProviderFiberID, d.ProviderActivationID, d.Status, d.Reason)
	}
	for _, e := range s.Effects {
		fmt.Fprintf(&b, "fx owner=%d/%d seq=%d kind=%s key=%s state=%s\n",
			e.OwnerFiberID, e.ActivationID, e.Seq, e.Kind, e.Key, e.State)
	}
	return b.String()
}

// --- tests ------------------------------------------------------------------

// 1. Empty Runtime Snapshot.
func TestUI02EmptyRuntimeSnapshot(t *testing.T) {
	rt := u2New(t)
	s := u2Snap(t, rt)
	if s.RuntimeID == "" {
		t.Fatal("empty runtime: missing RuntimeID")
	}
	if s.State != RuntimeRunning {
		t.Fatalf("state = %v, want Running", s.State)
	}
	if len(s.Fibers) != 0 || len(s.Providers) != 0 || len(s.Dependencies) != 0 || len(s.Effects) != 0 {
		t.Fatalf("empty runtime has rows: fibers=%d providers=%d deps=%d effects=%d",
			len(s.Fibers), len(s.Providers), len(s.Dependencies), len(s.Effects))
	}
	if len(s.Scopes) != 1 || s.Scopes[0].ID != 0 || s.Scopes[0].ParentID != 0 {
		t.Fatalf("empty runtime scopes = %+v, want exactly [root]", s.Scopes)
	}
	if s.EventSequence != 0 {
		t.Fatalf("empty runtime event sequence = %d, want 0", s.EventSequence)
	}
	// Determinism: two snapshots of an unchanged Runtime are semantically equal.
	a := u2Snap(t, rt)
	b := u2Snap(t, rt)
	if u2Canonical(a) != u2Canonical(b) {
		t.Fatalf("unchanged runtime snapshots differ:\n%s\n---\n%s", u2Canonical(a), u2Canonical(b))
	}
}

// 2 + 3. Single fiber; lifecycle states observable through Snapshot.
func TestUI02FiberLifecycleSnapshot(t *testing.T) {
	rt := u2New(t)
	staged := &u2Staged{
		name:           "staged",
		applyEntered:   make(chan struct{}, 1),
		blockApply:     make(chan struct{}),
		cleanupEntered: make(chan struct{}, 1),
		blockCleanup:   make(chan struct{}),
	}
	f, err := rt.Load(staged)
	if err != nil {
		t.Fatal(err)
	}
	u2Wait(t, "Apply entered", func() bool {
		select {
		case <-staged.applyEntered:
			return true
		default:
			return false
		}
	})

	// Loading
	s := u2Snap(t, rt)
	if len(s.Fibers) != 1 {
		t.Fatalf("fibers = %d, want 1", len(s.Fibers))
	}
	row := s.Fibers[0]
	if row.ID != f.ID() || row.State != StateLoading || row.ActivationID == 0 || row.ScopeID != 0 {
		t.Fatalf("loading row = %+v", row)
	}
	close(staged.blockApply)
	if err := f.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}

	// Active
	s = u2Snap(t, rt)
	row = s.Fibers[0]
	if row.State != StateActive || row.Intent != IntentMounted || row.ActivationID == 0 {
		t.Fatalf("active row = %+v", row)
	}

	// Unloading (cleanup blocked)
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	u2Wait(t, "Cleanup entered", func() bool {
		select {
		case <-staged.cleanupEntered:
			return true
		default:
			return false
		}
	})
	s = u2Snap(t, rt)
	row = s.Fibers[0]
	if row.State != StateUnloading || row.ActivationID == 0 || row.Intent != IntentUnmounted {
		t.Fatalf("unloading row = %+v", row)
	}
	close(staged.blockCleanup)
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatal(err)
	}

	// Gone fibers are terminal history, not live rows.
	s = u2Snap(t, rt)
	if len(s.Fibers) != 0 {
		t.Fatalf("live fibers after Gone = %d, want 0", len(s.Fibers))
	}

	// Canonical event history for the fiber (monotonic sequence).
	evs := rt.events.all()
	if len(evs) == 0 {
		t.Fatal("no events recorded")
	}
	for i := 1; i < len(evs); i++ {
		if evs[i].Sequence <= evs[i-1].Sequence {
			t.Fatalf("event sequence not monotonic at %d: %d -> %d", i, evs[i-1].Sequence, evs[i].Sequence)
		}
	}
	// State-machine transitions must occur in canonical order. Effect events
	// interleave inside the windows they belong to (Cleanup effect commits
	// during Loading->Active, unwinds during Unloading->Ended), so the
	// ordering assertion filters to the lifecycle transition types.
	wantTypes := []EventType{EventFiberCreated, EventActivationLoading, EventActivationActive, EventActivationUnloading, EventActivationEnded}
	isLifecycle := map[EventType]bool{}
	for _, wt := range wantTypes {
		isLifecycle[wt] = true
	}
	var all []EventType
	for _, ev := range evs {
		if ev.FiberID == f.ID() {
			all = append(all, ev.Type)
		}
	}
	var got []EventType
	for _, e := range all {
		if isLifecycle[e] {
			got = append(got, e)
		}
	}
	if len(got) != len(wantTypes) {
		t.Fatalf("fiber lifecycle events = %v, want exactly %v", got, wantTypes)
	}
	for i, wt := range wantTypes {
		if got[i] != wt {
			t.Fatalf("fiber lifecycle event #%d = %s, want %s (all fiber events: %v)", i, got[i], wt, all)
		}
	}
	// Canonical windows: the returned Cleanup effect is committed at the
	// Loading->Active boundary and unwound inside the Unloading->Ended window.
	idx := func(typ EventType) int {
		for i, e := range all {
			if e == typ {
				return i
			}
		}
		return -1
	}
	iLoading, iActive := idx(EventActivationLoading), idx(EventActivationActive)
	iCommit := idx(EventEffectCommitted)
	if iCommit < 0 || iCommit < iLoading || iCommit > iActive {
		t.Fatalf("cleanup commit not inside Loading->Active window: loading=%d commit=%d active=%d (events %v)", iLoading, iCommit, iActive, all)
	}
	iUnload, iEnded := idx(EventActivationUnloading), idx(EventActivationEnded)
	for _, u := range []EventType{EventEffectUndoing, EventEffectUndone} {
		if i := idx(u); i < 0 || i < iUnload || i > iEnded {
			t.Fatalf("%s not inside Unloading->Ended window: unloading=%d %s=%d ended=%d (events %v)", u, iUnload, u, i, iEnded, all)
		}
	}
}

// 4. Activation generation: reload is a NEW activation with a NEW provider
// identity.
func TestUI02ActivationGeneration(t *testing.T) {
	rt := u2New(t)
	key := u2Key
	p, err := rt.Load(&u2P{name: "p", key: key, tag: "gen"})
	if err != nil {
		t.Fatal(err)
	}
	if err := p.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	s := u2Snap(t, rt)
	gen1 := s.Fibers[0].ActivationID
	if gen1 == 0 {
		t.Fatal("no activation in snapshot")
	}
	prov1 := s.Providers[0]
	if prov1.OwnerActivationID != gen1 {
		t.Fatalf("provider identity act = %d, want fiber act %d", prov1.OwnerActivationID, gen1)
	}

	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := p.Gone(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	if err := p.Load(); err != nil {
		t.Fatal(err)
	}
	if err := p.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	s = u2Snap(t, rt)
	gen2 := s.Fibers[0].ActivationID
	if gen2 == 0 || gen2 == gen1 {
		t.Fatalf("activation generation unchanged after reload: %d", gen2)
	}
	if s.Providers[0].OwnerActivationID != gen2 {
		t.Fatalf("provider did not rebind to new generation: %+v", s.Providers[0])
	}
	// The provider identity is generation-exact (FiberID + ActivationID).
	if s.Providers[0].OwnerFiberID != p.ID() {
		t.Fatalf("provider owner fiber = %d, want %d", s.Providers[0].OwnerFiberID, p.ID())
	}
}

// 5 + 6. Provider identity, dependency binding, withdrawal -> Waiting, rebind.
func TestUI02DependencyBindingLifecycle(t *testing.T) {
	rt := u2New(t)
	key := u2Key
	p, err := rt.Load(&u2P{name: "db", key: key, tag: "d1"})
	if err != nil {
		t.Fatal(err)
	}
	if err := p.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	c, err := rt.Load(&u2C{name: "svc", key: key})
	if err != nil {
		t.Fatal(err)
	}
	if err := c.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}

	s := u2Snap(t, rt)
	pRow := s.Fibers[0]
	cRow := s.Fibers[1]
	if pRow.State != StateActive || cRow.State != StateActive {
		t.Fatalf("rows not active: %+v %+v", pRow, cRow)
	}
	if len(s.Providers) != 1 {
		t.Fatalf("providers = %+v, want 1", s.Providers)
	}
	prov := s.Providers[0]
	if prov.Key != key.Capability().String() || prov.ScopeID != 0 || prov.Retiring || prov.State != StateActive {
		t.Fatalf("provider row = %+v", prov)
	}
	if prov.OwnerFiberID != p.ID() || prov.OwnerActivationID != pRow.ActivationID {
		t.Fatalf("provider identity = %d/%d, want %d/%d", prov.OwnerFiberID, prov.OwnerActivationID, p.ID(), pRow.ActivationID)
	}
	var dep *DependencyView
	for i := range cRow.Dependencies {
		if cRow.Dependencies[i].Key == key.Capability().String() {
			dep = &cRow.Dependencies[i]
		}
	}
	if dep == nil {
		t.Fatalf("consumer rows = %+v, want db dep", cRow.Dependencies)
	}
	if dep.Status != DependencySatisfied || dep.ProviderFiberID != p.ID() || dep.ProviderActivationID != pRow.ActivationID {
		t.Fatalf("dependency = %+v", *dep)
	}

	// Withdraw the provider: the consumer loses its binding -> Pending ->
	// dependency row becomes Waiting.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := p.Gone(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	if err := c.WaitInactive(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	u2Wait(t, "consumer Pending", func() bool { return c.State() == StatePending })
	s = u2Snap(t, rt)
	// provider owner is Gone -> no live provider row; consumer Pending with
	// Waiting dep. (The snapshot preserves the Unloading/Retiring row during the
	// withdrawal window, so the owner must be Gone before asserting absence.)
	if len(s.Providers) != 0 {
		t.Fatalf("providers after withdraw = %+v, want none", s.Providers)
	}
	found := false
	for _, row := range s.Fibers {
		if row.ID == c.ID() {
			found = true
			if row.State != StatePending || row.ActivationID != 0 {
				t.Fatalf("consumer row = %+v", row)
			}
			if len(row.Dependencies) != 1 || row.Dependencies[0].Status != DependencyWaiting {
				t.Fatalf("consumer deps = %+v, want Waiting", row.Dependencies)
			}
			if row.Dependencies[0].Reason != "Provider unavailable" {
				t.Fatalf("waiting reason = %q", row.Dependencies[0].Reason)
			}
		}
	}
	if !found {
		t.Fatal("consumer fiber missing from snapshot")
	}

	// Reload the provider: the consumer rebinds (a NEW generation).
	if err := p.Load(); err != nil {
		t.Fatal(err)
	}
	if err := p.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	if err := c.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	s = u2Snap(t, rt)
	var rebound *DependencyView
	for _, row := range s.Fibers {
		if row.ID == c.ID() {
			for i := range row.Dependencies {
				d := &row.Dependencies[i]
				if d.Key == key.Capability().String() {
					rebound = d
				}
			}
		}
	}
	if rebound == nil || rebound.Status != DependencySatisfied {
		t.Fatalf("consumer did not rebind: %+v", rebound)
	}
	for _, prov := range s.Providers {
		if prov.OwnerFiberID == p.ID() && rebound.ProviderFiberID == p.ID() && prov.OwnerActivationID == rebound.ProviderActivationID {
			return // rebound to the NEW generation
		}
	}
	t.Fatalf("rebound provider %d/%d not present in providers %+v", rebound.ProviderFiberID, rebound.ProviderActivationID, s.Providers)
}

// 7. Scope hierarchy: sibling scopes with the same key are not merged; parent
// chain resolution reaches the root provider.
func TestUI02ScopeHierarchyIsolation(t *testing.T) {
	rt := u2New(t)
	key := u2Key
	rp, err := rt.Load(&u2P{name: "root-db", key: key, tag: "root"})
	if err != nil {
		t.Fatal(err)
	}
	if err := rp.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	chA := make(chan *Fiber, 2)
	chB := make(chan *Fiber, 2)
	chC := make(chan *Fiber, 1)
	af, err := rt.Load(&u2ScopeActivator{key: key, chA: chA, chB: chB, chC: chC})
	if err != nil {
		t.Fatal(err)
	}
	if err := af.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	pa, ca := <-chA, <-chA
	pb, cb := <-chB, <-chB
	cc := <-chC
	u2Wait(t, "scope fibers active", func() bool {
		for _, f := range []*Fiber{pa, ca, pb, cb, cc} {
			if f.State() != StateActive {
				return false
			}
		}
		return true
	})

	s := u2Snap(t, rt)
	// Scope rows: root + three child scopes; A/B own the key independently.
	if len(s.Scopes) != 4 {
		t.Fatalf("scopes = %+v, want root + 3 children", s.Scopes)
	}
	var root, scA, scB, scC *ScopeSnapshot
	for i := range s.Scopes {
		switch {
		case s.Scopes[i].ID == 0:
			root = &s.Scopes[i]
		case len(s.Scopes[i].ProviderKeys) == 1 && s.Scopes[i].ProviderKeys[0] == key.Capability().String():
			// host scope: the WithScope host fiber + provider + consumer all
			// resolve through the derived realm
			if scA == nil {
				scA = &s.Scopes[i]
			} else {
				scB = &s.Scopes[i]
			}
		default:
			// empty scope: only the consumer fiber
			scC = &s.Scopes[i]
		}
	}
	if root == nil || scA == nil || scB == nil || scC == nil {
		t.Fatalf("scope rows incomplete: root=%v A=%v B=%v C=%v", root, scA, scB, scC)
	}
	if scA.ParentID != 0 || scB.ParentID != 0 || scC.ParentID != 0 {
		t.Fatalf("child scope parents = %d/%d/%d, want root", scA.ParentID, scB.ParentID, scC.ParentID)
	}
	has := func(keys []string, want string) bool {
		for _, k := range keys {
			if k == want {
				return true
			}
		}
		return false
	}
	if !has(scA.ProviderKeys, key.Capability().String()) || !has(scB.ProviderKeys, key.Capability().String()) || len(scC.ProviderKeys) != 0 {
		t.Fatalf("scope provider keys: A=%v B=%v C=%v", scA.ProviderKeys, scB.ProviderKeys, scC.ProviderKeys)
	}
	if len(scA.FiberIDs) != 3 || len(scB.FiberIDs) != 3 || len(scC.FiberIDs) != 1 {
		t.Fatalf("scope fiber membership: A=%v B=%v C=%v", scA.FiberIDs, scB.FiberIDs, scC.FiberIDs)
	}
	in := func(fids []FiberID, fid FiberID) bool {
		for _, x := range fids {
			if x == fid {
				return true
			}
		}
		return false
	}
	if !in(scA.FiberIDs, pa.ID()) || !in(scA.FiberIDs, ca.ID()) || in(scA.FiberIDs, pb.ID()) || in(scA.FiberIDs, cb.ID()) {
		t.Fatalf("scope A membership wrong: A=%v want A fibers %d/%d not B fibers", scA.FiberIDs, pa.ID(), ca.ID())
	}
	if !in(scB.FiberIDs, pb.ID()) || !in(scB.FiberIDs, cb.ID()) || in(scB.FiberIDs, pa.ID()) || in(scB.FiberIDs, ca.ID()) {
		t.Fatalf("scope B membership wrong: B=%v want B fibers %d/%d not A fibers", scB.FiberIDs, pb.ID(), cb.ID())
	}
	if !in(scC.FiberIDs, cc.ID()) {
		t.Fatalf("scope C membership wrong: C=%v want consumer %d", scC.FiberIDs, cc.ID())
	}
	// Root provider lives in the root scope.
	if !has(root.ProviderKeys, key.Capability().String()) {
		t.Fatalf("root scope keys = %v, want root provider", root.ProviderKeys)
	}

	// Provider rows: three db providers with distinct scope + activation
	// identity (never merged).
	if len(s.Providers) != 3 {
		t.Fatalf("providers = %+v, want 3", s.Providers)
	}
	seenScope := map[ScopeID]bool{}
	for _, p := range s.Providers {
		if p.Key != key.Capability().String() {
			t.Fatalf("unexpected provider key %q", p.Key)
		}
		seenScope[p.ScopeID] = true
	}
	if !seenScope[0] || !seenScope[scA.ID] || !seenScope[scB.ID] {
		t.Fatalf("provider scope coverage = %v", seenScope)
	}

	// Bindings: A->A, B->B (nearest wins), C->root (parent chain).
	binding := func(fid FiberID) (FiberID, ActivationID) {
		for _, row := range s.Fibers {
			if row.ID == fid {
				for _, d := range row.Dependencies {
					if d.Status == DependencySatisfied {
						return d.ProviderFiberID, d.ProviderActivationID
					}
				}
			}
		}
		t.Fatalf("no satisfied binding for fiber %d", fid)
		return 0, 0
	}
	provFiberOf := func(fid FiberID) FiberID {
		for _, p := range s.Providers {
			if p.OwnerFiberID == fid {
				return fid
			}
		}
		t.Fatalf("no provider row for fiber %d", fid)
		return 0
	}
	if pf, _ := binding(ca.ID()); pf != provFiberOf(pa.ID()) {
		t.Fatalf("consumer A bound to %d, want its scope provider %d", pf, pa.ID())
	}
	if pf, _ := binding(cb.ID()); pf != provFiberOf(pb.ID()) {
		t.Fatalf("consumer B bound to %d, want its scope provider %d", pf, pb.ID())
	}
	if pf, pact := binding(cc.ID()); pf != rp.ID() || pact != ActivationID(actIDOf(rp)) {
		t.Fatalf("consumer C bound to %d/%d, want root %d/%d", pf, pact, rp.ID(), actIDOf(rp))
	}

	// Dispose scope A's provider: A's consumer goes Pending; B stays Active
	// and keeps its binding (sibling isolation).
	if err := pa.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := ca.WaitInactive(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	u2Wait(t, "scope A consumer Pending", func() bool { return ca.State() == StatePending })
	s = u2Snap(t, rt)
	var caRow *FiberSnapshot
	for i := range s.Fibers {
		if s.Fibers[i].ID == ca.ID() {
			caRow = &s.Fibers[i]
		}
	}
	if caRow == nil {
		t.Fatalf("scope A consumer row missing: %+v", s.Fibers)
	}
	if caRow.State != StatePending {
		t.Fatalf("scope A consumer = %+v, want Pending", *caRow)
	}
	for _, row := range s.Fibers {
		if row.ID == cb.ID() && row.State != StateActive {
			t.Fatalf("scope B consumer disturbed by scope A dispose: %+v", row)
		}
	}
}

// 8. Effect metadata: Provide -> Provider kind with key; Apply Cleanup ->
// Cleanup kind; unwind transitions observable in the event sequence; a
// failed-Apply provider record is withdrawn without prior retirement.
func TestUI02EffectMetadataAndEvents(t *testing.T) {
	rt := u2New(t)
	f, err := rt.Load(&u2EffectComp{name: "fx", key: u2Key})
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	s := u2Snap(t, rt)
	row := s.Fibers[0]
	if len(row.Effects) != 2 {
		t.Fatalf("effects = %+v, want Provider + Cleanup", row.Effects)
	}
	provFx, cleanupFx := row.Effects[0], row.Effects[1]
	if provFx.Kind != EffectKindProvider || provFx.Key != u2Key.Capability().String() || provFx.State != "Committed" {
		t.Fatalf("provider effect = %+v", provFx)
	}
	if cleanupFx.Kind != EffectKindCleanup || cleanupFx.Key != "" || cleanupFx.State != "Committed" {
		t.Fatalf("cleanup effect = %+v", cleanupFx)
	}
	if provFx.Seq >= cleanupFx.Seq {
		t.Fatalf("effect order: provider seq %d must precede cleanup seq %d", provFx.Seq, cleanupFx.Seq)
	}
	if len(s.Effects) != 2 {
		t.Fatalf("global effects = %+v, want 2", s.Effects)
	}

	// Unwind: EffectUndoing/EffectUndone events fire for each slot (LIFO).
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	evs := rt.events.all()
	var undoSeq []string
	for _, ev := range evs {
		if ev.FiberID == f.ID() && (ev.Type == EventEffectUndoing || ev.Type == EventEffectUndone) {
			undoSeq = append(undoSeq, string(ev.Type))
		}
	}
	wantUndo := []string{"EffectUndoing", "EffectUndone", "EffectUndoing", "EffectUndone"}
	if len(undoSeq) != len(wantUndo) {
		t.Fatalf("effect undo events = %v, want %v", undoSeq, wantUndo)
	}
	for i := range wantUndo {
		if undoSeq[i] != wantUndo[i] {
			t.Fatalf("effect undo #%d = %s, want %s", i, undoSeq[i], wantUndo[i])
		}
	}
	published := false
	for _, ev := range evs {
		if ev.Type == EventProviderPublished && ev.FiberID == f.ID() {
			published = true
		}
	}
	if !published {
		t.Fatal("ProviderPublished event missing")
	}

	// Failed-Apply provider record: removed without retirement -> withdraw
	// event, Failure event, terminal Failed row.
	rt2 := u2New(t)
	g, err := rt2.Load(&u2FailAfterProvide{key: u2Key})
	if err != nil {
		t.Fatal(err)
	}
	if err := g.Ready(testCtx(t)); err == nil {
		t.Fatal("failed component became ready")
	}
	u2Wait(t, "failed fiber terminal", func() bool { return g.State() == StateFailed })
	s2 := u2Snap(t, rt2)
	if len(s2.Providers) != 0 {
		t.Fatalf("failed activation left provider rows: %+v", s2.Providers)
	}
	if s2.Fibers[0].State != StateFailed || s2.Fibers[0].Err == "" {
		t.Fatalf("failed row = %+v", s2.Fibers[0])
	}
	var sawFailure, sawWithdraw bool
	for _, ev := range rt2.events.all() {
		if ev.Type == EventFailure && ev.FiberID == g.ID() {
			sawFailure = true
		}
		if ev.Type == EventProviderWithdrawn && ev.FiberID == g.ID() {
			sawWithdraw = true
		}
	}
	if !sawFailure || !sawWithdraw {
		t.Fatalf("failed activation events: failure=%v withdraw=%v", sawFailure, sawWithdraw)
	}
}

// 9. RuntimeID: explicit and default.
func TestUI02RuntimeID(t *testing.T) {
	rt := u2New(t)
	if rt.RuntimeID() == "" || rt.RuntimeID() != rt.runtimeID {
		t.Fatalf("runtime id = %q", rt.RuntimeID())
	}
	if !regexp.MustCompile(`^runtime:\d+$`).MatchString(string(rt.RuntimeID())) {
		t.Fatalf("default runtime id = %q, want runtime:<n>", rt.RuntimeID())
	}
	if u2Snap(t, rt).RuntimeID != rt.RuntimeID() {
		t.Fatal("snapshot runtime id mismatch")
	}

	rt2, err := New(WithRuntimeID("console-a"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt2.Close(ctx)
	})
	if rt2.RuntimeID() != "console-a" {
		t.Fatalf("explicit runtime id = %q, want console-a", rt2.RuntimeID())
	}
	if rt.RuntimeID() == rt2.RuntimeID() {
		t.Fatal("two runtimes share an id")
	}
}

// 10. Snapshot <-> observe() semantic equivalence (root-realm compositions).
func TestUI02SnapshotMatchesObserve(t *testing.T) {
	key := u2Key
	cases := []struct {
		name string
		load func(rt *Runtime) []*Fiber
	}{
		{name: "empty", load: func(*Runtime) []*Fiber { return nil }},
		{
			name: "provider-consumer",
			load: func(rt *Runtime) []*Fiber {
				p, err := rt.Load(&u2P{name: "p", key: key, tag: "v"})
				if err != nil {
					t.Fatal(err)
				}
				c, err := rt.Load(&u2C{name: "c", key: key})
				if err != nil {
					t.Fatal(err)
				}
				return []*Fiber{p, c}
			},
		},
		{
			name: "cleanup-effects",
			load: func(rt *Runtime) []*Fiber {
				f, err := rt.Load(&u2EffectComp{name: "fx", key: key})
				if err != nil {
					t.Fatal(err)
				}
				return []*Fiber{f}
			},
		},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			rt := u2New(t)
			fs := tc.load(rt)
			for _, f := range fs {
				if err := f.Ready(testCtx(t)); err != nil {
					t.Fatal(err)
				}
			}
			if err := semanticQuiescent(rt); err != nil {
				t.Fatalf("not quiescent: %v", err)
			}
			snap := u2Snap(t, rt)
			got := u2ObserveFromSnapshot(snap)
			want := observe(rt)
			if !ObservationsEquivalent(got, want) {
				t.Fatalf("snapshot observation diverges from observe():\n got %s\nwant %s", got.canonical(), want.canonical())
			}
		})
	}
}

// u2ObserveFromSnapshot converts a root-realm-compatible snapshot into the
// theorem Observation shape (same canonical semantics as observe()).
func u2ObserveFromSnapshot(s RuntimeSnapshot) Observation {
	var o Observation
	for _, f := range s.Fibers {
		o.Fibers = append(o.Fibers, FiberObservation{
			ID:         f.ID,
			Name:       f.Name,
			State:      f.State,
			Activation: uint64(f.ActivationID),
			Parent:     uint64(f.ParentFiberID),
		})
		for _, cid := range f.ChildFiberIDs {
			o.Children = append(o.Children, ChildObservation{ParentID: uint64(f.ID), ChildID: uint64(cid)})
		}
	}
	for _, p := range s.Providers {
		if p.ScopeID != 0 {
			continue // observe() predates scoping and only reads the root realm
		}
		o.Providers = append(o.Providers, ProviderObservation{
			Capability:   p.Key,
			OwnerFiberID: uint64(p.OwnerFiberID),
			ActivationID: uint64(p.OwnerActivationID),
			Retiring:     p.Retiring,
		})
	}
	for _, d := range s.Dependencies {
		if d.ConsumerActivationID == 0 {
			continue // observe() only sees activation-captured dependencies
		}
		o.Dependencies = append(o.Dependencies, DependencyObservation{
			ConsumerFiberID: uint64(d.ConsumerFiberID),
			Capability:      d.Key,
			ProviderFiberID: uint64(d.ProviderFiberID),
			ProviderActID:   uint64(d.ProviderActivationID),
		})
	}
	counts := map[[2]uint64][2]int{}
	for _, e := range s.Effects {
		k := [2]uint64{uint64(e.OwnerFiberID), uint64(e.ActivationID)}
		c := counts[k]
		switch e.State {
		case "Committed":
			c[0]++
		case "Undone":
			c[1]++
		}
		counts[k] = c
	}
	// observe() emits one EffectObservation per activated fiber (counts can be
	// 0/0 when the activation carries no effect slots); mirror that so the
	// snapshot-derived Observation has identical canonical semantics.
	for _, f := range s.Fibers {
		if f.ActivationID == 0 {
			continue
		}
		k := [2]uint64{uint64(f.ID), uint64(f.ActivationID)}
		c := counts[k]
		o.Effects = append(o.Effects, EffectObservation{OwnerFiberID: k[0], ActivationID: k[1], Committed: c[0], Undone: c[1]})
	}
	u2SortObservation(&o)
	return o
}

func u2SortObservation(o *Observation) {
	sort.Slice(o.Fibers, func(i, j int) bool { return o.Fibers[i].ID < o.Fibers[j].ID })
	sort.Slice(o.Providers, func(i, j int) bool {
		a, b := o.Providers[i], o.Providers[j]
		if a.Capability != b.Capability {
			return a.Capability < b.Capability
		}
		return a.OwnerFiberID < b.OwnerFiberID
	})
	sort.Slice(o.Dependencies, func(i, j int) bool {
		a, b := o.Dependencies[i], o.Dependencies[j]
		if a.ConsumerFiberID != b.ConsumerFiberID {
			return a.ConsumerFiberID < b.ConsumerFiberID
		}
		return a.Capability < b.Capability
	})
	sort.Slice(o.Effects, func(i, j int) bool {
		a, b := o.Effects[i], o.Effects[j]
		if a.OwnerFiberID != b.OwnerFiberID {
			return a.OwnerFiberID < b.OwnerFiberID
		}
		return a.ActivationID < b.ActivationID
	})
	sort.Slice(o.Children, func(i, j int) bool {
		a, b := o.Children[i], o.Children[j]
		if a.ParentID != b.ParentID {
			return a.ParentID < b.ParentID
		}
		return a.ChildID < b.ChildID
	})
}

// 11 + 12. Snapshot exposes only value data (no kernel internals) and cannot
// be used to mutate Runtime state.
func TestUI02SnapshotNoInternalsAndImmutable(t *testing.T) {
	for _, typ := range []reflect.Type{
		reflect.TypeOf(RuntimeSnapshot{}),
		reflect.TypeOf(FiberSnapshot{}),
		reflect.TypeOf(ProviderView{}),
		reflect.TypeOf(DependencyView{}),
		reflect.TypeOf(EffectView{}),
		reflect.TypeOf(ScopeSnapshot{}),
	} {
		u2AssertValueOnly(t, typ, typ.Name())
	}

	rt := u2New(t)
	p, err := rt.Load(&u2Noop{name: "n"})
	if err != nil {
		t.Fatal(err)
	}
	if err := p.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	base := u2Canonical(u2Snap(t, rt))

	// Aggressive mutation of a returned snapshot (fields, slices, rows).
	s := u2Snap(t, rt)
	if len(s.Fibers) != 1 {
		t.Fatalf("fibers = %d", len(s.Fibers))
	}
	s.Fibers[0].State = StateGone
	s.Fibers[0].ActivationID = 999
	s.Fibers[0].ChildFiberIDs = append(s.Fibers[0].ChildFiberIDs, FiberID(123))
	s.Fibers = append(s.Fibers, FiberSnapshot{ID: FiberID(777)})
	s.Providers = append(s.Providers, ProviderView{Key: "forged"})
	s.Scopes[0].ProviderKeys = append(s.Scopes[0].ProviderKeys, "forged")
	s.Dependencies = nil
	s.Effects = nil
	s.EventSequence = 1

	after := u2Canonical(u2Snap(t, rt))
	if after != base {
		t.Fatalf("snapshot mutation leaked into Runtime:\n got %s\nwant %s", after, base)
	}
	// Runtime state itself is untouched.
	if p.State() != StateActive {
		t.Fatalf("runtime state mutated through snapshot: %v", p.State())
	}
}

func u2AssertValueOnly(t *testing.T, typ reflect.Type, path string) {
	t.Helper()
	switch typ.Kind() {
	case reflect.Ptr, reflect.Chan, reflect.Func, reflect.Map, reflect.Interface, reflect.UnsafePointer:
		t.Fatalf("%s exposes forbidden kernel type %v", path, typ.Kind())
	case reflect.Slice, reflect.Array:
		u2AssertValueOnly(t, typ.Elem(), path+"[]")
	case reflect.Struct:
		for i := 0; i < typ.NumField(); i++ {
			f := typ.Field(i)
			u2AssertValueOnly(t, f.Type, path+"."+f.Name)
		}
	case reflect.String, reflect.Bool, reflect.Int, reflect.Int8, reflect.Int16, reflect.Int32, reflect.Int64,
		reflect.Uint, reflect.Uint8, reflect.Uint16, reflect.Uint32, reflect.Uint64:
		// primitive value
	default:
		t.Fatalf("%s: unexpected kind %v", path, typ.Kind())
	}
}

// 13 + 14. Orchestrator-linearized reads under concurrent Apply/Cleanup: every
// snapshot is internally coherent (structural invariants) and deterministically
// ordered.
func TestUI02SnapshotLinearizedUnderConcurrency(t *testing.T) {
	rt := u2New(t)
	key := u2Key
	p, err := rt.Load(&u2P{name: "db", key: key, tag: "c"})
	if err != nil {
		t.Fatal(err)
	}
	c, err := rt.Load(&u2C{name: "svc", key: key})
	if err != nil {
		t.Fatal(err)
	}
	if err := p.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	if err := c.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}

	stop := make(chan struct{})
	var wg sync.WaitGroup
	wg.Add(2)
	go func() { // churn provider reloads (concurrent Apply/unwind)
		defer wg.Done()
		for i := 0; i < 60; i++ {
			select {
			case <-stop:
				return
			default:
			}
			if p.State() == StateActive || p.State() == StateLoading || p.State() == StateUnloading {
				_ = p.Dispose()
			} else {
				_ = p.Load()
			}
			time.Sleep(time.Millisecond)
		}
	}()
	go func() { // churn consumer reloads
		defer wg.Done()
		for i := 0; i < 60; i++ {
			select {
			case <-stop:
				return
			default:
			}
			if c.State() == StateActive || c.State() == StateLoading || c.State() == StateUnloading {
				_ = c.Dispose()
			} else {
				_ = c.Load()
			}
			time.Sleep(time.Millisecond)
		}
	}()

	var snaps []RuntimeSnapshot
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) && len(snaps) < 200 {
		snaps = append(snaps, u2Snap(t, rt))
		time.Sleep(time.Millisecond)
	}
	close(stop)
	wg.Wait()

	if len(snaps) < 10 {
		t.Fatalf("only %d snapshots during churn", len(snaps))
	}
	var lastSeq uint64
	for i, s := range snaps {
		if s.EventSequence < lastSeq {
			t.Fatalf("snapshot %d event sequence regressed: %d < %d", i, s.EventSequence, lastSeq)
		}
		lastSeq = s.EventSequence
		u2CheckSnapshotCoherent(t, s, rt)
	}
}

// u2CheckSnapshotCoherent asserts structural consistency of one snapshot.
func u2CheckSnapshotCoherent(t *testing.T, s RuntimeSnapshot, rt *Runtime) {
	t.Helper()
	fibers := map[FiberID]FiberSnapshot{}
	for i, f := range s.Fibers {
		if _, dup := fibers[f.ID]; dup {
			t.Fatalf("duplicate fiber row %d", f.ID)
		}
		fibers[f.ID] = f
		if i > 0 && !(s.Fibers[i-1].ID < s.Fibers[i].ID) {
			t.Fatalf("fiber rows not sorted at %d", i)
		}
	}
	scopes := map[ScopeID]ScopeSnapshot{}
	for i, sc := range s.Scopes {
		if _, dup := scopes[sc.ID]; dup {
			t.Fatalf("duplicate scope row %d", sc.ID)
		}
		scopes[sc.ID] = sc
		if sc.ID != 0 && sc.ParentID != 0 {
			if _, ok := scopes[sc.ParentID]; !ok {
				t.Fatalf("scope %d parent %d missing", sc.ID, sc.ParentID)
			}
		}
		if i > 0 && !(s.Scopes[i-1].ID < s.Scopes[i].ID) {
			t.Fatalf("scope rows not sorted at %d", i)
		}
	}
	if _, ok := scopes[0]; !ok {
		t.Fatal("root scope missing")
	}
	providerByOwner := map[FiberID]map[ActivationID]bool{}
	for _, p := range s.Providers {
		if _, ok := scopes[p.ScopeID]; !ok {
			t.Fatalf("provider scope %d missing", p.ScopeID)
		}
		if _, ok := providerByOwner[p.OwnerFiberID]; !ok {
			providerByOwner[p.OwnerFiberID] = map[ActivationID]bool{}
		}
		providerByOwner[p.OwnerFiberID][p.OwnerActivationID] = true
		if f, ok := fibers[p.OwnerFiberID]; ok {
			if f.ScopeID != p.ScopeID {
				t.Fatalf("provider owner %d scope %d != record scope %d", p.OwnerFiberID, f.ScopeID, p.ScopeID)
			}
		} else if !u2FiberRegistered(rt, p.OwnerFiberID) {
			t.Fatalf("provider owner %d not registered", p.OwnerFiberID)
		}
	}
	for _, f := range s.Fibers {
		if _, ok := scopes[f.ScopeID]; !ok {
			t.Fatalf("fiber %d scope %d missing", f.ID, f.ScopeID)
		}
		for _, cid := range f.ChildFiberIDs {
			if _, ok := fibers[cid]; !ok && !u2FiberRegistered(rt, cid) {
				t.Fatalf("fiber %d child %d not registered", f.ID, cid)
			}
		}
		for _, d := range f.Dependencies {
			if d.ConsumerFiberID != f.ID {
				t.Fatalf("dependency consumer %d != fiber %d", d.ConsumerFiberID, f.ID)
			}
			switch d.Status {
			case DependencySatisfied:
				if !providerByOwner[d.ProviderFiberID][d.ProviderActivationID] {
					t.Fatalf("satisfied dep %s on %d/%d without provider row", d.Key, d.ProviderFiberID, d.ProviderActivationID)
				}
			case DependencyWaiting:
				if d.ProviderFiberID != 0 || d.ProviderActivationID != 0 {
					t.Fatalf("waiting dep has provider fields: %+v", d)
				}
			}
		}
		for _, e := range f.Effects {
			if e.OwnerFiberID != f.ID || e.ActivationID != f.ActivationID {
				t.Fatalf("effect %+v not owned by fiber row %+v", e, f)
			}
			switch e.State {
			case "Installing", "Committed", "Undoing", "Undone":
			default:
				t.Fatalf("effect %+v has invalid state %q", e, e.State)
			}
		}
	}
}

func u2FiberRegistered(rt *Runtime, id FiberID) bool {
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	_, ok := rt.fibers[id]
	return ok
}

// Snapshot after Runtime.Close fails with ErrRuntimeClosed.
func TestUI02SnapshotAfterClose(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(&u2Noop{name: "n"})
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(testCtx(t)); err != nil {
		t.Fatal(err)
	}
	if err := rt.Close(context.Background()); err != nil {
		t.Fatal(err)
	}
	if _, err := rt.Snapshot(context.Background()); !errors.Is(err, ErrRuntimeClosed) {
		t.Fatalf("Snapshot after Close = %v, want ErrRuntimeClosed", err)
	}
}

// testCtx is a per-test bounded context for waits.
func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// u2ScopeActivator mounts two sibling explicit scopes (provider + consumer
// each, provider-first for deterministic nearest-wins) plus one empty scope
// consumer that must resolve through the parent chain.
type u2ScopeActivator struct {
	key      Key[string]
	chA, chB chan *Fiber
	chC      chan *Fiber
}

func (c *u2ScopeActivator) Name() string          { return "u2-scope-activator" }
func (c *u2ScopeActivator) Inject() []Dependency  { return nil }
func (c *u2ScopeActivator) Provide() []Capability { return nil }
func (c *u2ScopeActivator) Apply(ctx *Context) (Cleanup, error) {
	hostA := &u2ScopeHost{name: "scope-A", key: c.key, tag: "A", ch: c.chA}
	if _, err := ctx.Child(hostA, WithScope()); err != nil {
		return nil, err
	}
	hostB := &u2ScopeHost{name: "scope-B", key: c.key, tag: "B", ch: c.chB}
	if _, err := ctx.Child(hostB, WithScope()); err != nil {
		return nil, err
	}
	empty, err := ctx.Child(&u2C{name: "scope-C-consumer", key: c.key}, WithScope())
	if err != nil {
		return nil, err
	}
	c.chC <- empty
	return nil, nil
}

// u2ScopeHost mounts one provider and, once it is Active, one consumer inside
// the scope realm the host was mounted into.
type u2ScopeHost struct {
	name string
	key  Key[string]
	tag  string
	ch   chan *Fiber
}

func (c *u2ScopeHost) Name() string          { return c.name }
func (c *u2ScopeHost) Inject() []Dependency  { return nil }
func (c *u2ScopeHost) Provide() []Capability { return nil }
func (c *u2ScopeHost) Apply(ctx *Context) (Cleanup, error) {
	p, err := ctx.Child(&u2P{name: c.name + ":provider", key: c.key, tag: c.tag})
	if err != nil {
		return nil, err
	}
	if err := p.Ready(ctx.Context()); err != nil {
		return nil, err
	}
	x, err := ctx.Child(&u2C{name: c.name + ":consumer", key: c.key})
	if err != nil {
		return nil, err
	}
	c.ch <- p
	c.ch <- x
	return nil, nil
}
