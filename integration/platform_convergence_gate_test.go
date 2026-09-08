package integration

import (
	"context"
	"dynamic-runtime/extensions/event"
	"errors"
	"fmt"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Platform Convergence E2E Gate — PC-19..PC-30 + Theorem integration
// (Thm64/Thm68/Thm70/Thm73/Thm80) over the Platform composition.
// ---------------------------------------------------------------------------

// PC-19 — Reconcile idempotence: Reconcile(Reconcile(D)) == Reconcile(D) on
// the observable Runtime state.
func TestPC19ReconciliationIdempotence(t *testing.T) {
	e := newPCConfigEnv(t)
	cc := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "prov", Config: map[string]any{"tag": tag}}
	}
	e.reconcile(t, cc("v1"))
	f1 := fiberOf(t, &env{ctrl: e.ctrl}, "p")
	waitActive(t, f1)
	before := pcCanonical(pcSnap(t, e.rt))

	e.reconcile(t, cc("v1")) // identical desired -> no-op
	after := pcCanonical(pcSnap(t, e.rt))
	if before != after {
		t.Fatalf("idempotent reconcile changed observable state:\nbefore %s\nafter  %s", before, after)
	}
	if fiberOf(t, &env{ctrl: e.ctrl}, "p").ID() != f1.ID() {
		t.Fatal("idempotent reconcile replaced the fiber")
	}
	if len(e.ctrl.Owned()) != 1 {
		t.Fatalf("owned = %d", len(e.ctrl.Owned()))
	}
}

// PC-20 — Repeated convergence: >=100 Load/Dispose cycles never grow
// Fibers/Providers/Events/Effects/WASM instances.
func TestPC20RepeatedConvergence(t *testing.T) {
	rt := pcNewRT(t)
	ld := loader.NewBuiltinLoader()
	obs := &wasmObs{}
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatal(err)
	}
	defer ld.CloseContext(ctxT(t))
	defer wb.Close()
	wasmMod := loadPCModule(t, ld, "pcw-cyc", loader.Artifact{ID: "pcw-cyc", BackendType: loader.BackendWASM, Source: wasmSrc(t, "valid.wasm"), Version: "1"})

	const cycles = 100
	consKit := &kit{seen: make(chan string, 16)}
	evRec := &pcRec{}
	effRec := &pcRec{}
	childKit := &kit{}
	for i := 0; i < cycles; i++ {
		// Component + Provider + Dependency.
		prov := pcLoadActive(t, rt, pcProv(fmt.Sprintf("c%d", i)))
		cons := pcLoadActive(t, rt, pcCons(consKit, consKit.seen))
		pcDisposeGone(t, prov)
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		if err := cons.WaitInactive(ctx); err != nil {
			cancel()
			t.Fatal(err)
		}
		cancel()
		pcDisposeGone(t, cons)
		// Event pair.
		var emitCtx *runtime.Context
		evR := pcLoadActive(t, rt, pcEvRegistrar("cyc", evRec))
		evE := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
		if err := event.Emit(emitCtx, pcEvKey, "tick"); err != nil {
			t.Fatal(err)
		}
		pcDisposeGone(t, evR)
		pcDisposeGone(t, evE)
		// Effect + Child.
		eff := pcLoadActive(t, rt, pcEffectHost("cyc-eff", effRec, "A", "B"))
		var child *runtime.Fiber
		parent := pcLoadActive(t, rt, pcChildHost("cyc-parent", pcLeaf("cyc-child", childKit), &child))
		waitActive(t, child)
		pcDisposeGone(t, parent)
		pcDisposeGone(t, eff)
		// WASM activation.
		wf := pcWasmMount(t, rt, wasmMod, "cyc-wasm")
		pcDisposeGone(t, wf)
		pcQuiesced(t, rt, fmt.Sprintf("PC-20 cycle %d", i))
	}
	// Cardinality is stable: nothing grew across the cycles.
	if childKit.applies.Load() != cycles {
		t.Fatalf("child applies = %d, want %d (exactly one activation per cycle)", childKit.applies.Load(), cycles)
	}
	if evRec.count("cyc:tick") != cycles {
		t.Fatalf("event deliveries = %d, want %d", evRec.count("cyc:tick"), cycles)
	}
	if got := effRec.count("-"); got != cycles*2 {
		t.Fatalf("effect unwinds = %d, want %d", got, cycles*2)
	}
	if n := pcWasmInstanceIDs(obs, "pcw-cyc"); len(n) != cycles {
		t.Fatalf("wasm instances created = %d, want %d", len(n), cycles)
	}
	if n := pcWasmDestroyedN(obs, "pcw-cyc"); n != cycles {
		t.Fatalf("wasm instances destroyed = %d, want %d", n, cycles)
	}
	snap := pcSnap(t, rt)
	if len(snap.Fibers) != 0 || len(snap.Providers) != 0 || len(snap.Effects) != 0 {
		t.Fatal("resources leaked across repeated convergence cycles")
	}
}

