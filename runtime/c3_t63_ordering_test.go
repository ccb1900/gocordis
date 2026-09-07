package runtime

import (
	"context"
	"fmt"
	"testing"
	"time"
)

// C-3 (T63 Ordering): minimal Provider -> Consumer topology under the
// deterministic driver, verified from a semantic lifecycle event trace by a
// pure-function oracle. Single realm, one provider P of key K, one consumer C
// requiring K, acyclic, non-retiring. No randomization, no T73 schedules.
//
// Paper mapping (recorded project authority, NOT derived from the runtime):
//
//	docs/review/GOCORDIS — Paper-Level Theorem Verification Spec v0.1
//	  §16-18: a consumer may only enter Loading/Apply once every required
//	          dependency is satisfied (provider Active); the event oracle
//	          checks that the most recent provider state before
//	          Apply(consumer) is Active.
//	docs/review/GOCORDIS — Theorem Verification Infrastructure Spec v0.2
//	  §23 (activation): Provider Apply < Consumer Apply, and a consumer
//	          never observes an unready dependency during activation.
//	  §24 (withdrawal): C Unwind < P Unwind AND C Cleanup < P Cleanup,
//	          verified through an observable event oracle.
//
// Runtime transition      -> recorded T63 semantic event
//
//	P: Loading -> Active      -> ProviderReady(P, AP)
//	C: Pending -> Loading     -> ConsumerLoading(C, AC)   (Apply begins)
//	C: Loading -> Active      -> ConsumerActive(C, AC)
//	C: Active  -> Unloading   -> ConsumerUnwindBegin(C, AC)
//	C: activation ended       -> ConsumerUnwindEnd(C, AC) (cleanup done)
//	P: Active  -> Unloading   -> ProviderUnwindBegin(P, AP)
//	P: activation ended       -> ProviderUnwindEnd(P, AP)
//
// T63-A predicate: ProviderReady(P) < ConsumerLoading(C) < ConsumerActive(C)
// (strict seq order; ConsumerActive is a consequence, not an independent
// property). T63-B predicate: ConsumerUnwindBegin < ProviderUnwindBegin AND
// ConsumerUnwindEnd < ProviderUnwindEnd.
//
// Events are recorded from runtime lifecycle state transitions observed ONLY
// at orchestrator-confirmed semantic step boundaries (probe command), never
// from dependency-snapshot existence (which would be oracle self-proof) and
// never from wall-clock/goroutine ordering. Seq is assigned by the driver.

// ---------------------------------------------------------------------------
// Semantic event trace
// ---------------------------------------------------------------------------

type c3EventKind uint8

const (
	c3ProviderReady c3EventKind = iota
	c3ConsumerLoading
	c3ConsumerActive
	c3ConsumerUnwindBegin
	c3ConsumerUnwindEnd
	c3ProviderUnwindBegin
	c3ProviderUnwindEnd
)

func (k c3EventKind) String() string {
	switch k {
	case c3ProviderReady:
		return "ProviderReady"
	case c3ConsumerLoading:
		return "ConsumerLoading"
	case c3ConsumerActive:
		return "ConsumerActive"
	case c3ConsumerUnwindBegin:
		return "ConsumerUnwindBegin"
	case c3ConsumerUnwindEnd:
		return "ConsumerUnwindEnd"
	case c3ProviderUnwindBegin:
		return "ProviderUnwindBegin"
	case c3ProviderUnwindEnd:
		return "ProviderUnwindEnd"
	default:
		return fmt.Sprintf("c3EventKind(%d)", uint8(k))
	}
}

// c3Event is one recorded semantic lifecycle event. Seq is assigned by the
// deterministic driver; FiberID + ActivationID identify the activation
// generation (T63 is activation-level ordering, never fiber-level only).
type c3Event struct {
	Seq          int
	Kind         c3EventKind
	FiberID      FiberID
	ActivationID ActivationID
}

