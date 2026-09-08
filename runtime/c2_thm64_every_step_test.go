package runtime

import (
	"context"
	"fmt"
	"testing"
	"time"
)

// C-2 (Thm64 every-step): registry well-formedness at every deterministic
// lifecycle step of a single independent fiber P.
//
//	Load(P) -> [ApplyDone parked] -> Execute(ApplyDone) -> Active ->
//	Dispose() -> [UnwindDone parked] -> Execute(UnwindDone) -> Gone -> Close
//
// checkThm64 is the preservation oracle: it must hold after EVERY driver step,
// including the transitional Loading/Unloading states where semanticQuiescent
// reports non-quiescence. It never waits, sleeps, or requires quiescence.
//
// Thm64 is grounded in the recorded definitions, not in implementation habits:
//   - docs/review/GOCORDIS — Paper-Level Theorem Verification Specification
//     v0.1 §9.1: P1 parent validity, P2 provider uniqueness (identity includes
//     the activation generation), P3 dependency validity (Active fiber),
//     P4 provider state validity, P5 snapshot consistency.
//   - docs/review/GOCORDIS — Theorem Verification Infrastructure Spec v0.2
//     §25 (per-step CheckWellFormed): provider uniqueness, provider identity
//     (an old activation inverse must never delete a newer activation's
//     provider), active dependency -> valid provider generation, no dangling
//     graph edges, ownership validity.
//
// Oracle arms (Paper-required | status):
//
//	A Fiber registry     | map identity/integrity is Implementation-only; kept
//	                      | as a precondition so corrupt registries cannot mask
//	                      | the paper arms below.
//	B Ownership          | P1 parent-exists + v0.2 child-owner-exists: Yes.
//	C Activation         | state/activation coherence is Implementation-only:
//	                      | it is the identity-model precondition that makes D/E
//	                      | reads meaningful (single ActivationID model, §10).
//	D Provider registry  | P2 uniqueness per resolution realm + P4/v0.2
//	                      | record-owner generation coherence: Yes. retiring is
//	                      | a legal transitional state, never "retiring ==
//	                      | invalid": a retiring record must still carry the
//	                      | owning generation until its inverse removes it.
//	E Dependency/graph   | P3 (Active deps satisfied), P5 (Active snapshot ==
//	                      | resolved), v0.2 dependency validity (snapshot ->
//	                      | existing record, same generation, no dangling
//	                      | edges): Yes.
//	F Other              | effect stack = Thm68, withdrawal ordering = Thm70,
//	                      | quiescence/progress = Thm73: explicitly excluded.
//
// Explicitly NOT in Thm64 (boundary discipline): provider-ready-before-Loading
// and consumer-before-provider withdrawal ordering (Thm70), runtime idleness
// (Thm73), effect LIFO/exactness (Thm68).

type c2FiberSnap struct {
	f        *Fiber
	id       FiberID
	state    FiberState
	realm    *realm
	parent   *Fiber
	act      *activation
	actID    ActivationID
	childIDs []FiberID
	inject   []Dependency
	deps     []DependencySnapshot
}

type c2RecordSnap struct {
	identity ProviderIdentity
	retiring bool
}

type c2RealmSnap struct {
	byKey map[CapabilityKey]c2RecordSnap
}

