package event_test

import (
	"context"
	. "dynamic-runtime/extensions/event"
	"sync/atomic"
	"testing"
)

// G-1 / G-2: the two test gaps identified in the P1 Event Runtime
// Conformance Review v0.1 (docs/review, commit f630c26).
//
// G-1 — generation correctness at the Event level: a dispose -> reload cycle
// must give the Component a NEW activation whose registrations replace the
// old ones. Generation A handlers must not be visible after generation B
// loads, in any dispatch mode, and unwinding must leave zero residue.
//
// G-2 — dependency interaction: an event handler registered by a consumer
// whose dependency comes from a provider must disappear when the provider
// withdraws (consumer unload -> Effect unwind -> event unregister). A stale
// handler after provider loss would violate §21 of the P1 contract.

var (
	p1GapEvKey = NewEventKey[string]("p1.gap.evt")
	p1GapWFKey = NewEventKey[string]("p1.gap.wf")
	p1GapSvc   = NewKey[string]("p1.gap.svc")
)

// p1GapGen registers one plain handler (event) and one chain-aware handler
// (waterfall) on every activation; the recorded tag distinguishes activation
// A (1st) from activation B (2nd).
type p1GapGen struct {
	name    string
	rec     *p1Rec
	n       atomic.Int32
	selfCtx *Context
}

func (c *p1GapGen) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1GapGen) Name() string          { return c.name }
func (c *p1GapGen) Inject() []Dependency  { return nil }
func (c *p1GapGen) Provide() []Capability { return nil }
func (c *p1GapGen) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	tag := "A"
	if c.n.Add(1) >= 2 {
		tag = "B"
	}
	if err := On(ctx, p1GapEvKey, func(_ context.Context, p string) error {
		c.rec.add(tag + "-ev")
		return nil
	}); err != nil {
		return nil, err
	}
	if err := OnWaterfall(ctx, p1GapWFKey, func(_ context.Context, p string, next Next) error {
		c.rec.add(tag + "-wf")
		return next()
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

// G-1: activation generation replacement for event registrations.
//
//	Activation A (On + OnWaterfall) -- Emit/Waterfall hit A only
//	Dispose -> Gone                       -- registry residue 0
//	Activation B (On + OnWaterfall)       -- Emit/Waterfall hit B only
//	Dispose -> Gone                       -- registry residue 0
func TestP1ReviewGap1GenerationEventRegistration(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	host := p1Ready(t, rt, &p1Registrar{name: "host", rec: rec}) // live emitter, no handlers
	hostCtx := p1Ctx(host)
	gen := p1Ready(t, rt, &p1GapGen{name: "gen", rec: rec})

	// Generation A active: both dispatch modes reach the A handlers.
	if err := Emit(hostCtx, p1GapEvKey, "x"); err != nil {
		t.Fatalf("Emit (gen A): %v", err)
	}
	if got := p1Join(rec.got()); got != "A-ev" {
		t.Fatalf("gen A Emit calls = %q, want A-ev", got)
	}
	rec.calls = nil
	if err := Waterfall(context.Background(), hostCtx, p1GapWFKey, "x"); err != nil {
		t.Fatalf("Waterfall (gen A): %v", err)
	}
	if got := p1Join(rec.got()); got != "A-wf" {
		t.Fatalf("gen A Waterfall calls = %q, want A-wf", got)
	}
	p1AssertBindings(t, hostCtx, p1GapEvKey.ID(), 1)
	p1AssertBindings(t, hostCtx, p1GapWFKey.ID(), 1)

	// End generation A: every registration must be physically unwound.
	if err := gen.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := gen.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("gen A not gone: %v", err)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "gap-probe-x"), p1GapEvKey.ID(), p1GapWFKey.ID())

	// Generation B: the SAME Component instance activates again with fresh
	// registrations; stale A handlers must not be visible in any dispatch.
	if err := gen.Load(); err != nil {
		t.Fatalf("gen B Load: %v", err)
	}
	if err := gen.Ready(p1Timeout(t)); err != nil {
		t.Fatalf("gen B not ready: %v", err)
	}
	rec.calls = nil
	if err := Emit(hostCtx, p1GapEvKey, "y"); err != nil {
		t.Fatalf("Emit (gen B): %v", err)
	}
	if got := p1Join(rec.got()); got != "B-ev" {
		t.Fatalf("gen B Emit calls = %q, want B-ev only (no A-ev)", got)
	}
	rec.calls = nil
	if err := Waterfall(context.Background(), hostCtx, p1GapWFKey, "y"); err != nil {
		t.Fatalf("Waterfall (gen B): %v", err)
	}
	if got := p1Join(rec.got()); got != "B-wf" {
		t.Fatalf("gen B Waterfall calls = %q, want B-wf only (no A-wf)", got)
	}
	p1AssertBindings(t, hostCtx, p1GapEvKey.ID(), 1)
	p1AssertBindings(t, hostCtx, p1GapWFKey.ID(), 1)

	// End generation B: zero residue again.
	if err := gen.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := gen.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("gen B not gone: %v", err)
	}
	p1AssertNoBindings(t, p1ProbeCtx(t, rt, "gap-probe-x"), p1GapEvKey.ID(), p1GapWFKey.ID())
}

