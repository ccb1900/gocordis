package runtime

import (
	"errors"
	"fmt"
	"sync"
	"testing"
	"time"
)

// Isolation conformance under the paper model (ADR-0001; paper §4.4 Isolation,
// Definition 24) — supersedes the archived GAP-01 "retiring shadows ancestor"
// decision, which presumed an ancestor-fallback lookup the paper does not have.
//
// Paper semantics verified here:
//
//	Each declared key resolves against EXACTLY ONE realm — the fiber's
//	effective realm for that key (the per-key table ρ, Definition 24). There
//	is no ancestor walk: a child namespace simply has no provider after its
//	provider goes Gone, and the root namespace is unaffected either way.
//	Retiring != Absent is preserved: while a provider record is Retiring it
//	stays physically present in its namespace (owner still unwinding) and the
//	namespace keeps reporting unavailable; after the owner's unwind removes
//	the record, recovery happens when a provider becomes Active again IN THE
//	SAME namespace (B reload), never by falling back to another realm's
//	provider.
//
// The scenario is executed on the deterministic kernel driver: every
// lifecycle step (Active -> Retiring -> Gone) is controlled by parked
// completion admission, and every semantic observation is an
// orchestrator-linearized probe / Snapshot. B's own Cleanup is gated by the
// test so the observation can deterministically catch B mid-Unwind while its
// retiring record is still physically present (the deepest "Retiring" point);
// the final rebind of the child consumer onto the ancestor is explicitly
// harness-driven (a reconcile probe) — the runtime does not auto-sweep on
// record removal, only on a new activation becoming Active.

var gap01Key = NewKey[string]("gap01.retiring-shadow")

// gap01Provider provides key with a fixed tag. When release is non-nil, its
// Cleanup blocks until release is closed, holding the owning activation in
// Unloading so the test can observe the retiring record mid-unwind.
type gap01Provider struct {
	name    string
	tag     string
	release chan struct{}
}

func (c *gap01Provider) Name() string          { return c.name }
func (c *gap01Provider) Inject() []Dependency  { return nil }
func (c *gap01Provider) Provide() []Capability { return []Capability{gap01Key.Capability()} }
func (c *gap01Provider) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, gap01Key, c.tag); err != nil {
		return nil, err
	}
	if c.release == nil {
		return nil, nil
	}
	return func() error {
		<-c.release
		return nil
	}, nil
}

// gap01Consumer requires key and reports the value each successful activation
// resolved (one message per activation; the runtime applies each activation
// exactly once).
type gap01Consumer struct {
	name string
	seen chan string
}

func (c *gap01Consumer) Name() string          { return c.name }
func (c *gap01Consumer) Inject() []Dependency  { return []Dependency{Requires(gap01Key)} }
func (c *gap01Consumer) Provide() []Capability { return nil }
func (c *gap01Consumer) Apply(ctx *Context) (Cleanup, error) {
	v, err := Require(ctx, gap01Key)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v
	}
	return nil, nil
}

// gap01ScopeHost mounts provider B and, once B is Active, consumer C inside
// the explicit child realm the host itself was mounted into (children inherit
// the host realm). Mounting C after B is Active makes C's nearest-binding
// capture deterministically B (never the root ancestor A). ch receives B then
// C, in creation order.
type gap01ScopeHost struct {
	ch    chan *Fiber
	bRel  chan struct{}
	cSeen chan string
}

func (c *gap01ScopeHost) Name() string          { return "gap01-scope-host" }
func (c *gap01ScopeHost) Inject() []Dependency  { return nil }
func (c *gap01ScopeHost) Provide() []Capability { return nil }
func (c *gap01ScopeHost) Apply(ctx *Context) (Cleanup, error) {
	b, err := ctx.Child(&gap01Provider{name: "B", tag: "B", release: c.bRel})
	if err != nil {
		return nil, err
	}
	if err := b.Ready(ctx.Context()); err != nil {
		return nil, err
	}
	x, err := ctx.Child(&gap01Consumer{name: "C", seen: c.cSeen})
	if err != nil {
		return nil, err
	}
	c.ch <- b // C is mounted after B is Active; C's own activation is not waited on here
	c.ch <- x
	return nil, nil
}

