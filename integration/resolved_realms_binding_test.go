package integration

import (
	"context"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// NEW-01 (audit): WithResolvedRealms must follow the provider the consumer
// was BOUND to through its realm move — never a global provider scan. Two
// isolated realms may legally provide the same key; a fiber-id-ordered scan
// would switch the dependency to a DIFFERENT legal provider when the scanned
// one sorts first, turning a restore into a silent retarget.
//
// Choreography: provider A and provider B each live in their own isolated
// realm and provide the same key (A mounted FIRST, so a fiber-id scan would
// find A first). The consumer binds B (its per-key table points at B's realm).
// B rehomes; the consumer goes Pending; the consumer revises with
// WithResolvedRealms — it must rebind to B (same activation), never to A.

var resolvedRealmsKey = runtime.NewKey[string]("resolved-realms.svc")

type rrProvider struct {
	kind string // "A" | "B"
	key  runtime.Key[string]
	cons chan *runtime.Fiber // delivers the spawned consumer (B only)
}

func (c *rrProvider) Name() string                  { return "rr-prov:" + c.kind }
func (c *rrProvider) Inject() []runtime.Dependency  { return nil }
func (c *rrProvider) Provide() []runtime.Capability { return []runtime.Capability{c.key.Capability()} }
func (c *rrProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, c.key, c.kind); err != nil {
		return nil, err
	}
	// The consumer is a child of THIS provider: its per-key table points at
	// this provider's realm — that is how a consumer binds to a SPECIFIC
	// isolated provider.
	child, err := ctx.Child(&rrConsumer{key: c.key, want: c.kind})
	if err != nil {
		return nil, err
	}
	if c.cons != nil {
		c.cons <- child
	}
	return nil, nil
}

type rrConsumer struct {
	key  runtime.Key[string]
	want string
}

func (c *rrConsumer) Name() string { return "rr-cons:" + c.want }
func (c *rrConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(c.key)}
}
func (c *rrConsumer) Provide() []runtime.Capability { return nil }
func (c *rrConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, c.key)
	if err != nil {
		return nil, err
	}
	if v != c.want {
		return nil, contextCanceledError("bound to " + v + ", want " + c.want)
	}
	return nil, nil
}

type contextCanceledError string

func (e contextCanceledError) Error() string { return string(e) }

type rrHost struct {
	kind  string
	key   runtime.Key[string]
	prov  runtime.Component
	child chan *runtime.Fiber
}

func (c *rrHost) Name() string { return "rr-host:" + c.kind }
func (c *rrHost) Inject() []runtime.Dependency {
	return nil
}
func (c *rrHost) Provide() []runtime.Capability { return nil }
func (c *rrHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	p, err := ctx.Child(c.prov, runtime.WithScope())
	if err != nil {
		return nil, err
	}
	if c.child != nil {
		c.child <- p
	}
	return nil, nil
}

func rrWaitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState, what string) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatalf("%s: fiber stuck in %v, want %v", what, f.State(), want)
}

func TestResolvedRealmsFollowsBoundProviderNotScanOrder(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	svc := resolvedRealmsKey

	// Provider A mounted FIRST (smaller fiber id — a scan would find it first).
	childA := make(chan *runtime.Fiber, 1)
	hostA, err := rt.Load(&rrHost{
		kind:  "A",
		key:   svc,
		prov:  &rrProvider{kind: "A", key: svc},
		child: childA,
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := hostA.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	pa := <-childA
	rrWaitState(t, pa, runtime.StateActive, "provider A")

	// Provider B mounted SECOND; its consumer binds B's realm.
	consCh := make(chan *runtime.Fiber, 1)
	childB := make(chan *runtime.Fiber, 1)
	hostB, err := rt.Load(&rrHost{
		kind:  "B",
		key:   svc,
		prov:  &rrProvider{kind: "B", key: svc, cons: consCh},
		child: childB,
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := hostB.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	pb := <-childB
	rrWaitState(t, pb, runtime.StateActive, "provider B")

	// The consumer (B's child) is Active and verified bound to B (the consumer
	// fails its Apply if it resolves anything but B).
	cons := <-consCh
	rrWaitState(t, cons, runtime.StateActive, "consumer binding to B")

	// B rehomes (short route — same activation).
	if err := pb.Rehome(ctx, runtime.RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}

	// The consumer loses its binding (old-namespace dependent).
	rrWaitState(t, cons, runtime.StatePending, "consumer withdrawal after B's rehome")

	// The revision follows the CAPTURED binding: it must rebind to B (same
	// activation), never to A — even though a fiber-id scan would find A first.
	nf, err := cons.Revise(ctx, &rrConsumer{key: svc, want: "B"}, runtime.WithResolvedRealms())
	if err != nil {
		t.Fatalf("revise: %v", err)
	}
	if err := nf.Ready(ctx); err != nil {
		t.Fatalf("revised consumer never rebinds: %v", err)
	}
	rrWaitState(t, nf, runtime.StateActive, "revised consumer")

	// Sanity: A still serves its own realm untouched.
	rrWaitState(t, pa, runtime.StateActive, "provider A untouched")
}
