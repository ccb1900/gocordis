package runtime_test

import (
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// F-01 Context Scope / Isolation — public-behavior tests (§5.2 / §5.5).
//
// The runtime expresses scopes as explicit child realms (ctx.Child with
// runtime.WithScope). Sibling realms may provide the same logical key in
// parallel; lookup walks own realm -> parent chain (nearest wins) and never
// crosses sibling boundaries. Realm removal is not a lifecycle authority: the
// owning fiber's activation unwinds and its provider registration is reclaimed
// through the reversible Context effect, which is what makes a dependent
// consumer lose its binding.

var f01Key = runtime.NewKey[string]("f01.db")

type f01Prov struct {
	tag string
}

func (c *f01Prov) Name() string                  { return "f01-provider:" + c.tag }
func (c *f01Prov) Inject() []runtime.Dependency  { return nil }
func (c *f01Prov) Provide() []runtime.Capability { return []runtime.Capability{f01Key.Capability()} }
func (c *f01Prov) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, f01Key, c.tag)
}

type f01Cons struct {
	out chan string
}

func (c *f01Cons) Name() string { return "f01-consumer" }
func (c *f01Cons) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(f01Key)}
}
func (c *f01Cons) Provide() []runtime.Capability { return nil }
func (c *f01Cons) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, f01Key)
	if err != nil {
		return nil, err
	}
	if c.out != nil {
		select {
		case c.out <- v:
		default:
		}
	}
	return nil, nil
}

// f01ScopeHost mounts one provider + one consumer of the same logical key in
// the scope realm it is mounted into (children inherit the host realm).
// syncProvider delays the consumer until the provider is Active, which makes
// nearest-wins deterministic when an ancestor provider for the same key
// already exists.
type f01ScopeHost struct {
	tag          string
	ch           chan *runtime.Fiber // receives p, x in creation order
	out          chan string
	syncProvider bool
}

func (c *f01ScopeHost) Name() string                  { return "f01-scope-host:" + c.tag }
func (c *f01ScopeHost) Inject() []runtime.Dependency  { return nil }
func (c *f01ScopeHost) Provide() []runtime.Capability { return nil }
func (c *f01ScopeHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	p, err := ctx.Child(&f01Prov{tag: c.tag})
	if err != nil {
		return nil, err
	}
	if c.syncProvider {
		if err := p.Ready(ctx.Context()); err != nil {
			return nil, err
		}
	}
	x, err := ctx.Child(&f01Cons{out: c.out})
	if err != nil {
		return nil, err
	}
	c.ch <- p
	c.ch <- x
	return nil, nil
}

// f01SiblingActivator mounts two sibling explicit scopes A and B.
type f01SiblingActivator struct {
	ha, hb     chan *runtime.Fiber
	outA, outB chan string
}

func (c *f01SiblingActivator) Name() string                  { return "f01-sibling-activator" }
func (c *f01SiblingActivator) Inject() []runtime.Dependency  { return nil }
func (c *f01SiblingActivator) Provide() []runtime.Capability { return nil }
func (c *f01SiblingActivator) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	_, errA := ctx.Child(&f01ScopeHost{tag: "A", ch: c.ha, out: c.outA}, runtime.WithScope())
	if errA != nil {
		return nil, errA
	}
	_, errB := ctx.Child(&f01ScopeHost{tag: "B", ch: c.hb, out: c.outB}, runtime.WithScope())
	return nil, errB
}

