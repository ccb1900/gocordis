package integration

import (
	"context"
	"dynamic-runtime/extensions/event"
	"fmt"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Platform Convergence E2E Gate — PC-01..PC-09 (core composition through the
// real public boundaries: Loader / Factory / Config / Runtime / Context).
// ---------------------------------------------------------------------------

// PC-01 — Artifact -> Module -> Component -> Fiber -> Active.
func TestPC01ArtifactToFiber(t *testing.T) {
	rt := pcNewRT(t)
	ld := loader.NewBuiltinLoader()
	defer ld.CloseContext(ctxT(t))

	if err := ld.RegisterBuiltin("builtin://pc-svc", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcProv(cc.ID), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}

	art := loader.Artifact{ID: "pc-mod-svc", Type: "pc.service", Source: "builtin://pc-svc", Version: "1"}
	mod, err := ld.Load(pcTimeout(t), art)
	if err != nil {
		t.Fatalf("loader.Load: %v", err)
	}
	// 1. Artifact materialized by the Loader into a Module.
	if !ld.Has("pc-mod-svc") {
		t.Fatal("artifact was not materialized into the loader module registry")
	}
	if mod.Factory == nil {
		t.Fatal("module has no Factory")
	}

	// 2-3. Module != Component != Fiber: distinct identities on the same path.
	comp, err := mod.Factory.Create(config.ComponentConfig{ID: "svc", Type: mod.Type})
	if err != nil {
		t.Fatalf("factory.Create: %v", err)
	}
	fiber, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("runtime.Load: %v", err)
	}
	if err := fiber.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("fiber not active: %v", err)
	}
	if mod.Identity().ID == comp.Name() || mod.Identity().ID == fmt.Sprint(fiber.ID()) {
		t.Fatal("module identity conflated with component/fiber identity")
	}
	if comp.Name() == fmt.Sprint(fiber.ID()) {
		t.Fatal("component identity conflated with fiber identity")
	}

	// 4, 8. The Runtime is the sole Fiber authority: the module's factory only
	// produced a Component; Active was reached through Runtime.Load + Ready.
	if fiber.State() != runtime.StateActive {
		t.Fatalf("fiber state = %s, want Active", fiber.State())
	}
	snap := pcSnap(t, rt)
	if len(snap.Fibers) != 1 || len(snap.Providers) != 1 {
		t.Fatalf("unexpected snapshot after activation: fibers=%d providers=%d",
			len(snap.Fibers), len(snap.Providers))
	}
	if snap.Fibers[0].ActivationID == 0 {
		t.Fatal("active fiber has no live activation")
	}
}

// PC-02 — Config Desired / Applied / Active are three distinct planes.
func TestPC02ConfigDesiredReconcile(t *testing.T) {
	rt := pcNewRT(t)
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	defer ctrl.CloseContext(ctxT(t))

	release := make(chan struct{})
	register := func(typ string, build func(cc config.ComponentConfig) (runtime.Component, error)) {
		t.Helper()
		if err := reg.Register(typ, &adapterFactory{build: build}); err != nil {
			t.Fatal(err)
		}
	}
	register("slow", func(config.ComponentConfig) (runtime.Component, error) { return pcGateApply("slow", release), nil })
	register("prov", func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		return pcProv(tag), nil
	})

	desired := config.Config{Components: []config.ComponentConfig{
		{ID: "slow", Type: "slow"},
		{ID: "p", Type: "prov", Config: map[string]any{"tag": "v1"}},
	}}
	if err := ctrl.Reconcile(pcTimeout(t), desired); err != nil {
		t.Fatalf("reconcile desired: %v", err)
	}
	slow := fiberOf(t, &env{ctrl: ctrl}, "slow")
	p := fiberOf(t, &env{ctrl: ctrl}, "p")

	// Applied (Owned) matches Desired immediately...
	if len(ctrl.Owned()) != 2 {
		t.Fatalf("applied components = %d, want 2", len(ctrl.Owned()))
	}
	// ...but Applied is NOT Active: the gated fiber stays Loading until its
	// Apply completes, so Desired != Applied != Active are observable planes.
	eventually(t, 5*time.Second, "slow reaches Loading", func() bool {
		return slow.State() == runtime.StateLoading
	})
	if slow.State() == runtime.StateActive {
		t.Fatal("Applied fiber reported Active before activation completed")
	}
	if err := p.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("provider not active: %v", err)
	}
	if p.State() != runtime.StateActive {
		t.Fatalf("provider state = %s, want Active", p.State())
	}

	close(release)
	if err := slow.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("released fiber did not activate: %v", err)
	}

	// Reconciliation to the empty desired state removes every owned component.
	if err := ctrl.Reconcile(pcTimeout(t), config.Config{}); err != nil {
		t.Fatalf("reconcile empty: %v", err)
	}
	if len(ctrl.Owned()) != 0 {
		t.Fatalf("applied components = %d after empty desired, want 0", len(ctrl.Owned()))
	}
	for _, f := range []*runtime.Fiber{slow, p} {
		if err := f.Gone(pcTimeout(t)); err != nil {
			t.Fatalf("fiber %s did not reach Gone: %v", f.Name(), err)
		}
	}
	pcQuiesced(t, rt, "PC-02")
}

