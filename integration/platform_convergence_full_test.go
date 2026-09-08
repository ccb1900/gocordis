package integration

import (
	"context"
	"dynamic-runtime/extensions/event"
	"errors"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Platform Convergence E2E Gate — PC-14/PC-15 (WASM composition) and PC-18
// (full application composition).
// ---------------------------------------------------------------------------

func pcWasmInstanceIDs(o *wasmObs, module string) []uint64 {
	o.mu.Lock()
	defer o.mu.Unlock()
	var out []uint64
	for _, e := range o.created {
		if e.module == module {
			out = append(out, e.instance)
		}
	}
	return out
}

func pcWasmDestroyedN(o *wasmObs, module string) int {
	o.mu.Lock()
	defer o.mu.Unlock()
	n := 0
	for _, e := range o.destroyed {
		if e.module == module {
			n++
		}
	}
	return n
}

// pcWasmMount materializes a Fiber from a loaded WASM Module.
func pcWasmMount(t *testing.T, rt *runtime.Runtime, mod *loader.Module, id string) *runtime.Fiber {
	t.Helper()
	comp, err := mod.Factory.Create(config.ComponentConfig{ID: id, Type: mod.Type})
	if err != nil {
		t.Fatalf("wasm factory.Create: %v", err)
	}
	return pcLoadActive(t, rt, comp)
}

// PC-14 — WASM composition: instance lifetime == activation lifetime; a fresh
// activation always materializes a fresh instance.
func TestPC14WASMComposition(t *testing.T) {
	rt := pcNewRT(t)
	ld := loader.NewBuiltinLoader()
	obs := &wasmObs{}
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatal(err)
	}
	defer ld.CloseContext(ctxT(t))
	defer wb.Close()

	mod := loadPCModule(t, ld, "pcw", loader.Artifact{ID: "pcw", BackendType: loader.BackendWASM, Source: wasmSrc(t, "valid.wasm"), Version: "1"})
	fA := pcWasmMount(t, rt, mod, "wasm-a")
	if ids := pcWasmInstanceIDs(obs, "pcw"); len(ids) != 1 {
		t.Fatalf("instances created for activation A = %v, want exactly 1", ids)
	}
	instA := pcWasmInstanceIDs(obs, "pcw")[0]

	// Dispose A: the instance dies with the activation.
	pcDisposeGone(t, fA)
	if n := pcWasmDestroyedN(obs, "pcw"); n != 1 {
		t.Fatalf("destroyed instances = %d, want 1 after disposal", n)
	}

	// Activation B materializes instance B; A's instance is never reused.
	fB := pcWasmMount(t, rt, mod, "wasm-b")
	ids := pcWasmInstanceIDs(obs, "pcw")
	if len(ids) != 2 {
		t.Fatalf("instances created = %v, want 2", ids)
	}
	instB := ids[1]
	if instA == instB {
		t.Fatalf("activation B reused instance %d of activation A", instA)
	}
	pcDisposeGone(t, fB)
	pcQuiesced(t, rt, "PC-14")
	if n := pcWasmDestroyedN(obs, "pcw"); n != 2 {
		t.Fatalf("destroyed instances = %d, want 2", n)
	}
}

// PC-15 — WASM + HMR: V1 instance destroyed, V2 instance created and fully
// independent; module unload never races a live fiber.
func TestPC15WASMHMR(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("pcw-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "svc")
	e.bind(t, "svc", "svc", m1, f1, wArtifact("pcw-v2", "camera-v2.wasm", "2"))
	if ids := pcWasmInstanceIDs(e.obs, "pcw-v1"); len(ids) != 1 {
		t.Fatalf("V1 instances = %v, want 1", ids)
	}
	instV1 := pcWasmInstanceIDs(e.obs, "pcw-v1")[0]

	e.replaceOK(t, "svc", wArtifact("pcw-v2", "camera-v2.wasm", "2"))
	b := e.binding(t, "svc")
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("HMR reused the old fiber")
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatalf("V2 state = %s", b.Fiber.State())
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("V1 state = %s, want Gone", f1.State())
	}
	// V1 instance destroyed; V2 instance created with a different identity.
	if n := pcWasmDestroyedN(e.obs, "pcw-v1"); n != 1 {
		t.Fatalf("V1 destroyed = %d, want 1", n)
	}
	idsV2 := pcWasmInstanceIDs(e.obs, "pcw-v2")
	if len(idsV2) != 1 {
		t.Fatalf("V2 instances = %v, want 1", idsV2)
	}
	if idsV2[0] == instV1 {
		t.Fatal("V2 instance reused V1's instance identity")
	}
	// Module unload happens only after the old fiber is Gone (usage moved).
	if e.ld.Usage().InUse("pcw-v1") {
		t.Fatal("V1 module usage leaked")
	}
	if !e.ld.Usage().InUse("pcw-v2") {
		t.Fatal("V2 module usage not held")
	}
	if e.ld.Has("pcw-v1") {
		t.Fatal("V1 module unloaded before the old fiber was fully released")
	}
}