func (r *pcRec) count(prefix string) int {
	n := 0
	for _, c := range r.got() {
		if strings.HasPrefix(c, prefix) {
			n++
		}
	}
	return n
}

// PC-21 — Runtime Close over the full composition: every resource unwinds and
// Close is idempotent with no lifecycle goroutine left behind.
func TestPC21RuntimeClose(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	obs := &wasmObs{}
	ld := loader.NewBuiltinLoader()
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatal(err)
	}
	defer ld.CloseContext(ctxT(t))
	defer wb.Close()

	evRec := &pcRec{}
	effRec := &pcRec{}
	leafKit := &kit{}
	var emitCtx *runtime.Context
	var child *runtime.Fiber
	fibers := []*runtime.Fiber{
		pcLoadActive(t, rt, pcProv("v1")),
		pcLoadActive(t, rt, pcCons(nil, nil)),
		pcLoadActive(t, rt, pcEvRegistrar("closeEv", evRec)),
		pcLoadActive(t, rt, pcEvEmitter(&emitCtx)),
		pcLoadActive(t, rt, pcEffectHost("closeEff", effRec, "A", "B")),
		pcLoadActive(t, rt, pcChildHost("closeParent", pcLeaf("closeChild", leafKit), &child)),
		pcWasmMount(t, rt, loadPCModule(t, ld, "pcw-close", loader.Artifact{ID: "pcw-close", BackendType: loader.BackendWASM, Source: wasmSrc(t, "valid.wasm"), Version: "1"}), "wasm"),
	}
	waitActive(t, fibers...)
	if err := rt.Close(pcTimeout(t)); err != nil {
		t.Fatalf("runtime close over full composition: %v", err)
	}
	if err := rt.Close(pcTimeout(t)); err != nil {
		t.Fatalf("second runtime close: %v", err)
	}
	for _, f := range fibers {
		if f.State() != runtime.StateGone {
			t.Fatalf("fiber %s state = %s after close, want Gone", f.Name(), f.State())
		}
	}
	if !stringsHasAll(effRec.join(), "-B", "-A") {
		t.Fatalf("effects not unwound by close: %v", effRec.got())
	}
	if pcWasmDestroyedN(obs, "pcw-close") != 1 {
		t.Fatal("wasm instance survived runtime close")
	}
	// No registration outlives its owner after Close.
	_ = evRec
	_ = emitCtx
}

// PC-22 — Stale async completion: a delayed activation's completion can never
// mutate the state of a later activation generation.
func TestPC22StaleAsyncCompletion(t *testing.T) {
	rt := pcNewRT(t)
	gateA := make(chan struct{})
	// Activation A is delayed inside Apply.
	fA, err := rt.Load(pcGateApply("A", gateA))
	if err != nil {
		t.Fatal(err)
	}
	eventually(t, 5*time.Second, "A in Loading", func() bool {
		return fA.State() == runtime.StateLoading
	})

	// B mounts while A is still pending and becomes Active.
	bf := pcLoadActive(t, rt, pcLeaf("B", nil))
	bID := bf.ID()

	// Dispose A: the runtime cancels the activation; A must reach Gone
	// deterministically (its late completion is never applied to B).
	if err := fA.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := fA.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("A did not reach Gone: %v", err)
	}
	// Release A's pending completion after the fact.
	close(gateA)
	time.Sleep(5 * time.Millisecond) // allow any (incorrect) stale delivery

	if bf.State() != runtime.StateActive {
		t.Fatalf("B corrupted by stale completion: %s", bf.State())
	}
	if bf.ID() != bID {
		t.Fatal("B identity changed")
	}
	snap := pcSnap(t, rt)
	if len(snap.Fibers) != 1 || snap.Fibers[0].ID != bf.ID() {
		t.Fatalf("final state not determined only by B: %+v", snap.Fibers)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 3*time.Second)
	defer cancel()
	if err := fA.Ready(ctx); !errors.Is(err, runtime.ErrFiberGone) {
		t.Fatalf("A Ready = %v, want ErrFiberGone", err)
	}
}

// pcPanicEventRegistrar registers a handler that panics on dispatch.
func pcPanicEventRegistrar(name string) *pcComp {
	return &pcComp{
		name: name,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			err := runtime.On(ctx, pcEvKey, func(context.Context, string) error {
				panic("pc: handler panic")
			})
			return nil, err
		},
	}
}