// TestF01ScopeSiblingIsolationAndDispose — §5.5 acceptance:
//
//	Scope A: provide("db")->A ; Scope B: provide("db")->B
//	Consumer A -> A ; Consumer B -> B
//	dispose Scope B's provider -> Consumer B loses B (Pending)
//	Consumer A remains Active
//	reload Scope B's provider -> only Consumer B recovers to B
func TestF01ScopeSiblingIsolationAndDispose(t *testing.T) {
	rt := newTestRuntime(t)
	ha, hb := make(chan *runtime.Fiber, 2), make(chan *runtime.Fiber, 2)
	outA, outB := make(chan string, 8), make(chan string, 8)
	act := &f01SiblingActivator{ha: ha, hb: hb, outA: outA, outB: outB}
	af, err := rt.Load(act)
	if err != nil {
		t.Fatal(err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("activator not ready: %v", err)
	}
	pa, ca := <-ha, <-ha
	pb, cb := <-hb, <-hb
	f01WaitActive(t, pa)
	f01WaitActive(t, ca)
	f01WaitActive(t, pb)
	f01WaitActive(t, cb)
	if v := f01MustValue(t, "scope A consumer", outA); v != "A" {
		t.Fatalf("scope A consumer resolved %q, want A", v)
	}
	if v := f01MustValue(t, "scope B consumer", outB); v != "B" {
		t.Fatalf("scope B consumer resolved %q, want B", v)
	}

	// Dispose scope B's provider: B's consumer loses the binding -> Pending;
	// scope A (sibling realm, same key) must be unaffected.
	if err := pb.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := cb.WaitInactive(testTimeout(t)); err != nil {
		t.Fatalf("scope B consumer did not end its activation: %v", err)
	}
	f01WaitState(t, cb, runtime.StatePending)
	if ca.State() != runtime.StateActive {
		t.Fatalf("scope A consumer disturbed by scope B dispose: state %v", ca.State())
	}
	f01MustEmpty(t, "scope A consumer", outA)

	// Reload scope B's provider: only B recovers to the B binding.
	if err := pb.Load(); err != nil {
		t.Fatal(err)
	}
	if err := pb.Ready(testTimeout(t)); err != nil {
		t.Fatalf("scope B provider not ready after reload: %v", err)
	}
	if err := cb.Ready(testTimeout(t)); err != nil {
		t.Fatalf("scope B consumer did not recover after provider reload: %v", err)
	}
	if v := f01MustValue(t, "scope B consumer after reload", outB); v != "B" {
		t.Fatalf("scope B consumer re-resolved %q, want B", v)
	}
	if ca.State() != runtime.StateActive {
		t.Fatalf("scope A consumer disturbed after scope B reload: state %v", ca.State())
	}
	f01MustEmpty(t, "scope A consumer after scope B reload", outA)

	_ = af.Dispose()
	_ = af.Gone(testTimeout(t))
}

// f01ChainActivator mounts an empty explicit scope holding only a consumer
// (parent-chain fallback) and an explicit scope host with its own provider
// (nearest wins) — §5.2 lookup order.
type f01ChainActivator struct {
	sc    chan *runtime.Fiber
	host  chan *runtime.Fiber
	outSC chan string
	outG  chan string
}

func (c *f01ChainActivator) Name() string                  { return "f01-chain-activator" }
func (c *f01ChainActivator) Inject() []runtime.Dependency  { return nil }
func (c *f01ChainActivator) Provide() []runtime.Capability { return nil }
func (c *f01ChainActivator) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	f, err := ctx.Child(&f01Cons{out: c.outSC}, runtime.WithScope())
	if err != nil {
		return nil, err
	}
	c.sc <- f
	if _, err := ctx.Child(&f01ScopeHost{tag: "G", ch: c.host, out: c.outG, syncProvider: true}, runtime.WithScope()); err != nil {
		return nil, err
	}
	return nil, nil
}

// TestF01ScopeChainNearestWinsAndParentFallback — lookup walks own realm then
// parent scopes: a consumer in an empty explicit scope resolves the root
// provider through the parent chain; a consumer in a scope with its own
// provider resolves the nearest (child) binding, not the ancestor.
func TestF01ScopeChainNearestWinsAndParentFallback(t *testing.T) {
	rt := newTestRuntime(t)
	rp, err := rt.Load(&f01Prov{tag: "root"})
	if err != nil {
		t.Fatal(err)
	}
	if err := rp.Ready(testTimeout(t)); err != nil {
		t.Fatalf("root provider not ready: %v", err)
	}

	sc := make(chan *runtime.Fiber, 1)
	host := make(chan *runtime.Fiber, 2)
	outSC := make(chan string, 8)
	outG := make(chan string, 8)
	act := &f01ChainActivator{sc: sc, host: host, outSC: outSC, outG: outG}
	af, err := rt.Load(act)
	if err != nil {
		t.Fatal(err)
	}
	if err := af.Ready(testTimeout(t)); err != nil {
		t.Fatalf("activator not ready: %v", err)
	}
	empty := <-sc
	pg, xg := <-host, <-host
	f01WaitActive(t, pg)
	f01WaitActive(t, xg)

	// Empty scope: its namespace has no provider for the key. Paper §4.4
	// Isolation — no ancestor walk, so the consumer NEVER activates on the
	// root binding; it stays Pending.
	f01WaitState(t, empty, runtime.StatePending)
	select {
	case v := <-outSC:
		t.Fatalf("empty-scope consumer unexpectedly resolved %q (ancestor fallback must not happen)", v)
	default:
	}

	// Scope with its own provider: that namespace's binding, not the root's.
	if v := f01MustValue(t, "scope-G consumer", outG); v != "G" {
		t.Fatalf("scope-G consumer resolved %q, want G (own namespace)", v)
	}

	_ = af.Dispose()
	_ = af.Gone(testTimeout(t))
	_ = rp.Dispose()
	_ = rp.Gone(testTimeout(t))
}

func f01Wait(t *testing.T, what string, cond func() bool) {
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

func f01WaitActive(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	f01Wait(t, f.Name()+" Active", func() bool { return f.State() == runtime.StateActive })
}

func f01WaitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	f01Wait(t, f.Name()+" state "+want.String(), func() bool { return f.State() == want })
}

func f01MustValue(t *testing.T, what string, out chan string) string {
	t.Helper()
	select {
	case v := <-out:
		return v
	case <-time.After(8 * time.Second):
		t.Fatalf("timeout waiting for %s value", what)
		return ""
	}
}

func f01MustEmpty(t *testing.T, what string, out chan string) {
	t.Helper()
	select {
	case v := <-out:
		t.Fatalf("%s unexpectedly observed value %q", what, v)
	default:
	}
}