// PC-03 — Dependency-driven activation: a consumer must not activate while its
// capability is unprovided; activation follows the provider.
func TestPC03DependencyActivation(t *testing.T) {
	rt := pcNewRT(t)
	k := &kit{seen: make(chan string, 8)}

	cf, err := rt.Load(pcCons(k, k.seen))
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	// Consumer exists but must NOT become Active without its dependency.
	eventually(t, 3*time.Second, "consumer stays Pending", func() bool {
		s := cf.State()
		return s != runtime.StateActive && s != runtime.StateGone
	})
	if cf.State() == runtime.StateActive {
		t.Fatal("consumer activated without a provider")
	}

	pf := pcLoadActive(t, rt, pcProv("v1"))
	if err := cf.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("consumer did not activate after provider: %v", err)
	}
	if got := pcRecv(t, k.seen, "consumer binding"); got != "v1" {
		t.Fatalf("consumer resolved tag %q, want v1", got)
	}
	if pf.State() != runtime.StateActive {
		t.Fatal("provider no longer active")
	}
}

// PC-04 — Dependency withdrawal -> dependent invalidation -> recovery on a
// NEW provider generation (activation isolation).
func TestPC04DependencyWithdrawalRecovery(t *testing.T) {
	rt := pcNewRT(t)
	k := &kit{seen: make(chan string, 8)}

	pf1 := pcLoadActive(t, rt, pcProv("v1"))
	cf := pcLoadActive(t, rt, pcCons(k, k.seen))
	if got := pcRecv(t, k.seen, "first binding"); got != "v1" {
		t.Fatalf("consumer resolved %q, want v1", got)
	}
	gen1 := func() (runtime.ActivationID, runtime.FiberID) {
		snap := pcSnap(t, rt)
		for _, f := range snap.Fibers {
			if f.ID == cf.ID() {
				return f.ActivationID, 0
			}
		}
		return 0, 0
	}
	a1, _ := gen1()
	if a1 == 0 {
		t.Fatal("consumer has no live activation")
	}

	// Withdraw the provider: the consumer must lose its dependency (it stops
	// being Active) but it must NOT be destroyed — dependency != ownership.
	pcDisposeGone(t, pf1)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := cf.WaitInactive(ctx); err != nil {
		t.Fatalf("consumer did not lose its dependency: %v", err)
	}
	if cf.State() == runtime.StateActive {
		t.Fatal("consumer remained Active after provider withdrawal")
	}

	// Recovery binds to a NEW provider generation with a fresh activation.
	pf2 := pcLoadActive(t, rt, pcProv("v2"))
	if err := cf.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("consumer did not recover: %v", err)
	}
	if got := pcRecv(t, k.seen, "recovered binding"); got != "v2" {
		t.Fatalf("consumer resolved %q after recovery, want v2", got)
	}
	a2, _ := gen1()
	if a2 == a1 {
		t.Fatal("consumer reactivation reused the stale activation identity")
	}
	snap := pcSnap(t, rt)
	var depOK bool
	for _, d := range snap.Dependencies {
		if d.ConsumerFiberID == cf.ID() {
			if d.Status != runtime.DependencySatisfied || d.ProviderFiberID != pf2.ID() {
				t.Fatalf("consumer dependency not bound to new provider: %+v", d)
			}
			depOK = true
		}
	}
	if !depOK {
		t.Fatal("consumer dependency missing from snapshot")
	}
}