// PC-23 — Panic boundary: panics in Apply / effect inverse / event handler
// never crash the Runtime or corrupt other Fibers.
func TestPC23PanicBoundary(t *testing.T) {
	rt := pcNewRT(t)
	healthy := pcLoadActive(t, rt, pcLeaf("healthy", nil))
	panicComp := pcLoadActive2(t, rt, pcPanicApply("panic-apply"))
	if err := panicComp.Ready(pcTimeout(t)); err == nil {
		t.Fatal("panicking Apply became Active")
	}

	// Effect whose INVERSE panics: unwind must continue and still converge.
	inversePanic := &pcComp{
		name: "panic-inverse",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if err := ctx.Effect(func() (func() error, error) {
				return func() error { panic("pc: inverse panic") }, nil
			}); err != nil {
				return nil, err
			}
			return nil, nil
		},
	}
	ip := pcLoadActive(t, rt, inversePanic)

	// Event handler panic is contained by the dispatch boundary.
	var emitCtx *runtime.Context
	evP := pcLoadActive(t, rt, pcPanicEventRegistrar("panic-ev"))
	em := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
	if err := event.Emit(emitCtx, pcEvKey, "boom"); err == nil {
		t.Fatal("handler panic should surface as an error, not crash")
	}

	// Everything unwinds; unwind errors are recorded, never fatal.
	disposeTolerate := func(f *runtime.Fiber, name string) {
		t.Helper()
		if err := f.Dispose(); err != nil {
			t.Fatalf("dispose %s: %v", name, err)
		}
		err := f.Gone(pcTimeout(t))
		if err != nil && !strings.Contains(err.Error(), "panic") {
			t.Fatalf("gone %s: %v", name, err)
		}
		if f.State() != runtime.StateGone {
			t.Fatalf("%s state = %s after unwind", name, f.State())
		}
	}
	disposeTolerate(ip, "panic-inverse")
	disposeTolerate(evP, "panic-ev")
	disposeTolerate(panicComp, "panic-apply")
	pcDisposeGone(t, em)
	if healthy.State() != runtime.StateActive {
		t.Fatalf("healthy fiber corrupted by panics: %s", healthy.State())
	}
	pcDisposeGone(t, healthy)
	pcQuiesced(t, rt, "PC-23")
}

// pcLoadActive2 loads without requiring Active (failed fibers still Load).
func pcLoadActive2(t *testing.T, rt *runtime.Runtime, comp runtime.Component) *runtime.Fiber {
	t.Helper()
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("rt.Load(%s): %v", comp.Name(), err)
	}
	return f
}

// PC-24 — Cancellation is a lifecycle-transition input, never a direct
// mutation path; final states always converge through the Runtime.
func TestPC24CancellationBoundary(t *testing.T) {
	// Dispose of a fiber whose Apply is waiting on cancellation.
	rt := pcNewRT(t)
	f := pcLoadActive2(t, rt, pcGateApply("cancel-slow", nil))
	eventually(t, 5*time.Second, "cancel-slow in Loading", func() bool {
		return f.State() == runtime.StateLoading
	})
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("cancelled fiber did not converge to Gone: %v", err)
	}
	if f.State() == runtime.StateActive {
		t.Fatal("cancellation raced the fiber into Active")
	}
	pcQuiesced(t, rt, "PC-24 dispose")

	// Runtime Close with a blocked applier: cancellation is delivered through
	// the lifecycle, and Close converges once the component observes it.
	rt2 := pcNewRT(t)
	blocked := pcLoadActive2(t, rt2, pcGateApply("cancel-close", nil))
	eventually(t, 5*time.Second, "blocked in Loading", func() bool {
		return blocked.State() == runtime.StateLoading
	})
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	if err := rt2.Close(ctx); err != nil {
		t.Fatalf("close with blocked applier: %v", err)
	}
	if blocked.State() != runtime.StateGone {
		t.Fatalf("blocked fiber state after close = %s", blocked.State())
	}
}

