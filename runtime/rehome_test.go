package runtime

import (
	"context"
	"errors"
	"fmt"
	"testing"
	"time"
)

// G-2 conformance: Rehome — the realm-move SHORT ROUTE (paper §5.2.1, "a
// realm moved without reloading its provider").
//
//	R1 (short route): the provider fiber stays Active on the SAME activation.
//	R2 (declaration semantics): old-namespace consumers withdraw to Pending
//	    and stay Pending (their ρ is unchanged).
//	R3 (fresh namespace): the provisions are resolvable in the fresh
//	    namespace and absent from the old one.
//	R4 (recovery): a provider in the OLD namespace re-satisfies old-namespace
//	    consumers (they follow the declaration, unprompted).
//	R5 (no leak): disposing the rehomed provider removes the provisions from
//	    the fresh namespace (the unwind finds the moved record).

type rehomeProvider struct{ tag string }

func (c *rehomeProvider) Name() string          { return "rehome-provider" }
func (c *rehomeProvider) Inject() []Dependency  { return nil }
func (c *rehomeProvider) Provide() []Capability { return []Capability{rehomeKey.Capability()} }
func (c *rehomeProvider) Apply(ctx *Context) (Cleanup, error) {
	return nil, Provide(ctx, rehomeKey, rehomeKeyVal{tag: c.tag})
}

type rehomeKeyVal struct{ tag string }

var rehomeKey = NewKey[rehomeKeyVal]("rehome.service")

// rehomeOtherKey exists only to give Isolate a DIFFERENT key to re-home, so
// that rehomeKey's provision stays in the PARENT realm (an isolation override
// whose realm differs from the fiber's own base realm — the Rehome bug case).
var rehomeOtherKey = NewKey[rehomeKeyVal]("rehome.other")

type rehomeDualProvider struct{}

func (c *rehomeDualProvider) Name() string         { return "rehome-dual-provider" }
func (c *rehomeDualProvider) Inject() []Dependency { return nil }
func (c *rehomeDualProvider) Provide() []Capability {
	return []Capability{rehomeKey.Capability(), rehomeOtherKey.Capability()}
}
func (c *rehomeDualProvider) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, rehomeKey, rehomeKeyVal{tag: "iso"}); err != nil {
		return nil, err
	}
	return nil, Provide(ctx, rehomeOtherKey, rehomeKeyVal{tag: "other"})
}

type rehomeConsumer struct {
	seen chan string
}

func (c *rehomeConsumer) Name() string          { return "rehome-consumer" }
func (c *rehomeConsumer) Inject() []Dependency  { return []Dependency{Requires(rehomeKey)} }
func (c *rehomeConsumer) Provide() []Capability { return nil }
func (c *rehomeConsumer) Apply(ctx *Context) (Cleanup, error) {
	v, err := Require(ctx, rehomeKey)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v.tag
	}
	return nil, nil
}

func rehomeWaitState(t *testing.T, f *Fiber, want FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout %s -> %v (state %v err %v)", f.Name(), want, f.State(), f.Err())
}

// resolveProbe assembles a consumer that resolves the key and reports the
// provider identity via an orchestrator-linearized read.
type resolveResult struct {
	id ProviderIdentity
	ok bool
}

func rehomeResolve(t *testing.T, rt *Runtime, consumer *Fiber, key CapabilityKey) (ProviderIdentity, bool) {
	t.Helper()
	res := make(chan struct {
		id ProviderIdentity
		ok bool
	})
	rt.submit(&resolveProbe{consumer: consumer, key: key, out: res})
	select {
	case r := <-res:
		return r.id, r.ok
	case <-time.After(8 * time.Second):
		t.Fatal("resolve probe timeout")
		return ProviderIdentity{}, false
	}
}

type resolveProbe struct {
	consumer *Fiber
	key      CapabilityKey
	out      chan struct {
		id ProviderIdentity
		ok bool
	}
}

func (p *resolveProbe) apply(o *orchestrator) {
	id, ok := o.resolveDependency(p.consumer, p.key)
	p.out <- struct {
		id ProviderIdentity
		ok bool
	}{id, ok}
}

