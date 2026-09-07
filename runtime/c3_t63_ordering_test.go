package runtime

import (
	"context"
	"fmt"
	"math/rand/v2"
	"sort"
	"strings"
	"testing"
	"time"
)

// C-3 (T63 Ordering) — activation/dependency-binding-level ordering theorem.
//
// Paper mapping (recorded project authority, NOT derived from the runtime):
//
//	docs/review/GOCORDIS — Paper-Level Theorem Verification Specification v0.1
//	  §16-18: a consumer may only enter Loading/Apply once every required
//	          dependency is satisfied (provider Active); the event oracle
//	          checks that the most recent provider state before
//	          Apply(consumer) is Active.
//	docs/review/GOCORDIS — Theorem Verification Infrastructure Spec v0.2
//	  §23 (activation): Provider Apply ≺ Consumer Apply, and a consumer
//	          never observes an unready dependency during activation.
//	  §24 (withdrawal): C Unwind ≺ P Unwind AND C Cleanup ≺ P Cleanup.
//
// This file upgrades the T63 tests from component-event sequencing to a
// paper-level, activation-identity ordering oracle over the four semantic
// events of the C-3 specification:
//
//	Runtime transition / state            -> recorded T63 semantic event
//	P: Loading -> Active + live record    -> ProviderReady(P/Fiber,Act)
//	C: -> Loading (deps snapshot binds P) -> ConsumerLoading(C -> P)
//	C: Loading -> Active (same snapshot)  -> ConsumerActive(C -> P)
//	P: provided record becomes retiring / -> ProviderWithdrawn(P/Fiber,Act)
//	   P leaves Active (same activation)
//
// Identity model: T63 constrains ACTIVATION ordering, never Fiber ordering.
// Every provider event is bound to ProviderIdentity{FiberID, ActivationID};
// every consumer event carries the provider identity from the consumer
// activation's captured DependencySnapshot — the runtime binding fact source.
// The oracle NEVER re-resolves dependencies through realm.lookup: that would
// verify the test's own lookup instead of the Runtime's binding.
//
// T63-A (establishment), per binding C → P:
//
//	seq(ProviderReady(P)) < seq(ConsumerLoading(C -> P))
//	seq(ConsumerLoading(C -> P)) < seq(ConsumerActive(C -> P))
//
// T63-B (withdrawal), per binding C → P:
//
//	seq(ConsumerActive(C -> P)) < seq(ProviderWithdrawn(P))   (if Active)
//	no ConsumerLoading / ConsumerActive for P after Withdrawn(P)
//
// Provider reload (P/A1 -> P/A2) MUST be two generations: assertions require
// oldProvider.ActivationID != newProvider.ActivationID even though the FiberID
// is identical, and generation-2 consumer bindings must reference P/A2 only.
//
// Explicitly NOT tested here (boundary discipline): final convergence (T66),
// schedule-independent quiescence (T73), registry well-formedness as the T63
// oracle (T59; checkT59 is used only as a per-step diagnostic), exact
// effect unwind (T61), goroutine scheduling fairness, and time.Sleep as a
// theorem step. All ordering is decided by detEnabledSteps/detExecute;
// c2Wait-style parking waits are only async-completion ADMISSION SETUP.
//
// Observation boundary: every observation runs only after an orchestrator
// probe command (c3Boundary) applied, so reads never race an in-flight
// lifecycle command. Events landing on the SAME boundary are ordered
// provider-then-consumer, mirroring the orchestrator's causal order inside a
// single command application (a provider's activation/retirement decisions
// always precede the consumer transitions they unlock); consumer-to-consumer
// order is never load-bearing (C-3 spec §17).

// ---------------------------------------------------------------------------
// Semantic event trace
// ---------------------------------------------------------------------------

type c3EventKind uint8

const (
	c3ProviderReady c3EventKind = iota
	c3ConsumerLoading
	c3ConsumerActive
	c3ProviderWithdrawn
)

func (k c3EventKind) String() string {
	switch k {
	case c3ProviderReady:
		return "ProviderReady"
	case c3ConsumerLoading:
		return "ConsumerLoading"
	case c3ConsumerActive:
		return "ConsumerActive"
	case c3ProviderWithdrawn:
		return "ProviderWithdrawn"
	default:
		return fmt.Sprintf("c3EventKind(%d)", uint8(k))
	}
}

// c3Event is one recorded activation-level semantic event. Seq is assigned by
// the recorder at orchestrator-confirmed boundaries. FiberID+ActivationID is
// the event subject; Provider/Consumer carry the full identities involved
// (see the event table in the header comment).
type c3Event struct {
	Seq          int
	Kind         c3EventKind
	FiberID      FiberID
	ActivationID ActivationID
	Provider     ProviderIdentity // provider events: the provider activation; consumer events: bound provider (from DependencySnapshot)
	Consumer     ProviderIdentity // consumer events: the consumer activation
}

func c3ID(id ProviderIdentity) string {
	return fmt.Sprintf("%d/%d", id.FiberID, id.ActivationID)
}

func (e c3Event) String() string {
	var b strings.Builder
	fmt.Fprintf(&b, "#%d %s fiber=%d activation=%d", e.Seq, e.Kind, e.FiberID, e.ActivationID)
	if e.Provider.FiberID != 0 {
		fmt.Fprintf(&b, " provider=%s", c3ID(e.Provider))
	}
	if e.Consumer.FiberID != 0 {
		fmt.Fprintf(&b, " consumer=%s", c3ID(e.Consumer))
	}
	return b.String()
}

// ---------------------------------------------------------------------------
// Recorder: converts runtime lifecycle state observed at orchestrator
// boundaries into the four semantic events above. The DependencySnapshot of a
// consumer activation is the binding fact source; the provider record
// (identity + retiring) in the realm is the provider fact source.
// ---------------------------------------------------------------------------

type c3FiberObs struct {
	state FiberState
	act   ActivationID
	deps  []DependencySnapshot
}

type c3RecordObs struct {
	present  bool
	retiring bool
	id       ProviderIdentity
}

type c3Recorder struct {
	key       CapabilityKey
	providers []*Fiber
	consumers []*Fiber

	seq    int
	Events []c3Event

	prevFiber map[FiberID]c3FiberObs
	// provReady / provWithdrawn track, per provider activation, whether the
	// recorder has emitted ProviderReady / ProviderWithdrawn.
	provReady     map[ProviderIdentity]bool
	provWithdrawn map[ProviderIdentity]bool
}

func c3NewRecorder(key CapabilityKey) *c3Recorder {
	return &c3Recorder{
		key:           key,
		prevFiber:     make(map[FiberID]c3FiberObs),
		provReady:     make(map[ProviderIdentity]bool),
		provWithdrawn: make(map[ProviderIdentity]bool),
	}
}

func (r *c3Recorder) addProvider(f *Fiber) {
	for _, x := range r.providers {
		if x == f {
			return
		}
	}
	r.providers = append(r.providers, f)
}

func (r *c3Recorder) addConsumer(f *Fiber) {
	for _, x := range r.consumers {
		if x == f {
			return
		}
	}
	r.consumers = append(r.consumers, f)
}

