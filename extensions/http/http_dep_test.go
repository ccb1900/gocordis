package http_test

import (
	"sync/atomic"
	"testing"
	"time"

	httpext "dynamic-runtime/extensions/http"
	"dynamic-runtime/extensions/registry"
	"dynamic-runtime/runtime"
)

// hCons is a consumer of the HTTP server capability.
type hCons struct {
	id      string
	applies atomic.Int32
}

func (c *hCons) Name() string { return "consumer:" + c.id }
func (c *hCons) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(httpext.ServerCapability)}
}
func (c *hCons) Provide() []runtime.Capability { return nil }
func (c *hCons) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if _, err := runtime.Require(ctx, httpext.ServerCapability); err != nil {
		return nil, err
	}
	c.applies.Add(1)
	return nil, nil
}

// aKey is a mid-level capability provided by component A (which requires HTTP).
var aKey = runtime.NewKey[string]("http.levelA")

type hA struct {
	id      string
	applies atomic.Int32
}

func (c *hA) Name() string { return "A:" + c.id }
func (c *hA) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(httpext.ServerCapability)}
}
func (c *hA) Provide() []runtime.Capability { return []runtime.Capability{aKey.Capability()} }
func (c *hA) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if _, err := runtime.Require(ctx, httpext.ServerCapability); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, aKey, c.id); err != nil {
		return nil, err
	}
	c.applies.Add(1)
	return nil, nil
}

type hB struct {
	id      string
	applies atomic.Int32
}

func (c *hB) Name() string                  { return "B:" + c.id }
func (c *hB) Inject() []runtime.Dependency  { return []runtime.Dependency{runtime.Requires(aKey)} }
func (c *hB) Provide() []runtime.Capability { return nil }
func (c *hB) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if _, err := runtime.Require(ctx, aKey); err != nil {
		return nil, err
	}
	c.applies.Add(1)
	return nil, nil
}

func waitStateEventually(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("fiber %s state = %v, want %v", f.Name(), f.State(), want)
}

// HTTP-09 — Consumer is Active only after the HTTP Provider is Active.
func TestHTTP09ConsumerDependency(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	// No provider yet: consumer stays Pending.
	waitStateEventually(t, cf, runtime.StatePending)

	comp := newComp(t, addr, httpext.WithProvider())
	pf := mount(t, rt, comp)
	// Provider Active -> consumer becomes Active.
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatalf("consumer not active after provider: %v", err)
	}
	_ = pf.Dispose()
	_ = pf.Gone(ctxT(t))
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}

// HTTP-10 — Provider withdrawal: the consumer leaves Active before the
// provider finishes (kernel consumer-first dependency semantics).
func TestHTTP10ProviderWithdrawal(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	comp := newComp(t, addr, httpext.WithProvider())
	pf := mount(t, rt, comp)
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	// The consumer's current activation must end first (WaitInactive), i.e.
	// before the provider reaches Gone.
	if err := cf.WaitInactive(ctxT(t)); err != nil {
		t.Fatalf("consumer did not withdraw: %v", err)
	}
	if err := pf.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if cf.State() != runtime.StatePending {
		t.Fatalf("consumer state = %v, want Pending after provider gone", cf.State())
	}
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}

// HTTP-11 — Provider recovery: a fresh provider reactivates the consumer.
func TestHTTP11ProviderRecovery(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	p1 := mount(t, rt, newComp(t, addr, httpext.WithProvider()))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	before := cons.applies.Load()
	_ = p1.Dispose()
	_ = p1.Gone(ctxT(t))

	addr2 := freeAddr(t)
	p2 := mount(t, rt, newComp(t, addr2, httpext.WithProvider()))
	eventuallyCons(t, cf, before)
	_ = p2.Dispose()
	_ = p2.Gone(ctxT(t))
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}

func eventuallyCons(t *testing.T, cf *runtime.Fiber, before int32) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if cf.State() == runtime.StateActive {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("consumer never recovered")
}