// TestRehomeShortRoute — R1+R2+R3: the provider stays Active on the same
// activation (never reloaded); the old-namespace consumer withdraws to
// Pending; the provisions move to the fresh namespace.
func TestRehomeShortRoute(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, err := rt.Load(&rehomeProvider{tag: "v1"})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)

	seen := make(chan string, 4)
	cf, err := rt.Load(&rehomeConsumer{seen: seen})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, cf)
	rehomeWaitState(t, cf, StateActive)
	if v := <-seen; v != "v1" {
		t.Fatalf("initial binding = %q, want v1", v)
	}

	actID := pf.activation.id
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}

	// R1 — same fiber, same activation, still Active (no reload).
	rehomeWaitState(t, pf, StateActive)
	pf.mu.RLock()
	sameAct := pf.activation != nil && pf.activation.id == actID
	pf.mu.RUnlock()
	if !sameAct {
		t.Fatal("rehome reloaded the provider — the short route must not")
	}

	// R2 — the old-namespace consumer withdrew to Pending.
	rehomeWaitState(t, cf, StatePending)

	// R3 — provisions live in the fresh namespace only. The provider's
	// declared keys all resolve through the fresh realm; the root namespace
	// no longer holds the record.
	fresh := pf.keyRealms[rehomeKey.Capability()]
	if fresh == nil || fresh == rt.rootRealm {
		t.Fatalf("fresh namespace missing: %v", pf.keyRealms)
	}
	if _, ok := rt.rootRealm.lookupOwn(rehomeKey.Capability()); ok {
		t.Fatal("old namespace still holds the provision after rehome")
	}
	probe := rehomeResolveAs(t, rt, pf, rehomeKey.Capability())
	if !probe.ok || probe.id.FiberID != pf.ID() {
		t.Fatalf("fresh-namespace resolve = %+v", probe)
	}
}

// TestRehomeOldNamespaceRecovers — R4: a provider mounted in the OLD
// namespace re-satisfies the withdrawn consumer, unprompted.
func TestRehomeOldNamespaceRecovers(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, _ := rt.Load(&rehomeProvider{tag: "v1"})
	rehomeAdmit(t, rt, pf)
	seen := make(chan string, 4)
	cf, _ := rt.Load(&rehomeConsumer{seen: seen})
	rehomeAdmit(t, rt, cf)
	rehomeWaitState(t, cf, StateActive)

	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, cf, StatePending)
	// Drain the stale delivery from the first activation.
	for {
		select {
		case v := <-seen:
			if v == "old-ns" {
				t.Fatal("unexpected stale delivery")
			}
		default:
			goto drained
		}
	}
drained:
	// A new provider in the OLD namespace (root): the consumer follows the
	// declaration unprompted.
	p2, err := rt.Load(&rehomeProvider{tag: "old-ns"})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, p2)
	rehomeWaitState(t, cf, StateActive)
	epWaitSeenLike(t, seen, "old-ns")
}

// TestRehomeDisposeNoLeak — R5: disposing the rehomed provider removes the
// provisions from the fresh namespace (the unwind finds the moved record).
func TestRehomeDisposeNoLeak(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, _ := rt.Load(&rehomeProvider{tag: "v1"})
	rehomeAdmit(t, rt, pf)
	seen := make(chan string, 4)
	cf, _ := rt.Load(&rehomeConsumer{seen: seen})
	rehomeAdmit(t, rt, cf)
	rehomeWaitState(t, cf, StateActive)

	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatal(err)
	}
	rehomeWaitState(t, cf, StatePending) // consumer detached before provider teardown

	fresh := pf.keyRealms[rehomeKey.Capability()]
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if _, ok := fresh.lookupOwn(rehomeKey.Capability()); ok {
		t.Fatal("fresh namespace still holds the provision after Gone (leak)")
	}
}

// TestRehomeDoubleAndGuards — double rehome is fine; rehome on a non-Active
// fiber fails with ErrRehomeNotActive.
func TestRehomeDoubleAndGuards(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, _ := rt.Load(&rehomeProvider{tag: "v1"})
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)

	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatal(err)
	}
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("second rehome: %v", err)
	}

	// A Pending (never-activated) fiber cannot rehome.
	pf2, err := rt.Load(&rehomeProvider{tag: "never"})
	if err != nil {
		t.Fatal(err)
	}
	pf2.mu.RLock()
	pf2.intent = IntentUnmounted
	pf2.mu.RUnlock()
	rt.submit(&cmdLoadIntent{fiber: pf2})
	if err := pf2.Rehome(ctx, RehomeWithFreshIsolation()); !errors.Is(err, ErrRehomeNotActive) {
		t.Fatalf("rehome on non-Active = %v, want ErrRehomeNotActive", err)
	}
}

func rehomeAdmit(t *testing.T, rt *Runtime, f *Fiber) {
	t.Helper()
	rehomeWaitState(t, f, StateActive)
}

func rehomeResolveAs(t *testing.T, rt *Runtime, f *Fiber, key CapabilityKey) struct {
	id ProviderIdentity
	ok bool
} {
	t.Helper()
	out := make(chan struct {
		id ProviderIdentity
		ok bool
	}, 1)
	rt.submit(&rehomeResolveProbe{fiber: f, key: key, out: out})
	select {
	case r := <-out:
		return r
	case <-time.After(8 * time.Second):
		t.Fatal("resolve timeout")
		return struct {
			id ProviderIdentity
			ok bool
		}{}
	}
}

type rehomeResolveProbe struct {
	fiber *Fiber
	key   CapabilityKey
	out   chan struct {
		id ProviderIdentity
		ok bool
	}
}

func (p *rehomeResolveProbe) apply(o *orchestrator) {
	id, ok := o.resolveDependency(p.fiber, p.key)
	p.out <- struct {
		id ProviderIdentity
		ok bool
	}{id, ok}
}