type c3FiberObs struct {
	state FiberState
	act   ActivationID
}

// c3Recorder diffs the lifecycle state of the scenario fibers between two
// orchestrator-confirmed boundaries and emits the T63 semantic events above.
// A fiber first seen mid-lifecycle has its Pending-prefix replayed by
// seedPrefix (fiber creation is StatePending by construction).
type c3Recorder struct {
	provider *Fiber
	consumer *Fiber
	prev     map[FiberID]c3FiberObs
	seq      int
	Events   []c3Event
}

func (r *c3Recorder) emit(kind c3EventKind, fid FiberID, act ActivationID) {
	r.seq++
	r.Events = append(r.Events, c3Event{Seq: r.seq, Kind: kind, FiberID: fid, ActivationID: act})
}

// seedPrefix replays the events implied between fiber creation (Pending) and
// the state first observed with a live activation. Only used when the
// recorder attached after the orchestrator already advanced the fiber.
func (r *c3Recorder) seedPrefix(f *Fiber, cur c3FiberObs) {
	if cur.act == 0 {
		return
	}
	if f == r.provider {
		switch cur.state {
		case StateActive:
			r.emit(c3ProviderReady, f.id, cur.act)
		case StateUnloading:
			r.emit(c3ProviderReady, f.id, cur.act)
			r.emit(c3ProviderUnwindBegin, f.id, cur.act)
		}
		return
	}
	if f == r.consumer {
		switch cur.state {
		case StateLoading:
			r.emit(c3ConsumerLoading, f.id, cur.act)
		case StateActive:
			r.emit(c3ConsumerLoading, f.id, cur.act)
			r.emit(c3ConsumerActive, f.id, cur.act)
		case StateUnloading:
			r.emit(c3ConsumerLoading, f.id, cur.act)
			r.emit(c3ConsumerActive, f.id, cur.act)
			r.emit(c3ConsumerUnwindBegin, f.id, cur.act)
		}
	}
}

func (r *c3Recorder) observe(rt *Runtime) {
	if r.prev == nil {
		r.prev = make(map[FiberID]c3FiberObs)
	}
	// Roles are iterated provider-then-consumer. Events landing on the SAME
	// boundary are ordered by this iteration; the T63 predicates in v1 only
	// compare pairs recorded on DISTINCT boundaries (ProviderReady vs
	// ConsumerLoading; ConsumerUnwindBegin/End vs ProviderUnwindBegin/End),
	// so intra-boundary iteration order is never load-bearing.
	roles := []*Fiber{r.provider, r.consumer}
	for _, f := range roles {
		if f == nil {
			continue
		}
		cur := c3FiberObs{state: f.State(), act: ActivationID(actIDOf(f))}
		prev, seen := r.prev[f.id]
		r.prev[f.id] = cur
		if !seen {
			// A fiber is created in StatePending (newFiber) and only then
			// transitions; if the recorder attaches after the orchestrator
			// already advanced the fiber, deterministically replay the
			// Pending->... prefix for the same live activation (single
			// activation per fiber is a C-3 scenario precondition).
			r.seedPrefix(f, cur)
			continue
		}
		if prev.state == cur.state && prev.act == cur.act {
			continue
		}
		switch {
		case f == r.provider:
			switch {
			case prev.state == StateLoading && cur.state == StateActive && cur.act != 0:
				r.emit(c3ProviderReady, f.id, cur.act)
			case prev.state == StateActive && cur.state == StateUnloading && cur.act != 0:
				r.emit(c3ProviderUnwindBegin, f.id, cur.act)
			case prev.act != 0 && cur.act == 0:
				r.emit(c3ProviderUnwindEnd, f.id, prev.act)
			}
		case f == r.consumer:
			switch {
			case prev.state == StatePending && cur.state == StateLoading && cur.act != 0:
				r.emit(c3ConsumerLoading, f.id, cur.act)
			case prev.state == StateLoading && cur.state == StateActive && cur.act != 0:
				r.emit(c3ConsumerActive, f.id, cur.act)
			case prev.state == StateActive && cur.state == StateUnloading && cur.act != 0:
				r.emit(c3ConsumerUnwindBegin, f.id, cur.act)
			case prev.act != 0 && cur.act == 0:
				r.emit(c3ConsumerUnwindEnd, f.id, prev.act)
			}
		}
	}
}

