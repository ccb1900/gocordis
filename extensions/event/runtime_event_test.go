package event_test

// Runtime integration tests: Event Bus as a Runtime-managed capability over the
// real Kernel (E12-E14). The Kernel is not modified.

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/event"
	"dynamic-runtime/runtime"
)

var eventBusKey = runtime.NewKey[*event.Bus]("event-bus")

func rtTimeout(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() { _ = rt.Close(rtTimeout(t)) })
	return rt
}

func rtWaitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
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

// busProvider creates an Event Bus, registers bus.Close as a reversible effect,
// and provides the Bus as a stable capability.
type busProvider struct {
	name    string
	ch      chan *event.Bus
	applies *atomic.Int32
}

func (c *busProvider) Name() string                 { return c.name }
func (c *busProvider) Inject() []runtime.Dependency { return nil }
func (c *busProvider) Provide() []runtime.Capability {
	return []runtime.Capability{eventBusKey.Capability()}
}
func (c *busProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	bus := event.New()
	if c.ch != nil {
		c.ch <- bus
	}
	// Runtime-managed cleanup: inverse closes the Bus on activation unwind.
	if err := ctx.Effect(func() (func() error, error) {
		return bus.Close, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, eventBusKey, bus); err != nil {
		return nil, err
	}
	return nil, nil
}

// busConsumer depends on the Event Bus capability (NOT on any subscription).
type busConsumer struct {
	name     string
	applies  *atomic.Int32
	inverses *atomic.Int32
	seen     chan *event.Bus
}

func (c *busConsumer) Name() string { return c.name }
func (c *busConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(eventBusKey)}
}
func (c *busConsumer) Provide() []runtime.Capability { return nil }
func (c *busConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	bus, err := runtime.Require(ctx, eventBusKey)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		select {
		case c.seen <- bus:
		default:
		}
	}
	return func() error {
		if c.inverses != nil {
			c.inverses.Add(1)
		}
		return nil
	}, nil
}

// E12 — Runtime integration: subscription churn on an Event Bus provided by a
// stable Provider never reactivates the Consumer and never changes the
// Provider identity.
func TestE12EventBusConsumerStableUnderSubscriptionChurn(t *testing.T) {
	rt := newRT(t)

	var pApplies, cApplies, cInverses atomic.Int32
	busCh := make(chan *event.Bus, 1)
	prov := &busProvider{name: "bus-prov", ch: busCh, applies: &pApplies}
	cons := &busConsumer{name: "bus-consumer", applies: &cApplies, inverses: &cInverses}

	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(rtTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	bus := <-busCh

	// Subscription churn is Event-internal resource churn.
	for i := 0; i < 50; i++ {
		s, err := bus.Subscribe("device.connected")
		if err != nil {
			t.Fatalf("Subscribe: %v", err)
		}
		if err := bus.Publish(event.Event{Type: "device.connected", Payload: i}); err != nil {
			t.Fatalf("Publish: %v", err)
		}
		select {
		case <-s.Events():
		case <-time.After(testTimeout):
			t.Fatal("subscription did not receive event")
		}
		if err := s.Close(); err != nil {
			t.Fatalf("Close: %v", err)
		}
	}

	if got := pApplies.Load(); got != 1 {
		t.Fatalf("bus provider Apply count = %d, want 1 (identity stable)", got)
	}
	if got := cApplies.Load(); got != 1 {
		t.Fatalf("consumer Apply count = %d, want 1 (no reactivation)", got)
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

// E13 — Provider replacement: when the Event Bus provider is replaced, the
// Consumer follows Kernel dependency identity replacement (reactivates onto the
// new Bus).
func TestE13EventBusProviderReplacement(t *testing.T) {
	rt := newRT(t)

	var cApplies atomic.Int32
	seen := make(chan *event.Bus, 4)
	cons := &busConsumer{name: "bus-consumer", applies: &cApplies, seen: seen}

	busCh1 := make(chan *event.Bus, 1)
	p1 := &busProvider{name: "bus-p1", ch: busCh1}
	p1f, err := rt.Load(p1)
	if err != nil {
		t.Fatalf("Load P1: %v", err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	if err := cf.Ready(rtTimeout(t)); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	bus1 := <-busCh1

	// Replace P1 with P2 (withdraw-then-load).
	if err := p1f.Dispose(); err != nil {
		t.Fatalf("Dispose P1: %v", err)
	}
	if err := p1f.Gone(rtTimeout(t)); err != nil {
		t.Fatalf("P1 Gone: %v", err)
	}
	rtWaitState(t, cf, runtime.StatePending)

	busCh2 := make(chan *event.Bus, 1)
	p2 := &busProvider{name: "bus-p2", ch: busCh2}
	p2f, err := rt.Load(p2)
	if err != nil {
		t.Fatalf("Load P2: %v", err)
	}
	if err := cf.Ready(rtTimeout(t)); err != nil {
		t.Fatalf("consumer Ready after replacement: %v", err)
	}
	bus2 := <-busCh2

	// Consumer reactivated exactly once onto the new provider identity.
	if got := cApplies.Load(); got != 2 {
		t.Fatalf("consumer Apply count = %d, want 2", got)
	}
	if bus1 == bus2 {
		t.Fatal("P1 and P2 must be distinct Bus instances")
	}
	seenBus1 := <-seen
	seenBus2 := <-seen
	if seenBus1 != bus1 {
		t.Fatal("first activation did not resolve P1's bus")
	}
	if seenBus2 != bus2 {
		t.Fatal("second activation did not resolve P2's bus")
	}

	_ = p2f.Dispose()
	_ = cf.Dispose()
}

// E14 — Runtime disposal: when the activation owning the Bus unloads, the
// registered cleanup effect closes the Bus; every subscription closes; no leak.
func TestE14EventBusActivationDisposal(t *testing.T) {
	rt := newRT(t)

	busCh := make(chan *event.Bus, 1)
	prov := &busProvider{name: "bus-prov", ch: busCh}
	pf, err := rt.Load(prov)
	if err != nil {
		t.Fatalf("Load provider: %v", err)
	}
	if err := pf.Ready(rtTimeout(t)); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	bus := <-busCh

	s1, err := bus.Subscribe("a")
	if err != nil {
		t.Fatalf("Subscribe: %v", err)
	}
	s2, err := bus.Subscribe("b")
	if err != nil {
		t.Fatalf("Subscribe: %v", err)
	}
	if err := bus.Publish(event.Event{Type: "a", Payload: 1}); err != nil {
		t.Fatalf("Publish: %v", err)
	}
	select {
	case e := <-s1.Events():
		if e.Payload != 1 {
			t.Fatalf("payload = %v, want 1", e.Payload)
		}
	case <-time.After(testTimeout):
		t.Fatal("no event received")
	}

	// Dispose the owning activation: effect inverse closes the Bus.
	if err := pf.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := pf.Gone(rtTimeout(t)); err != nil {
		t.Fatalf("provider Gone: %v", err)
	}

	// Bus is closed by the registered effect: subscriptions closed, publish
	// fails.
	if err := bus.Publish(event.Event{Type: "a", Payload: 2}); !errors.Is(err, event.ErrBusClosed) {
		t.Fatalf("Publish after disposal = %v, want ErrBusClosed", err)
	}
	drainClosed := func(s *event.Subscription) bool {
		for {
			select {
			case _, open := <-s.Events():
				if !open {
					return true
				}
			case <-time.After(testTimeout):
				return false
			}
		}
	}
	if !drainClosed(s1) || !drainClosed(s2) {
		t.Fatal("subscriptions were not closed by Bus disposal")
	}
}