func epWaitSeenLike(t *testing.T, ch chan string, want string) {
	t.Helper()
	deadline := time.After(10 * time.Second)
	for {
		select {
		case v := <-ch:
			if v == want {
				return
			}
		case <-deadline:
			t.Fatalf("timeout waiting for %q", want)
		}
	}
}

func rehomeNewRT(t *testing.T) *Runtime {
	t.Helper()
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

// TestRehomeIsolatedNamespace — R3 extension: a provision that lives in a
// per-key ISOLATION realm (not the fiber's base realm) must also migrate on
// Rehome. Phase 1 retires it in every fiberNamespaces(f); phase 2 must use
// that SAME pre-migration snapshot to find and move the record. Deriving the
// old namespaces AFTER replacing f.keyRealms loses the isolated realm: the
// record stays retiring there and Dispose can no longer find it.
type rehomeIsolatedHost struct {
	child chan *Fiber
}

func (c *rehomeIsolatedHost) Name() string          { return "rehome-isolated-host" }
func (c *rehomeIsolatedHost) Inject() []Dependency  { return nil }
func (c *rehomeIsolatedHost) Provide() []Capability { return nil }
func (c *rehomeIsolatedHost) Apply(ctx *Context) (Cleanup, error) {
	child, err := ctx.Child(&rehomeDualProvider{}, Isolate(rehomeOtherKey.Capability()))
	if err != nil {
		return nil, err
	}
	if c.child != nil {
		c.child <- child
	}
	return nil, nil
}

func TestRehomeIsolatedNamespace(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	childCh := make(chan *Fiber, 1)
	host, err := rt.Load(&rehomeIsolatedHost{child: childCh})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, host)
	rehomeWaitState(t, host, StateActive)
	pf := <-childCh
	rehomeWaitState(t, pf, StateActive)

	// The isolated realm is distinct from the fiber base realm and holds the
	// provision for rehomeKey.
	pf.mu.RLock()
	isoRealm := pf.keyRealms[rehomeKey.Capability()]
	baseRealm := pf.realm
	pf.mu.RUnlock()
	if isoRealm == nil || isoRealm == baseRealm {
		t.Fatalf("isolated realm not established: iso=%v base=%v", isoRealm, baseRealm)
	}
	pID := ProviderIdentity{FiberID: pf.ID(), ActivationID: pf.activation.id}
	if _, ok := isoRealm.lookupOwn(rehomeKey.Capability()); !ok {
		t.Fatal("provision not in isolated realm before rehome")
	}
	if rec, ok := isoRealm.lookupOwn(rehomeKey.Capability()); !ok || rec.identity != pID {
		t.Fatalf("isolated realm does not own the provision record: %+v/%v", rec, ok)
	}

	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}

	pf.mu.RLock()
	fresh := pf.keyRealms[rehomeKey.Capability()]
	pf.mu.RUnlock()
	if fresh == nil || fresh == isoRealm {
		t.Fatalf("fresh namespace missing: %v", pf.keyRealms)
	}
	// R3 isolated: the OLD isolated realm must no longer hold the provision.
	if _, ok := isoRealm.lookupOwn(rehomeKey.Capability()); ok {
		t.Fatal("old isolated realm still holds the provision after rehome")
	}
	// And the fresh realm must hold it with the SAME activation (short route).
	rec, ok := fresh.lookupOwn(rehomeKey.Capability())
	if !ok || rec.identity != pID {
		t.Fatalf("fresh realm missing the migrated provision record: %+v/%v", rec, ok)
	}
	if rec.retiring {
		t.Fatal("migrated record is still retiring")
	}

	// R5 isolated: disposing the provider removes the migrated record too.
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if _, ok := fresh.lookupOwn(rehomeKey.Capability()); ok {
		t.Fatal("fresh isolated realm still holds the provision after dispose")
	}
}

// Isolate must re-home ONLY the listed keys into a fresh namespace. If the
// scopeKeys are dropped between Context.Child and cmdSpawnChild, Isolate
// degenerates to WithScope (all keys) or no isolation at all — breaking the
// per-key realm contract (paper Def 24/25) that Rehome isolation depends on.
func TestIsolatePerKeyRealm(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	childCh := make(chan *Fiber, 1)
	host, err := rt.Load(&rehomeIsolatedHost{child: childCh})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, host)
	rehomeWaitState(t, host, StateActive)
	pf := <-childCh
	rehomeWaitState(t, pf, StateActive)

	pf.mu.RLock()
	iso := pf.keyRealms[rehomeKey.Capability()]
	base := pf.realm
	pf.mu.RUnlock()
	if iso == nil || iso == base {
		t.Fatalf("Isolate should leave rehomeKey in a realm distinct from the fiber base: iso=%v base=%v", iso, base)
	}
	if iso != rt.rootRealm {
		t.Fatalf("rehomeKey provision realm = %v, want parent (root) realm %v", iso, rt.rootRealm)
	}
}