// gap01Activator mounts the scope host with WithScope (one explicit child
// realm whose parent is the root realm) and reports the host fiber handle.
type gap01Activator struct {
	host   *gap01ScopeHost
	hostCh chan *Fiber
}

func (c *gap01Activator) Name() string          { return "gap01-activator" }
func (c *gap01Activator) Inject() []Dependency  { return nil }
func (c *gap01Activator) Provide() []Capability { return nil }
func (c *gap01Activator) Apply(ctx *Context) (Cleanup, error) {
	host, err := ctx.Child(c.host, WithScope())
	if err != nil {
		return nil, err
	}
	c.hostCh <- host
	return nil, nil
}

// ---------------------------------------------------------------------------
// Deterministic driver helpers
// ---------------------------------------------------------------------------

func gap01Boundary(t *testing.T, rt *Runtime) {
	t.Helper()
	p := &c3BoundaryProbe{done: make(chan struct{})}
	if !rt.submit(p) {
		t.Fatalf("gap01: boundary probe rejected (runtime closed?)")
	}
	select {
	case <-p.done:
	case <-time.After(10 * time.Second):
		t.Fatal("gap01: boundary probe timed out (orchestrator stalled?)")
	}
}

// gap01Admit drives the parked ApplyDone completion of f into the
// orchestrator, retrying transient admission rejections (never lost, Phase B).
func gap01AdmitApply(t *testing.T, rt *Runtime, f *Fiber) {
	t.Helper()
	if !waitB(t, rt, 5000, func() bool {
		for _, s := range rt.detEnabledSteps() {
			if s.Kind == StepApplyDone && s.FiberID == f.ID() {
				err := rt.detExecute(s)
				if err == nil {
					return true
				}
				if errors.Is(err, errDetStale) {
					t.Fatalf("gap01: ApplyDone(%d) stale: %v", f.ID(), err)
				}
				return false // admission rejected; parked completion retries
			}
		}
		return false
	}) {
		t.Fatalf("gap01: ApplyDone(%d) never parked; state=%v pending=%d", f.ID(), f.State(), rt.detPending())
	}
}

func gap01AdmitUnwind(t *testing.T, rt *Runtime, f *Fiber) {
	t.Helper()
	if !waitB(t, rt, 5000, func() bool {
		for _, s := range rt.detEnabledSteps() {
			if s.Kind == StepUnwindDone && s.FiberID == f.ID() {
				err := rt.detExecute(s)
				if err == nil {
					return true
				}
				if errors.Is(err, errDetStale) {
					t.Fatalf("gap01: UnwindDone(%d) stale: %v", f.ID(), err)
				}
				return false
			}
		}
		return false
	}) {
		t.Fatalf("gap01: UnwindDone(%d) never parked; state=%v pending=%d", f.ID(), f.State(), rt.detPending())
	}
}

func gap01WaitState(t *testing.T, rt *Runtime, f *Fiber, want FiberState) {
	t.Helper()
	if !waitB(t, rt, 5000, func() bool { return f.State() == want }) {
		t.Fatalf("gap01: fiber %s state=%v, want %v (pending=%d)", f.Name(), f.State(), want, rt.detPending())
	}
}

// gap01DriveActive admits every parked ApplyDone completion until all target
// fibers are Active. Child fibers park asynchronously as their Apply
// goroutines finish, so the driver keeps admitting whatever is enabled; the
// lifecycle order is decided by the orchestrator, not by this loop.
func gap01DriveActive(t *testing.T, rt *Runtime, targets ...*Fiber) {
	t.Helper()
	if !waitB(t, rt, 8000, func() bool {
		for _, s := range rt.detEnabledSteps() {
			if s.Kind == StepApplyDone {
				if err := rt.detExecute(s); err != nil && !errors.Is(err, errDetAdmission) && !errors.Is(err, errDetStale) {
					return false
				}
			}
		}
		for _, f := range targets {
			if f == nil || f.State() != StateActive {
				return false
			}
		}
		return true
	}) {
		var states []string
		for _, f := range targets {
			if f != nil {
				states = append(states, fmt.Sprintf("%s=%v", f.Name(), f.State()))
			}
		}
		t.Fatalf("gap01: drive-active timeout; states: %v pending=%d", states, rt.detPending())
	}
}

func gap01Recv(t *testing.T, ch chan *Fiber, what string) *Fiber {
	t.Helper()
	select {
	case f := <-ch:
		return f
	case <-time.After(10 * time.Second):
		t.Fatalf("gap01: timeout waiting for %s", what)
		return nil
	}
}