// ---------------------------------------------------------------------------
// Orchestrator-confirmed semantic step boundary
// ---------------------------------------------------------------------------

// c3BoundaryProbe is a no-op command whose apply returns only after every
// previously submitted command (including the just-executed deterministic
// step) has been fully applied by the orchestrator. This is the C-3
// orchestrator-owned checkpoint: observations after the probe never race an
// in-flight lifecycle command and never substitute Fiber.State() polling for
// an orchestrator-idle proof.
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
	case <-time.After(5 * time.Second):
		t.Fatal("c3: boundary probe timed out (orchestrator stalled?)")
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

func c3Check59(t *testing.T, rt *Runtime, when string) {
	t.Helper()
	if err := checkT59(rt); err != nil {
		t.Fatalf("T59 at %s: %v", when, err)
	}
}

func c3LogTrace(t *testing.T, events []c3Event) {
	t.Helper()
	for _, e := range events {
		t.Logf("c3 trace seq=%d kind=%s fiber=%d activation=%d", e.Seq, e.Kind, e.FiberID, e.ActivationID)
	}
}

// ---------------------------------------------------------------------------
// Pure-function T63 oracles (no Runtime access, no waiting)
// ---------------------------------------------------------------------------

func c3FindEvent(events []c3Event, kind c3EventKind, fid FiberID) (c3Event, bool) {
	for _, e := range events {
		if e.Kind == kind && e.FiberID == fid {
			return e, true
		}
	}
	return c3Event{}, false
}

// c3CheckT63Activation verifies T63-A for one provider/consumer pair:
//
//	ProviderReady(P)  ≺  ConsumerLoading(C)  ≺  ConsumerActive(C)
//
// with STRICT seq precedence (P.Ready == C.Loading is rejected).
func c3CheckT63Activation(events []c3Event, provider, consumer FiberID) error {
	ready, okR := c3FindEvent(events, c3ProviderReady, provider)
	load, okL := c3FindEvent(events, c3ConsumerLoading, consumer)
	active, okA := c3FindEvent(events, c3ConsumerActive, consumer)
	if !okR {
		return fmt.Errorf("T63-A: missing ProviderReady(P %d)", provider)
	}
	if !okL {
		return fmt.Errorf("T63-A: missing ConsumerLoading(C %d)", consumer)
	}
	if !okA {
		return fmt.Errorf("T63-A: missing ConsumerActive(C %d)", consumer)
	}
	if ready.ActivationID == 0 || load.ActivationID == 0 || active.ActivationID == 0 {
		return fmt.Errorf("T63-A: events must carry a nonzero ActivationID (activation-level ordering)")
	}
	if ready.Seq >= load.Seq {
		return fmt.Errorf("T63-A: ProviderReady(P %d) seq %d not strictly before ConsumerLoading(C %d) seq %d",
			provider, ready.Seq, consumer, load.Seq)
	}
	if load.Seq >= active.Seq {
		return fmt.Errorf("T63-A: ConsumerLoading(C %d) seq %d not before ConsumerActive seq %d",
			consumer, load.Seq, active.Seq)
	}
	return nil
}

