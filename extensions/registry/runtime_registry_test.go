package registry_test

// Integration tests: Registry Extension over the real Runtime Kernel. These
// prove the stable-provider semantics (R1/R2/R8/R9/R10) using the kernel's own
// Provide/Require/Effect lifecycle. The Kernel itself is not modified.

import (
	"context"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/registry"
	"dynamic-runtime/runtime"
)

type handler struct{ name string }

var handlerRegKey = runtime.NewKey[*registry.Registry[handler]]("handler-registry")

func ctxTimeout(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newTestRuntime(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() { _ = rt.Close(ctxTimeout(t)) })
	return rt
}

func waitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(time.Millisecond)
	}
	t.Fatalf("fiber %s state = %v, want %v", f.Name(), f.State(), want)
}

func addMemberEffect(ctx *runtime.Context, reg *registry.Registry[handler], id registry.MemberID, v handler) error {
	return ctx.Effect(func() (func() error, error) {
		if err := reg.Add(id, v); err != nil {
			return nil, err
		}
		return func() error { return reg.Remove(id) }, nil
	})
}

// registryProvider creates a Registry, provides it as a stable capability, and
// optionally registers initial members as reversible effects.
type registryProvider struct {
	name    string
	ch      chan *registry.Registry[handler]
	initial []string
	applies *atomic.Int32
}