func (r *c3Recorder) provides(fid FiberID) bool {
	for _, p := range r.providers {
		if p.id == fid {
			return true
		}
	}
	return false
}

func (r *c3Recorder) emit(kind c3EventKind, fid FiberID, act ActivationID, prov, cons ProviderIdentity) {
	r.seq++
	r.Events = append(r.Events, c3Event{Seq: r.seq, Kind: kind, FiberID: fid, ActivationID: act, Provider: prov, Consumer: cons})
}

func c3snapFiber(f *Fiber) c3FiberObs {
	f.mu.RLock()
	defer f.mu.RUnlock()
	o := c3FiberObs{state: f.state}
	if act := f.activation; act != nil {
		o.act = act.id
		o.deps = append([]DependencySnapshot(nil), act.deps...)
	}
	return o
}

func c3snapProviderRecord(f *Fiber, key CapabilityKey) c3RecordObs {
	f.realm.mu.RLock()
	defer f.realm.mu.RUnlock()
	rec, ok := f.realm.own[key]
	if !ok {
		return c3RecordObs{}
	}
	return c3RecordObs{present: true, retiring: rec.retiring, id: rec.identity}
}

// emitConsumerLoading records ConsumerLoading(C activation -> bound provider)
// for every scenario-provider dependency in the activation snapshot.
func (r *c3Recorder) emitConsumerLoading(f *Fiber, cur c3FiberObs) {
	for _, d := range cur.deps {
		if !r.provides(d.Provider.FiberID) {
			continue
		}
		r.emit(c3ConsumerLoading, f.id, cur.act, d.Provider, ProviderIdentity{FiberID: f.id, ActivationID: cur.act})
	}
}

func (r *c3Recorder) emitConsumerActive(f *Fiber, cur c3FiberObs) {
	for _, d := range cur.deps {
		if !r.provides(d.Provider.FiberID) {
			continue
		}
		r.emit(c3ConsumerActive, f.id, cur.act, d.Provider, ProviderIdentity{FiberID: f.id, ActivationID: cur.act})
	}
}

// observe diffs the previous boundary snapshot against the current one and
// emits T63 semantic events. It must only be called at an orchestrator
// boundary (after c3Boundary). It returns an error when the observed
// lifecycle contradicts the event model (e.g., a provider Active without a
// live non-retiring record, or a consumer Loading without a scenario-provider
// binding), so model drift fails loudly instead of silently weakening the
// oracle.
func (r *c3Recorder) observe() error {
	provs := append([]*Fiber(nil), r.providers...)
	sort.Slice(provs, func(i, j int) bool { return provs[i].id < provs[j].id })
	cons := append([]*Fiber(nil), r.consumers...)
	sort.Slice(cons, func(i, j int) bool { return cons[i].id < cons[j].id })

	// Providers first: a provider's Ready/Withdrawn transition causally
	// precedes any consumer Loading it unlocks on the same boundary.
	for _, f := range provs {
		cur := c3snapFiber(f)
		prev, seen := r.prevFiber[f.id]
		r.prevFiber[f.id] = cur
		rec := c3snapProviderRecord(f, r.key)

		if cur.act != 0 {
			id := ProviderIdentity{FiberID: f.id, ActivationID: cur.act}
			if seen && prev.state == StateLoading && cur.state == StateActive {
				if !rec.present || rec.id != id || rec.retiring {
					return fmt.Errorf("provider %s (%s) became Active activation %s without a live non-retiring registry record (record present=%v id=%s retiring=%v)",
						f.Name(), f.State(), id.ActivationID, rec.present, c3ID(rec.id), rec.retiring)
				}
				r.emit(c3ProviderReady, f.id, cur.act, id, ProviderIdentity{})
				r.provReady[id] = true
			}
			// ProviderWithdrawn: a previously-Ready activation whose record is
			// now retiring, or whose fiber left Active (Unloading), or whose
			// activation ended (defensive), is withdrawn exactly once.
			if !r.provReady[id] && !seen && cur.state == StateActive {
				// Late attach while the provider is already Active (used by
				// no current scenario; kept for recorder completeness).
				if rec.present && rec.id == id && !rec.retiring {
					r.emit(c3ProviderReady, f.id, cur.act, id, ProviderIdentity{})
					r.provReady[id] = true
				}
			}
			if r.provReady[id] && !r.provWithdrawn[id] {
				withdrawn := false
				switch {
				case rec.present && rec.id == id && rec.retiring:
					withdrawn = true
				case cur.state == StateUnloading:
					withdrawn = true
				}
				if withdrawn {
					r.emit(c3ProviderWithdrawn, f.id, cur.act, id, ProviderIdentity{})
					r.provWithdrawn[id] = true
				}
			}
		}
		// Defensive: a Ready activation whose activation ended between two
		// boundaries without an observed Unloading/retiring.
		if seen && prev.act != 0 && cur.act == 0 {
			pid := ProviderIdentity{FiberID: f.id, ActivationID: prev.act}
			if r.provReady[pid] && !r.provWithdrawn[pid] {
				r.emit(c3ProviderWithdrawn, f.id, prev.act, pid, ProviderIdentity{})
				r.provWithdrawn[pid] = true
			}
		}
	}

	for _, f := range cons {
		cur := c3snapFiber(f)
		prev, seen := r.prevFiber[f.id]
		r.prevFiber[f.id] = cur

		if !seen {
			switch cur.state {
			case StateLoading:
				if cur.act != 0 {
					r.emitConsumerLoading(f, cur)
				}
			case StateActive:
				if cur.act != 0 {
					r.emitConsumerLoading(f, cur)
					r.emitConsumerActive(f, cur)
				}
			}
			continue
		}
		switch {
		case prev.act == 0 && cur.act != 0 && cur.state == StateLoading:
			// Loading entry from Pending or Gone: the binding comes from the
			// activation's DependencySnapshot captured at startActivation.
			if !r.consumerHasScenarioDep(cur) {
				return fmt.Errorf("consumer %s entered Loading activation %s with no dependency snapshot bound to a scenario provider (deps=%+v)",
					f.Name(), cur.act, cur.deps)
			}
			r.emitConsumerLoading(f, cur)
		case prev.act != 0 && prev.act == cur.act && prev.state == StateLoading && cur.state == StateActive:
			r.emitConsumerActive(f, cur)
		}
	}
	return nil
}

func (r *c3Recorder) consumerHasScenarioDep(cur c3FiberObs) bool {
	for _, d := range cur.deps {
		if r.provides(d.Provider.FiberID) {
			return true
		}
	}
	return false
}

// ---------------------------------------------------------------------------
// Ledger / diagnostics
// ---------------------------------------------------------------------------

func c3Ledger(events []c3Event) string {
	var b strings.Builder
	for _, e := range events {
		b.WriteString("  ")
		b.WriteString(e.String())
		b.WriteString("\n")
	}
	return b.String()
}

func c3Counts(events []c3Event) (ready, loading, active, withdrawn int) {
	for _, e := range events {
		switch e.Kind {
		case c3ProviderReady:
			ready++
		case c3ConsumerLoading:
			loading++
		case c3ConsumerActive:
			active++
		case c3ProviderWithdrawn:
			withdrawn++
		}
	}
	return
}