// c3CheckT63Withdrawal verifies T63-B (v0.2 §24) for one provider/consumer
// pair, both at Unwind begin and at cleanup (activation) end:
//
//	ConsumerUnwindBegin(C) ≺ ProviderUnwindBegin(P)
//	ConsumerUnwindEnd(C)   ≺ ProviderUnwindEnd(P)
func c3CheckT63Withdrawal(events []c3Event, provider, consumer FiberID) error {
	cub, okCB := c3FindEvent(events, c3ConsumerUnwindBegin, consumer)
	cue, okCE := c3FindEvent(events, c3ConsumerUnwindEnd, consumer)
	pub, okPB := c3FindEvent(events, c3ProviderUnwindBegin, provider)
	pue, okPE := c3FindEvent(events, c3ProviderUnwindEnd, provider)
	switch {
	case !okCB:
		return fmt.Errorf("T63-B: missing ConsumerUnwindBegin(C %d)", consumer)
	case !okCE:
		return fmt.Errorf("T63-B: missing ConsumerUnwindEnd(C %d)", consumer)
	case !okPB:
		return fmt.Errorf("T63-B: missing ProviderUnwindBegin(P %d)", provider)
	case !okPE:
		return fmt.Errorf("T63-B: missing ProviderUnwindEnd(P %d)", provider)
	}
	if cub.ActivationID == 0 || cue.ActivationID == 0 || pub.ActivationID == 0 || pue.ActivationID == 0 {
		return fmt.Errorf("T63-B: events must carry a nonzero ActivationID (activation-level ordering)")
	}
	if cub.Seq >= pub.Seq {
		return fmt.Errorf("T63-B: ConsumerUnwindBegin(C %d) seq %d not strictly before ProviderUnwindBegin(P %d) seq %d",
			consumer, cub.Seq, provider, pub.Seq)
	}
	if cue.Seq >= pue.Seq {
		return fmt.Errorf("T63-B: ConsumerUnwindEnd(C %d) seq %d not strictly before ProviderUnwindEnd(P %d) seq %d",
			consumer, cue.Seq, provider, pue.Seq)
	}
	return nil
}

// ---------------------------------------------------------------------------
// Scenarios
// ---------------------------------------------------------------------------

func c3DriveProviderActive(t *testing.T, rt *Runtime, p *Fiber, rec *c3Recorder) {
	t.Helper()
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && p.State() == StateLoading }) {
		t.Fatalf("P ApplyDone never parked; state=%v pending=%d", p.State(), rt.detPending())
	}
	c3Boundary(t, rt)
	rec.observe(rt) // P Loading: no T63 event
	c3Check59(t, rt, "P Loading")
	if err := rt.detExecute(c3EnabledStep(t, rt, p.ID(), StepApplyDone)); err != nil {
		t.Fatal(err)
	}
	c3Boundary(t, rt)
	rec.observe(rt) // ProviderReady(P)
	c3Check59(t, rt, "P Active")
	if p.State() != StateActive {
		t.Fatalf("P not Active: %v", p.State())
	}
}

func c3LoadConsumerActive(t *testing.T, rt *Runtime, c *Fiber, rec *c3Recorder) {
	t.Helper()
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && c.State() == StateLoading }) {
		t.Fatalf("C ApplyDone never parked; state=%v pending=%d", c.State(), rt.detPending())
	}
	c3Boundary(t, rt)
	rec.observe(rt) // ConsumerLoading(C)
	c3Check59(t, rt, "C Loading")
	if err := rt.detExecute(c3EnabledStep(t, rt, c.ID(), StepApplyDone)); err != nil {
		t.Fatal(err)
	}
	c3Boundary(t, rt)
	rec.observe(rt) // ConsumerActive(C)
	c3Check59(t, rt, "P+C Active")
	if c.State() != StateActive {
		t.Fatalf("C not Active: %v", c.State())
	}
}

func c3Close(t *testing.T, rt *Runtime) {
	t.Helper()
	clctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	if err := rt.Close(clctx); err != nil {
		t.Fatalf("Close: %v", err)
	}
}