func (c *registryProvider) Name() string                 { return c.name }
func (c *registryProvider) Inject() []runtime.Dependency { return nil }
func (c *registryProvider) Provide() []runtime.Capability {
	return []runtime.Capability{handlerRegKey.Capability()}
}
func (c *registryProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	reg := registry.New[handler]()
	if c.ch != nil {
		c.ch <- reg
	}
	if err := runtime.Provide(ctx, handlerRegKey, reg); err != nil {
		return nil, err
	}
	for _, m := range c.initial {
		if err := addMemberEffect(ctx, reg, registry.MemberID(m), handler{name: m}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

// registryConsumer depends only on the stable Registry capability.
type registryConsumer struct {
	name     string
	applies  *atomic.Int32
	inverses *atomic.Int32
}

func (c *registryConsumer) Name() string { return c.name }
func (c *registryConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(handlerRegKey)}
}
func (c *registryConsumer) Provide() []runtime.Capability { return nil }
func (c *registryConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	if _, err := runtime.Require(ctx, handlerRegKey); err != nil {
		return nil, err
	}
	return func() error {
		if c.inverses != nil {
			c.inverses.Add(1)
		}
		return nil
	}, nil
}

// R1 + R2 — Stable identity + consumer stability: Add/Remove/Replace on the
// registry never reactivates the provider or the consumer.
func TestR1R2StableIdentityAndConsumerStability(t *testing.T) {
	rt := newTestRuntime(t)

	var pApplies, cApplies, cInverses atomic.Int32
	regCh := make(chan *registry.Registry[handler], 1)

	prov := &registryProvider{name: "prov", ch: regCh, applies: &pApplies}
	cons := &registryConsumer{name: "cons", applies: &cApplies, inverses: &cInverses}

	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(ctxTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	reg := <-regCh

	// Member churn: membership mutations only, never kernel operations.
	_ = reg.Add("http", handler{"http"})
	_ = reg.Remove("http")
	_ = reg.Add("grpc", handler{"grpc"})
	_ = reg.Replace("grpc", handler{"grpc2"})
	_ = reg.Remove("grpc")
	_ = reg.Add("mqtt", handler{"mqtt"})

	// Provider activation unchanged -> provider identity unchanged.
	if got := pApplies.Load(); got != 1 {
		t.Fatalf("registry provider Apply count = %d, want 1 (identity stable)", got)
	}
	// Consumer never reactivated: no re-Apply, no cleanup (inverse).
	if got := cApplies.Load(); got != 1 {
		t.Fatalf("consumer Apply count = %d, want 1", got)
	}
	if got := cInverses.Load(); got != 0 {
		t.Fatalf("consumer inverse count = %d, want 0", got)
	}
	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %v, want Active", got)
	}
	if got := pf.State(); got != runtime.StateActive {
		t.Fatalf("provider state = %v, want Active", got)
	}
	if got := reg.Snapshot().Len(); got != 1 {
		t.Fatalf("registry members = %d, want 1 (mqtt)", got)
	}

	_ = pf.Dispose()
	_ = cf.Dispose()
}

// R8 — Registry disposal: runtime-managed members are removed before the
// registry provider is Gone; all resources cleaned.
func TestR8RegistryDisposalCleansMembers(t *testing.T) {
	rt := newTestRuntime(t)

	regCh := make(chan *registry.Registry[handler], 1)
	prov := &registryProvider{name: "prov", ch: regCh, initial: []string{"A", "B"}}
	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	if err := pf.Ready(ctxTimeout(t)); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	reg := <-regCh
	if got := reg.Snapshot().Len(); got != 2 {
		t.Fatalf("members before disposal = %d, want 2", got)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := pf.Gone(ctxTimeout(t)); err != nil {
		t.Fatalf("provider Gone: %v", err)
	}

	// Membership effects unwound LIFO before unregister: all members absent.
	if got := reg.Len(); got != 0 {
		t.Fatalf("registry members after disposal = %d, want 0", got)
	}
	if reg.Has("A") || reg.Has("B") {
		t.Fatal("runtime-managed members still present after registry disposal")
	}
}

// R9 — Registry dependency loss follows the Kernel contract: disposing the
// registry provider withdraws the consumer to Pending (never a special member-
// removal lifecycle).
func TestR9RegistryDependencyLossConsumerPending(t *testing.T) {
	rt := newTestRuntime(t)

	var cApplies atomic.Int32
	regCh := make(chan *registry.Registry[handler], 1)
	prov := &registryProvider{name: "prov", ch: regCh}
	cons := &registryConsumer{name: "cons", applies: &cApplies}

	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(ctxTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	reg := <-regCh
	_ = reg.Add("X", handler{"x"})

	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose: %v", err)
	}
	// Consumer's current activation cycle ends (kernel dependency loss).
	if err := cf.WaitInactive(ctxTimeout(t)); err != nil {
		t.Fatalf("consumer WaitInactive: %v", err)
	}
	waitState(t, cf, runtime.StatePending)
	if cf.Err() != nil {
		t.Fatalf("consumer Err = %v, want nil (dependency loss is not failure)", cf.Err())
	}
	if got := cApplies.Load(); got != 1 {
		t.Fatalf("consumer Apply count = %d, want 1 (no reactivation)", got)
	}

	_ = cf.Dispose()
	_ = cf.Gone(ctxTimeout(t))
	_ = pf.Gone(ctxTimeout(t))
}

// R10 — heavy member churn: registry identity and consumer activation stay
// stable throughout.
func TestR10MemberChurnConsumerStable(t *testing.T) {
	rt := newTestRuntime(t)

	var pApplies, cApplies, cInverses atomic.Int32
	regCh := make(chan *registry.Registry[handler], 1)
	prov := &registryProvider{name: "prov", ch: regCh, applies: &pApplies}
	cons := &registryConsumer{name: "cons", applies: &cApplies, inverses: &cInverses}

	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(ctxTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	reg := <-regCh

	const ids = 8
	const rounds = 200
	for r := 0; r < rounds; r++ {
		for i := 0; i < ids; i++ {
			id := registry.MemberID(string(rune('a' + i)))
			_ = reg.Add(id, handler{string(id)})
		}
		for i := 0; i < ids; i += 2 {
			_ = reg.Replace(registry.MemberID(string(rune('a'+i))), handler{"new"})
		}
		for i := 1; i < ids; i += 2 {
			_ = reg.Remove(registry.MemberID(string(rune('a' + i))))
		}
	}
	// Re-add all and settle.
	for i := 0; i < ids; i++ {
		_ = reg.Add(registry.MemberID(string(rune('a'+i))), handler{"final"})
	}

	if got := reg.Snapshot().Len(); got != ids {
		t.Fatalf("registry members = %d, want %d", got, ids)
	}
	if got := pApplies.Load(); got != 1 {
		t.Fatalf("provider Apply count = %d, want 1 (identity preserved under churn)", got)
	}
	if got := cApplies.Load(); got != 1 {
		t.Fatalf("consumer Apply count = %d, want 1", got)
	}
	if got := cInverses.Load(); got != 0 {
		t.Fatalf("consumer inverse count = %d, want 0", got)
	}
	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %v, want Active", got)
	}

	_ = pf.Dispose()
	_ = cf.Dispose()
}

// §7 / §17 — membership vs fiber lifecycle separation: a member Fiber adds
// itself through a reversible membership effect; its unload removes only its
// own membership, while the registry provider and the consumer stay stable.
func TestMemberFiberLifecycleSeparation(t *testing.T) {
	rt := newTestRuntime(t)

	var pApplies, cApplies atomic.Int32
	regCh := make(chan *registry.Registry[handler], 1)
	prov := &registryProvider{name: "registry", ch: regCh, applies: &pApplies}
	cons := &registryConsumer{name: "consumer", applies: &cApplies}

	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load registry: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(ctxTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	reg := <-regCh

	// Member fiber: requires the registry, adds itself while Active.
	member := &memberFiber{name: "tool-A", id: "tool-A"}
	mf, err := rt.Load(member)
	if err != nil {
		t.Fatalf("Load member: %v", err)
	}
	if err := mf.Ready(ctxTimeout(t)); err != nil {
		t.Fatalf("member Ready: %v", err)
	}
	if !reg.Has("tool-A") {
		t.Fatal("member not registered while Active")
	}

	// Dispose the member: its membership effect removes only itself.
	if err := mf.Dispose(); err != nil {
		t.Fatalf("member Dispose: %v", err)
	}
	if err := mf.Gone(ctxTimeout(t)); err != nil {
		t.Fatalf("member Gone: %v", err)
	}
	if reg.Has("tool-A") {
		t.Fatal("member membership leaked after member fiber Gone")
	}
	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %v, want Active (member removal is not dependency loss)", got)
	}
	if got := cApplies.Load(); got != 1 {
		t.Fatalf("consumer Apply count = %d, want 1", got)
	}
	if got := pApplies.Load(); got != 1 {
		t.Fatalf("registry Apply count = %d, want 1", got)
	}

	_ = pf.Dispose()
	_ = cf.Dispose()
}

type memberFiber struct {
	name string
	id   registry.MemberID
}

func (c *memberFiber) Name() string { return c.name }
func (c *memberFiber) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(handlerRegKey)}
}
func (c *memberFiber) Provide() []runtime.Capability { return nil }
func (c *memberFiber) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	reg, err := runtime.Require(ctx, handlerRegKey)
	if err != nil {
		return nil, err
	}
	if err := addMemberEffect(ctx, reg, c.id, handler{name: c.name}); err != nil {
		return nil, err
	}
	return nil, nil
}