// HTTP-12 — Multi-level HTTP -> A -> B withdrawal/recovery.
func TestHTTP12MultiLevel(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	a := &hA{id: "a"}
	b := &hB{id: "b"}
	af, err := rt.Load(a)
	if err != nil {
		t.Fatal(err)
	}
	bf, err := rt.Load(b)
	if err != nil {
		t.Fatal(err)
	}
	p1 := mount(t, rt, newComp(t, addr, httpext.WithProvider()))
	waitStateEventually(t, af, runtime.StateActive)
	waitStateEventually(t, bf, runtime.StateActive)
	if a.applies.Load() < 1 || b.applies.Load() < 1 {
		t.Fatalf("A/B not active: %d %d", a.applies.Load(), b.applies.Load())
	}

	addr2 := freeAddr(t)
	_ = p1.Dispose()
	_ = p1.Gone(ctxT(t))
	p2 := mount(t, rt, newComp(t, addr2, httpext.WithProvider()))

	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if af.State() == runtime.StateActive && bf.State() == runtime.StateActive &&
			a.applies.Load() >= 2 && b.applies.Load() >= 2 {
			break
		}
		time.Sleep(2 * time.Millisecond)
	}
	if af.State() != runtime.StateActive || bf.State() != runtime.StateActive {
		t.Fatalf("A/B did not recover: %v %v", af.State(), bf.State())
	}
	_ = p2.Dispose()
	_ = p2.Gone(ctxT(t))
	_ = af.Dispose()
	_ = af.Gone(ctxT(t))
	_ = bf.Dispose()
	_ = bf.Gone(ctxT(t))
}

// HTTP-13 — Registry stability: member churn does not reactivate a consumer
// that depends on the HTTP provider identity.
func TestHTTP13RegistryStable(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	reg := registry.New[httpext.Handle]()
	comp := newComp(t, addr, httpext.WithProvider(), httpext.WithRegistryMember(reg, "http-main"))
	pf := mount(t, rt, comp)

	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if reg.Len() != 1 {
		t.Fatalf("member count = %d, want 1", reg.Len())
	}
	before := cons.applies.Load()

	// Member churn on the same registry object (add/remove others, replace).
	if err := reg.Add("ephemeral", httpext.Handle{}); err != nil {
		t.Fatal(err)
	}
	_ = reg.Remove("ephemeral")
	if err := reg.Replace("http-main", httpext.Handle{ID: 999, Addr: addr}); err != nil {
		t.Fatal(err)
	}
	if cf.State() != runtime.StateActive {
		t.Fatal("consumer reactivated by member churn")
	}
	if cons.applies.Load() != before {
		t.Fatalf("consumer re-applied after member churn: %d -> %d", before, cons.applies.Load())
	}
	_ = pf.Dispose()
	_ = pf.Gone(ctxT(t))
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}

// HTTP-14 — Member cleanup: when the HTTP server Activation ends, its registry
// member is removed (no stale handle).
func TestHTTP14MemberCleanup(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	reg := registry.New[httpext.Handle]()
	comp := newComp(t, addr, httpext.WithProvider(), httpext.WithRegistryMember(reg, "http-main"))
	pf := mount(t, rt, comp)
	if reg.Len() != 1 {
		t.Fatal("member missing while active")
	}
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if reg.Len() != 0 {
		t.Fatal("stale registry member after activation ended")
	}
}

// HTTP-15 — Provider replacement changes the provider identity; member churn
// on its own never does.
func TestHTTP15ProviderReplacement(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	reg := registry.New[httpext.Handle]()
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}

	p1 := mount(t, rt, newComp(t, addr, httpext.WithProvider(), httpext.WithRegistryMember(reg, "http-p1")))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if reg.Len() != 1 {
		t.Fatal("member p1 missing")
	}
	before := cons.applies.Load()
	_ = p1.Dispose()
	_ = p1.Gone(ctxT(t))
	waitStateEventually(t, cf, runtime.StatePending)
	if reg.Len() != 0 {
		t.Fatal("old member not removed after provider gone")
	}

	addr2 := freeAddr(t)
	p2 := mount(t, rt, newComp(t, addr2, httpext.WithProvider(), httpext.WithRegistryMember(reg, "http-p2")))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if reg.Len() != 1 || reg.Has("http-p1") || !reg.Has("http-p2") {
		t.Fatal("registry members wrong after provider replacement")
	}
	if cons.applies.Load() <= before {
		t.Fatal("consumer did not rebind to the new provider identity")
	}
	_ = p2.Dispose()
	_ = p2.Gone(ctxT(t))
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}
