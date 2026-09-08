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
// Executable invariants (Phase 5, preservation).
// Assumptions: finite fibers/effects; cooperative Apply/Cleanup; acyclic
// declared dependency graph; Runtime-managed effects only.
// ---------------------------------------------------------------------------

func walkRealms(root *realm, visit func(*realm)) {
	seen := map[*realm]bool{}
	var walk func(r *realm)
	walk = func(r *realm) {
		if r == nil || seen[r] {
			return
		}
		seen[r] = true
		visit(r)
		// children realms are only reachable through fibers; iterate fibers
		// elsewhere. Here we only walk parent links upward from fibers.
	}
	_ = walk
	_ = root
}

// checkPreservationInvariants asserts, at one observation point:
//  1. ownership tree is valid (parent/children consistent, acyclic);
//  2. every provider record resolves to a live owner activation;
//  3. every Active Fiber's declared deps are satisfiable on its realm path.
func (rt *Runtime) checkPreservationInvariants() error {
	rt.mu.RLock()
	fibers := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		fibers = append(fibers, f)
	}
	rt.mu.RUnlock()

	// Ownership: parent links consistent and acyclic.
	parentOf := make(map[FiberID]FiberID)
	for _, f := range fibers {
		if f.parent != nil {
			parentOf[f.id] = f.parent.id
			f.parent.mu.RLock()
			_, ok := f.parent.children[f.id]
			f.parent.mu.RUnlock()
			if !ok {
				return fmt.Errorf("invariant: fiber %d lists parent %d but parent does not own it", f.id, f.parent.id)
			}
		}
		// acyclic via walk up
		cur := f.parent
		steps := 0
		for cur != nil {
			if cur.id == f.id || steps > len(fibers)+1 {
				return fmt.Errorf("invariant: ownership cycle at fiber %d", f.id)
			}
			cur = cur.parent
			steps++
		}
	}

	// Provider records: owner fiber + activation live.
	recs := rt.collectProviderRecords()
	for _, rec := range recs {
		owner := rt.lookupFiber(rec.identity.FiberID)
		if owner == nil {
			return fmt.Errorf("invariant: provider %s owned by missing fiber %d", rec.key, rec.identity.FiberID)
		}
		owner.mu.RLock()
		// A provider record is legitimately present while its owner is Loading
		// (registered mid-Apply), Active, or Unloading that same activation (the
		// inverse has not removed the record yet). It must never outlive the
		// activation.
		live := (owner.state == StateLoading || owner.state == StateActive || owner.state == StateUnloading) &&
			owner.activation != nil && owner.activation.id == rec.identity.ActivationID
		owner.mu.RUnlock()
		if !live {
			return fmt.Errorf("invariant: provider %s owner not live (state %v)", rec.key, owner.State())
		}
	}

	// Active consumers have all declared deps satisfiable on their realm path.
	for _, f := range fibers {
		f.mu.RLock()
		st := f.state
		f.mu.RUnlock()
		if st != StateActive {
			continue
		}
		for _, dep := range f.inject {
			if _, ok := rt.orch.resolveDependency(f, dep.Key); !ok {
				return fmt.Errorf("invariant: Active fiber %d dependency %s unsatisfiable", f.id, dep.Key)
			}
		}
	}
	return nil
}

func (rt *Runtime) collectProviderRecords() []*providerRecord {
	var out []*providerRecord
	for _, f := range rt.fibersSnapshot() {
		if f.realm == nil {
			continue
		}
		out = append(out, f.realm.recordsOwnedBy(ProviderIdentity{FiberID: f.id, ActivationID: 0})...)
	}
	// recordsOwnedBy by (fiber, any activation): query each activation of live
	// fibers is complex; simpler: walk each live fiber's realm own map and take
	// every record whose identity.FiberID belongs to a mounted fiber.
	ids := map[FiberID]bool{}
	for _, f := range rt.fibersSnapshot() {
		ids[f.id] = true
	}
	seenKey := map[*realm]bool{}
	for _, f := range rt.fibersSnapshot() {
		r := f.realm
		for r != nil && !seenKey[r] {
			seenKey[r] = true
			r.mu.RLock()
			for _, rec := range r.own {
				if ids[rec.identity.FiberID] {
					out = append(out, rec)
				}
			}
			r.mu.RUnlock()
			r = r.parent
		}
	}
	return out
}