// ---------------------------------------------------------------------------
// Pure-function T63 oracle (no Runtime access, no waiting). The oracle checks
// the recorded trace only — it is the theorem evidence, and it is agnostic to
// how many bindings a scenario exercised.
// ---------------------------------------------------------------------------

func c3ExpectProviderEvent(events []c3Event, kind c3EventKind, id ProviderIdentity) (c3Event, error) {
	for _, e := range events {
		if e.Kind == kind && e.Provider == id && e.FiberID == id.FiberID && e.ActivationID == id.ActivationID {
			return e, nil
		}
	}
	return c3Event{}, fmt.Errorf("missing %s(%s); ledger:\n%s", kind, c3ID(id), c3Ledger(events))
}

func c3ExpectConsumerBindingEvent(events []c3Event, kind c3EventKind, cid FiberID, cact ActivationID, prov ProviderIdentity) (c3Event, error) {
	for _, e := range events {
		if e.Kind == kind && e.FiberID == cid && e.ActivationID == cact && e.Consumer == (ProviderIdentity{FiberID: cid, ActivationID: cact}) && e.Provider == prov {
			return e, nil
		}
	}
	return c3Event{}, fmt.Errorf("missing %s(C %d/%d -> P %s); ledger:\n%s", kind, cid, cact, c3ID(prov), c3Ledger(events))
}

// c3CheckT63 validates every binding in the ledger:
//
//	ProviderReady(P) ≺ ConsumerLoading(C->P) ≺ ConsumerActive(C->P)
//	ConsumerActive(C->P) ≺ ProviderWithdrawn(P)  (when both exist)
//	no ConsumerLoading / ConsumerActive after ProviderWithdrawn(P)
//	ProviderWithdrawn(P) only after ProviderReady(P)
//
// All comparisons are STRICT on Seq. Identities are activation-level
// (FiberID + ActivationID); the oracle never compares by FiberID alone.
func c3CheckT63(events []c3Event) error {
	readySeq := make(map[ProviderIdentity]int)
	withdrawnSeq := make(map[ProviderIdentity]int)
	loadingSeq := make(map[ProviderIdentity]int) // key: consumer activation identity; one scenario dep per consumer
	for _, e := range events {
		switch e.Kind {
		case c3ProviderReady, c3ProviderWithdrawn:
			id := e.Provider
			if id.FiberID == 0 || id.ActivationID == 0 {
				return fmt.Errorf("T63: %s with invalid provider identity %s (event %s)", e.Kind, c3ID(id), e)
			}
			if id != (ProviderIdentity{FiberID: e.FiberID, ActivationID: e.ActivationID}) {
				return fmt.Errorf("T63: %s subject %d/%d does not match provider identity %s (event %s)",
					e.Kind, e.FiberID, e.ActivationID, c3ID(id), e)
			}
			if e.Kind == c3ProviderReady {
				if _, dup := readySeq[id]; dup {
					return fmt.Errorf("T63: duplicate ProviderReady for provider activation %s", c3ID(id))
				}
				readySeq[id] = e.Seq
				continue
			}
			if _, dup := withdrawnSeq[id]; dup {
				return fmt.Errorf("T63: duplicate ProviderWithdrawn for provider activation %s", c3ID(id))
			}
			if rs, ok := readySeq[id]; !ok || rs >= e.Seq {
				return fmt.Errorf("T63-B: ProviderWithdrawn(%s) at seq %d without an earlier ProviderReady", c3ID(id), e.Seq)
			}
			withdrawnSeq[id] = e.Seq

		case c3ConsumerLoading, c3ConsumerActive:
			consID := ProviderIdentity{FiberID: e.FiberID, ActivationID: e.ActivationID}
			if e.Consumer != consID {
				return fmt.Errorf("T63: consumer event subject %s does not match consumer identity %s (event %s)",
					c3ID(consID), c3ID(e.Consumer), e)
			}
			prov := e.Provider
			if prov.FiberID == 0 || prov.ActivationID == 0 {
				return fmt.Errorf("T63: %s with invalid bound provider identity (event %s)", e.Kind, e)
			}
			// Establishment direction: Ready must precede Loading and Active.
			if rs, ok := readySeq[prov]; !ok || rs >= e.Seq {
				return fmt.Errorf("T63-A: %s(C %s -> P %s) at seq %d before ProviderReady(P %s)",
					e.Kind, c3ID(consID), c3ID(prov), e.Seq, c3ID(prov))
			}
			// Withdrawal direction: nothing may bind a withdrawn generation.
			if ws, ok := withdrawnSeq[prov]; ok && ws < e.Seq {
				return fmt.Errorf("T63-B: %s(C %s -> P %s) at seq %d AFTER ProviderWithdrawn(P %s) at seq %d",
					e.Kind, c3ID(consID), c3ID(prov), e.Seq, c3ID(prov), ws)
			}
			ckey := ProviderIdentity{FiberID: e.FiberID, ActivationID: e.ActivationID}
			if e.Kind == c3ConsumerActive {
				ls, ok := loadingSeq[ckey]
				if !ok {
					return fmt.Errorf("T63-A: ConsumerActive(C %s -> P %s) without a preceding ConsumerLoading for the same activation",
						c3ID(consID), c3ID(prov))
				}
				if ls >= e.Seq {
					return fmt.Errorf("T63-A: ConsumerLoading seq %d not strictly before ConsumerActive seq %d for C %s -> P %s",
						ls, e.Seq, c3ID(consID), c3ID(prov))
				}
				continue
			}
			if _, dup := loadingSeq[ckey]; dup {
				return fmt.Errorf("T63: duplicate ConsumerLoading for consumer activation %s", c3ID(consID))
			}
			loadingSeq[ckey] = e.Seq
		}
	}
	return nil
}

// ---------------------------------------------------------------------------
// Deterministic driver helpers (orchestrator boundary protocol)
// ---------------------------------------------------------------------------

// c3BoundaryProbe is a no-op command whose apply returns only after every
// previously submitted command has been fully applied by the orchestrator.
// Observations after the probe never race an in-flight lifecycle command.
type c3BoundaryProbe struct {
	done chan struct{}
}

func (p *c3BoundaryProbe) apply(o *orchestrator) { close(p.done) }

func c3Boundary(t *testing.T, rt *Runtime) {
	t.Helper()
	p := &c3BoundaryProbe{done: make(chan struct{})}
	if !rt.submit(p) {
		t.Fatal("c3: boundary probe rejected (runtime closed?)")
	}
	select {
	case <-p.done:
	case <-time.After(10 * time.Second):
		t.Fatal("c3: boundary probe timed out (orchestrator stalled?)")
	}
}

// c3Settle reaches an orchestrator boundary and then runs the full per-step
// verification: recorder diff -> recorder model check -> T59 diagnostic ->
// T63 oracle over the accumulated ledger.
func c3Settle(t *testing.T, rt *Runtime, rec *c3Recorder, when string) {
	t.Helper()
	c3Boundary(t, rt)
	if err := rec.observe(); err != nil {
		t.Fatalf("T63 recorder model violation at %s: %v", when, err)
	}
	c3Check59(t, rt, when)
	if err := c3CheckT63(rec.Events); err != nil {
		t.Fatalf("T63 at %s: %v\nledger:\n%s", when, err, c3Ledger(rec.Events))
	}
}