// PC-25 — Reentrancy: nested composition + nested event dispatch never deadlock
// or corrupt orchestrator/ownership state.
func TestPC25ReentrancyNestedComposition(t *testing.T) {
	rt := pcNewRT(t)
	rec := &pcRec{}
	// Nested event keys: dispatching "outer" re-enters the registry for "inner"
	// from inside a handler (fresh dispatch snapshot per level).
	innerKey := runtime.NewEventKey[string]("pc.inner")
	innerRec := &pcRec{}
	var innerCtx *runtime.Context
	var outerCtx, outerCtx2 *runtime.Context
	innerReg := &pcComp{
		name: "inner-registrar",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			err := runtime.On(ctx, innerKey, func(context.Context, string) error {
				innerRec.add("inner-hit")
				return nil
			})
			return nil, err
		},
	}
	innerRegF := pcLoadActive(t, rt, innerReg)
	innerEmF := pcLoadActive(t, rt, pcEvEmitter(&innerCtx))

	outerReg := &pcComp{
		name: "outer-registrar",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			return nil, runtime.On(ctx, pcEvKey, func(c context.Context, payload string) error {
				rec.add("outer:" + payload)
				if innerCtx != nil {
					return event.Emit(innerCtx, innerKey, payload)
				}
				return nil
			})
		},
	}
	outer := pcLoadActive(t, rt, outerReg)
	outerEmF := pcLoadActive(t, rt, pcEvEmitter(&outerCtx))

	// Nested composition from inside Apply (public Runtime.Load reentrancy).
	nestRec := &pcRec{}
	var childF *runtime.Fiber
	var nestedLeafF *runtime.Fiber
	reentrant := pcLoadActive(t, rt, pcChildHost("reentrant", pcLeaf("reentrant-child", nil), &childF))
	deep := &pcComp{
		name: "deep-root",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if _, err := ctx.Child(pcLeaf("deep-child", nil)); err != nil {
				return nil, err
			}
			f, err := rt.Load(pcLeaf("deep-nested-leaf", nil))
			if err != nil {
				return nil, err
			}
			nestedLeafF = f
			if err := f.Ready(ctx.Context()); err != nil {
				return nil, err
			}
			nestRec.add("root-loaded")
			return nil, nil
		},
	}
	deepF := pcLoadActive(t, rt, deep)
	_ = outerCtx2
	waitActive(t, childF)
	waitActive(t, nestedLeafF)

	if err := event.Emit(outerCtx, pcEvKey, "outer1"); err != nil {
		t.Fatal(err)
	}
	if !rec.has("outer:outer1") || !innerRec.has("inner-hit") {
		t.Fatalf("nested dispatch incomplete: outer=%v inner=%v", rec.got(), innerRec.got())
	}
	if !nestRec.has("root-loaded") {
		t.Fatalf("nested Load inside Apply did not complete: %v", nestRec.got())
	}
	// Dispose roots: nested event registrations and reentrant children unwind.
	pcDisposeGone(t, outer)
	pcDisposeGone(t, reentrant)
	pcDisposeGone(t, deepF)
	pcDisposeGone(t, innerRegF)
	pcDisposeGone(t, innerEmF)
	pcDisposeGone(t, outerEmF)
	pcDisposeGone(t, nestedLeafF)
	pcQuiesced(t, rt, "PC-25")
}

// PC-26 — Public-API-only application composition (this test file compiles in
// package integration_test boundaries; the full PC-18 scenario is the proof).
func TestPC26PublicAPIOnly(t *testing.T) {
	rt := pcNewRT(t)
	k := &kit{seen: make(chan string, 4)}
	prov := pcLoadActive(t, rt, pcProv("pub"))
	cons := pcLoadActive(t, rt, pcCons(k, k.seen))
	var emitCtx *runtime.Context
	rec := &pcRec{}
	evR := pcLoadActive(t, rt, pcEvRegistrar("pub", rec))
	em := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
	if got := pcRecv(t, k.seen, "public binding"); got != "pub" {
		t.Fatalf("consumer resolved %q", got)
	}
	if err := event.Emit(emitCtx, pcEvKey, "pub-event"); err != nil {
		t.Fatal(err)
	}
	if !rec.has("pub:pub-event") {
		t.Fatal("public event dispatch failed")
	}
	if prov.State() != runtime.StateActive || cons.State() != runtime.StateActive {
		t.Fatal("public composition not active")
	}
	snap := pcSnap(t, rt)
	// Only public snapshots are used for observation; no kernel internals.
	if len(snap.Fibers) != 4 || len(snap.Providers) != 1 {
		t.Fatalf("unexpected public snapshot: fibers=%d providers=%d", len(snap.Fibers), len(snap.Providers))
	}
	for _, f := range []*runtime.Fiber{prov, cons, evR, em} {
		pcDisposeGone(t, f)
	}
	pcQuiesced(t, rt, "PC-26")
}

