package integration

import (
	"context"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/event"
	"dynamic-runtime/extensions/scheduler"
	"dynamic-runtime/runtime"
)

// E2E-06 / P-05 Registry Member Churn: member add/remove/replace never changes
// provider identity nor the consumer fiber.
func TestE2E06RegistryMemberChurn(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())

	ownerKit := &kit{}
	consKit := &kit{}
	owner := regOwnerComp(ownerKit, "R")
	cons := regConsumerComp(consKit, "C")

	of, err := rt.Load(owner)
	if err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, of, cf)
	reg := owner.reg
	if reg == nil {
		t.Fatal("owner did not expose registry")
	}
	appliesBefore := consKit.applies.Load()

	_ = reg.Add("A", "a")
	_ = reg.Remove("A")
	_ = reg.Add("B", "b")
	_ = reg.Replace("B", "b2")

	if got := cf.State(); got != runtime.StateActive {
		t.Fatalf("consumer state = %s", got)
	}
	if got := consKit.applies.Load(); got != appliesBefore {
		t.Fatalf("consumer re-applied on member churn: %d -> %d", appliesBefore, got)
	}
	if reg.Len() != 1 {
		t.Fatalf("registry len = %d", reg.Len())
	}
	_ = of
}

// E2E-07 Registry Provider Replacement: replacing the registry OWNER changes
// provider identity and the consumer follows via the kernel.
func TestE2E07RegistryProviderReplacement(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())

	owner1Kit := &kit{}
	owner2Kit := &kit{}
	consKit := &kit{seen: make(chan string, 8)}

	owner1 := regOwnerComp(owner1Kit, "R1")
	cons := regConsumerComp(consKit, "C")
	o1f, err := rt.Load(owner1)
	if err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, o1f, cf)
	reg1 := owner1.reg

	// Replace the owner.
	if err := o1f.Dispose(); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := cf.WaitInactive(ctx); err != nil {
		t.Fatal(err)
	}
	// The replacement may only provide the same capability after the old
	// provider fiber has fully exited; otherwise R2 can collide with R1's
	// still-registered provider (duplicate provider).
	if err := o1f.Gone(ctx); err != nil {
		t.Fatal(err)
	}

	owner2 := regOwnerComp(owner2Kit, "R2")
	o2f, err := rt.Load(owner2)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, o2f)
	waitActive(t, cf) // consumer recovers onto the new registry provider
	reg2 := owner2.reg
	if reg1 == reg2 {
		t.Fatal("registry object reused across provider replacement")
	}
	if consKit.applies.Load() < 2 {
		t.Fatalf("consumer applies = %d, want >= 2", consKit.applies.Load())
	}
	_ = reg1
}

// E2E-15 / P-06 Event Independence: publishing events never changes fibers.
func TestE2E15EventIndependence(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())

	kitA := &kit{}
	f, err := rt.Load(independentComp(kitA, "job", false))
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)

	bus := event.New()
	sub, err := bus.Subscribe("topic")
	if err != nil {
		t.Fatal(err)
	}
	for i := 0; i < 10; i++ {
		if err := bus.Publish(event.Event{Type: "topic", Payload: i}); err != nil {
			t.Fatal(err)
		}
	}
	count := 0
	for count < 10 {
		select {
		case <-sub.Events():
			count++
		case <-time.After(3 * time.Second):
			t.Fatal("event not delivered")
		}
	}
	_ = sub.Close()
	_ = bus.Close()

	if f.State() != runtime.StateActive {
		t.Fatal("fiber state changed by events")
	}
	if kitA.applies.Load() != 1 {
		t.Fatalf("fiber re-applied due to events: %d", kitA.applies.Load())
	}
}