func gap01RecvTag(t *testing.T, ch chan string, what string) string {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(10 * time.Second):
		t.Fatalf("gap01: timeout waiting for %s", what)
		return ""
	}
}

// ---------------------------------------------------------------------------
// Orchestrator-linearized semantic probes
// ---------------------------------------------------------------------------

// gap01Resolve runs resolveDependency at the orchestrator boundary for ONE
// consumer fiber: the probe reports the actual dependency resolution result
// the consumer would see — the same decision captureDependencies /
// dependenciesSatisfied rely on. Paper model (ADR-0001): resolution is a
// fiber-level question; the consumer's per-key table decides the namespace.
type gap01ResolveProbe struct {
	consumer *Fiber
	key      CapabilityKey
	reply    chan gap01ResolveResult
}

type gap01ResolveResult struct {
	id ProviderIdentity
	ok bool
}

func (p *gap01ResolveProbe) apply(o *orchestrator) {
	id, ok := o.resolveDependency(p.consumer, p.key)
	p.reply <- gap01ResolveResult{id: id, ok: ok}
}

func gap01Resolve(t *testing.T, rt *Runtime, consumer *Fiber, key CapabilityKey) (ProviderIdentity, bool) {
	t.Helper()
	p := &gap01ResolveProbe{consumer: consumer, key: key, reply: make(chan gap01ResolveResult, 1)}
	if !rt.submit(p) {
		t.Fatalf("gap01: resolve probe rejected (runtime closed?)")
	}
	select {
	case res := <-p.reply:
		return res.id, res.ok
	case <-time.After(10 * time.Second):
		t.Fatalf("gap01: resolve probe timed out (orchestrator stalled?)")
		return ProviderIdentity{}, false
	}
}

// gap01Reconcile asks the orchestrator to re-evaluate one fiber. This is the
// driver analog of the sweepWaiting that a replacement activation would
// trigger automatically; the runtime does not auto-sweep on record removal.
type gap01Reconcile struct {
	fiber *Fiber
	done  chan struct{}
}

func (p *gap01Reconcile) apply(o *orchestrator) {
	o.reconcile(p.fiber)
	close(p.done)
}

func gap01ReconcileFiber(t *testing.T, rt *Runtime, f *Fiber) {
	t.Helper()
	p := &gap01Reconcile{fiber: f, done: make(chan struct{})}
	if !rt.submit(p) {
		t.Fatalf("gap01: reconcile probe rejected (runtime closed?)")
	}
	select {
	case <-p.done:
	case <-time.After(10 * time.Second):
		t.Fatal("gap01: reconcile probe timed out (orchestrator stalled?)")
	}
}

// ---------------------------------------------------------------------------
// Snapshot helpers
// ---------------------------------------------------------------------------

func gap01Snap(t *testing.T, rt *Runtime) RuntimeSnapshot {
	t.Helper()
	return u2Snap(t, rt)
}

func gap01FiberRow(s RuntimeSnapshot, id FiberID) (FiberSnapshot, bool) {
	for _, f := range s.Fibers {
		if f.ID == id {
			return f, true
		}
	}
	return FiberSnapshot{}, false
}

func gap01ProviderRow(s RuntimeSnapshot, owner FiberID) (ProviderView, bool) {
	for _, p := range s.Providers {
		if p.OwnerFiberID == owner {
			return p, true
		}
	}
	return ProviderView{}, false
}

func gap01Dep(s RuntimeSnapshot, consumer FiberID, key CapabilityKey) (DependencyView, bool) {
	ks := key.String()
	for _, d := range s.Dependencies {
		if d.ConsumerFiberID == consumer && d.Key == ks {
			return d, true
		}
	}
	return DependencyView{}, false
}

// ---------------------------------------------------------------------------
// Test
// ---------------------------------------------------------------------------