// pcAppActivator mounts the scoped consumers and the child leaf of the PC-18
// reference application.
func pcAppActivator(chA, chB chan<- *runtime.Fiber, outA, outB chan<- string, gotChild **runtime.Fiber, leafKit *kit) *pcComp {
	return &pcComp{
		name: "pc-app-activator",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if _, err := ctx.Child(pcScopedConsHost(chA, outA), runtime.WithScope()); err != nil {
				return nil, err
			}
			if _, err := ctx.Child(pcScopedConsHost(chB, outB), runtime.WithScope()); err != nil {
				return nil, err
			}
			cf, err := ctx.Child(pcLeaf("pc-app-child", leafKit))
			if err != nil {
				return nil, err
			}
			*gotChild = cf
			return nil, nil
		},
	}
}

// PC-18 — Full application composition: Config + Loader + Component +
// Provider + Dependency + Event + Effect + Child + Realm + Registry + WASM +
// ConfigWatch + HMR under one lifecycle, converging leak-free to quiescence.
func TestPC18FullApplicationComposition(t *testing.T) {
	rt := pcNewRT(t)

	// One Loader serves builtin (HMR) and WASM backends on the shared Runtime.
	ld := loader.NewBuiltinLoader()
	obs := &wasmObs{}
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatal(err)
	}
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))
	defer wb.Close()

	// Config Controller over the same Runtime.
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	defer ctrl.CloseContext(shortCtx())
	consKit := &kit{seen: make(chan string, 8)}
	var regOwner *testComp
	register := func(typ string, build func(cc config.ComponentConfig) (runtime.Component, error)) {
		t.Helper()
		if err := reg.Register(typ, &adapterFactory{build: build}); err != nil {
			t.Fatal(err)
		}
	}
	register("prov", func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		if tag == "" {
			tag = cc.ID
		}
		return pcProv(tag), nil
	})
	register("regowner", func(cc config.ComponentConfig) (runtime.Component, error) {
		regOwner = regOwnerComp(&kit{}, cc.ID)
		return regOwner, nil
	})
	register("regconsumer", func(cc config.ComponentConfig) (runtime.Component, error) {
		return regConsumerComp(&kit{}, cc.ID), nil
	})
	fw := watch.NewFileWatcher()
	defer fw.Close()
	path := filepath.Join(t.TempDir(), "app.toml")
	writeCfg := func(comps ...config.ComponentConfig) {
		t.Helper()
		if err := os.WriteFile(path, []byte(tomlOf(comps...)), 0o644); err != nil {
			t.Fatal(err)
		}
		if err := adapterSync(t, ctrl, fw, path); err != nil {
			t.Fatalf("sync: %v", err)
		}
	}
	envCtrl := &env{ctrl: ctrl}

	// (1) Config Desired v1: Core Service (provider), Registry owner/consumer.
	writeCfg(
		config.ComponentConfig{ID: "svc", Type: "prov", Config: map[string]any{"tag": "v1"}},
		config.ComponentConfig{ID: "reg", Type: "regowner"},
		config.ComponentConfig{ID: "regc", Type: "regconsumer"},
	)
	pv1 := fiberOf(t, envCtrl, "svc")
	regF := fiberOf(t, envCtrl, "reg")
	regCF := fiberOf(t, envCtrl, "regc")
	waitActive(t, pv1, regF, regCF)

	// (2) Activate the remaining composition through Loader + Runtime.
	evRec := &pcRec{}
	effRec := &pcRec{}
	var emitCtx *runtime.Context
	consF := pcLoadActive(t, rt, pcCons(consKit, consKit.seen))
	evR := pcLoadActive(t, rt, pcEvRegistrar("evApp", evRec))
	evE := pcLoadActive(t, rt, pcEvEmitter(&emitCtx))
	eff := pcLoadActive(t, rt, pcEffectHost("pc-app-effects", effRec, "A", "B", "C"))
	wasmMod := loadPCModule(t, ld, "pcw-app", loader.Artifact{ID: "pcw-app", BackendType: loader.BackendWASM, Source: wasmSrc(t, "valid.wasm"), Version: "1"})
	wasmF := pcWasmMount(t, rt, wasmMod, "wasm-app")
	chA := make(chan *runtime.Fiber, 2)
	chB := make(chan *runtime.Fiber, 2)
	outA := make(chan string, 4)
	outB := make(chan string, 4)
	leafKit := &kit{}
	var childF *runtime.Fiber
	act := pcLoadActive(t, rt, pcAppActivator(chA, chB, outA, outB, &childF, leafKit))
	_ = childF
	scoA := pcRecv(t, chA, "scoped consumer A")
	scoB := pcRecv(t, chB, "scoped consumer B")
	waitActive(t, consF, scoA, scoB)
	if got := pcRecv(t, consKit.seen, "consumer v1"); got != "v1" {
		t.Fatalf("consumer resolved %q, want v1", got)
	}
	if got := pcRecv(t, outA, "scoped A v1"); got != "v1" {
		t.Fatalf("scoped A resolved %q", got)
	}
	if got := pcRecv(t, outB, "scoped B v1"); got != "v1" {
		t.Fatalf("scoped B resolved %q", got)
	}

	// HMR target over the shared loader: independent module V1 -> V2.
	if err := ld.RegisterBuiltin("builtin://pc-appx-v1", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcLeaf("pc-appx-v1", nil), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	if err := ld.RegisterBuiltin("builtin://pc-appx-v2", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return pcLeaf("pc-appx-v2", nil), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	mx1 := loadPCModule(t, ld, "pc-appx-v1", artifact("pc-appx-v1", "pc", "builtin://pc-appx-v1", "1"))
	x1comp, err := mx1.Factory.Create(config.ComponentConfig{ID: "appx", Type: mx1.Type})
	if err != nil {
		t.Fatal(err)
	}
	x1 := pcLoadActive(t, rt, x1comp)
	xTgt := hmr.Target{ID: "appx", ComponentID: "appx", Artifact: artifact("pc-appx-v2", "pc", "builtin://pc-appx-v2", "2")}
	if err := h.Register(xTgt); err != nil {
		t.Fatal(err)
	}
	if err := h.Bind("appx", *mx1, x1); err != nil {
		t.Fatal(err)
	}

	// (3) Use: kernel event dispatch + registry member churn + live effects.
	if err := event.Emit(emitCtx, pcEvKey, "use1"); err != nil {
		t.Fatal(err)
	}
	if !evRec.has("evApp:use1") {
		t.Fatalf("event handler missed use1: %v", evRec.got())
	}
	if regOwner == nil || regOwner.reg == nil {
		t.Fatal("registry owner did not expose its capability")
	}
	if err := regOwner.reg.Add("app", "member"); err != nil {
		t.Fatal(err)
	}
	if got := pcEffectCount(pcSnap(t, rt)); got < 3 {
		t.Fatalf("effect count = %d, want >= 3 across composition", got)
	}

	// (4) Dependency withdrawal: remove the service from Desired.
	writeCfg(
		config.ComponentConfig{ID: "reg", Type: "regowner"},
		config.ComponentConfig{ID: "regc", Type: "regconsumer"},
	)
	if err := pv1.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("withdrawn service did not reach Gone: %v", err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := consF.WaitInactive(ctx); err != nil {
		t.Fatalf("dependent %s did not lose its binding: %v", consF.Name(), err)
	}
	// Paper isolation (ADR-0001): the scoped consumers bind their OWN namespace
	// provider ("v1"), so the root service withdrawal does not touch them.
	for _, f := range []*runtime.Fiber{scoA, scoB} {
		if f.State() != runtime.StateActive {
			t.Fatalf("scoped consumer %s disturbed by root withdrawal: %v", f.Name(), f.State())
		}
	}
	if regF.State() != runtime.StateActive || regCF.State() != runtime.StateActive {
		t.Fatal("registry plane corrupted by service withdrawal")
	}

	// (5) Config change: service v2 -> dependents recover.
	writeCfg(
		config.ComponentConfig{ID: "svc", Type: "prov", Config: map[string]any{"tag": "v2"}},
		config.ComponentConfig{ID: "reg", Type: "regowner"},
		config.ComponentConfig{ID: "regc", Type: "regconsumer"},
	)
	pv2 := fiberOf(t, envCtrl, "svc")
	waitActive(t, pv2)
	if err := consF.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("consumer did not recover: %v", err)
	}
	if got := pcRecv(t, consKit.seen, "consumer v2"); got != "v2" {
		t.Fatalf("consumer resolved %q, want v2", got)
	}
	// Scoped consumers keep their scope-local v1 binding through the root v2
	// recovery (namespace isolation, never cross-bound).
	if scoA.State() != runtime.StateActive || scoB.State() != runtime.StateActive {
		t.Fatalf("scoped consumers disturbed by root recovery: %v/%v", scoA.State(), scoB.State())
	}

	// (6) HMR on the shared runtime.
	if err := h.Replace(pcTimeout(t), xTgt); err != nil {
		t.Fatalf("hmr replace: %v", err)
	}
	xb, _ := h.CurrentBinding("appx")
	x2 := xb.Fiber
	if x2.ID() == x1.ID() || x2.State() != runtime.StateActive {
		t.Fatalf("HMR did not produce a new active fiber (old %s)", x1.State())
	}
	if x1.State() != runtime.StateGone {
		t.Fatalf("old HMR fiber state = %s", x1.State())
	}

	// (7) Event still live under the new service generation.
	if err := event.Emit(emitCtx, pcEvKey, "use2"); err != nil {
		t.Fatal(err)
	}
	if !evRec.has("evApp:use2") {
		t.Fatalf("event handler missed use2: %v", evRec.got())
	}

	// (8) Child + scope disposal: the activator owns leaf + scopes.
	pcDisposeGone(t, act)
	if err := scoA.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("scoped A orphan survived: %v", err)
	}
	if err := scoB.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("scoped B orphan survived: %v", err)
	}

	// (9) Application disposal: reconcile away the controller-owned plane.
	if err := ctrl.Reconcile(pcTimeout(t), config.Config{}); err != nil {
		t.Fatalf("final reconcile: %v", err)
	}
	if len(ctrl.Owned()) != 0 {
		t.Fatal("controller still owns components after application disposal")
	}
	for _, f := range []*runtime.Fiber{pv2, regF, regCF} {
		if err := f.Gone(pcTimeout(t)); err != nil {
			t.Fatalf("controller fiber %s not gone: %v", f.Name(), err)
		}
	}
	// Event registration dies with its (direct-loaded) owner.
	pcDisposeGone(t, evR)
	if err := event.Emit(emitCtx, pcEvKey, "after"); err != nil {
		t.Fatal(err)
	}
	if evRec.has("evApp:after") {
		t.Fatal("event handler survived application disposal")
	}

	// (10) Remaining direct fibers disposed; everything converges.
	pcDisposeGone(t, consF)
	pcDisposeGone(t, evE)
	pcDisposeGone(t, wasmF)
	pcDisposeGone(t, x2)
	pcDisposeGone(t, eff)
	if !stringsHasAll(effRec.join(), "-C", "-B", "-A") {
		t.Fatalf("effects not unwound: %v", effRec.got())
	}
	if n := pcWasmDestroyedN(obs, "pcw-app"); n != 1 {
		t.Fatalf("wasm instances destroyed = %d, want 1", n)
	}
	pcQuiesced(t, rt, "PC-18")
	snap := pcSnap(t, rt)
	if len(snap.Scopes) > 1 {
		t.Fatalf("scope tree leaked after disposal: %d scopes", len(snap.Scopes))
	}

	// (11) Runtime Close is idempotent and drains every remaining resource.
	if err := rt.Close(pcTimeout(t)); err != nil {
		t.Fatalf("runtime close: %v", err)
	}
	if err := rt.Close(pcTimeout(t)); err != nil {
		t.Fatalf("second runtime close not idempotent: %v", err)
	}
}

func adapterSync(t *testing.T, ctrl *config.Controller, fw *watch.FileWatcher, path string) error {
	t.Helper()
	a := newPCAdapter(t, ctrl, fw, path)
	defer a.CloseContext(ctxT(t))
	return a.Sync(pcTimeout(t))
}

func newPCAdapter(t *testing.T, ctrl *config.Controller, fw *watch.FileWatcher, path string) *configwatch.Adapter {
	t.Helper()
	adapter, err := configwatch.New(configwatch.Source{ID: "app", Path: path, Format: configwatch.FormatTOML}, ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	return adapter
}