func c3WaitParked(t *testing.T, rt *Runtime, why string) {
	t.Helper()
	if !c2Wait(5000, func() bool { return rt.detPending() >= 1 }) {
		t.Fatalf("c3 %s: no completion parked; pending=0", why)
	}
}

func c3EnabledStep(t *testing.T, rt *Runtime, fid FiberID, kind StepKind) Step {
	t.Helper()
	for _, s := range rt.detEnabledSteps() {
		if s.FiberID == fid && s.Kind == kind {
			return s
		}
	}
	t.Fatalf("c3: no enabled step %v for fiber %d; enabled=%v pending=%d", kind, fid, rt.detEnabledSteps(), rt.detPending())
	return Step{}
}

func c3ExecAndSettle(t *testing.T, rt *Runtime, rec *c3Recorder, step Step) {
	t.Helper()
	if err := rt.detExecute(step); err != nil {
		t.Fatalf("c3 detExecute(%v): %v (enabled=%v pending=%d)", step, err, rt.detEnabledSteps(), rt.detPending())
	}
	c3Settle(t, rt, rec, fmt.Sprintf("execute %v", step))
}

// c3Drain executes every enabled deterministic step (one at a time, each
// observed and verified) until no completion parks anymore. Order among the
// enabled steps is the deterministic detEnabledSteps order (sorted).
func c3Drain(t *testing.T, rt *Runtime, rec *c3Recorder) {
	t.Helper()
	// idleGrace is only the dead-end confirmation window: parking after the
	// orchestrator applies a command happens in microseconds, so a half-second
	// of silence means no completion is coming without any further driver step.
	const idleGrace = 500
	for iter := 0; iter < 500; iter++ {
		if !c2Wait(idleGrace, func() bool { return rt.detPending() >= 1 }) {
			return
		}
		en := rt.detEnabledSteps()
		if len(en) == 0 {
			if rt.detPending() == 0 {
				return
			}
			continue
		}
		for _, s := range en {
			c3ExecAndSettle(t, rt, rec, s)
		}
		if rt.detPending() == 0 {
			continue
		}
	}
	t.Fatalf("c3: drain exceeded 500 iterations; pending=%d", rt.detPending())
}

func c3Check59(t *testing.T, rt *Runtime, when string) {
	t.Helper()
	if err := checkT59(rt); err != nil {
		t.Fatalf("T59 diagnostic at %s: %v", when, err)
	}
}

// c3LoadFiber loads one scenario fiber, registers its role in the recorder,
// and settles at the first orchestrator boundary (seeding the recorder).
func c3LoadFiber(t *testing.T, rt *Runtime, rec *c3Recorder, comp Component, providerRole bool) *Fiber {
	t.Helper()
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if providerRole {
		rec.addProvider(f)
	} else {
		rec.addConsumer(f)
	}
	c3Settle(t, rt, rec, fmt.Sprintf("Load(%s)", comp.Name()))
	return f
}

func c3Close(t *testing.T, rt *Runtime) {
	t.Helper()
	clctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(clctx); err != nil {
		t.Fatalf("c3 Close: %v", err)
	}
	select {
	case <-rt.orch.done:
	default:
		t.Fatal("c3: orchestrator not done after Close")
	}
}

// c3BoundProvider reads the consumer activation's captured DependencySnapshot
// for the scenario key — the runtime binding fact source. It never resolves
// through realm.lookup.
func c3BoundProvider(c *Fiber, key CapabilityKey) (ProviderIdentity, bool) {
	c.mu.RLock()
	defer c.mu.RUnlock()
	if c.activation == nil {
		return ProviderIdentity{}, false
	}
	for _, d := range c.activation.deps {
		if d.Key == key {
			return d.Provider, true
		}
	}
	return ProviderIdentity{}, false
}

// c3AssertRecord asserts the provider fiber's realm record for the scenario
// key is present, matches the given identity, and has the given retiring flag.
func c3AssertRecord(t *testing.T, p *Fiber, key CapabilityKey, want ProviderIdentity, wantRetiring bool, when string) {
	t.Helper()
	rec := c3snapProviderRecord(p, key)
	if !rec.present || rec.id != want || rec.retiring != wantRetiring {
		t.Fatalf("T63 %s: provider record = present=%v id=%s retiring=%v, want %s/%v",
			when, rec.present, c3ID(rec.id), rec.retiring, c3ID(want), wantRetiring)
	}
}

// c3ActivateProvider drives a freshly loaded provider (Loading, Apply parked)
// through ApplyDone to Active and returns its activation id.
func c3ActivateProvider(t *testing.T, rt *Runtime, rec *c3Recorder, p *Fiber) ActivationID {
	t.Helper()
	c3WaitParked(t, rt, "provider ApplyDone park")
	step := c3EnabledStep(t, rt, p.ID(), StepApplyDone)
	act := step.ActivationID
	c3ExecAndSettle(t, rt, rec, step)
	if p.State() != StateActive {
		t.Fatalf("provider %s not Active after ApplyDone: state=%v", p.Name(), p.State())
	}
	return act
}

// c3ActivateConsumer drives a freshly loaded consumer (already Loading with a
// parked ApplyDone, or Loading on the next boundary) to Active.
func c3ActivateConsumer(t *testing.T, rt *Runtime, rec *c3Recorder, c *Fiber) ActivationID {
	t.Helper()
	c3WaitParked(t, rt, "consumer ApplyDone park")
	step := c3EnabledStep(t, rt, c.ID(), StepApplyDone)
	act := step.ActivationID
	c3ExecAndSettle(t, rt, rec, step)
	if c.State() != StateActive {
		t.Fatalf("consumer %s not Active after ApplyDone: state=%v", c.Name(), c.State())
	}
	return act
}

// ---------------------------------------------------------------------------
// C3-01 — P -> C basic activation ordering
// ---------------------------------------------------------------------------

func TestC3T63ActivationOrderingSingleProviderConsumer(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("c3.t63.activation").Capability()
	rec := c3NewRecorder(key)

	p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
	pAct := c3ActivateProvider(t, rt, rec, p)
	c3AssertRecord(t, p, key, ProviderIdentity{FiberID: p.ID(), ActivationID: pAct}, false, "after P Active")

	c := c3LoadFiber(t, rt, rec, &t66Comp{name: "C", key: key, consumer: true}, false)
	cAct := c3ActivateConsumer(t, rt, rec, c)

	prov := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct}
	ready, err := c3ExpectProviderEvent(rec.Events, c3ProviderReady, prov)
	if err != nil {
		t.Fatal(err)
	}
	load, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerLoading, c.ID(), cAct, prov)
	if err != nil {
		t.Fatal(err)
	}
	active, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerActive, c.ID(), cAct, prov)
	if err != nil {
		t.Fatal(err)
	}
	if !(ready.Seq < load.Seq && load.Seq < active.Seq) {
		t.Fatalf("T63-A ordering violated: ready=%d load=%d active=%d", ready.Seq, load.Seq, active.Seq)
	}
	if bind, ok := c3BoundProvider(c, key); !ok || bind != prov {
		t.Fatalf("T63: consumer binding = %+v (ok=%v), want P %s", bind, ok, c3ID(prov))
	}
	if pAct == 0 || cAct == 0 {
		t.Fatalf("T63: zero activation ids (P=%d C=%d)", pAct, cAct)
	}
	c3Close(t, rt)
	t.Log("C3-01 PASS: ProviderReady < ConsumerLoading < ConsumerActive with binding from DependencySnapshot")
}