// TestC3T63ActivationOrderingSingleProviderConsumer verifies T63-A on the
// minimal P->C scenario: Load(P), P Active, then Load(C), C Loading, C Active.
// The driver probes an orchestrator boundary before every observation, so the
// ProviderReady event is recorded on a strictly earlier boundary than
// ConsumerLoading.
func TestC3T63ActivationOrderingSingleProviderConsumer(t *testing.T) {
	rt := detNew(t)
	c3Check59(t, rt, "initial")
	key := NewKey[string]("c3.t63.activation").Capability()
	p, err := rt.Load(&t66Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	rec := &c3Recorder{provider: p}
	rec.observe(rt)
	c3DriveProviderActive(t, rt, p, rec)

	c, err := rt.Load(&t66Comp{name: "C", key: key, consumer: true})
	if err != nil {
		t.Fatal(err)
	}
	rec.consumer = c
	c3LoadConsumerActive(t, rt, c, rec)

	if err := c3CheckT63Activation(rec.Events, p.ID(), c.ID()); err != nil {
		t.Fatalf("T63-A: %v", err)
	}
	c3LogTrace(t, rec.Events)
	c3Close(t, rt)
	t.Log("C-3 T63-A PASS: ProviderReady < ConsumerLoading < ConsumerActive")
}

// TestC3T63WithdrawalOrderingSingleProviderConsumer verifies T63-B: after
// Dispose(P) on the active P->C pair, C unwinds (and its cleanup completes)
// strictly before P begins unloading / finishes cleanup.
func TestC3T63WithdrawalOrderingSingleProviderConsumer(t *testing.T) {
	rt := detNew(t)
	c3Check59(t, rt, "initial")
	key := NewKey[string]("c3.t63.withdrawal").Capability()
	p, err := rt.Load(&t66Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	rec := &c3Recorder{provider: p}
	rec.observe(rt)
	c3DriveProviderActive(t, rt, p, rec)

	c, err := rt.Load(&t66Comp{name: "C", key: key, consumer: true})
	if err != nil {
		t.Fatal(err)
	}
	rec.consumer = c
	c3LoadConsumerActive(t, rt, c, rec)
	if err := c3CheckT63Activation(rec.Events, p.ID(), c.ID()); err != nil {
		t.Fatalf("precondition T63-A before withdrawal: %v", err)
	}

	// Dispose(P): consumer-first withdrawal. C begins unloading and its
	// UnwindDone parks; P stays Active (withdrawing) until C has ended.
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && c.State() == StateUnloading }) {
		t.Fatalf("C never Unloading after Dispose(P); C=%v P=%v pending=%d", c.State(), p.State(), rt.detPending())
	}
	c3Boundary(t, rt)
	rec.observe(rt) // ConsumerUnwindBegin(C)
	c3Check59(t, rt, "C Unloading (P withdrawing)")
	if p.State() == StateUnloading {
		t.Fatal("P must not unload before consumer C has ended (T63-B)")
	}

	// Execute C's UnwindDone: C cleanup completes (activation ends) and only
	// then may P begin unloading.
	if err := rt.detExecute(c3EnabledStep(t, rt, c.ID(), StepUnwindDone)); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return rt.detPending() >= 1 && p.State() == StateUnloading }) {
		t.Fatalf("P never Unloading after C ended; C=%v P=%v pending=%d", c.State(), p.State(), rt.detPending())
	}
	c3Boundary(t, rt)
	rec.observe(rt) // ConsumerUnwindEnd(C) + ProviderUnwindBegin(P)
	c3Check59(t, rt, "P Unloading (C ended)")

	if err := rt.detExecute(c3EnabledStep(t, rt, p.ID(), StepUnwindDone)); err != nil {
		t.Fatal(err)
	}
	if !c2Wait(2000, func() bool { return p.State() == StateGone && rt.detPending() == 0 }) {
		t.Fatalf("P never Gone; P=%v C=%v pending=%d", p.State(), c.State(), rt.detPending())
	}
	c3Boundary(t, rt)
	rec.observe(rt) // ProviderUnwindEnd(P)
	c3Check59(t, rt, "P Gone")

	if err := c3CheckT63Withdrawal(rec.Events, p.ID(), c.ID()); err != nil {
		t.Fatalf("T63-B: %v", err)
	}
	c3LogTrace(t, rec.Events)
	c3Close(t, rt)
	t.Log("C-3 T63-B PASS: ConsumerUnwindBegin < ProviderUnwindBegin; ConsumerUnwindEnd < ProviderUnwindEnd")
}