// A consumer whose Cleanup blocks until released: Rehome phase 1 waits for this
// consumer to detach, so blocking the Cleanup holds Rehome in-flight at a
// deterministic point (no Sleep).
type rehomeBlockingConsumer struct {
	cleanupEntered chan struct{}
	releaseCleanup chan struct{}
}

func (c *rehomeBlockingConsumer) Name() string          { return "rehome-blocking-consumer" }
func (c *rehomeBlockingConsumer) Inject() []Dependency  { return []Dependency{Requires(rehomeKey)} }
func (c *rehomeBlockingConsumer) Provide() []Capability { return nil }
func (c *rehomeBlockingConsumer) Apply(ctx *Context) (Cleanup, error) {
	if _, err := Require(ctx, rehomeKey); err != nil {
		return nil, err
	}
	return func() error {
		close(c.cleanupEntered)
		<-c.releaseCleanup
		return nil
	}, nil
}

// rehomeBarrier is a no-op orchestrator command: submitting it after Dispose
// and waiting for its completion proves the Dispose command was already
// processed (FIFO command queue) — without timing windows.
type rehomeBarrier struct{ done chan struct{} }

func (b *rehomeBarrier) apply(*orchestrator) { close(b.done) }

func rehomeOrchestratorBarrier(t *testing.T, rt *Runtime) {
	t.Helper()
	b := &rehomeBarrier{done: make(chan struct{})}
	if !rt.submit(b) {
		t.Fatal("barrier submit failed")
	}
	select {
	case <-b.done:
	case <-time.After(8 * time.Second):
		t.Fatal("orchestrator barrier timeout")
	}
}

func rehomeAwaitGone(t *testing.T, ctx context.Context, f *Fiber, phase string) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == StateGone {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("%s: fiber stuck in %v (intent=%v) — Unmounted intent was lost across Rehome", phase, f.State(), f.intent)
}