// ---------------------------------------------------------------------------
// C3-02 — Provider withdrawal: no consumer stays Active after Withdrawn
// ---------------------------------------------------------------------------

func TestC3T63WithdrawalOrderingSingleProviderConsumer(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("c3.t63.withdrawal").Capability()
	rec := c3NewRecorder(key)

	p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
	pAct := c3ActivateProvider(t, rt, rec, p)
	c := c3LoadFiber(t, rt, rec, &t66Comp{name: "C", key: key, consumer: true}, false)
	cAct := c3ActivateConsumer(t, rt, rec, c)

	prov := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct}
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P)") // ProviderWithdrawn(P) + C begins unloading

	// Consumer-first withdrawal: P must still be Active (not yet Unloading)
	// while C is leaving Active.
	if p.State() != StateActive {
		t.Fatalf("T63-B: P = %v while C unwinding; provider must not unload before consumers end", p.State())
	}
	if c.State() == StateActive {
		t.Fatalf("T63-B: C still Active after ProviderWithdrawn; C=%v", c.State())
	}
	if _, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov); err != nil {
		t.Fatal(err)
	}

	c3Drain(t, rt, rec)
	if p.State() != StateGone {
		t.Fatalf("P not Gone after drain: %v", p.State())
	}
	if c.State() == StateActive {
		t.Fatalf("T63-B: C=%v Active after provider withdrawal drained; want Pending/Gone", c.State())
	}
	if bind, ok := c3BoundProvider(c, key); ok {
		t.Fatalf("T63-B: C still carries a live dependency snapshot %+v after withdrawal", bind)
	}
	active, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerActive, c.ID(), cAct, prov)
	if err != nil {
		t.Fatal(err)
	}
	wd, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov)
	if err != nil {
		t.Fatal(err)
	}
	if active.Seq >= wd.Seq {
		t.Fatalf("T63-B: ConsumerActive seq %d not before ProviderWithdrawn seq %d", active.Seq, wd.Seq)
	}
	c3Close(t, rt)
	t.Log("C3-02 PASS: ConsumerActive < ProviderWithdrawn; no Active consumer after withdrawal")
}

// ---------------------------------------------------------------------------
// C3-03 — Provider reload: generations never mix
// ---------------------------------------------------------------------------

func TestC3T63ReloadGeneration(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("c3.t63.reload").Capability()
	rec := c3NewRecorder(key)

	p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
	pAct1 := c3ActivateProvider(t, rt, rec, p)
	c := c3LoadFiber(t, rt, rec, &t66Comp{name: "C", key: key, consumer: true}, false)
	cAct1 := c3ActivateConsumer(t, rt, rec, c)
	prov1 := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct1}

	// Withdraw generation 1 completely.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P/A1)")
	c3Drain(t, rt, rec)
	if p.State() != StateGone || c.State() != StatePending {
		t.Fatalf("after withdrawal drain: P=%v C=%v, want Gone/Pending", p.State(), c.State())
	}
	if _, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov1); err != nil {
		t.Fatal(err)
	}

	// Reload: P/A2 -> C/Ac2, bound to the NEW generation only.
	if err := p.Load(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Load(P/A2)")
	pAct2 := c3ActivateProvider(t, rt, rec, p)
	prov2 := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct2}
	if pAct2 == pAct1 {
		t.Fatalf("T63 reload: provider activation did not advance (A1=%d A2=%d)", pAct1, pAct2)
	}
	if prov1 == prov2 {
		t.Fatalf("T63 reload: ProviderIdentity did not change across reload (%s)", c3ID(prov1))
	}
	cAct2 := c3ActivateConsumer(t, rt, rec, c)
	if cAct2 == cAct1 {
		t.Fatalf("T63 reload: consumer activation did not advance (A1=%d A2=%d)", cAct1, cAct2)
	}

	// Generation-2 events must bind P/A2, never P/A1.
	if _, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerLoading, c.ID(), cAct2, prov2); err != nil {
		t.Fatal(err)
	}
	if _, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerActive, c.ID(), cAct2, prov2); err != nil {
		t.Fatal(err)
	}
	for _, e := range rec.Events {
		if e.Kind == c3ConsumerLoading || e.Kind == c3ConsumerActive {
			if e.ActivationID == cAct2 && e.Provider == prov1 {
				t.Fatalf("T63 reload: generation-2 consumer event bound to withdrawn generation 1: %s", e)
			}
		}
	}
	c3AssertRecord(t, p, key, prov2, false, "after reload")
	if bind, ok := c3BoundProvider(c, key); !ok || bind != prov2 {
		t.Fatalf("T63 reload: consumer binding = %+v (ok=%v), want P %s", bind, ok, c3ID(prov2))
	}

	// Full generation-1 + generation-2 ledger is ordered per binding.
	if err := c3CheckT63(rec.Events); err != nil {
		t.Fatalf("T63 reload ledger: %v\nledger:\n%s", err, c3Ledger(rec.Events))
	}
	c3Close(t, rt)
	t.Logf("C3-03 PASS: P/A1 -> C/Ac1 -> Withdrawn(A1) -> P/A2 -> C/Ac2 binds %s only", c3ID(prov2))
}

// ---------------------------------------------------------------------------
// C3-04 — Multiple consumers of one provider activation
// ---------------------------------------------------------------------------

func TestC3T63MultiConsumerOrdering(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("c3.t63.multi").Capability()
	rec := c3NewRecorder(key)

	p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
	pAct := c3ActivateProvider(t, rt, rec, p)
	prov := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct}

	var cs []*Fiber
	var cActs []ActivationID
	for i := 0; i < 3; i++ {
		c := c3LoadFiber(t, rt, rec, &t66Comp{name: fmt.Sprintf("C%d", i), key: key, consumer: true}, false)
		cs = append(cs, c)
		cActs = append(cActs, c3ActivateConsumer(t, rt, rec, c))
	}
	ready, err := c3ExpectProviderEvent(rec.Events, c3ProviderReady, prov)
	if err != nil {
		t.Fatal(err)
	}
	for i, c := range cs {
		load, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerLoading, c.ID(), cActs[i], prov)
		if err != nil {
			t.Fatal(err)
		}
		active, err := c3ExpectConsumerBindingEvent(rec.Events, c3ConsumerActive, c.ID(), cActs[i], prov)
		if err != nil {
			t.Fatal(err)
		}
		if !(ready.Seq < load.Seq && load.Seq < active.Seq) {
			t.Fatalf("T63-A C%d: ready=%d load=%d active=%d not strictly ordered", i, ready.Seq, load.Seq, active.Seq)
		}
	}

	// Withdraw: every consumer must leave Active and none may rebind.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P)")
	if _, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov); err != nil {
		t.Fatal(err)
	}
	c3Drain(t, rt, rec)
	for i, c := range cs {
		if c.State() == StateActive {
			t.Fatalf("T63-B: C%d=%v still Active after provider withdrawal", i, c.State())
		}
		if _, ok := c3BoundProvider(c, key); ok {
			t.Fatalf("T63-B: C%d still carries a live dependency snapshot after withdrawal", i)
		}
	}
	c3Close(t, rt)
	t.Log("C3-04 PASS: every consumer Ready < Loading < Active < Withdrawn; none Active after withdrawal")
}