func (rt *Runtime) fibersSnapshot() []*Fiber {
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	out := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		out = append(out, f)
	}
	return out
}

func (rt *Runtime) lookupFiber(id FiberID) *Fiber {
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	return rt.fibers[id]
}

// checkOnOrch runs the preservation-invariant check synchronously inside the
// orchestrator decision domain, so the sampler can never observe a
// mid-transition window between provider retirement and consumer withdrawal.
func (rt *Runtime) checkOnOrch(t *testing.T) error {
	t.Helper()
	var err error
	onOrchestrator(t, rt, func(o *orchestrator) {
		err = rt.checkPreservationInvariants()
	})
	return err
}

// ---------------------------------------------------------------------------
// Minimal components for schedule generation (white-box).
// ---------------------------------------------------------------------------

type invProvider struct {
	key CapabilityKey
	tag string
}

func (c *invProvider) Name() string          { return "inv-provider:" + c.tag }
func (c *invProvider) Inject() []Dependency  { return nil }
func (c *invProvider) Provide() []Capability { return []Capability{c.key} }
func (c *invProvider) Apply(ctx *Context) (Cleanup, error) {
	return nil, ctx.provideCap(c.key, c.tag)
}

type invConsumer struct {
	key CapabilityKey
}

func (c *invConsumer) Name() string          { return "inv-consumer" }
func (c *invConsumer) Inject() []Dependency  { return []Dependency{{Key: c.key}} }
func (c *invConsumer) Provide() []Capability { return nil }
func (c *invConsumer) Apply(ctx *Context) (Cleanup, error) {
	_, ok := ctx.realm.lookupOwn(c.key)
	if !ok {
		return nil, ErrDependencyMissing
	}
	return nil, nil
}

// TestProofPreservationDeterministicScenario — scripted composition with
// invariant checks after every quiescence point.
func TestProofPreservationDeterministicScenario(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(contextOf(t))
	key := NewKey[string]("proof.det").Capability()

	prov := rt.mustLoad(t, &invProvider{key: key, tag: "p1"})
	cons1 := rt.mustLoad(t, &invConsumer{key: key})
	cons2 := rt.mustLoad(t, &invConsumer{key: key})
	waitActiveList(t, prov, cons1, cons2)
	if err := rt.checkOnOrch(t); err != nil {
		t.Fatalf("after mount: %v", err)
	}

	// Provider replacement: withdraw old, activate new.
	if err := prov.Dispose(); err != nil {
		t.Fatal(err)
	}
	waitGoneList(t, prov)
	waitPendingList(t, cons1, cons2)
	prov2 := rt.mustLoad(t, &invProvider{key: key, tag: "p2"})
	waitActiveList(t, prov2, cons1, cons2)
	if err := rt.checkOnOrch(t); err != nil {
		t.Fatalf("after replacement: %v", err)
	}

	// Tear everything down.
	for _, f := range []*Fiber{prov2, cons1, cons2} {
		_ = f.Dispose()
	}
	for _, f := range []*Fiber{prov2, cons1, cons2} {
		waitGoneList(t, f)
	}
	if err := rt.checkOnOrch(t); err != nil {
		t.Fatalf("after teardown: %v", err)
	}
}

func contextOf(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func (rt *Runtime) mustLoad(t *testing.T, c Component) *Fiber {
	t.Helper()
	f, err := rt.Load(c)
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	return f
}

func waitActiveList(t *testing.T, fs ...*Fiber) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for _, f := range fs {
		for f.State() != StateActive && time.Now().Before(deadline) {
			time.Sleep(time.Millisecond)
		}
		if f.State() != StateActive {
			t.Fatalf("%s not Active: %v", f.Name(), f.State())
		}
	}
}

func waitGoneList(t *testing.T, f *Fiber) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for f.State() != StateGone && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if f.State() != StateGone {
		t.Fatalf("%s not Gone: %v", f.Name(), f.State())
	}
}

func waitPendingList(t *testing.T, fs ...*Fiber) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for _, f := range fs {
		for f.State() != StatePending && time.Now().Before(deadline) {
			time.Sleep(time.Millisecond)
		}
		if f.State() != StatePending {
			t.Fatalf("%s not Pending: %v", f.Name(), f.State())
		}
	}
}