// TestC3T63OracleSanity proves the pure T63 oracle can recognize precedence
// violations in synthetic traces (no Runtime needed; the runtime never enters
// the bad states).
func TestC3T63OracleSanity(t *testing.T) {
	const (
		pID  FiberID      = 1
		cID  FiberID      = 2
		pAct ActivationID = 1
		cAct ActivationID = 2
	)
	goodAct := []c3Event{
		{Seq: 1, Kind: c3ProviderReady, FiberID: pID, ActivationID: pAct},
		{Seq: 2, Kind: c3ConsumerLoading, FiberID: cID, ActivationID: cAct},
		{Seq: 3, Kind: c3ConsumerActive, FiberID: cID, ActivationID: cAct},
	}
	if err := c3CheckT63Activation(goodAct, pID, cID); err != nil {
		t.Fatalf("good activation trace must PASS: %v", err)
	}
	badEarly := []c3Event{
		{Seq: 1, Kind: c3ConsumerLoading, FiberID: cID, ActivationID: cAct},
		{Seq: 2, Kind: c3ProviderReady, FiberID: pID, ActivationID: pAct},
		{Seq: 3, Kind: c3ConsumerActive, FiberID: cID, ActivationID: cAct},
	}
	if err := c3CheckT63Activation(badEarly, pID, cID); err == nil {
		t.Fatal("ConsumerLoading before ProviderReady must FAIL")
	}
	eqSeq := []c3Event{
		{Seq: 1, Kind: c3ProviderReady, FiberID: pID, ActivationID: pAct},
		{Seq: 1, Kind: c3ConsumerLoading, FiberID: cID, ActivationID: cAct},
	}
	if err := c3CheckT63Activation(eqSeq, pID, cID); err == nil {
		t.Fatal("ProviderReady == ConsumerLoading (non-strict) must FAIL")
	}
	if err := c3CheckT63Activation(goodAct[1:], pID, cID); err == nil {
		t.Fatal("missing ProviderReady must FAIL")
	}

	goodWd := []c3Event{
		{Seq: 1, Kind: c3ConsumerUnwindBegin, FiberID: cID, ActivationID: cAct},
		{Seq: 2, Kind: c3ConsumerUnwindEnd, FiberID: cID, ActivationID: cAct},
		{Seq: 3, Kind: c3ProviderUnwindBegin, FiberID: pID, ActivationID: pAct},
		{Seq: 4, Kind: c3ProviderUnwindEnd, FiberID: pID, ActivationID: pAct},
	}
	if err := c3CheckT63Withdrawal(goodWd, pID, cID); err != nil {
		t.Fatalf("good withdrawal trace must PASS: %v", err)
	}
	badWd := []c3Event{
		{Seq: 1, Kind: c3ProviderUnwindBegin, FiberID: pID, ActivationID: pAct},
		{Seq: 2, Kind: c3ConsumerUnwindBegin, FiberID: cID, ActivationID: cAct},
		{Seq: 3, Kind: c3ProviderUnwindEnd, FiberID: pID, ActivationID: pAct},
		{Seq: 4, Kind: c3ConsumerUnwindEnd, FiberID: cID, ActivationID: cAct},
	}
	if err := c3CheckT63Withdrawal(badWd, pID, cID); err == nil {
		t.Fatal("Provider unload before Consumer unload must FAIL")
	}
	if err := c3CheckT63Withdrawal(goodWd[2:], pID, cID); err == nil {
		t.Fatal("missing consumer withdrawal events must FAIL")
	}
	t.Log("C-3 oracle sanity PASS: good traces PASS, mutated traces FAIL")
}