// p1GapProvider provides the capability the gap consumer depends on.
type p1GapProvider struct {
	name    string
	selfCtx *Context
}

func (c *p1GapProvider) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1GapProvider) Name() string          { return c.name }
func (c *p1GapProvider) Inject() []Dependency  { return nil }
func (c *p1GapProvider) Provide() []Capability { return []Capability{p1GapSvc.Capability()} }
func (c *p1GapProvider) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if err := Provide(ctx, p1GapSvc, "svc"); err != nil {
		return nil, err
	}
	return nil, nil
}

// p1GapConsumer declares a dependency on the provider and registers an event
// handler once its dependency is satisfied.
type p1GapConsumer struct {
	name    string
	rec     *p1Rec
	selfCtx *Context
}

func (c *p1GapConsumer) p1PublishedCtx() *Context { return c.selfCtx }

func (c *p1GapConsumer) Name() string          { return c.name }
func (c *p1GapConsumer) Inject() []Dependency  { return []Dependency{{Key: p1GapSvc.Capability()}} }
func (c *p1GapConsumer) Provide() []Capability { return nil }
func (c *p1GapConsumer) Apply(ctx *Context) (Cleanup, error) {
	c.selfCtx = ctx
	if _, err := Require(ctx, p1GapSvc); err != nil {
		return nil, err
	}
	if err := On(ctx, p1GapEvKey, func(_ context.Context, p string) error {
		c.rec.add("consumer-ev")
		return nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

// G-2: provider withdrawal must remove the dependent consumer's event
// handler. "Provider gone but event handler remains active" is forbidden.
func TestP1ReviewGap2DependencyLossUnregistersHandler(t *testing.T) {
	rt := p1Runtime(t)
	rec := &p1Rec{}
	host := p1Ready(t, rt, &p1Registrar{name: "host", rec: rec}) // live emitter
	hostCtx := p1Ctx(host)

	// Consumer mounts first: Pending until its provider exists.
	cf, err := rt.Load(&p1GapConsumer{name: "consumer", rec: rec})
	if err != nil {
		t.Fatalf("Load consumer: %v", err)
	}
	p1WaitState(t, cf, StatePending)

	pf := p1Ready(t, rt, &p1GapProvider{name: "provider"})
	if err := cf.Ready(p1Timeout(t)); err != nil {
		t.Fatalf("consumer not ready after provider: %v", err)
	}
	if cf.State() != StateActive {
		t.Fatalf("consumer state = %v, want Active", cf.State())
	}

	// While the dependency holds, the consumer handler fires.
	if err := Emit(hostCtx, p1GapEvKey, "a"); err != nil {
		t.Fatalf("Emit: %v", err)
	}
	if got := p1Join(rec.got()); got != "consumer-ev" {
		t.Fatalf("handler calls while satisfied = %q, want consumer-ev", got)
	}
	p1AssertBindings(t, hostCtx, p1GapEvKey.ID(), 1)
	rec.calls = nil

	// Provider withdraws: consumer unloads (Pending, not Failed) and its
	// Effect unwind must unregister the event handler.
	if err := pf.Dispose(); err != nil {
		t.Fatalf("provider Dispose: %v", err)
	}
	if err := pf.Gone(p1Timeout(t)); err != nil {
		t.Fatalf("provider not gone: %v", err)
	}
	if err := cf.WaitInactive(p1Timeout(t)); err != nil {
		t.Fatalf("consumer did not end its activation: %v", err)
	}
	p1WaitState(t, cf, StatePending)
	if cf.Err() != nil {
		t.Fatalf("consumer Err after dependency loss = %v, want nil", cf.Err())
	}

	// Stale handler must be 0: emitting must not reach the old handler.
	if err := Emit(hostCtx, p1GapEvKey, "b"); err != nil {
		t.Fatalf("Emit after withdrawal: %v", err)
	}
	if got := p1Join(rec.got()); got != "" {
		t.Fatalf("stale handler fired after dependency loss: %q", got)
	}
	p1AssertNoBindings(t, hostCtx, p1GapEvKey.ID(), p1GapWFKey.ID())
}