// E2E-16 / P-07 Scheduler Independence: running a job never creates fibers or
// changes fiber state.
func TestE2E16SchedulerIndependence(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())

	kitA := &kit{}
	f, err := rt.Load(independentComp(kitA, "svc", false))
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)

	sch := scheduler.New()
	var runs atomic.Int32
	if err := sch.Add(scheduler.Job{
		ID:       "tick",
		Schedule: scheduler.Interval{Every: 10 * time.Millisecond},
		Task: func(ctx context.Context) error {
			runs.Add(1)
			return nil
		},
	}); err != nil {
		t.Fatal(err)
	}
	eventually(t, 5*time.Second, "scheduler ran", func() bool { return runs.Load() >= 3 })
	if err := sch.Close(); err != nil {
		t.Fatal(err)
	}
	if f.State() != runtime.StateActive {
		t.Fatal("fiber state changed by scheduler")
	}
	if kitA.applies.Load() != 1 {
		t.Fatalf("fiber re-applied due to scheduler: %d", kitA.applies.Load())
	}
}

// E2E-24 Cross-Extension Isolation: failures in one plane don't corrupt others.
func TestE2E24CrossExtensionIsolation(t *testing.T) {
	e, k := newCfgEnv(t)
	// Config: an independent component that stays healthy.
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{
		{ID: "ok", Type: "independent"},
	}}); err != nil {
		t.Fatal(err)
	}
	healthy := fiberOf(t, e, "ok")
	waitActive(t, healthy)

	// A config failure: a component whose Apply fails (isolated Failed).
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{
		{ID: "ok", Type: "independent"},
		{ID: "bad", Type: "independent", Config: map[string]any{"fail": true}},
	}}); err != nil {
		t.Fatal(err) // reconcile submits; failure shows on the fiber
	}
	bad := fiberOf(t, e, "bad")
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := bad.Ready(ctx); err == nil {
		t.Fatal("bad component should fail")
	}
	_ = bad
	_ = k

	// Registry mutation, event publishing, and scheduler run concurrently with
	// the failed component; nothing corrupts the healthy fiber.
	rt := e.rt
	regComp := regOwnerComp(&kit{}, "R")
	regF, err := rt.Load(regComp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, regF)
	_ = regComp.reg.Add("x", "1")
	_ = regComp.reg.Remove("x")

	bus := event.New()
	_ = bus.Publish(event.Event{Type: "x"})
	_ = bus.Close()

	sch := scheduler.New()
	_ = sch.Add(scheduler.Job{ID: "j", Schedule: scheduler.Interval{Every: 5 * time.Millisecond},
		Task: func(context.Context) error { return nil }})
	time.Sleep(15 * time.Millisecond)
	_ = sch.Close()

	if healthy.State() != runtime.StateActive {
		t.Fatal("healthy fiber corrupted by other extensions' activity")
	}
}

// E2E-25 / P-09 Ownership Isolation: controller A removal never touches B or
// foreign fibers.
func TestE2E25OwnershipIsolation(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())

	mk := func() (*config.Controller, *kit) {
		reg := config.NewFactoryRegistry()
		k := &kit{}
		if err := reg.Register("independent", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return independentComp(k, cc.ID, false), nil
		}}); err != nil {
			t.Fatal(err)
		}
		return config.NewController(rt, reg), k
	}
	ctrlA, kitA := mk()
	ctrlB, kitB := mk()
	defer ctrlA.CloseContext(shortCtx())
	defer ctrlB.CloseContext(shortCtx())

	foreign, err := rt.Load(independentComp(&kit{}, "foreign", false))
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, foreign)

	cc := func(id string) config.ComponentConfig {
		return config.ComponentConfig{ID: id, Type: "independent"}
	}
	if err := ctrlA.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc("A")}}); err != nil {
		t.Fatal(err)
	}
	if err := ctrlB.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{cc("B")}}); err != nil {
		t.Fatal(err)
	}
	waitActive(t, fiberOf(t, &env{ctrl: ctrlA}, "A"))
	waitActive(t, fiberOf(t, &env{ctrl: ctrlB}, "B"))

	if err := ctrlA.Reconcile(ctxT(t), config.Config{}); err != nil {
		t.Fatal(err)
	}
	if len(ctrlA.Owned()) != 0 {
		t.Fatal("controller A still owns components")
	}
	if len(ctrlB.Owned()) != 1 {
		t.Fatal("controller B affected by A removal")
	}
	if foreign.State() != runtime.StateActive {
		t.Fatal("foreign fiber affected by A removal")
	}
	_ = kitA
	_ = kitB
}