// checkThm64 reports the first registry well-formedness violation, or nil.
//
// It snapshots semantic state under the established lock order (rt.mu -> f.mu;
// realm.mu taken alone afterwards), mirroring observe/thm64CheckP, and must be
// invoked at driver step boundaries where the orchestrator is idle (the C-2
// protocol: wait for the target state after Execute). The provider->consumer
// edge index (o.graph) is orchestrator-owned and read only at such boundaries;
// a concurrent multi-fiber harness (C-3) should run checkThm64 on the
// orchestrator via a probe instead.
func checkThm64(rt *Runtime) error {
	// --- fiber registry snapshot (rt.mu -> f.mu) ---
	type regEntry struct {
		key FiberID
		f   *Fiber
	}
	rt.mu.RLock()
	entries := make([]regEntry, 0, len(rt.fibers))
	byID := make(map[FiberID]*Fiber, len(rt.fibers))
	for key, f := range rt.fibers {
		entries = append(entries, regEntry{key, f})
		if f != nil {
			byID[f.id] = f
		}
	}
	rt.mu.RUnlock()

	fsnaps := make([]*c2FiberSnap, 0, len(entries))
	fsnapByFiber := make(map[*Fiber]*c2FiberSnap, len(entries))
	for _, e := range entries {
		if e.f == nil {
			return fmt.Errorf("Thm64 registry: nil fiber under id %v", e.key)
		}
		if e.f.id != e.key {
			return fmt.Errorf("Thm64 registry: fiber %v registered under key %v", e.f.id, e.key)
		}
		if e.f.id == 0 {
			return fmt.Errorf("Thm64 registry: fiber with invalid id 0")
		}
		f := e.f
		s := &c2FiberSnap{f: f, id: f.id}
		f.mu.RLock()
		s.state = f.state
		s.realm = f.realm
		s.parent = f.parent
		if act := f.activation; act != nil {
			s.act = act
			s.actID = act.id
		}
		s.childIDs = make([]FiberID, 0, len(f.children))
		for cid := range f.children {
			s.childIDs = append(s.childIDs, cid)
		}
		s.inject = append([]Dependency(nil), f.inject...)
		if s.act != nil {
			s.deps = append([]DependencySnapshot(nil), s.act.deps...)
		}
		f.mu.RUnlock()
		fsnaps = append(fsnaps, s)
		fsnapByFiber[f] = s
	}

	// --- realm snapshot (realm.mu, taken alone after fiber locks are free) ---
	realmSnaps := make(map[*realm]*c2RealmSnap)
	snapRealm := func(r *realm) *c2RealmSnap {
		if r == nil {
			return nil
		}
		if s, ok := realmSnaps[r]; ok {
			return s
		}
		s := &c2RealmSnap{byKey: make(map[CapabilityKey]c2RecordSnap)}
		realmSnaps[r] = s
		r.mu.RLock()
		for k, rec := range r.own {
			s.byKey[k] = c2RecordSnap{identity: rec.identity, retiring: rec.retiring}
		}
		r.mu.RUnlock()
		return s
	}
	snapRealm(rt.rootRealm)
	for _, s := range fsnaps {
		for r := s.realm; r != nil; r = r.parent {
			snapRealm(r)
		}
	}

	// --- provider->consumer edge index snapshot (orchestrator-owned; read at
	// idle step boundaries only) ---
	edgesByID := make(map[ProviderIdentity]map[*Fiber]struct{})
	for id, set := range rt.orch.graph {
		m := make(map[*Fiber]struct{}, len(set))
		for c := range set {
			m[c] = struct{}{}
		}
		edgesByID[id] = m
	}

	// A registry integrity (precondition).
	for _, s := range fsnaps {
		if s.realm == nil {
			return fmt.Errorf("Thm64 fiber %d (%s): nil realm", s.id, s.f.Name())
		}
	}

	// B ownership: P1 parent validity + v0.2 child-owner existence.
	for _, s := range fsnaps {
		f := s.f
		if s.parent != nil {
			if byID[s.parent.id] == nil {
				return fmt.Errorf("Thm64 P1 parent validity: fiber %d (%s) parent %d not registered", f.id, f.Name(), s.parent.id)
			}
			if s.parent.id == f.id {
				return fmt.Errorf("Thm64 P1 parent validity: fiber %d (%s) is its own parent", f.id, f.Name())
			}
		}
		for _, cid := range s.childIDs {
			if byID[cid] == nil {
				return fmt.Errorf("Thm64 ownership validity: fiber %d (%s) child %d not registered", f.id, f.Name(), cid)
			}
		}
	}

	// C activation/state coherence (identity-model precondition).
	for _, s := range fsnaps {
		f := s.f
		switch {
		case s.act != nil:
			if s.state != StateLoading && s.state != StateActive && s.state != StateUnloading {
				return fmt.Errorf("Thm64 activation coherence: fiber %d (%s) live activation in state %v", f.id, f.Name(), s.state)
			}
			if s.actID == 0 {
				return fmt.Errorf("Thm64 activation coherence: fiber %d (%s) activation id 0", f.id, f.Name())
			}
			if s.act.fiber != f {
				return fmt.Errorf("Thm64 activation coherence: fiber %d (%s) activation owner mismatch", f.id, f.Name())
			}
			if s.act.ctx == nil {
				return fmt.Errorf("Thm64 activation coherence: fiber %d (%s) activation has nil context", f.id, f.Name())
			}
		case s.state == StateLoading || s.state == StateActive:
			return fmt.Errorf("Thm64 activation coherence: fiber %d (%s) state %v without live activation", f.id, f.Name(), s.state)
		}
	}

	// D provider registry: P2 (at most one record per capability per realm is
	// structural: realm.own is a map) + P4/v0.2 record-owner generation
	// coherence. A retiring record is legal; it must still name the live
	// owning generation (its inverse removes it physically on unwind).
	for _, rsnap := range realmSnaps {
		for key, rec := range rsnap.byKey {
			id := rec.identity
			if id.FiberID == 0 || id.ActivationID == 0 {
				return fmt.Errorf("Thm64 P2 provider registry: record %s has invalid identity %d/%d", key, id.FiberID, id.ActivationID)
			}
			owner := byID[id.FiberID]
			if owner == nil {
				return fmt.Errorf("Thm64 P2/P4 provider registry: record %s owner fiber %d not registered", key, id.FiberID)
			}
			osnap := fsnapByFiber[owner]
			if osnap == nil || osnap.act == nil || osnap.actID != id.ActivationID {
				return fmt.Errorf("Thm64 P2/P4 provider registry: record %s generation %d/%d no longer matches owner %d activation %v",
					key, id.FiberID, id.ActivationID, id.FiberID, actOrNone(osnap))
			}
		}
	}

	// E dependency registry / graph.
	//
	// E3 (v0.2 dependency validity): every dependency snapshot of a live
	// activation corresponds to an existing provider record in the owner
	// fiber's realm, same key and same generation. Retiring is allowed: a
	// consumer may legitimately hold a snapshot of a provider that is
	// withdrawing (consumer-first unload, Thm70) while the record still exists.
	for _, s := range fsnaps {
		if s.act == nil {
			continue
		}
		for _, dep := range s.deps {
			owner := byID[dep.Provider.FiberID]
			if owner == nil {
				return fmt.Errorf("Thm64 dependency validity: fiber %d (%s) dep %s references unregistered provider fiber %d",
					s.id, s.f.Name(), dep.Key, dep.Provider.FiberID)
			}
			osnap := fsnapByFiber[owner]
			if osnap == nil {
				return fmt.Errorf("Thm64 dependency validity: fiber %d (%s) dep %s provider fiber %d missing snapshot",
					s.id, s.f.Name(), dep.Key, dep.Provider.FiberID)
			}
			orealm := realmSnaps[osnap.realm]
			if orealm == nil {
				return fmt.Errorf("Thm64 dependency validity: fiber %d (%s) dep %s provider realm missing",
					s.id, s.f.Name(), dep.Key)
			}
			rec, ok := orealm.byKey[dep.Key]
			if !ok {
				return fmt.Errorf("Thm64 dependency validity: fiber %d (%s) dep %s snapshot %d/%d has no registry record (dangling)",
					s.id, s.f.Name(), dep.Key, dep.Provider.FiberID, dep.Provider.ActivationID)
			}
			if rec.identity != dep.Provider {
				return fmt.Errorf("Thm64 dependency validity: fiber %d (%s) dep %s snapshot generation %d/%d != registry generation %d/%d",
					s.id, s.f.Name(), dep.Key, dep.Provider.FiberID, dep.Provider.ActivationID,
					rec.identity.FiberID, rec.identity.ActivationID)
			}
		}
	}

	// E4 (v0.2 graph validity): every edge names a live provider generation and
	// a registered consumer whose live activation snapshots that identity, and
	// every snapshot of a live activation has its provider->consumer edge.
	for id, consumers := range edgesByID {
		owner := byID[id.FiberID]
		if owner == nil {
			return fmt.Errorf("Thm64 graph validity: edge for unregistered provider %d/%d", id.FiberID, id.ActivationID)
		}
		osnap := fsnapByFiber[owner]
		if osnap == nil || osnap.act == nil || osnap.actID != id.ActivationID {
			return fmt.Errorf("Thm64 graph validity: edge for ended provider generation %d/%d", id.FiberID, id.ActivationID)
		}
		for c := range consumers {
			csnap := fsnapByFiber[c]
			if csnap == nil {
				return fmt.Errorf("Thm64 graph validity: edge %d/%d -> consumer not registered", id.FiberID, id.ActivationID)
			}
			if csnap.act == nil {
				return fmt.Errorf("Thm64 graph validity: edge %d/%d -> fiber %d (%s) has no live activation",
					id.FiberID, id.ActivationID, csnap.id, csnap.f.Name())
			}
			if !c2SnapRefs(csnap.deps, id) {
				return fmt.Errorf("Thm64 graph validity: edge %d/%d -> fiber %d (%s) has no matching dependency snapshot",
					id.FiberID, id.ActivationID, csnap.id, csnap.f.Name())
			}
		}
	}
	for _, s := range fsnaps {
		if s.act == nil {
			continue
		}
		for _, dep := range s.deps {
			if _, ok := edgesByID[dep.Provider]; !ok {
				return fmt.Errorf("Thm64 graph validity: fiber %d (%s) dep %s missing provider->consumer edge %d/%d",
					s.id, s.f.Name(), dep.Key, dep.Provider.FiberID, dep.Provider.ActivationID)
			}
			if _, ok := edgesByID[dep.Provider][s.f]; !ok {
				return fmt.Errorf("Thm64 graph validity: fiber %d (%s) dep %s missing edge from provider %d/%d",
					s.id, s.f.Name(), dep.Key, dep.Provider.FiberID, dep.Provider.ActivationID)
			}
		}
	}

	// E1 (P3) + E2 (P5): an Active fiber's declared dependencies resolve to an
	// Active provider on its realm path, and the resolved identity equals the
	// captured snapshot. Loading/Unloading dependency progress is Thm70
	// ordering, not Thm64, so the check is state-conditional on Active only.
	resolve := func(s *c2FiberSnap, key CapabilityKey) (ProviderIdentity, bool) {
		for r := s.realm; r != nil; r = r.parent {
			rs := realmSnaps[r]
			if rs == nil {
				continue
			}
			rec, ok := rs.byKey[key]
			if !ok {
				continue
			}
			// The nearest record decides, retiring blocks re-binding.
			if rec.retiring {
				return ProviderIdentity{}, false
			}
			owner := byID[rec.identity.FiberID]
			os := fsnapByFiber[owner]
			if owner == nil || os == nil || os.state != StateActive || os.act == nil || os.actID != rec.identity.ActivationID {
				return ProviderIdentity{}, false
			}
			return rec.identity, true
		}
		return ProviderIdentity{}, false
	}
	for _, s := range fsnaps {
		if s.state != StateActive || s.act == nil {
			continue
		}
		snapByKey := make(map[CapabilityKey]ProviderIdentity, len(s.deps))
		for _, dep := range s.deps {
			snapByKey[dep.Key] = dep.Provider
		}
		for _, dep := range s.inject {
			resolved, ok := resolve(s, dep.Key)
			if !ok {
				return fmt.Errorf("Thm64 P3 dependency validity: Active fiber %d (%s) dep %s unsatisfied",
					s.id, s.f.Name(), dep.Key)
			}
			if snap, has := snapByKey[dep.Key]; !has || snap != resolved {
				return fmt.Errorf("Thm64 P5 snapshot consistency: Active fiber %d (%s) dep %s resolved %d/%d != snapshot %+v",
					s.id, s.f.Name(), dep.Key, resolved.FiberID, resolved.ActivationID, snapByKey[dep.Key])
			}
		}
	}

	return nil
}