// TestProofPreservationRandomSchedules — bounded random schedules with
// invariant checks after every step; records the seed on failure.
func TestProofPreservationRandomSchedules(t *testing.T) {
	seeds := []int64{1, 7, 42, 2026}
	keys := []CapabilityKey{
		NewKey[string]("proof.a").Capability(),
		NewKey[string]("proof.b").Capability(),
	}
	for _, seed := range seeds {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rt, err := New()
			if err != nil {
				t.Fatal(err)
			}
			defer rt.Close(contextOf(t))
			rng := rand.New(rand.NewSource(seed))
			var live []*Fiber
			for step := 0; step < 40; step++ {
				key := keys[rng.Intn(len(keys))]
				switch rng.Intn(3) {
				case 0:
					f, lerr := rt.Load(&invProvider{key: key, tag: fmt.Sprintf("s%d", step)})
					if lerr == nil {
						live = append(live, f)
					}
				case 1:
					f, lerr := rt.Load(&invConsumer{key: key})
					if lerr == nil {
						live = append(live, f)
					}
				default:
					if len(live) > 0 {
						i := rng.Intn(len(live))
						f := live[i]
						live = append(live[:i], live[i+1:]...)
						_ = f.Dispose()
					}
				}
				// Converge quickly before checking invariants.
				rt.waitQuiesce()
				if err := rt.checkOnOrch(t); err != nil {
					t.Fatalf("seed %d step %d: %v", seed, step, err)
				}
			}
			for _, f := range live {
				_ = f.Dispose()
			}
			for _, f := range live {
				waitGoneList(t, f)
			}
			if err := rt.checkOnOrch(t); err != nil {
				t.Fatalf("seed %d teardown: %v", seed, err)
			}
		})
	}
}

// waitQuiesce spins until the multiset of fiber states is stable across
// consecutive samples with no transient (Loading/Unloading) state. Initial
// Pending fibers settle as queued mount commands are processed, so a snapshot
// is only taken once the system has actually converged.
func (rt *Runtime) waitQuiesce() {
	deadline := time.Now().Add(10 * time.Second)
	sig := func() string {
		var s []string
		for _, f := range rt.fibersSnapshot() {
			s = append(s, fmt.Sprintf("%d:%v", f.id, f.State()))
		}
		sort.Strings(s)
		return fmt.Sprint(s)
	}
	last := ""
	stable := 0
	for time.Now().Before(deadline) {
		busy := false
		cur := sig()
		for _, f := range rt.fibersSnapshot() {
			st := f.State()
			if st == StateLoading || st == StateUnloading {
				busy = true
				break
			}
		}
		if !busy && cur == last {
			stable++
			if stable >= 3 {
				return
			}
		} else {
			stable = 0
			last = cur
		}
		time.Sleep(time.Millisecond)
	}
}

// ---------------------------------------------------------------------------
// Fuzz smoke (CI: go test -fuzz=FuzzPreservation -fuzztime=5s)
// ---------------------------------------------------------------------------

func FuzzPreservation(f *testing.F) {
	f.Add([]byte{1, 2, 3, 4})
	keys := []CapabilityKey{
		NewKey[string]("fuzz.a").Capability(),
		NewKey[string]("fuzz.b").Capability(),
	}
	f.Fuzz(func(t *testing.T, data []byte) {
		rt, err := New()
		if err != nil {
			t.Skip()
		}
		defer rt.Close(contextOf(t))
		rng := rand.New(rand.NewSource(int64(len(data))*2654435761 + 1))
		var live []*Fiber
		for step := 0; step < 24; step++ {
			key := keys[(len(data)+step)%len(keys)]
			switch rng.Intn(3) {
			case 0:
				if f, lerr := rt.Load(&invProvider{key: key, tag: "f"}); lerr == nil {
					live = append(live, f)
				}
			case 1:
				if f, lerr := rt.Load(&invConsumer{key: key}); lerr == nil {
					live = append(live, f)
				}
			default:
				if len(live) > 0 {
					i := rng.Intn(len(live))
					_ = live[i].Dispose()
					live = append(live[:i], live[i+1:]...)
				}
			}
			rt.waitQuiesce()
			if err := rt.checkOnOrch(t); err != nil {
				t.Fatalf("invariant: %v", err)
			}
		}
		for _, f := range live {
			_ = f.Dispose()
		}
	})
}