// ---------------------------------------------------------------------------
// C3-05 — Deterministic randomized schedules (5 seeds x 40 cycles)
// ---------------------------------------------------------------------------

func TestC3T63RandomizedSchedule(t *testing.T) {
	for _, seed := range []uint64{1, 2, 3, 4, 5} {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewPCG(seed, seed^0x9e3779b97f4a7c15))
			rt := detNew(t)
			key := NewKey[string]("c3.t63.random").Capability()
			rec := c3NewRecorder(key)

			p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
			var cs []*Fiber
			for i := 0; i < 3; i++ {
				cs = append(cs, c3LoadFiber(t, rt, rec, &t66Comp{name: fmt.Sprintf("C%d", i), key: key, consumer: true}, false))
			}
			all := append([]*Fiber{p}, cs...)
			mounted := make(map[FiberID]bool, len(all))
			for _, f := range all {
				mounted[f.id] = true
			}

			// Deterministic warmup: reach the canonical stable state
			// P/A1 Active + every consumer Active on P/A1, so every seed is
			// guaranteed to exercise Ready/Loading/Active before the
			// randomized churn below begins.
			c3ActivateProvider(t, rt, rec, p)
			for i := range cs {
				c3ActivateConsumer(t, rt, rec, cs[i])
			}

			// Randomized cycles. op=4 executes ONE currently-parked enabled
			// step chosen by the seed; the set of parked steps is the
			// timing-dependent completion set, so two runs of a seed may take
			// different (but always legal) interleavings. The T63 oracle is
			// schedule-agnostic: every prefix must satisfy the ordering.
			for cycle := 0; cycle < 40; cycle++ {
				op := rng.IntN(5)
				switch {
				case op == 0: // provider toggle
					c3Toggle(t, rt, p, mounted)
				case op >= 1 && op <= 3: // consumer toggle
					c3Toggle(t, rt, cs[op-1], mounted)
				default: // execute one random currently-parked enabled step
					// Non-blocking: if nothing is parked right now the cycle
					// is a no-op (work parked from an earlier toggle is picked
					// up on a later cycle or by the final drain).
					en := rt.detEnabledSteps()
					if len(en) == 0 {
						continue
					}
					c3ExecAndSettle(t, rt, rec, en[rng.IntN(len(en))])
					continue
				}
				c3Settle(t, rt, rec, fmt.Sprintf("seed=%d cycle=%d op=%d", seed, cycle, op))
			}

			c3Drain(t, rt, rec)
			c3Settle(t, rt, rec, "post-drain")

			nReady, nLoading, nActive, nWithdrawn := c3Counts(rec.Events)
			if nReady < 1 || nWithdrawn < 1 || nActive < 1 || nLoading < 1 {
				t.Fatalf("C3-05 seed %d: degenerate schedule (ready=%d loading=%d active=%d withdrawn=%d); ledger:\n%s",
					seed, nReady, nLoading, nActive, nWithdrawn, c3Ledger(rec.Events))
			}
			if err := c3CheckT63(rec.Events); err != nil {
				t.Fatalf("C3-05 seed %d: %v\nledger:\n%s", seed, err, c3Ledger(rec.Events))
			}
			c3Close(t, rt)
			t.Logf("C3-05 seed %d PASS: ready=%d loading=%d active=%d withdrawn=%d", seed, nReady, nLoading, nActive, nWithdrawn)
		})
	}
}

func c3Toggle(t *testing.T, rt *Runtime, f *Fiber, mounted map[FiberID]bool) {
	t.Helper()
	if mounted[f.id] {
		if f.State() != StateActive {
			return // not at a legal dispose point yet
		}
		if err := f.Dispose(); err != nil {
			t.Fatal(err)
		}
		mounted[f.id] = false
		return
	}
	if f.State() != StateGone {
		return // not at a legal reload point yet
	}
	if err := f.Load(); err != nil {
		t.Fatal(err)
	}
	mounted[f.id] = true
}

// ---------------------------------------------------------------------------
// C3-06 — Stale ApplyDone injection: an old activation completion must never
// revive a withdrawn generation or rebind a consumer to it.
// ---------------------------------------------------------------------------