// PC-05 — Effect ownership: LIFO unwind (A,B,C -> C,B,A), no leak after
// disposal or Runtime Close.
func TestPC05EffectOwnership(t *testing.T) {
	rec := &pcRec{}
	rt := pcNewRT(t)
	f := pcLoadActive(t, rt, pcEffectHost("pc-effect-host", rec, "A", "B", "C"))
	if got := pcEffectCount(pcSnap(t, rt)); got != 3 {
		t.Fatalf("effect count = %d, want 3", got)
	}
	pcDisposeGone(t, f)
	got := rec.got()
	want := []string{"+A", "+B", "+C", "-C", "-B", "-A"}
	if len(got) != len(want) {
		t.Fatalf("effect trace = %v, want %v", got, want)
	}
	for i := range want {
		if got[i] != want[i] {
			t.Fatalf("effect trace = %v, want %v (LIFO inverse)", got, want)
		}
	}
	pcQuiesced(t, rt, "PC-05 dispose")

	// Runtime Close also unwinds every effect without leaking or hanging.
	rt2 := pcNewRT(t)
	_ = pcLoadActive(t, rt2, pcEffectHost("pc-effect-host-2", rec, "A", "B"))
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	if err := rt2.Close(ctx); err != nil {
		t.Fatalf("runtime close with live effects: %v", err)
	}
	if !rec.has("-B") || !rec.has("-A") {
		t.Fatalf("close did not unwind effects: %v", rec.got())
	}
}

// PC-06 — Event ownership through the plugin boundary: registration lives and
// dies with the owner activation.
func TestPC06EventOwnership(t *testing.T) {
	rt := pcNewRT(t)
	rec := &pcRec{}
	var emitCtx *runtime.Context

	regA := pcLoadActive(t, rt, pcEvRegistrar("A", rec))
	emitter := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
	if emitCtx == nil {
		t.Fatal("emitter did not capture its context")
	}
	if err := event.Emit(emitCtx, pcEvKey, "x"); err != nil {
		t.Fatalf("emit: %v", err)
	}
	if got := rec.join(); got != "A:x" {
		t.Fatalf("handler trace = %q, want A:x", got)
	}

	// Dispose the owner: the registration must die with the activation.
	pcDisposeGone(t, regA)
	if err := event.Emit(emitCtx, pcEvKey, "y"); err != nil {
		t.Fatalf("emit after owner gone: %v", err)
	}
	if got := rec.join(); got != "A:x" {
		t.Fatalf("handler ran after owner disposal: %q", got)
	}
	pcDisposeGone(t, emitter)
	pcQuiesced(t, rt, "PC-06")
}

// PC-07 — Event + Dependency composition: both are Runtime-owned and neither
// mechanism can outlive or bypass the other.
func TestPC07EventDependencyComposition(t *testing.T) {
	rt := pcNewRT(t)
	depKit := &kit{seen: make(chan string, 8)}
	rec := &pcRec{}
	var emitCtx *runtime.Context

	pf := pcLoadActive(t, rt, pcProv("v1"))
	cf := pcLoadActive(t, rt, pcCons(depKit, depKit.seen))
	if got := pcRecv(t, depKit.seen, "consumer binding"); got != "v1" {
		t.Fatalf("consumer resolved %q", got)
	}
	evC := pcLoadActive(t, rt, pcEvRegistrar("evC", rec))
	em := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))

	if err := event.Emit(emitCtx, pcEvKey, "e1"); err != nil {
		t.Fatal(err)
	}
	if !rec.has("evC:e1") {
		t.Fatalf("event handler did not run: %v", rec.got())
	}

	// Withdraw the provider: dependency goes away, but the event registration
	// of a DIFFERENT fiber must remain live (mechanisms are independent).
	pcDisposeGone(t, pf)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := cf.WaitInactive(ctx); err != nil {
		t.Fatalf("consumer did not lose dependency: %v", err)
	}
	if err := event.Emit(emitCtx, pcEvKey, "e2"); err != nil {
		t.Fatal(err)
	}
	if !rec.has("evC:e2") {
		t.Fatalf("event registration was damaged by dependency withdrawal: %v", rec.got())
	}

	// Unregister via owner disposal; then the producer is disposed last.
	pcDisposeGone(t, evC)
	if err := event.Emit(emitCtx, pcEvKey, "e3"); err != nil {
		t.Fatal(err)
	}
	if rec.has("evC:e3") {
		t.Fatal("handler ran after its owner was disposed")
	}
	pcDisposeGone(t, cf)
	pcDisposeGone(t, em)
	pcQuiesced(t, rt, "PC-07")
}

