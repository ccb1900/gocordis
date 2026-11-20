package runtime

import (
	"context"
	"errors"
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