func TestC3T63StaleApplyDoneInjection(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("c3.t63.stale.apply").Capability()
	rec := c3NewRecorder(key)

	p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
	pAct1 := c3ActivateProvider(t, rt, rec, p)
	c := c3LoadFiber(t, rt, rec, &t66Comp{name: "C", key: key, consumer: true}, false)
	cAct1 := c3ActivateConsumer(t, rt, rec, c)

	// Sentinel: if a stale ApplyDone ever leaks into the NEW activation's
	// effect stack, unwinding the new activation runs it.
	var staleFired bool

	// Full withdrawal of generation 1.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P/A1)")
	c3Drain(t, rt, rec)
	if p.State() != StateGone || c.State() != StatePending {
		t.Fatalf("after drain: P=%v C=%v", p.State(), c.State())
	}

	// Reload generation 2; P is Loading with ApplyDone(A2) parked.
	if err := p.Load(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Load(P/A2)")
	c3WaitParked(t, rt, "P/A2 ApplyDone park")
	pA2step := c3EnabledStep(t, rt, p.ID(), StepApplyDone)
	pAct2 := pA2step.ActivationID
	eventsBeforeInject := len(rec.Events)

	// Injection point 1: stale ApplyDone(P/A1) while P/A2 is Loading. It must
	// be ignored (activation identity guard) — P stays Loading, the real
	// ApplyDone(A2) stays enabled, and no event is produced.
	if !rt.submit(&cmdApplyDone{fiberID: p.ID(), activationID: pAct1, cleanup: func() error { staleFired = true; return nil }}) {
		t.Fatal("c3: stale ApplyDone submit rejected")
	}
	c3Settle(t, rt, rec, "stale ApplyDone(P/A1) while P/A2 Loading")
	if p.State() != StateLoading || ActivationID(actIDOf(p)) != pAct2 {
		t.Fatalf("T63 stale: P = %v act=%d after stale ApplyDone, want Loading/%d", p.State(), actIDOf(p), pAct2)
	}
	if staleFired {
		t.Fatal("T63 stale: stale ApplyDone effect was committed into the new activation")
	}
	if len(rec.Events) != eventsBeforeInject {
		t.Fatalf("T63 stale: stale ApplyDone produced events; before=%d after=%d\n%s", eventsBeforeInject, len(rec.Events), c3Ledger(rec.Events))
	}
	prov2 := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct2}

	// Execute the real A2 ApplyDone: P/A2 Ready, C/Ac2 Loading (binding A2).
	c3ExecAndSettle(t, rt, rec, pA2step)
	if p.State() != StateActive {
		t.Fatalf("P not Active after real A2 ApplyDone: %v", p.State())
	}

	// Injection point 1b: stale ApplyDone(C/Ac1) while C is Loading(Ac2): the
	// old consumer completion must not fast-forward the new activation.
	c3WaitParked(t, rt, "C/Ac2 ApplyDone park")
	cA2step := c3EnabledStep(t, rt, c.ID(), StepApplyDone)
	cAct2 := cA2step.ActivationID
	before1b := len(rec.Events)
	if !rt.submit(&cmdApplyDone{fiberID: c.ID(), activationID: cAct1}) {
		t.Fatal("c3: stale consumer ApplyDone submit rejected")
	}
	c3Settle(t, rt, rec, "stale ApplyDone(C/Ac1) while C/Ac2 Loading")
	if c.State() != StateLoading || ActivationID(actIDOf(c)) != cAct2 {
		t.Fatalf("T63 stale: C = %v act=%d after stale consumer ApplyDone, want Loading/%d", c.State(), actIDOf(c), cAct2)
	}
	if len(rec.Events) != before1b {
		t.Fatalf("T63 stale: stale consumer ApplyDone produced events\n%s", c3Ledger(rec.Events))
	}

	// Complete generation 2 activation.
	c3ExecAndSettle(t, rt, rec, cA2step)
	if c.State() != StateActive {
		t.Fatalf("C not Active after real A2 ApplyDone: %v", c.State())
	}
	if bind, ok := c3BoundProvider(c, key); !ok || bind != prov2 {
		t.Fatalf("T63 stale: consumer bound to %+v (ok=%v), want %s", bind, ok, c3ID(prov2))
	}

	// Injection point 2: stale ApplyDone(P/A1) + ApplyDone(C/Ac1) while both
	// generations-2 activations are Active — no disturbance allowed.
	before2 := len(rec.Events)
	if !rt.submit(&cmdApplyDone{fiberID: p.ID(), activationID: pAct1}) {
		t.Fatal("submit rejected")
	}
	if !rt.submit(&cmdApplyDone{fiberID: c.ID(), activationID: cAct1}) {
		t.Fatal("submit rejected")
	}
	c3Settle(t, rt, rec, "stale ApplyDones while A2 Active")
	if p.State() != StateActive || ActivationID(actIDOf(p)) != pAct2 {
		t.Fatalf("T63 stale: P disturbed by stale apply; P=%v act=%d", p.State(), actIDOf(p))
	}
	if c.State() != StateActive || ActivationID(actIDOf(c)) != cAct2 {
		t.Fatalf("T63 stale: C disturbed by stale apply; C=%v act=%d", c.State(), actIDOf(c))
	}
	if len(rec.Events) != before2 {
		t.Fatalf("T63 stale: stale apply while Active produced events\n%s", c3Ledger(rec.Events))
	}

	// Withdraw generation 2 fully; the stale sentinel must never have fired.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P/A2)")
	c3Drain(t, rt, rec)
	if staleFired {
		t.Fatal("T63 stale: stale ApplyDone effect leaked into generation 2 and ran during unwind")
	}
	if _, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov2); err != nil {
		t.Fatal(err)
	}
	c3Close(t, rt)
	t.Log("C3-06 PASS: stale ApplyDone(P/A1)/(C/Ac1) ignored; no revival, no rebinding, no effect leak")
}

// ---------------------------------------------------------------------------
// C3-07 — Stale UnwindDone injection
// ---------------------------------------------------------------------------

func TestC3T63StaleUnwindDoneInjection(t *testing.T) {
	rt := detNew(t)
	key := NewKey[string]("c3.t63.stale.unwind").Capability()
	rec := c3NewRecorder(key)

	p := c3LoadFiber(t, rt, rec, &t66Comp{name: "P", key: key, provide: true}, true)
	pAct1 := c3ActivateProvider(t, rt, rec, p)
	c := c3LoadFiber(t, rt, rec, &t66Comp{name: "C", key: key, consumer: true}, false)
	cAct1 := c3ActivateConsumer(t, rt, rec, c)
	prov1 := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct1}

	// Withdraw generation 1 fully.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P/A1)")
	c3Drain(t, rt, rec)
	if p.State() != StateGone || c.State() != StatePending {
		t.Fatalf("after drain: P=%v C=%v", p.State(), c.State())
	}

	// Reload generation 2.
	if err := p.Load(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Load(P/A2)")
	c3WaitParked(t, rt, "P/A2 ApplyDone park")
	pA2step := c3EnabledStep(t, rt, p.ID(), StepApplyDone)
	pAct2 := pA2step.ActivationID
	prov2 := ProviderIdentity{FiberID: p.ID(), ActivationID: pAct2}

	// Injection point 1: stale UnwindDone(P/A1) while P/A2 is Loading.
	before := len(rec.Events)
	if !rt.submit(&cmdUnwindDone{fiberID: p.ID(), activationID: pAct1}) {
		t.Fatal("c3: stale UnwindDone submit rejected")
	}
	c3Settle(t, rt, rec, "stale UnwindDone(P/A1) while P/A2 Loading")
	if p.State() != StateLoading || ActivationID(actIDOf(p)) != pAct2 {
		t.Fatalf("T63 stale: P = %v act=%d after stale UnwindDone, want Loading/%d", p.State(), actIDOf(p), pAct2)
	}
	if len(rec.Events) != before {
		t.Fatalf("T63 stale: stale UnwindDone produced events\n%s", c3Ledger(rec.Events))
	}

	// Complete generation 2 activation (P/A2 Active, C/Ac2 Active binding A2).
	c3ExecAndSettle(t, rt, rec, pA2step)
	c3WaitParked(t, rt, "C/Ac2 ApplyDone park")
	cA2step := c3EnabledStep(t, rt, c.ID(), StepApplyDone)
	c3ExecAndSettle(t, rt, rec, cA2step)
	if c.State() != StateActive {
		t.Fatalf("C not Active: %v", c.State())
	}
	if bind, ok := c3BoundProvider(c, key); !ok || bind != prov2 {
		t.Fatalf("T63 stale: consumer bound to %+v (ok=%v), want %s", bind, ok, c3ID(prov2))
	}

	// Withdraw generation 2; drain the consumer, leave P Unloading with its
	// real UnwindDone(A2) parked.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	c3Settle(t, rt, rec, "Dispose(P/A2)")
	c3WaitParked(t, rt, "C/Ac2 UnwindDone park")
	cUw := c3EnabledStep(t, rt, c.ID(), StepUnwindDone)
	c3ExecAndSettle(t, rt, rec, cUw)
	c3WaitParked(t, rt, "P/A2 UnwindDone park")
	pUw := c3EnabledStep(t, rt, p.ID(), StepUnwindDone)
	if pUw.ActivationID != pAct2 {
		t.Fatalf("enabled P UnwindDone act=%d, want %d", pUw.ActivationID, pAct2)
	}
	if p.State() != StateUnloading {
		t.Fatalf("P not Unloading before stale injection: %v", p.State())
	}

	// Injection point 2: stale UnwindDone(P/A1) + stale UnwindDone(C/Ac1)
	// while P/A2 is Unloading with its own UnwindDone parked.
	before2 := len(rec.Events)
	if !rt.submit(&cmdUnwindDone{fiberID: p.ID(), activationID: pAct1}) {
		t.Fatal("submit rejected")
	}
	if !rt.submit(&cmdUnwindDone{fiberID: c.ID(), activationID: cAct1}) {
		t.Fatal("submit rejected")
	}
	c3Settle(t, rt, rec, "stale UnwindDones while P/A2 Unloading")
	if p.State() != StateUnloading || ActivationID(actIDOf(p)) != pAct2 {
		t.Fatalf("T63 stale: P disturbed by stale unwind; P=%v act=%d", p.State(), actIDOf(p))
	}
	if len(rec.Events) != before2 {
		t.Fatalf("T63 stale: stale UnwindDones produced events\n%s", c3Ledger(rec.Events))
	}
	en := rt.detEnabledSteps()
	if len(en) != 1 || en[0] != pUw {
		t.Fatalf("T63 stale: enabled steps = %v, want exactly [%v]", en, pUw)
	}

	// Real UnwindDone(A2): P/A2 ends; generation-1 events untouched.
	c3ExecAndSettle(t, rt, rec, pUw)
	if p.State() != StateGone {
		t.Fatalf("P not Gone after real A2 UnwindDone: %v", p.State())
	}
	if _, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov1); err != nil {
		t.Fatal(err)
	}
	if _, err := c3ExpectProviderEvent(rec.Events, c3ProviderWithdrawn, prov2); err != nil {
		t.Fatal(err)
	}
	c3Close(t, rt)
	t.Log("C3-07 PASS: stale UnwindDone(P/A1)/(C/Ac1) ignored during P/A2 Loading and Unloading")
}