// TestRehomeConcurrentDispose — P0: a Dispose issued WHILE Rehome is in flight
// must not be swallowed by the "f.rehome != nil" guard in beginWithdrawal.
// After the rehome completes the fiber must reconcile and reach Gone.
func TestRehomeConcurrentDispose(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	pf, _ := rt.Load(&rehomeProvider{tag: "v1"})
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)

	bc := &rehomeBlockingConsumer{
		cleanupEntered: make(chan struct{}),
		releaseCleanup: make(chan struct{}),
	}
	cf, _ := rt.Load(bc)
	rehomeAdmit(t, rt, cf)
	rehomeWaitState(t, cf, StateActive)

	// Start Rehome in the background: phase 1 retires provisions and asks the
	// consumer to withdraw; its Cleanup blocks, holding Rehome in-flight.
	rehomeDone := make(chan error, 1)
	go func() { rehomeDone <- pf.Rehome(ctx, RehomeWithFreshIsolation()) }()

	select {
	case <-bc.cleanupEntered:
	case <-ctx.Done():
		t.Fatal("consumer Cleanup never entered — Rehome not gated")
	}
	pf.mu.RLock()
	inFlight := pf.rehome != nil
	pf.mu.RUnlock()
	if !inFlight {
		t.Fatal("Rehome already completed before Dispose")
	}

	// Dispose WHILE Rehome is in flight. The barrier proves the orchestrator
	// processed cmdDispose before we release the consumer.
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	rehomeOrchestratorBarrier(t, rt)

	// Release the consumer → Rehome phase 2 completes.
	close(bc.releaseCleanup)
	select {
	case err := <-rehomeDone:
		if err != nil {
			t.Fatalf("rehome: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("rehome never completed")
	}
	rehomeAwaitGone(t, ctx, pf, "concurrent dispose")
	// The consumer was never disposed: after the provider moved to a fresh
	// realm its old-namespace binding is gone, so it legitimately rests at
	// Pending (paper §5.2.1: old-namespace dependents do not follow a realm
	// move). Only the disposed provider must reach Gone.
	rehomeWaitState(t, cf, StatePending)
	// No provision residue in any namespace.
	for _, rr := range fiberNamespaces(pf) {
		for _, rec := range rr.recordsOwnedBy(ProviderIdentity{FiberID: pf.ID()}) {
			t.Fatalf("provision residue after rehome+dispose: key=%v retiring=%v", rec.key, rec.retiring)
		}
	}
}

// TestRuntimeCloseDuringRehome — P0: Close issued during Rehome must still
// reach Gone for the rehoming fiber.
func TestRuntimeCloseDuringRehome(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)

	pf, _ := rt.Load(&rehomeProvider{tag: "v1"})
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)

	bc := &rehomeBlockingConsumer{
		cleanupEntered: make(chan struct{}),
		releaseCleanup: make(chan struct{}),
	}
	cf, _ := rt.Load(bc)
	rehomeAdmit(t, rt, cf)
	rehomeWaitState(t, cf, StateActive)

	rehomeDone := make(chan error, 1)
	go func() { rehomeDone <- pf.Rehome(ctx, RehomeWithFreshIsolation()) }()
	select {
	case <-bc.cleanupEntered:
	case <-ctx.Done():
		t.Fatal("consumer Cleanup never entered")
	}

	// Close while Rehome is in flight (cmdClose processed before barrier).
	closeDone := make(chan error, 1)
	go func() { closeDone <- rt.Close(ctx) }()
	rehomeOrchestratorBarrier(t, rt)

	close(bc.releaseCleanup)
	select {
	case err := <-rehomeDone:
		if err != nil {
			t.Fatalf("rehome: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("rehome never completed")
	}
	select {
	case err := <-closeDone:
		if err != nil {
			t.Fatalf("close: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("Close never completed after Rehome")
	}
	if pf.State() != StateGone || cf.State() != StateGone {
		t.Fatalf("fibers not Gone after Close: provider=%v consumer=%v", pf.State(), cf.State())
	}
}

// P2 probe: a late Provide issued during Rehome. provideCap captures the
// effective realm on the caller goroutine BEFORE effect() installs; if a
// rehome moves the fiber between capture and install, the record can land in
// the (now stale) old realm, after phase 2's migration scan.
type rehomeLateProvider struct {
	lateKey Key[rehomeKeyVal]
	gate    chan struct{}
	issued  chan struct{}
}

func (c *rehomeLateProvider) Name() string         { return "rehome-late-provider" }
func (c *rehomeLateProvider) Inject() []Dependency { return nil }
func (c *rehomeLateProvider) Provide() []Capability {
	return []Capability{rehomeKey.Capability(), c.lateKey.Capability()}
}
func (c *rehomeLateProvider) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, rehomeKey, rehomeKeyVal{tag: "v1"}); err != nil {
		return nil, err
	}
	go func() {
		<-c.gate
		_ = Provide(ctx, c.lateKey, rehomeKeyVal{tag: "late"})
		close(c.issued)
	}()
	return nil, nil
}

// TestRehomeLateProvideProbe — deterministic probe of the late-Provide race.
// The late Provide is issued while Rehome phase 1 is gated by a blocking
// consumer Cleanup. We assert the NO-LEAK invariant: after rehome+dispose,
// no provision record may remain in any of the fiber's namespaces.
func TestRehomeLateProvideProbe(t *testing.T) {
	for round := 0; round < 50; round++ {
		rt := rehomeNewRT(t)
		ctx := ctxBG(t)

		lateKey := NewKey[rehomeKeyVal]("rehome.late")
		gate := make(chan struct{})
		issued := make(chan struct{})
		lp := &rehomeLateProvider{lateKey: lateKey, gate: gate, issued: issued}
		pf, err := rt.Load(lp)
		if err != nil {
			t.Fatal(err)
		}
		rehomeAdmit(t, rt, pf)
		rehomeWaitState(t, pf, StateActive)

		bc := &rehomeBlockingConsumer{
			cleanupEntered: make(chan struct{}),
			releaseCleanup: make(chan struct{}),
		}
		cf, _ := rt.Load(bc)
		rehomeAdmit(t, rt, cf)
		rehomeWaitState(t, cf, StateActive)

		rehomeDone := make(chan error, 1)
		go func() { rehomeDone <- pf.Rehome(ctx, RehomeWithFreshIsolation()) }()
		<-bc.cleanupEntered // Rehome phase 1 gated

		// Issue a late Provide while Rehome is in flight.
		close(gate)
		<-issued
		close(bc.releaseCleanup)
		if err := <-rehomeDone; err != nil {
			t.Fatalf("rehome: %v", err)
		}
		if err := pf.Dispose(); err != nil {
			t.Fatal(err)
		}
		if err := pf.Gone(ctx); err != nil {
			t.Fatal(err)
		}

		// No-leak invariant across every namespace the fiber has ever used.
		for _, rr := range fiberNamespaces(pf) {
			for _, rec := range rr.recordsOwnedBy(ProviderIdentity{FiberID: pf.ID()}) {
				t.Fatalf("round %d: late Provide left residue key=%v retiring=%v", round, rec.key, rec.retiring)
			}
		}
		_ = rt.Close(ctx)
	}
}

// rehomeAsyncProvider fires one Provide from a background goroutine at the
// precise moment the test releases fire. The record is created only then, so
// the test owns the capture-vs-rehome interleaving.
type rehomeAsyncProvider struct {
	lateKey Key[rehomeKeyVal]
	fire    chan struct{}
	done    chan error
}

func (c *rehomeAsyncProvider) Name() string         { return "rehome-async-provider" }
func (c *rehomeAsyncProvider) Inject() []Dependency { return nil }
func (c *rehomeAsyncProvider) Provide() []Capability {
	return []Capability{rehomeKey.Capability(), c.lateKey.Capability()}
}
func (c *rehomeAsyncProvider) Apply(ctx *Context) (Cleanup, error) {
	if err := Provide(ctx, rehomeKey, rehomeKeyVal{tag: "v1"}); err != nil {
		return nil, err
	}
	go func() {
		<-c.fire
		c.done <- Provide(ctx, c.lateKey, rehomeKeyVal{tag: "late"})
	}()
	return nil, nil
}

// TestRehomeAsyncProvideLinearization — P0: a Provide whose route decision is
// paused at the hook cannot publish into a namespace that Rehome has already
// migrated past. The choreography is fully deterministic (channels only):
//
//  1. async Provide reaches the route-decision hook and blocks (has NOT registered);
//  2. Rehome runs to completion: replaces the key table, scans the old namespaces;
//  3. the hook is released, so Provide registers;
//  4. the provision must be resolvable in the CURRENT effective realm, and
//     Dispose must leave no residue in ANY namespace.
//
// On the buggy code the capture happens before the hook, so step 3 registers
// into the stale realm that step 2 already scanned -> step 4 fails.
func TestRehomeAsyncProvideLinearization(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	lateKey := NewKey[rehomeKeyVal]("rehome.async-late")
	entered := make(chan struct{})
	resume := make(chan struct{})
	provideDone := make(chan error, 1)

	prev := testProvideHook
	defer func() { testProvideHook = prev }()
	testProvideHook = func(key CapabilityKey, _ ProviderIdentity) {
		if key == lateKey.Capability() {
			close(entered)
			<-resume
		}
	}

	p := &rehomeAsyncProvider{
		lateKey: lateKey,
		fire:    make(chan struct{}),
		done:    provideDone,
	}
	pf, err := rt.Load(p)
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)

	// 1. fire the async Provide, wait until it is paused at the route hook.
	close(p.fire)
	select {
	case <-entered:
	case <-ctx.Done():
		t.Fatal("async Provide never reached the route hook")
	}

	// 2. Rehome runs to completion while the Provide is paused (no consumers,
	// so phase 2 fires immediately). It replaces the key table and scans old
	// namespaces — the late record is not there yet.
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}

	// 3. release the Provide; it registers now.
	close(resume)
	select {
	case err := <-provideDone:
		if err != nil {
			t.Fatalf("late Provide: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("late Provide never completed")
	}

	// 4a. the provision must live in the CURRENT effective realm (the fresh
	// namespace after rehome), not in a stale realm that was already scanned.
	eff := effectiveRealm(pf, lateKey.Capability())
	rec, ok := eff.lookupOwn(lateKey.Capability())
	if !ok || rec == nil || rec.value == nil {
		var stale []string
		for _, rr := range fiberNamespaces(pf) {
			if r, found := rr.lookupOwn(lateKey.Capability()); found {
				stale = append(stale, rr.id.String()+"/"+fmt.Sprintf("%+v", r.identity))
			}
		}
		t.Fatalf("late Provide stranded in stale realm: current effective realm has no record; stale=%v", stale)
	}

	// 4b. resolve-by-probe agrees with the direct lookup.
	if _, ok := rehomeResolve(t, rt, pf, lateKey.Capability()); !ok {
		t.Fatal("late key does not resolve on the provider after rehome")
	}

	// 4c. Dispose must leave no residue in any namespace (the inverse must
	// find the record where it actually landed).
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	for _, rr := range fiberNamespaces(pf) {
		if r, found := rr.lookupOwn(lateKey.Capability()); found {
			t.Fatalf("late Provide residue after dispose: realm=%s id=%+v", rr.id.String(), r.identity)
		}
	}
}

// rehomeMultiAsyncProvider fires one async Provide per key, all released by
// the same fire channel. Used to prove the route linearization holds for N
// concurrent installs (not just one).
type rehomeMultiAsyncProvider struct {
	keys []Key[rehomeKeyVal]
	fire chan struct{}
	done chan error
}

func (c *rehomeMultiAsyncProvider) Name() string         { return "rehome-multi-async" }
func (c *rehomeMultiAsyncProvider) Inject() []Dependency { return nil }
func (c *rehomeMultiAsyncProvider) Provide() []Capability {
	out := make([]Capability, len(c.keys))
	for i, k := range c.keys {
		out[i] = k.Capability()
	}
	return out
}
func (c *rehomeMultiAsyncProvider) Apply(ctx *Context) (Cleanup, error) {
	for _, k := range c.keys {
		k := k
		go func() {
			<-c.fire
			c.done <- Provide(ctx, k, rehomeKeyVal{tag: "n-" + k.Capability().String()})
		}()
	}
	return nil, nil
}

// TestRehomeAsyncProvideMultiple — P2: N async Provides paused at the route
// hook, then a Rehome, then released. Every record must land in the fresh
// namespace and be removed by Dispose; none may be stranded in a stale realm.
func TestRehomeAsyncProvideMultiple(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	const n = 3
	keys := make([]Key[rehomeKeyVal], n)
	keySet := make(map[CapabilityKey]struct{}, n)
	for i := range keys {
		keys[i] = NewKey[rehomeKeyVal](fmt.Sprintf("rehome.async-%d", i))
		keySet[keys[i].Capability()] = struct{}{}
	}
	entered := make(chan struct{}, n)
	resume := make(chan struct{})
	prev := testProvideHook
	defer func() { testProvideHook = prev }()
	testProvideHook = func(key CapabilityKey, _ ProviderIdentity) {
		if _, ok := keySet[key]; ok {
			entered <- struct{}{}
			<-resume
		}
	}

	fire := make(chan struct{})
	done := make(chan error, n)
	pf, err := rt.Load(&rehomeMultiAsyncProvider{keys: keys, fire: fire, done: done})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)

	close(fire)
	for i := 0; i < n; i++ {
		select {
		case <-entered:
		case <-ctx.Done():
			t.Fatalf("only %d/%d async Provides reached the hook", i, n)
		}
	}
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}
	close(resume)
	for i := 0; i < n; i++ {
		select {
		case err := <-done:
			if err != nil {
				t.Fatalf("async Provide %d: %v", i, err)
			}
		case <-ctx.Done():
			t.Fatalf("only %d/%d async Provides completed", i, n)
		}
	}
	for _, k := range keys {
		if _, ok := effectiveRealm(pf, k.Capability()).lookupOwn(k.Capability()); !ok {
			t.Fatalf("key %v not in the current effective realm after rehome", k)
		}
	}
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	for _, k := range keys {
		assertNoResidue(t, pf, k.Capability())
	}
}