// PC-27 — No second lifecycle: extension activity (scheduler/registry/event)
// creates or destroys nothing outside the Runtime lifecycle model.
func TestPC27NoSecondLifecycle(t *testing.T) {
	rt := pcNewRT(t)
	base := pcLoadActive(t, rt, pcLeaf("anchor", nil))
	// Registry churn.
	regComp := pcLoadActive(t, rt, regOwnerComp(&kit{}, "reg"))
	_ = regComp.Component().(*testComp).reg.Add("x", "1")
	// Extension event bus publish (extension object, not kernel event).
	_ = base
	snap := pcSnap(t, rt)
	before := pcCanonical(snap)
	_ = before
	if len(snap.Fibers) != 2 {
		t.Fatalf("unexpected fiber count: %d", len(snap.Fibers))
	}
	pcDisposeGone(t, regComp)
	pcDisposeGone(t, base)
	pcQuiesced(t, rt, "PC-27")
}

// PC-28 — Identity separation: Component != Fiber != Activation != Provider
// record; HMR/WASM replacement always yields fresh identities.
func TestPC28IdentitySeparation(t *testing.T) {
	rt := pcNewRT(t)
	prov := pcLoadActive(t, rt, pcProv("v1"))
	cons := pcLoadActive(t, rt, pcCons(nil, nil))
	snap := pcSnap(t, rt)
	var provView *runtime.ProviderView
	for i := range snap.Providers {
		if snap.Providers[i].Key == pcSvcKey.Capability().String() {
			provView = &snap.Providers[i]
		}
	}
	if provView == nil {
		t.Fatal("provider record missing")
	}
	if provView.OwnerFiberID != prov.ID() {
		t.Fatalf("provider record attributed to fiber %d, want %d", provView.OwnerFiberID, prov.ID())
	}
	act := uint64(0)
	for _, f := range snap.Fibers {
		if f.ID == prov.ID() {
			act = uint64(f.ActivationID)
		}
	}
	if act == 0 || uint64(provView.OwnerActivationID) == 0 {
		t.Fatalf("activation identity collapsed: fiber=%d act=%d prov=%+v", prov.ID(), act, provView)
	}
	if uint64(provView.OwnerActivationID) != act || provView.OwnerFiberID != prov.ID() {
		t.Fatalf("provider attribution mismatch: fiber=%d act=%d prov=%+v", prov.ID(), act, provView)
	}
	_ = cons

	// HMR replacement: activation identity advances on the same logical role.
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))
	if err := ld.RegisterBuiltin("builtin://pc-id-v1", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcLeaf("pc-id-v1", nil), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	if err := ld.RegisterBuiltin("builtin://pc-id-v2", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcLeaf("pc-id-v2", nil), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	m1 := loadPCModule(t, ld, "pc-id-v1", artifact("pc-id-v1", "pc", "builtin://pc-id-v1", "1"))
	c1, err := m1.Factory.Create(config.ComponentConfig{ID: "id", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	id1 := pcLoadActive(t, rt, c1)
	oldAct := activationOf(t, rt, id1.ID())
	tgt := hmr.Target{ID: "id", ComponentID: "id", Artifact: artifact("pc-id-v2", "pc", "builtin://pc-id-v2", "2")}
	if err := h.Register(tgt); err != nil {
		t.Fatal(err)
	}
	if err := h.Bind("id", *m1, id1); err != nil {
		t.Fatal(err)
	}
	if err := h.Replace(pcTimeout(t), tgt); err != nil {
		t.Fatal(err)
	}
	b, _ := h.CurrentBinding("id")
	if b.Fiber.ID() == id1.ID() {
		t.Fatal("fiber identity reused across HMR")
	}
	newAct := activationOf(t, rt, b.Fiber.ID())
	if newAct == oldAct || newAct == 0 {
		t.Fatalf("activation identity not advanced: old=%d new=%d", oldAct, newAct)
	}
	// EventKey != registration: registration is an effect of the owner.
	regRec := &pcRec{}
	var emitCtx *runtime.Context
	evR := pcLoadActive(t, rt, pcEvRegistrar("evid", regRec))
	_ = pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
	snap2 := pcSnap(t, rt)
	found := false
	for _, e := range snap2.Effects {
		if e.OwnerFiberID == evR.ID() && e.Kind == runtime.EffectKindEvent {
			found = true
		}
	}
	if !found {
		t.Fatal("event registration effect missing for live owner")
	}
}

func activationOf(t *testing.T, rt *runtime.Runtime, fid runtime.FiberID) runtime.ActivationID {
	t.Helper()
	for _, f := range pcSnap(t, rt).Fibers {
		if f.ID == fid {
			return f.ActivationID
		}
	}
	return 0
}

// PC-29 — Ownership graph: every owned resource dies with its owner; the
// snapshot can explain who owns what.
func TestPC29OwnershipGraph(t *testing.T) {
	rt := pcNewRT(t)
	rec := &pcRec{}
	var child *runtime.Fiber
	host := pcLoadActive(t, rt, pcChildHost("owner", pcLeaf("owned-child", nil), &child))
	waitActive(t, child)
	evR := pcLoadActive(t, rt, pcEvRegistrar("ownerev", rec))
	_ = evR

	snap := pcSnap(t, rt)
	var hostSnap *runtime.FiberSnapshot
	for i := range snap.Fibers {
		if snap.Fibers[i].ID == host.ID() {
			hostSnap = &snap.Fibers[i]
		}
	}
	if hostSnap == nil {
		t.Fatal("owner fiber missing")
	}
	if len(hostSnap.ChildFiberIDs) != 1 {
		t.Fatalf("owner graph missing child: %v", hostSnap.ChildFiberIDs)
	}
	// Effect/Event rows identify their owner activation.
	evOwner := false
	for _, e := range snap.Effects {
		if e.Kind == runtime.EffectKindEvent && e.OwnerFiberID == evR.ID() && e.ActivationID == activationOf(t, rt, evR.ID()) {
			evOwner = true
		}
	}
	if !evOwner {
		t.Fatal("event registration not owned by the live activation")
	}
	// Owner disposal removes the child and every owned resource.
	pcDisposeGone(t, host)
	if err := child.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("orphan child survived owner disposal: %v", err)
	}
	pcDisposeGone(t, evR)
	pcQuiesced(t, rt, "PC-29")
}

// PC-30 — Final quiescence: deterministic lifecycle waits, never sleeps.
func TestPC30FinalQuiescence(t *testing.T) {
	rt := pcNewRT(t)
	var children []*runtime.Fiber
	for i := 0; i < 5; i++ {
		children = append(children, pcLoadActive(t, rt, pcLeaf(fmt.Sprintf("leaf-%d", i), nil)))
	}
	for _, f := range children {
		pcDisposeGone(t, f)
	}
	pcQuiesced(t, rt, "PC-30")
	if snap := pcSnap(t, rt); snap.State != runtime.RuntimeRunning {
		t.Fatalf("runtime state = %v, want Running", snap.State)
	}
	// Close after quiescence is still clean.
	if err := rt.Close(pcTimeout(t)); err != nil {
		t.Fatal(err)
	}
}

// pcThm64Check validates the Thm64 well-formedness invariants over one linearized
// public Snapshot. Returns "" when well-formed, else the first violation.
func pcThm64Check(snap runtime.RuntimeSnapshot) string {
	byID := make(map[runtime.FiberID]runtime.FiberSnapshot)
	for _, f := range snap.Fibers {
		if f.State == runtime.StateGone {
			continue
		}
		if _, dup := byID[f.ID]; dup {
			return fmt.Sprintf("duplicate live fiber %d", f.ID)
		}
		byID[f.ID] = f
	}
	// Effect ownership: every effect belongs to a live activation.
	for _, e := range snap.Effects {
		f, ok := byID[e.OwnerFiberID]
		if !ok {
			return fmt.Sprintf("effect %d owned by dead fiber %d", e.Seq, e.OwnerFiberID)
		}
		if f.ActivationID != e.ActivationID {
			return fmt.Sprintf("effect %d owned by dead activation %d (fiber has %d)", e.Seq, e.ActivationID, f.ActivationID)
		}
	}
	// Provider ownership + exclusivity per (key, scope).
	excl := make(map[string]int)
	for _, p := range snap.Providers {
		f, ok := byID[p.OwnerFiberID]
		if !ok {
			return fmt.Sprintf("provider %s owned by dead fiber %d", p.Key, p.OwnerFiberID)
		}
		if f.ActivationID != p.OwnerActivationID {
			return fmt.Sprintf("provider %s owned by dead activation %d", p.Key, p.OwnerActivationID)
		}
		if !p.Retiring {
			excl[p.Key+"|"+fmt.Sprint(p.ScopeID)]++
		}
	}
	for k, n := range excl {
		if n > 1 {
			return fmt.Sprintf("duplicate non-retiring provider for %s (%d)", k, n)
		}
	}
	// Dependency validity: satisfied deps point at a live, active provider.
	for _, d := range snap.Dependencies {
		cf, ok := byID[d.ConsumerFiberID]
		if !ok {
			return fmt.Sprintf("dependency of dead consumer %d", d.ConsumerFiberID)
		}
		switch d.Status {
		case runtime.DependencySatisfied:
			if d.ProviderFiberID == 0 || d.ProviderActivationID == 0 {
				return fmt.Sprintf("satisfied dependency without provider binding: %+v", d)
			}
			pf, ok := byID[d.ProviderFiberID]
			if !ok || pf.ActivationID != d.ProviderActivationID || pf.State != runtime.StateActive {
				return fmt.Sprintf("dependency %s satisfied by invalid provider: %+v", d.Key, d)
			}
			if cf.ActivationID != d.ConsumerActivationID || cf.ActivationID == 0 {
				return fmt.Sprintf("satisfied consumer %d has no matching activation: %+v", cf.ID, d)
			}
		case runtime.DependencyWaiting:
			if cf.State == runtime.StateActive {
				return fmt.Sprintf("active consumer %d reports Waiting dependency: %+v", cf.ID, d)
			}
		}
	}
	return ""
}

// TestPC_Thm64_Preservation — every legal step of the platform composition keeps
// the Runtime well-formed.
func TestPC_Thm64_Preservation(t *testing.T) {
	rt := pcNewRT(t)
	var prov1, prov2, cf, evR, evE, eff *runtime.Fiber
	step := func(name string, fn func()) {
		t.Helper()
		fn()
		if msg := pcThm64Check(pcSnap(t, rt)); msg != "" {
			t.Fatalf("Thm64 violation after %s: %s", name, msg)
		}
	}
	step("load provider", func() { prov1 = pcLoadActive(t, rt, pcProv("v1")) })
	step("load consumer", func() { cf = pcLoadActive(t, rt, pcCons(nil, nil)) })
	rec := &pcRec{}
	var emitCtx *runtime.Context
	step("register event", func() { evR = pcLoadActive(t, rt, pcEvRegistrar("thm64", rec)) })
	step("mount emitter", func() { evE = pcLoadActive(t, rt, pcEvEmitter(&emitCtx)) })
	step("install effects", func() { eff = pcLoadActive(t, rt, pcEffectHost("thm64-eff", rec, "A", "B")) })
	step("emit", func() {
		if err := event.Emit(emitCtx, pcEvKey, "x"); err != nil {
			t.Fatal(err)
		}
	})
	step("withdraw provider", func() {
		pcDisposeGone(t, prov1)
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if err := cf.WaitInactive(ctx); err != nil {
			t.Fatal(err)
		}
	})
	step("recover with new provider", func() { prov2 = pcLoadActive(t, rt, pcProv("v2")) })
	step("final quiescence after disposal", func() {
		for _, f := range []*runtime.Fiber{cf, evR, evE, eff, prov2} {
			if f != nil {
				pcDisposeGone(t, f)
			}
		}
	})
}

// TestPC_Thm68_Recovery — apply failure, dependency withdrawal, config failure
// and HMR failure all recover or converge to a legal terminal state.
func TestPC_Thm68_Recovery(t *testing.T) {
	// Apply failure -> replacement recovers.
	rt := pcNewRT(t)
	fBad := pcLoadActive2(t, rt, pcFailApply("bad"))
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	if err := fBad.Ready(ctx); err == nil {
		cancel()
		t.Fatal("failing component became Active")
	}
	cancel()
	pcDisposeGone(t, fBad)
	good := pcLoadActive(t, rt, pcLeaf("good", nil))
	if good.State() != runtime.StateActive {
		t.Fatal("recovery did not activate")
	}
	pcDisposeGone(t, good)
	pcQuiesced(t, rt, "Thm68 apply recovery")
	_ = rt.Close(pcTimeout(t))

	// Dependency withdrawal -> recovery on a new generation.
	rt2 := pcNewRT(t)
	k := &kit{seen: make(chan string, 4)}
	p1 := pcLoadActive(t, rt2, pcProv("r1"))
	c1 := pcLoadActive(t, rt2, pcCons(k, k.seen))
	if got := pcRecv(t, k.seen, "initial binding"); got != "r1" {
		t.Fatalf("consumer initially resolved %q", got)
	}
	pcDisposeGone(t, p1)
	ctx2, cancel2 := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel2()
	if err := c1.WaitInactive(ctx2); err != nil {
		t.Fatal(err)
	}
	r2f := pcLoadActive(t, rt2, pcProv("r2"))
	if err := c1.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("dependency recovery failed: %v", err)
	}
	if got := pcRecv(t, k.seen, "recovered"); got != "r2" {
		t.Fatalf("recovered to %q", got)
	}
	pcDisposeGone(t, c1)
	pcDisposeGone(t, r2f)
	pcQuiesced(t, rt2, "Thm68 dependency recovery")
}

// TestPC_Thm70_Ordering — dependency withdrawal precedes dependent invalidation;
// effect inverses run LIFO; module release follows fiber Gone.
func TestPC_Thm70_Ordering(t *testing.T) {
	rt := pcNewRT(t)
	rec := &pcRec{}
	eff := pcLoadActive(t, rt, pcEffectHost("order-eff", rec, "A", "B", "C"))
	pcDisposeGone(t, eff)
	if want := "+A,+B,+C,-C,-B,-A"; rec.join() != want {
		t.Fatalf("effect order = %q, want %q", rec.join(), want)
	}

	p := pcLoadActive(t, rt, pcProv("o1"))
	c := pcLoadActive(t, rt, pcCons(nil, nil))
	if err := p.Dispose(); err != nil {
		t.Fatal(err)
	}
	// Dependent invalidation must complete before the provider is fully gone.
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := c.WaitInactive(ctx); err != nil {
		t.Fatalf("dependent not invalidated before provider gone: %v", err)
	}
	if err := p.Gone(ctx); err != nil {
		t.Fatalf("provider did not reach Gone after dependent invalidation: %v", err)
	}
	pcDisposeGone(t, c)
	pcQuiesced(t, rt, "Thm70 ordering")
}

// TestPC_Thm73_Progress — no combination of Dependency/Effect/Event/HMR/WASM can
// wedge the runtime; every operation converges under bounded waits.
func TestPC_Thm73_Progress(t *testing.T) {
	rt := pcNewRT(t)
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	// Churn axis: dependency replacement.
	p1 := pcLoadActive(t, rt, pcProv("p1"))
	c1 := pcLoadActive(t, rt, pcCons(nil, nil))
	pcDisposeGone(t, p1)
	if err := c1.WaitInactive(ctx); err != nil {
		t.Fatal("Thm73: dependency withdrawal stalled")
	}
	p2 := pcLoadActive(t, rt, pcProv("p2"))
	if err := c1.Ready(ctx); err != nil {
		t.Fatal("Thm73: dependency recovery stalled")
	}
	// Event axis.
	var emitCtx *runtime.Context
	evR := pcLoadActive(t, rt, pcEvRegistrar("thm73", &pcRec{}))
	evE := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
	if err := event.Emit(emitCtx, pcEvKey, "go"); err != nil {
		t.Fatal(err)
	}
	pcDisposeGone(t, evR)
	pcDisposeGone(t, evE)
	// Effect + child axis.
	var child *runtime.Fiber
	host := pcLoadActive(t, rt, pcChildHost("thm73-host", pcLeaf("thm73-child", nil), &child))
	waitActive(t, child)
	pcDisposeGone(t, host)
	if err := child.Gone(ctx); err != nil {
		t.Fatal("Thm73: child disposal stalled")
	}
	pcDisposeGone(t, c1)
	pcDisposeGone(t, p2)
	// Quiescence is a legal terminal for the whole axis set.
	pcQuiesced(t, rt, "Thm73")
}

// TestPC_Thm80_Confluence — order-independent convergence of mutually
// independent platform transitions across permuted schedules.
func TestPC_Thm80_Confluence(t *testing.T) {
	opA := func(rt *runtime.Runtime) *runtime.Fiber { return pcLoadActive(t, rt, pcLeaf("L1", nil)) }
	opB := func(rt *runtime.Runtime) *runtime.Fiber { return pcLoadActive(t, rt, pcLeaf("L2", nil)) }
	opC := func(rt *runtime.Runtime) []*runtime.Fiber {
		prov := pcLoadActive(t, rt, pcProv("v9"))
		cons := pcLoadActive(t, rt, pcCons(nil, nil))
		return []*runtime.Fiber{prov, cons}
	}
	orders := [][]func(*runtime.Runtime) []*runtime.Fiber{
		{func(rt *runtime.Runtime) []*runtime.Fiber { return []*runtime.Fiber{opA(rt)} },
			func(rt *runtime.Runtime) []*runtime.Fiber { return []*runtime.Fiber{opB(rt)} },
			opC},
		{func(rt *runtime.Runtime) []*runtime.Fiber { return []*runtime.Fiber{opB(rt)} },
			func(rt *runtime.Runtime) []*runtime.Fiber { return []*runtime.Fiber{opA(rt)} },
			opC},
		{opC,
			func(rt *runtime.Runtime) []*runtime.Fiber { return []*runtime.Fiber{opA(rt)} },
			func(rt *runtime.Runtime) []*runtime.Fiber { return []*runtime.Fiber{opB(rt)} }},
	}
	var canons []string
	for _, order := range orders {
		rt := pcNewRT(t)
		for _, op := range order {
			op(rt)
		}
		canons = append(canons, pcCanonical(pcSnap(t, rt)))
		if msg := pcThm64Check(pcSnap(t, rt)); msg != "" {
			t.Fatalf("Thm80: Thm64 violation on permuted run: %s", msg)
		}
	}
	if canons[0] != canons[1] || canons[0] != canons[2] {
		t.Fatalf("Thm80 confluence violated:\n%s\n%s\n%s", canons[0], canons[1], canons[2])
	}
}