// ---------------------------------------------------------------------------
// Oracle self-check: the pure T63 oracle recognizes violations in synthetic
// traces (no Runtime needed).
// ---------------------------------------------------------------------------

func TestC3T63OracleSanity(t *testing.T) {
	p1 := ProviderIdentity{FiberID: 1, ActivationID: 1}
	p2 := ProviderIdentity{FiberID: 1, ActivationID: 2}
	c1 := ProviderIdentity{FiberID: 2, ActivationID: 1}
	c2 := ProviderIdentity{FiberID: 2, ActivationID: 2}
	seq := 0
	ev := func(kind c3EventKind, cons, prov ProviderIdentity) c3Event {
		seq++
		return c3Event{Seq: seq, Kind: kind, FiberID: cons.FiberID, ActivationID: cons.ActivationID, Provider: prov, Consumer: cons}
	}
	evP := func(kind c3EventKind, prov ProviderIdentity) c3Event {
		seq++
		return c3Event{Seq: seq, Kind: kind, FiberID: prov.FiberID, ActivationID: prov.ActivationID, Provider: prov}
	}

	// Good generation-1 + generation-2 ledger with full withdrawal between.
	good := []c3Event{
		evP(c3ProviderReady, p1),
		ev(c3ConsumerLoading, c1, p1),
		ev(c3ConsumerActive, c1, p1),
		evP(c3ProviderWithdrawn, p1),
		evP(c3ProviderReady, p2),
		ev(c3ConsumerLoading, c2, p2),
		ev(c3ConsumerActive, c2, p2),
	}
	if err := c3CheckT63(good); err != nil {
		t.Fatalf("good two-generation ledger must PASS: %v", err)
	}

	// Mutation 1: ConsumerLoading before ProviderReady.
	seq = 0
	early := []c3Event{
		ev(c3ConsumerLoading, c1, p1),
		evP(c3ProviderReady, p1),
		ev(c3ConsumerActive, c1, p1),
	}
	if err := c3CheckT63(early); err == nil {
		t.Fatal("ConsumerLoading before ProviderReady must FAIL")
	}

	// Mutation 2: ConsumerActive without ConsumerLoading.
	seq = 0
	noLoad := []c3Event{
		evP(c3ProviderReady, p1),
		ev(c3ConsumerActive, c1, p1),
	}
	if err := c3CheckT63(noLoad); err == nil {
		t.Fatal("ConsumerActive without ConsumerLoading must FAIL")
	}

	// Mutation 3: ConsumerActive after ProviderWithdrawn.
	seq = 0
	lateActive := []c3Event{
		evP(c3ProviderReady, p1),
		ev(c3ConsumerLoading, c1, p1),
		evP(c3ProviderWithdrawn, p1),
		ev(c3ConsumerActive, c1, p1),
	}
	if err := c3CheckT63(lateActive); err == nil {
		t.Fatal("ConsumerActive after ProviderWithdrawn must FAIL")
	}

	// Mutation 4: ConsumerLoading starts after ProviderWithdrawn (rebinding a
	// withdrawn generation).
	seq = 0
	lateLoad := []c3Event{
		evP(c3ProviderReady, p1),
		ev(c3ConsumerLoading, c1, p1),
		evP(c3ProviderWithdrawn, p1),
		ev(c3ConsumerLoading, c2, p1),
	}
	if err := c3CheckT63(lateLoad); err == nil {
		t.Fatal("ConsumerLoading after ProviderWithdrawn must FAIL")
	}

	// Mutation 5: Withdrawn without Ready.
	seq = 0
	orphanWd := []c3Event{evP(c3ProviderWithdrawn, p1)}
	if err := c3CheckT63(orphanWd); err == nil {
		t.Fatal("ProviderWithdrawn without ProviderReady must FAIL")
	}

	// Mutation 6: duplicate ProviderReady for one activation.
	seq = 0
	dupReady := []c3Event{
		evP(c3ProviderReady, p1),
		ev(c3ConsumerLoading, c1, p1),
		evP(c3ProviderReady, p1),
	}
	if err := c3CheckT63(dupReady); err == nil {
		t.Fatal("duplicate ProviderReady must FAIL")
	}

	// Mutation 7: non-strict equality (same seq).
	seq = 0
	eq := []c3Event{
		evP(c3ProviderReady, p1),
		{Seq: 1, Kind: c3ConsumerLoading, FiberID: c1.FiberID, ActivationID: c1.ActivationID, Provider: p1, Consumer: c1},
	}
	if err := c3CheckT63(eq); err == nil {
		t.Fatal("non-strict ProviderReady == ConsumerLoading must FAIL")
	}

	// Mutation 8: consumer event bound to the wrong generation.
	seq = 0
	wrongGen := []c3Event{
		evP(c3ProviderReady, p1),
		ev(c3ConsumerLoading, c1, p1),
		evP(c3ProviderWithdrawn, p1),
		evP(c3ProviderReady, p2),
		ev(c3ConsumerLoading, c2, p1), // new consumer activation bound to OLD generation
	}
	if err := c3CheckT63(wrongGen); err == nil {
		t.Fatal("generation-2 consumer bound to withdrawn generation-1 must FAIL")
	}

	t.Log("C3 oracle sanity PASS: good ledger PASSes, all 8 mutations FAIL")
}