// TestRetiringProviderShadowsAncestor (GAP-01 conformance, §7–§15 of the
// GAP-01 spec): a Retiring nearest provider must shadow an Active ancestor
// for the same key until the record is physically removed (owner Gone); the
// ancestor must remain valid outside the child realm; after Gone the ancestor
// becomes eligible for the child realm again.
//
// Timeline (deterministic driver):
//
//	T0 root Provider A(K) Active
//	T1 root consumer D(K) binds A
//	T2 child realm {Provider B(K), Consumer C(K)}; C binds B (nearest)
//	T3 B.Dispose() -> B record retiring; C withdraws (consumer-first)
//	T4 observation while B is still gated / then while B is Unloading with its
//	   retiring record physically present: resolve(C, K) unavailable (and
//	   never A); A valid at root (D binds A); D unaffected
//	T5 release B's gated Cleanup -> B's unwind removes the record -> B Gone
//	T6 resolve(C, K) still unavailable — no ancestor fallback (paper §4.4:
//	   one namespace per binding); B reload makes B Active again in the child
//	   namespace; a driver reconcile rebinds C onto B and C activates on B
func TestRetiringProviderShadowsAncestor(t *testing.T) {
	rt := detNew(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	key := gap01Key

	// T0 — root provider A (ancestor) Active.
	aF, err := rt.Load(&gap01Provider{name: "A", tag: "A"})
	if err != nil {
		t.Fatal(err)
	}
	gap01AdmitApply(t, rt, aF)
	gap01WaitState(t, rt, aF, StateActive)

	// T1 — root consumer D binds A (ancestor valid outside any shadow).
	dSeen := make(chan string, 4)
	dF, err := rt.Load(&gap01Consumer{name: "D", seen: dSeen})
	if err != nil {
		t.Fatal(err)
	}
	gap01AdmitApply(t, rt, dF)
	gap01WaitState(t, rt, dF, StateActive)
	if v := gap01RecvTag(t, dSeen, "D activation"); v != "A" {
		t.Fatalf("D resolved %q, want A's tag", v)
	}

	// T2 — child realm (explicit scope) mounts B then C.
	hostCh := make(chan *Fiber, 1)
	scopeCh := make(chan *Fiber, 2)
	bRel := make(chan struct{})
	var closeRel sync.Once
	t.Cleanup(func() {
		// Never leave B's gated Cleanup blocked: a failed assertion must not
		// hang the deferred Close drain.
		closeRel.Do(func() {
			select {
			case <-bRel:
			default:
				close(bRel)
			}
		})
	})
	cSeen := make(chan string, 4)
	actF, err := rt.Load(&gap01Activator{
		host:   &gap01ScopeHost{ch: scopeCh, bRel: bRel, cSeen: cSeen},
		hostCh: hostCh,
	})
	if err != nil {
		t.Fatal(err)
	}
	hostF := gap01Recv(t, hostCh, "scope host fiber")
	// Admit all ApplyDone completions until activator + scope host are Active;
	// this deterministically admits B's ApplyDone (B Active) inside the host's
	// Apply, then C mounts, then C's ApplyDone parks.
	gap01DriveActive(t, rt, actF, hostF)
	bF := gap01Recv(t, scopeCh, "B provider fiber")
	cF := gap01Recv(t, scopeCh, "C consumer fiber")
	gap01DriveActive(t, rt, cF)
	gap01WaitState(t, rt, bF, StateActive)
	gap01WaitState(t, rt, cF, StateActive)
	if bF.realm == nil || cF.realm == nil || bF.realm != cF.realm {
		t.Fatalf("B/C must share the child realm: b realm %p c realm %p", bF.realm, cF.realm)
	}
	if bF.realm == rt.rootRealm {
		t.Fatal("B must live in a child realm, not the root realm")
	}
	if aF.realm != rt.rootRealm {
		t.Fatalf("A must live in the root realm, got %p", aF.realm)
	}

	// C bound B (nearest), not A.
	if v := gap01RecvTag(t, cSeen, "C first activation"); v != "B" {
		t.Fatalf("C first activation resolved %q, want B's tag (nearest)", v)
	}
	pre := gap01Snap(t, rt)
	if row, ok := gap01FiberRow(pre, cF.ID()); !ok || row.State != StateActive {
		t.Fatalf("C not Active in snapshot: %+v", row)
	} else if dep, ok := gap01Dep(pre, cF.ID(), key.Capability()); !ok || dep.Status != DependencySatisfied || dep.ProviderFiberID != bF.ID() {
		t.Fatalf("C pre-bound to %+v (want B %d): %+v", dep, bF.ID(), dep)
	}

	// T3 — Dispose B through the real lifecycle (no synthetic retiring flag).
	if err := bF.Dispose(); err != nil {
		t.Fatal(err)
	}
	gap01Boundary(t, rt)

	// Phase 1a — retiring window while C is still withdrawing (Unloading):
	// the consumer-first withdrawal has gated B's own Unload on C's end.
	// resolveDependency must NOT fall back to A.
	gap01WaitState(t, rt, cF, StateUnloading)
	if st := bF.State(); st != StateActive {
		t.Fatalf("B state = %v during consumer gate, want Active (withdrawing but gated)", st)
	}
	gap01AssertRetiringShadow(t, rt, key, aF, bF, cF, dF, "phase-1a (C Unloading)")
	snap1a := gap01Snap(t, rt)
	row, ok := gap01FiberRow(snap1a, cF.ID())
	if !ok || row.State != StateUnloading {
		t.Fatalf("C snapshot during retirement = %+v ok=%v, want Unloading", row, ok)
	}
	if dep, ok := gap01Dep(snap1a, cF.ID(), key.Capability()); !ok || dep.Status != DependencyWithdrawn {
		t.Fatalf("C dependency during retirement = %+v, want Withdrawn", dep)
	}
	if st := dF.State(); st != StateActive {
		t.Fatalf("root consumer D disturbed by child retirement: state %v", st)
	}

	// Phase 1b — admit C's UnwindDone: C lands Pending (dependency loss, not
	// failure) and B's own Unload starts. B's Cleanup is gated by the test, so
	// B is deterministically Unloading with its retiring record still
	// physically present — the deepest Retiring observation point.
	gap01AdmitUnwind(t, rt, cF)
	gap01Boundary(t, rt)
	gap01WaitState(t, rt, cF, StatePending)
	gap01WaitState(t, rt, bF, StateUnloading)
	if err := cF.Err(); err != nil {
		t.Fatalf("C Err() = %v, want nil (dependency loss is not a failure)", err)
	}
	gap01AssertRetiringShadow(t, rt, key, aF, bF, cF, dF, "phase-1b (B Unloading, record present)")
	snap1b := gap01Snap(t, rt)
	if row, ok := gap01ProviderRow(snap1b, bF.ID()); !ok {
		t.Fatal("B's retiring record must still exist during B's own Unloading")
	} else if !row.Retiring || row.State != StateUnloading {
		t.Fatalf("B record during Unloading = %+v, want Retiring + owner Unloading", row)
	} else if row.ScopeID == 0 || row.ScopeID != cF.realm.id {
		t.Fatalf("B record scope = %d, want child realm %d (realm-local)", row.ScopeID, cF.realm.id)
	}
	if st := dF.State(); st != StateActive {
		t.Fatalf("root consumer D disturbed during B Unloading: state %v", st)
	}

	// T5 — release B's gated Cleanup; B's unwind removes the record and parks
	// UnwindDone; admit it -> B Gone.
	closeRel.Do(func() { close(bRel) })
	gap01AdmitUnwind(t, rt, bF)
	gap01Boundary(t, rt)
	gap01WaitState(t, rt, bF, StateGone)

	// T6 — after the record is removed (owner Gone): paper §4.4 Isolation —
	// C's key resolves in exactly one namespace, the child realm. It has no
	// provider there and MUST NOT fall back to A; the root namespace is
	// unaffected (D still binds A).
	if id, ok := gap01Resolve(t, rt, cF, key.Capability()); ok {
		t.Fatalf("phase-2 resolve(C) = %+v, want unavailable (no ancestor fallback after B Gone)", id)
	}
	if id, ok := gap01Resolve(t, rt, dF, key.Capability()); !ok || id.FiberID != aF.ID() {
		t.Fatalf("root resolve (as D) = %+v ok=%v after B Gone, want A", id, ok)
	}
	snap2 := gap01Snap(t, rt)
	if row, ok := gap01ProviderRow(snap2, bF.ID()); ok {
		t.Fatalf("B's record must be physically removed after Gone: %+v", row)
	}
	if _, ok := gap01FiberRow(snap2, bF.ID()); ok {
		t.Fatal("Gone fiber must not appear in the live snapshot")
	}
	if row, ok := gap01ProviderRow(snap2, aF.ID()); !ok || row.Retiring {
		t.Fatalf("A record after B Gone = %+v, want present + not retiring", row)
	}
	if st := cF.State(); st != StatePending {
		t.Fatalf("C state after B Gone = %v, want Pending (no automatic rebind on record removal)", st)
	}

	// T7 — recovery INSIDE the child namespace: B reloads and becomes Active
	// again; C re-activates on B (never on A). This is the paper-true rebinding
	// path that the archived GAP-01 rule attributed to ancestor fallback.
	if err := bF.Load(); err != nil {
		t.Fatal(err)
	}
	gap01AdmitApply(t, rt, bF)
	gap01WaitState(t, rt, bF, StateActive)
	gap01ReconcileFiber(t, rt, cF)
	gap01AdmitApply(t, rt, cF)
	gap01WaitState(t, rt, cF, StateActive)
	if id, ok := gap01Resolve(t, rt, cF, key.Capability()); !ok || id.FiberID != bF.ID() {
		t.Fatalf("phase-3 resolve(C) = %+v ok=%v, want B (%d) after reload", id, ok, bF.ID())
	}
	if v := gap01RecvTag(t, cSeen, "C re-activation"); v != "B" {
		t.Fatalf("C re-activation resolved %q, want B's tag", v)
	}
	if st := dF.State(); st != StateActive {
		t.Fatalf("root consumer D disturbed by child recovery: state %v", st)
	}

	// Post-recovery snapshot: C bound to B, root D still bound to A.
	post := gap01Snap(t, rt)
	if row, ok := gap01FiberRow(post, cF.ID()); !ok || row.State != StateActive {
		t.Fatalf("C not Active after recovery: %+v", row)
	} else if dep, ok := gap01Dep(post, cF.ID(), key.Capability()); !ok || dep.Status != DependencySatisfied || dep.ProviderFiberID != bF.ID() {
		t.Fatalf("C post-bound to %+v (want B %d): %+v", dep, bF.ID(), dep)
	}

	// Deterministic Close: every fiber reaches Gone, pending completions zero,
	// orchestrator drained.
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("Close: %v", err)
	}
	for name, f := range map[string]*Fiber{"A": aF, "D": dF, "activator": actF, "host": hostF, "B": bF, "C": cF} {
		if f.State() != StateGone {
			t.Fatalf("%s state after Close = %v, want Gone", name, f.State())
		}
	}
	if rt.detPending() != 0 {
		t.Fatalf("pending = %d after Close, want 0", rt.detPending())
	}
	select {
	case <-rt.orch.done:
	default:
		t.Fatal("orchestrator not done after Close")
	}
}