// rehomeAsyncSingleProvider fires exactly one async Provide on lateKey.
type rehomeAsyncSingleProvider struct {
	lateKey Key[rehomeKeyVal]
	fire    chan struct{}
	done    chan error
}

func (c *rehomeAsyncSingleProvider) Name() string         { return "rehome-async-single" }
func (c *rehomeAsyncSingleProvider) Inject() []Dependency { return nil }
func (c *rehomeAsyncSingleProvider) Provide() []Capability {
	return []Capability{c.lateKey.Capability()}
}
func (c *rehomeAsyncSingleProvider) Apply(ctx *Context) (Cleanup, error) {
	go func() {
		<-c.fire
		c.done <- Provide(ctx, c.lateKey, rehomeKeyVal{tag: "late"})
	}()
	return nil, nil
}

// TestRehomeAsyncProvideThenClose — P2: a late Provide that landed after a
// rehome must still be unwound cleanly by Runtime.Close (no residue).
func TestRehomeAsyncProvideThenClose(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx) // idempotent; guards t.Fatal paths before the explicit Close

	lateKey := NewKey[rehomeKeyVal]("rehome.async-close")
	entered := make(chan struct{})
	resume := make(chan struct{})
	done := make(chan error, 1)
	prev := testProvideHook
	defer func() { testProvideHook = prev }()
	testProvideHook = func(key CapabilityKey, _ ProviderIdentity) {
		if key == lateKey.Capability() {
			close(entered)
			<-resume
		}
	}

	fire := make(chan struct{})
	pf, err := rt.Load(&rehomeAsyncSingleProvider{lateKey: lateKey, fire: fire, done: done})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)
	close(fire)
	<-entered
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}
	close(resume)
	if err := <-done; err != nil {
		t.Fatalf("late Provide: %v", err)
	}

	closeDone := make(chan error, 1)
	go func() { closeDone <- rt.Close(ctx) }()
	select {
	case err := <-closeDone:
		if err != nil {
			t.Fatalf("close: %v", err)
		}
	case <-ctx.Done():
		t.Fatal("Close never completed")
	}
	if pf.State() != StateGone {
		t.Fatalf("provider not Gone after Close: %v", pf.State())
	}
	assertNoResidue(t, pf, lateKey.Capability())
}