// PC-08 — Realm / Scope composition: sibling realms isolate the same logical
// key; a parent realm provider is visible to a child-scope consumer.
func TestPC08RealmScopeComposition(t *testing.T) {
	// (1) Sibling isolation.
	rt := pcNewRT(t)
	chA := make(chan *runtime.Fiber, 4)
	chB := make(chan *runtime.Fiber, 4)
	outA := make(chan string, 4)
	outB := make(chan string, 4)
	root := pcLoadActive(t, rt, pcRealmActivator(chA, chB, outA, outB))
	pfA := pcRecv(t, chA, "provider A")
	xfA := pcRecv(t, chA, "consumer A")
	pfB := pcRecv(t, chB, "provider B")
	xfB := pcRecv(t, chB, "consumer B")
	waitActive(t, pfA, xfA, pfB, xfB)
	if got := pcRecv(t, outA, "A consumer value"); got != "A" {
		t.Fatalf("consumer A resolved %q, want A", got)
	}
	if got := pcRecv(t, outB, "B consumer value"); got != "B" {
		t.Fatalf("consumer B resolved %q, want B", got)
	}
	// Cross-scope interference must never happen.
	pcDisposeGone(t, pfB)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := xfB.WaitInactive(ctx); err != nil {
		t.Fatalf("consumer B did not lose its scope provider: %v", err)
	}
	if xfA.State() != runtime.StateActive {
		t.Fatal("consumer A was corrupted by sibling realm B withdrawal")
	}
	pcDisposeGone(t, root)
	pcQuiesced(t, rt, "PC-08 isolation")

	// (2) No ancestor fallback (paper §4.4 Isolation, ADR-0001): a child-scope
	// consumer without a scope-local provider stays Pending even though the
	// root realm provides the same key — one namespace per binding.
	rt2 := pcNewRT(t)
	rootP2 := pcLoadActive(t, rt2, pcProv("ROOT"))
	chS := make(chan *runtime.Fiber, 2)
	outS := make(chan string, 2)
	actS := pcLoadActive(t, rt2, pcEmptyScopeConsActivator(chS, outS))
	xfS := pcRecv(t, chS, "scoped consumer")
	ctx2, cancel2 := context.WithTimeout(context.Background(), 2*time.Second)
	defer cancel2()
	if err := xfS.Ready(ctx2); err == nil {
		t.Fatal("scoped consumer activated on the root provider — ancestor fallback must not happen")
	}
	if st := xfS.State(); st != runtime.StatePending {
		t.Fatalf("scoped consumer state = %v, want Pending (no fallback)", st)
	}
	select {
	case v := <-outS:
		t.Fatalf("scoped consumer unexpectedly resolved %q", v)
	default:
	}
	pcDisposeGone(t, actS)
	pcDisposeGone(t, rootP2)
	pcQuiesced(t, rt2, "PC-08 no-fallback")
}

// PC-09 — Child ownership: disposing the parent disposes the child; no orphan
// Fiber survives. Ownership (dispose -> Gone) is not dependency (withdrawal ->
// Pending).
func TestPC09ChildOwnership(t *testing.T) {
	rt := pcNewRT(t)
	childKit := &kit{}
	var child *runtime.Fiber
	parent := pcLoadActive(t, rt, pcChildHost("pc-parent", pcLeaf("pc-child", childKit), &child))
	if child == nil {
		t.Fatal("parent did not create its child")
	}
	waitActive(t, child)

	snap := pcSnap(t, rt)
	var parentSnap, childSnap *runtime.FiberSnapshot
	for i := range snap.Fibers {
		if snap.Fibers[i].ID == parent.ID() {
			parentSnap = &snap.Fibers[i]
		}
		if snap.Fibers[i].ID == child.ID() {
			childSnap = &snap.Fibers[i]
		}
	}
	if parentSnap == nil || childSnap == nil {
		t.Fatal("snapshot missing parent/child fiber")
	}
	if len(parentSnap.ChildFiberIDs) != 1 || parentSnap.ChildFiberIDs[0] != child.ID() {
		t.Fatalf("parent does not own the child: %v", parentSnap.ChildFiberIDs)
	}
	if childSnap.ParentFiberID != parent.ID() {
		t.Fatalf("child parent = %d, want %d", childSnap.ParentFiberID, parent.ID())
	}

	// Dependency contrast in the same runtime: a consumer whose provider is
	// withdrawn becomes Pending, never Gone.
	pf := pcLoadActive(t, rt, pcProv("dep"))
	cf := pcLoadActive(t, rt, pcCons(nil, nil))
	pcDisposeGone(t, pf)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := cf.WaitInactive(ctx); err != nil {
		t.Fatal(err)
	}
	if cf.State() == runtime.StateGone {
		t.Fatal("dependent consumer was destroyed by provider withdrawal")
	}
	pcDisposeGone(t, cf)

	// Ownership cascade: parent dispose -> child Gone.
	pcDisposeGone(t, parent)
	if err := child.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("owned child did not reach Gone: %v", err)
	}
	pcQuiesced(t, rt, "PC-09")
	if childKit.applies.Load() != 1 {
		t.Fatalf("child applies = %d", childKit.applies.Load())
	}
}