// gap01AssertRetiringShadow asserts the shared Phase-1 invariants at an
// orchestrator boundary (paper model):
//   - resolveDependency for the child consumer reports unavailable (the child
//     namespace has no Active provider while B retires; no ancestor fallback);
//   - B's record exists and is retiring (never removed while Retiring);
//   - A remains resolvable in the root namespace (D's binding unaffected).
func gap01AssertRetiringShadow(t *testing.T, rt *Runtime, key Key[string], aF, bF, cF, dF *Fiber, when string) {
	t.Helper()
	id, ok := gap01Resolve(t, rt, cF, key.Capability())
	if ok {
		t.Fatalf("%s: resolve(C) unexpectedly selected %+v — retiring namespace must report unavailable", when, id)
	}
	if id.FiberID == aF.ID() {
		t.Fatalf("%s: resolve(C) selected root provider A (%d) while B retires — no ancestor fallback", when, aF.ID())
	}
	id, ok = gap01Resolve(t, rt, dF, key.Capability())
	if !ok || id.FiberID != aF.ID() {
		t.Fatalf("%s: resolve(D) = %+v ok=%v, want A (root namespace unaffected)", when, id, ok)
	}
	snap := gap01Snap(t, rt)
	row, found := gap01ProviderRow(snap, bF.ID())
	if !found {
		t.Fatalf("%s: B's retiring record missing — Retiring must not be implemented as Absent", when)
	}
	if !row.Retiring {
		t.Fatalf("%s: B record = %+v, want Retiring=true", when, row)
	}
	aRow, aFound := gap01ProviderRow(snap, aF.ID())
	if !aFound || aRow.Retiring {
		t.Fatalf("%s: A record = %+v found=%v, want present + not retiring", when, aRow, aFound)
	}
}