// rehomeAsyncIsolatedHost spawns a child whose lateKey is isolated into its
// own namespace, then fires an async Provide into that isolated namespace.
type rehomeAsyncIsolatedHost struct {
	child chan *Fiber
}

func (c *rehomeAsyncIsolatedHost) Name() string          { return "rehome-async-isolated-host" }
func (c *rehomeAsyncIsolatedHost) Inject() []Dependency  { return nil }
func (c *rehomeAsyncIsolatedHost) Provide() []Capability { return nil }
func (c *rehomeAsyncIsolatedHost) Apply(ctx *Context) (Cleanup, error) {
	child, err := ctx.Child(&rehomeAsyncSingleProvider{
		lateKey: rehomeOtherKey,
		fire:    make(chan struct{}),
		done:    make(chan error, 1),
	}, Isolate(rehomeOtherKey.Capability()))
	if err != nil {
		return nil, err
	}
	if c.child != nil {
		c.child <- child
	}
	return nil, nil
}

// TestRehomeAsyncProvideIsolated — P2: an async Provide into a per-key ISOLATED
// namespace must also be linearized with Rehome. The isolated realm is part of
// the phase-1 snapshot; a record registered after the migration must land in
// the fresh realm, and Dispose must find it there.
func TestRehomeAsyncProvideIsolated(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	entered := make(chan struct{})
	resume := make(chan struct{})
	prev := testProvideHook
	defer func() { testProvideHook = prev }()
	testProvideHook = func(key CapabilityKey, _ ProviderIdentity) {
		if key == rehomeOtherKey.Capability() {
			close(entered)
			<-resume
		}
	}

	childCh := make(chan *Fiber, 1)
	host, err := rt.Load(&rehomeAsyncIsolatedHost{child: childCh})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, host)
	rehomeWaitState(t, host, StateActive)
	pf := <-childCh
	rehomeWaitState(t, pf, StateActive)

	// Isolate(rehomeOtherKey) gives that key an explicit per-key table entry.
	// (Because the child declares only that key, the entry coincides with the
	// child's base realm — what matters here is that Rehome replaces it with a
	// NEW realm and the async install must follow the move.)
	pf.routeMu.RLock()
	isoRealm := pf.keyRealms[rehomeOtherKey.Capability()]
	pf.routeMu.RUnlock()
	if isoRealm == nil {
		t.Fatal("isolated per-key realm not established")
	}

	// Fire the async Provide into the isolated realm and pause it.
	// The child's Apply stored its fire/done channels inside the component; we
	// need to reach them — re-find via a typed assertion on the child component.
	asp, ok := pf.component.(*rehomeAsyncSingleProvider)
	if !ok {
		t.Fatalf("child component type = %T", pf.component)
	}
	close(asp.fire)
	<-entered
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}
	close(resume)
	if err := <-asp.done; err != nil {
		t.Fatalf("async Provide: %v", err)
	}

	pf.routeMu.RLock()
	fresh := pf.keyRealms[rehomeOtherKey.Capability()]
	pf.routeMu.RUnlock()
	if fresh == nil || fresh == isoRealm {
		t.Fatalf("fresh isolated realm missing: %v", pf.keyRealms)
	}
	if _, ok := isoRealm.lookupOwn(rehomeOtherKey.Capability()); ok {
		t.Fatal("old isolated realm still holds the late provision after rehome")
	}
	if _, ok := fresh.lookupOwn(rehomeOtherKey.Capability()); !ok {
		t.Fatal("fresh isolated realm does not hold the late provision")
	}
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	assertNoResidue(t, pf, rehomeOtherKey.Capability())
}

