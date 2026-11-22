package broker_test

import (
	"context"
	"reflect"
	"testing"
	"time"

	"dynamic-runtime/extensions/broker"
	"dynamic-runtime/runtime"
)

// Conformance for the paper §6.2 service multiplexing pattern:
//
//	(1) Backing providers register through REVERSIBLE EFFECTS — unloading a
//	    provider reverts its registration and drops it from the routing set
//	    automatically, while the broker's own capability binding stays put.
//	(2) Rolling update = controlled provider transition: register v2, shift
//	    traffic, deregister v1 — the consumer never reloads and never observes
//	    a dependency change (the broker binding is stable).

func recvOut(t *testing.T, out <-chan string) string {
	t.Helper()
	select {
	case v := <-out:
		return v
	case <-time.After(5 * time.Second):
		t.Fatal("timed out waiting for broker response")
		return ""
	}
}

type encoder interface {
	Encode(string) string
}

// serviceBus is the capability the broker provides: dispatch over the routing
// set plus a view of it.
type serviceBus interface {
	Call(fn func(encoder) error) error
	Names() []string
}

var brokerKey = runtime.NewKey[serviceBus]("broker.encoder")

// encProv is one backing provider: registers with the broker in Apply (as a
// reversible effect) and exits the routing set when its activation unwinds.
type encProv struct {
	name string
	tag  string
	b    *broker.Broker[encoder]
}

func (c *encProv) Name() string { return "enc-provider:" + c.name }
func (c *encProv) Inject() []runtime.Dependency {
	return nil
}
func (c *encProv) Provide() []runtime.Capability { return nil }
func (c *encProv) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	var impl encoder = &upper{tag: c.tag}
	if err := broker.Register(ctx, c.b, c.name, impl); err != nil {
		return nil, err
	}
	return nil, nil
}

type upper struct{ tag string }

func (u *upper) Encode(s string) string { return u.tag + ":" + s }

// encConsumer consumes the broker capability and calls through it.
type encConsumer struct {
	out chan string
}

func (c *encConsumer) Name() string { return "enc-consumer" }
func (c *encConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(brokerKey)}
}
func (c *encConsumer) Provide() []runtime.Capability { return nil }
func (c *encConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	b, err := runtime.Require(ctx, brokerKey)
	if err != nil {
		return nil, err
	}
	_ = b.Call(func(e encoder) error {
		select {
		case c.out <- e.Encode("x"):
		default:
		}
		return nil
	})
	return nil, nil
}

// brokerStack provides the broker itself.
type brokerStack struct {
	b *broker.Broker[encoder]
}

func (c *brokerStack) Name() string { return "broker-stack" }
func (c *brokerStack) Inject() []runtime.Dependency {
	return nil
}
func (c *brokerStack) Provide() []runtime.Capability {
	return []runtime.Capability{brokerKey.Capability()}
}
func (c *brokerStack) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, brokerKey, serviceBus(c.b))
}

func waitActiveB(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == runtime.StateActive {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting %s Active (state %v err %v)", f.Name(), f.State(), f.Err())
}

func newBrokerRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

// TestBrokerReversibleRegistration — §6.2: providers add/remove to scale;
// unloading a provider drops it from the routing set automatically (revertible
// effect) and the broker binding stays put.
func TestBrokerReversibleRegistration(t *testing.T) {
	rt := newBrokerRT(t)
	b := broker.New[encoder](nil)
	out := make(chan string, 8)

	if _, err := rt.Load(&brokerStack{b: b}); err != nil {
		t.Fatal(err)
	}
	p1, err := rt.Load(&encProv{name: "v1", tag: "v1", b: b})
	if err != nil {
		t.Fatal(err)
	}
	p2, err := rt.Load(&encProv{name: "v2", tag: "v2", b: b})
	if err != nil {
		t.Fatal(err)
	}
	cons, err := rt.Load(&encConsumer{out: out})
	if err != nil {
		t.Fatal(err)
	}
	waitActiveB(t, p1)
	waitActiveB(t, p2)
	waitActiveB(t, cons)

	if got := b.Names(); len(got) != 2 {
		t.Fatalf("routing set = %v, want 2 entries", got)
	}

	// Unload v1: its registration reverts; v2 remains; broker still bound.
	if err := p1.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := p1.Gone(bailT(t)); err != nil {
		t.Fatal(err)
	}
	remaining := b.Names()
	if len(remaining) != 1 || remaining[0] != "v2" {
		t.Fatalf("routing set after unload = %v, want [v2]", remaining)
	}
	if cons.State() != runtime.StateActive {
		t.Fatalf("consumer disturbed by provider unload: %v", cons.State())
	}

	// Duplicate name is rejected while v2 is live (before any effect install).
	if err := broker.Register[encoder](nil, b, "v2", nil); err == nil {
		t.Fatal("duplicate registration must be rejected")
	} else if cons.State() != runtime.StateActive {
		t.Fatal("rejected duplicate must not disturb the broker")
	}
}

func bailT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// leakyProv calls broker.Register AFTER its activation context has been
// cancelled (Dispose while the fiber is Loading): the Effect must be refused
// and the registration must NOT leak into the broker beyond the fiber's
// lifecycle.
type leakyProv struct {
	b       *broker.Broker[encoder]
	entered chan struct{}
	regErr  chan error
}

func (c *leakyProv) Name() string                  { return "leaky-provider" }
func (c *leakyProv) Inject() []runtime.Dependency  { return nil }
func (c *leakyProv) Provide() []runtime.Capability { return nil }
func (c *leakyProv) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.entered <- struct{}{}
	// Wakes exactly when the withdrawal cancels this activation, so Register
	// below runs with an already-cancelling context (deterministic ordering).
	<-ctx.Done()
	var impl encoder = &upper{tag: "leak"}
	err := broker.Register(ctx, c.b, "leak", impl)
	c.regErr <- err
	return nil, err
}