// actOrNone renders a fiber snapshot's live activation id for diagnostics.
func actOrNone(s *c2FiberSnap) any {
	if s == nil || s.act == nil {
		return nil
	}
	return s.actID
}

// c2SnapRefs reports whether any dependency snapshot references identity id.
func c2SnapRefs(deps []DependencySnapshot, id ProviderIdentity) bool {
	for _, d := range deps {
		if d.Provider == id {
			return true
		}
	}
	return false
}

func c2Wait(ms int, cond func() bool) bool {
	deadline := time.Now().Add(time.Duration(ms) * time.Millisecond)
	for time.Now().Before(deadline) {
		if cond() {
			return true
		}
		time.Sleep(time.Millisecond)
	}
	return false
}

func c2Step(t *testing.T, step int, action string, f *Fiber, rt *Runtime) {
	t.Helper()
	t.Logf("c2 step=%d action=%s fiber=%s state=%v activation=%d pending=%d",
		step, action, f.Name(), f.State(), actIDOf(f), rt.detPending())
}

func c2Thm64Check(t *testing.T, rt *Runtime, when string) {
	t.Helper()
	if err := checkThm64(rt); err != nil {
		t.Fatalf("Thm64 every-step violated at %s: %v", when, err)
	}
}

// TestC2Thm64EveryStepSingleFiber drives the full single-fiber lifecycle under
// the deterministic driver and runs checkThm64 after every step, covering both
// detStepApplyDone and detStepUnwindDone.
func TestC2Thm64EveryStepSingleFiber(t *testing.T) {
	rt := detNew(t)
	c2Thm64Check(t, rt, "initial(empty registry)")

	step := 0
	f, err := rt.Load(&thm73Comp{name: "P"}) // independent single fiber
	if err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && f.State() == StateLoading }) {
		t.Fatalf("ApplyDone never parked after Load; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Load", f, rt)
	en := rt.detEnabledSteps()
	if len(en) != 1 || en[0].Kind != detStepApplyDone || en[0].FiberID != f.ID() {
		t.Fatalf("enabled=%v, want [ApplyDone(P)]", en)
	}
	actID := en[0].ActivationID
	step++

	// Transitional Loading: checkThm64 holds while quiescence does not.
	c2Thm64Check(t, rt, "Loading(ApplyDone parked)")
	if err := semanticQuiescent(rt); err == nil {
		t.Fatal("semanticQuiescent should report transitional Loading (checkThm64 is not a quiescence oracle)")
	}

	if err := rt.detExecute(en[0]); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return f.State() == StateActive }) {
		t.Fatalf("P never Active; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Execute(ApplyDone)", f, rt)
	step++
	c2Thm64Check(t, rt, "Active")
	if err := semanticQuiescent(rt); err != nil {
		t.Fatalf("not quiescent while Active: %v", err)
	}

	// Unmount the fiber (Intent = Unmounted, same decision Close would make)
	// so the UnwindDone completion parks under the deterministic driver.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && f.State() == StateUnloading }) {
		t.Fatalf("UnwindDone never parked after Dispose; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Dispose", f, rt)
	en2 := rt.detEnabledSteps()
	if len(en2) != 1 || en2[0].Kind != detStepUnwindDone || en2[0].FiberID != f.ID() {
		t.Fatalf("enabled=%v, want [UnwindDone(P)]", en2)
	}
	if en2[0].ActivationID != actID {
		t.Fatalf("UnwindDone activation %v != ApplyDone activation %v", en2[0].ActivationID, actID)
	}
	step++

	// Transitional Unloading: checkThm64 holds while quiescence does not.
	c2Thm64Check(t, rt, "Unloading(UnwindDone parked)")
	if err := semanticQuiescent(rt); err == nil {
		t.Fatal("semanticQuiescent should report transitional Unloading (checkThm64 is not a quiescence oracle)")
	}

	if err := rt.detExecute(en2[0]); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return f.State() == StateGone && rt.detPending() == 0 }) {
		t.Fatalf("P never Gone; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Execute(UnwindDone)", f, rt)
	step++
	c2Thm64Check(t, rt, "Gone")
	if err := semanticQuiescent(rt); err != nil {
		t.Fatalf("not quiescent after Gone: %v", err)
	}

	clctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	err = rt.Close(clctx)
	cancel()
	if err != nil {
		t.Fatalf("Close did not complete: state=%v pending=%d err=%v", f.State(), rt.detPending(), err)
	}
	select {
	case <-rt.orch.done:
	default:
		t.Fatalf("orchestrator not done after Close; state=%v pending=%d", f.State(), rt.detPending())
	}
	c2Step(t, step, "Close", f, rt)
	c2Thm64Check(t, rt, "after Close")
	t.Logf("C-2 PASS: checkThm64 held at every driver step (ApplyDone + UnwindDone; steps=%d)", step)
}