// TestRehomeAsyncProvideConsecutive — P2: two consecutive rehomes after a late
// Provide. The record must follow each move into the newest fresh namespace,
// with no residue left in any superseded realm.
func TestRehomeAsyncProvideConsecutive(t *testing.T) {
	rt := rehomeNewRT(t)
	ctx := ctxBG(t)
	defer rt.Close(ctx)

	lateKey := NewKey[rehomeKeyVal]("rehome.async-twice")
	entered := make(chan struct{})
	resume := make(chan struct{})
	done := make(chan error, 1)
	prev := testProvideHook
	defer func() { testProvideHook = prev }()
	testProvideHook = func(key CapabilityKey, _ ProviderIdentity) {
		if key == lateKey.Capability() {
			close(entered)
			<-resume
		}
	}

	fire := make(chan struct{})
	pf, err := rt.Load(&rehomeAsyncSingleProvider{lateKey: lateKey, fire: fire, done: done})
	if err != nil {
		t.Fatal(err)
	}
	rehomeAdmit(t, rt, pf)
	rehomeWaitState(t, pf, StateActive)
	close(fire)
	<-entered
	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome #1: %v", err)
	}
	close(resume)
	if err := <-done; err != nil {
		t.Fatalf("late Provide: %v", err)
	}
	firstFresh := effectiveRealm(pf, lateKey.Capability())

	if err := pf.Rehome(ctx, RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome #2: %v", err)
	}
	secondFresh := effectiveRealm(pf, lateKey.Capability())
	if secondFresh == firstFresh {
		t.Fatal("consecutive rehome did not move to a fresh namespace")
	}
	if _, ok := firstFresh.lookupOwn(lateKey.Capability()); ok {
		t.Fatal("first fresh realm still holds the record after second rehome")
	}
	if _, ok := secondFresh.lookupOwn(lateKey.Capability()); !ok {
		t.Fatal("second fresh realm missing the record")
	}
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	assertNoResidue(t, pf, lateKey.Capability())
}

// assertNoResidue checks every namespace the fiber currently knows about for a
// record of the given key (the inverse must find the record where it landed).
func assertNoResidue(t *testing.T, f *Fiber, key CapabilityKey) {
	t.Helper()
	for _, rr := range fiberNamespaces(f) {
		if r, found := rr.lookupOwn(key); found {
			t.Fatalf("residue for key %v: realm=%s id=%+v", key, rr.id.String(), r.identity)
		}
	}
}