func waitGoneB(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == runtime.StateGone {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting %s Gone (state %v err %v)", f.Name(), f.State(), f.Err())
}

// TestBrokerRegisterRefusedEffectLeaksNothing — the route registration is one
// ATOMIC Effect: the route is registered inside the Effect install function,
// so registration and its inverse are committed together by the effect system.
// If the effect is refused (activation context already cancelling/unwinding),
// nothing is registered — a provider must never leak into the broker beyond
// its fiber's lifecycle.
func TestBrokerRegisterRefusedEffectLeaksNothing(t *testing.T) {
	rt := newBrokerRT(t)
	b := broker.New[encoder](broker.First[encoder]())
	entered := make(chan struct{})
	regErr := make(chan error, 1)

	p, err := rt.Load(&leakyProv{b: b, entered: entered, regErr: regErr})
	if err != nil {
		t.Fatal(err)
	}
	<-entered // Apply is running and waiting for its withdrawal
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	// Apply wakes on ctx.Done(), Register is refused, and the fiber finishes
	// its whole lifecycle (Apply error + Unmounted intent -> Gone): nothing
	// may remain registered afterwards.
	waitGoneB(t, p)
	if err := <-regErr; err == nil {
		t.Fatal("Register must fail when the effect is refused")
	}
	if got := b.Names(); len(got) != 0 {
		t.Fatalf("provider leaked into the broker beyond its fiber's lifecycle: %v", got)
	}
}

// TestBrokerRoundRobinStartsAtOldest — the selector starts at index 0: the
// first call reaches the OLDEST registered provider and cycles from there.
func TestBrokerRoundRobinStartsAtOldest(t *testing.T) {
	rt := newBrokerRT(t)
	b := broker.New[encoder](nil) // nil policy = RoundRobin
	if _, err := rt.Load(&brokerStack{b: b}); err != nil {
		t.Fatal(err)
	}
	p1, err := rt.Load(&encProv{name: "v1", tag: "v1", b: b})
	if err != nil {
		t.Fatal(err)
	}
	p2, err := rt.Load(&encProv{name: "v2", tag: "v2", b: b})
	if err != nil {
		t.Fatal(err)
	}
	waitActiveB(t, p1)
	waitActiveB(t, p2)

	names := b.Names() // registration order (oldest first)
	if len(names) != 2 {
		t.Fatalf("routing set = %v, want 2 entries", names)
	}
	got := make([]string, 0, 4)
	for i := 0; i < 4; i++ {
		if err := b.Call(func(e encoder) error {
			got = append(got, e.Encode("x"))
			return nil
		}); err != nil {
			t.Fatal(err)
		}
	}
	want := []string{names[0] + ":x", names[1] + ":x", names[0] + ":x", names[1] + ":x"}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("round robin = %v, want %v (first call must select the oldest provider)", got, want)
	}
}

// TestBrokerRollingUpdate — §6.2 rolling updates: register the new provider,
// drain the old one; the consumer's dependency (the broker) never changes and
// the consumer never reloads.
func TestBrokerRollingUpdate(t *testing.T) {
	rt := newBrokerRT(t)
	b := broker.New[encoder](broker.First[encoder]())
	out := make(chan string, 8)

	if _, err := rt.Load(&brokerStack{b: b}); err != nil {
		t.Fatal(err)
	}
	pv1, err := rt.Load(&encProv{name: "v1", tag: "v1", b: b})
	if err != nil {
		t.Fatal(err)
	}
	cons, err := rt.Load(&encConsumer{out: out})
	if err != nil {
		t.Fatal(err)
	}
	waitActiveB(t, pv1)
	waitActiveB(t, cons)
	if v := recvOut(t, out); v != "v1:x" {
		t.Fatalf("v1 response = %q", v)
	}

	// (1) Load v2 as an additional fiber.
	pv2, err := rt.Load(&encProv{name: "v2", tag: "v2", b: b})
	if err != nil {
		t.Fatal(err)
	}
	waitActiveB(t, pv2)

	// (2) Shift traffic: retire v1.
	if err := pv1.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pv1.Gone(bailT(t)); err != nil {
		t.Fatal(err)
	}

	// (3) The consumer kept running against the stable broker binding and now
	// reaches v2 — no reload, no dependency perturbation.
	if cons.State() != runtime.StateActive {
		t.Fatalf("consumer reloaded during rolling update: %v", cons.State())
	}
	_ = b.Call(func(e encoder) error {
		select {
		case out <- e.Encode("y"):
		default:
		}
		return nil
	})
	if v := recvOut(t, out); v != "v2:y" {
		t.Fatalf("post-update response = %q, want v2:y", v)
	}
}
