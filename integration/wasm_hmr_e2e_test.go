package integration

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// WASM + HMR integration environment: real Runtime + Loader (builtin + wasm
// backends) + HMR Controller over explicit ModuleUsage + instance observer.
// ---------------------------------------------------------------------------

type whEnv struct {
	rt  *runtime.Runtime
	ld  *loader.BuiltinLoader
	wb  *wasm.Backend
	obs *wasmObs
	h   *hmr.Controller
}

func newWHEnv(t *testing.T) *whEnv {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	ld := loader.NewBuiltinLoader()
	obs := &wasmObs{}
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatalf("RegisterBackend(wasm): %v", err)
	}
	h := hmr.New(rt, ld, ld.Usage())
	e := &whEnv{rt: rt, ld: ld, wb: wb, obs: obs, h: h}
	t.Cleanup(func() { _ = h.CloseContext(ctxT(t)) })
	t.Cleanup(func() { _ = ld.CloseContext(ctxT(t)) })
	t.Cleanup(func() { _ = rt.Close(context.Background()) })
	t.Cleanup(func() { _ = wb.Close() })
	return e
}

func whSrc(file string) string {
	abs, err := filepath.Abs(filepath.Join("..", "extensions", "loader", "wasm", "testdata", file))
	if err != nil {
		panic(err)
	}
	return "file://" + abs
}

func wArtifact(id, file, ver string) loader.Artifact {
	return loader.Artifact{ID: id, BackendType: loader.BackendWASM, Source: whSrc(file), Version: ver}
}

func builtinArtifact(id, typ, src, ver string) loader.Artifact {
	return loader.Artifact{ID: id, BackendType: loader.BackendBuiltin, Type: typ, Source: src, Version: ver}
}

func (e *whEnv) load(t *testing.T, a loader.Artifact) *loader.Module {
	t.Helper()
	m, err := e.ld.Load(ctxT(t), a)
	if err != nil {
		t.Fatalf("Load(%+v): %v", a, err)
	}
	return m
}

func (e *whEnv) regBuiltin(t *testing.T, src string, f config.Factory) {
	t.Helper()
	if err := e.ld.RegisterBuiltin(src, func() config.Factory { return f }); err != nil {
		t.Fatalf("RegisterBuiltin(%s): %v", src, err)
	}
}

// mount creates a component from mod and loads its Fiber to Active.
func (e *whEnv) mount(t *testing.T, mod *loader.Module, componentID string) *runtime.Fiber {
	t.Helper()
	comp, err := mod.Factory.Create(config.ComponentConfig{ID: componentID, Type: mod.Type})
	if err != nil {
		t.Fatalf("Create(%s): %v", componentID, err)
	}
	f, err := e.rt.Load(comp)
	if err != nil {
		t.Fatalf("rt.Load(%s): %v", componentID, err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("fiber %s not active: %v", componentID, err)
	}
	return f
}

// bind installs an HMR target bound to the running fiber (usage acquired).
func (e *whEnv) bind(t *testing.T, targetID, componentID string, mod *loader.Module, fiber *runtime.Fiber, desired loader.Artifact) {
	t.Helper()
	tg := hmr.Target{ID: targetID, ComponentID: componentID, Artifact: desired}
	if err := e.h.Register(tg); err != nil {
		t.Fatalf("Register(%s): %v", targetID, err)
	}
	if err := e.h.Bind(targetID, *mod, fiber); err != nil {
		t.Fatalf("Bind(%s): %v", targetID, err)
	}
}

func (e *whEnv) replaceOK(t *testing.T, targetID string, artifact loader.Artifact) {
	t.Helper()
	err := e.h.Replace(ctxT(t), hmr.Target{ID: targetID, ComponentID: "cam", Artifact: artifact})
	if err != nil {
		t.Fatalf("Replace(%s): %v", targetID, err)
	}
}

func (e *whEnv) binding(t *testing.T, targetID string) hmr.Binding {
	t.Helper()
	b, ok := e.h.CurrentBinding(targetID)
	if !ok {
		t.Fatalf("no binding for %s", targetID)
	}
	return b
}

// W-HMR-01 — Basic WASM v1 -> v2 warm replacement.
func TestWHMR01BasicWASMReplacement(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-v2", "camera-v2.wasm", "2"))

	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	b := e.binding(t, "cam")
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("fiber identity not changed")
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new fiber not Active")
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %v, want Gone", f1.State())
	}
	// Old module released and unloaded.
	if e.ld.Usage().InUse("cam-v1") {
		t.Fatal("old module usage leaked")
	}
	if e.ld.Has("cam-v1") {
		t.Fatal("old module still loaded")
	}
	if !e.ld.Usage().InUse("cam-v2") {
		t.Fatal("new module usage not held")
	}
}

// W-HMR-02 — Identity: FiberA != FiberB; WASM InstanceA != InstanceB.
func TestWHMR02Identity(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-v2", "camera-v2.wasm", "2"))

	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	b := e.binding(t, "cam")
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("fiber reused")
	}
	created := e.obs.createdN("cam-v1") + e.obs.createdN("cam-v2")
	if created != 2 {
		t.Fatalf("wasm instances created = %d, want 2 (one per activation)", created)
	}
	// Old module's instance destroyed; new module's instance alive.
	if e.obs.destroyedN("cam-v1") != 1 {
		t.Fatal("old wasm instance not destroyed")
	}
	if e.obs.destroyedN("cam-v2") != 0 {
		t.Fatal("new wasm instance destroyed while Active")
	}
}

// W-HMR-03 — ModuleType preservation across replacement; BackendType is never
// the ModuleType.
func TestWHMR03ModuleTypePreservation(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-v2", "camera-v2.wasm", "2"))
	if m1.Type != "industrial.camera" {
		t.Fatalf("old type = %q", m1.Type)
	}
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	m2, ok := e.ld.Get("cam-v2")
	if !ok || m2.Type != "industrial.camera" {
		t.Fatalf("new type = %q, want industrial.camera", m2.Type)
	}
	if string(loader.BackendWASM) == m2.Type {
		t.Fatal("BackendType leaked into ModuleType")
	}
	if b := e.binding(t, "cam"); b.ModuleType != "industrial.camera" {
		t.Fatalf("binding module type = %q", b.ModuleType)
	}
}

// W-HMR-04 — Backend switch: builtin industrial.camera -> wasm industrial.camera.
func TestWHMR04BackendSwitch(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://cam-go", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	m1 := e.load(t, builtinArtifact("cam-go", "industrial.camera", "builtin://cam-go", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-wasm", "valid.wasm", "2"))

	e.replaceOK(t, "cam", wArtifact("cam-wasm", "valid.wasm", "2"))
	b := e.binding(t, "cam")
	if b.ModuleType != "industrial.camera" {
		t.Fatalf("module type = %q", b.ModuleType)
	}
	if b.Fiber.Component().Name() != "wasm:cam" {
		t.Fatalf("component name = %q, want wasm-backed", b.Fiber.Component().Name())
	}
	if f1.State() != runtime.StateGone {
		t.Fatal("old builtin fiber not Gone")
	}
	if e.ld.Has("cam-go") {
		t.Fatal("old builtin module not unloaded")
	}
}

// W-HMR-05 — Type incompatibility (camera -> sensor) fails; old preserved.
func TestWHMR05TypeIncompatibility(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("sensor-v1", "sensor.wasm", "1"))

	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("sensor-v1", "sensor.wasm", "1")})
	if !errors.Is(err, hmr.ErrIncompatibleModule) {
		t.Fatalf("Replace = %v, want ErrIncompatibleModule", err)
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after incompatible replace")
	}
	if !e.ld.Usage().InUse("cam-v1") || !e.ld.Has("cam-v1") {
		t.Fatal("old module not retained")
	}
	// New module cleaned up.
	if e.ld.Has("sensor-v1") {
		t.Fatal("incompatible new module leaked")
	}
	b, _ := e.h.CurrentBinding("cam")
	if b.Fiber != f1 {
		t.Fatal("binding changed")
	}
}

// W-HMR-06 — Invalid WASM replacement fails; old Active preserved.
func TestWHMR06InvalidWASM(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-bad", "invalid.wasm", "2"))

	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("cam-bad", "invalid.wasm", "2")})
	if !errors.Is(err, wasm.ErrInvalidWASM) {
		t.Fatalf("Replace = %v, want ErrInvalidWASM", err)
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active")
	}
	if e.ld.Has("cam-bad") {
		t.Fatal("invalid module registered")
	}
}

// W-HMR-07 — Missing ABI export: no ModuleUsage leak, no Fiber leak, old
// Active preserved.
func TestWHMR07MissingExport(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-bad", "missing-export.wasm", "2"))

	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("cam-bad", "missing-export.wasm", "2")})
	if !errors.Is(err, wasm.ErrWASMABI) {
		t.Fatalf("Replace = %v, want ErrWASMABI", err)
	}
	if e.ld.Usage().InUse("cam-bad") {
		t.Fatal("module usage leaked for failed module")
	}
	if e.ld.Has("cam-bad") {
		t.Fatal("failed module still loaded")
	}
	if e.obs.createdN("cam-bad") != 0 || e.obs.destroyedN("cam-bad") != 0 {
		t.Fatal("wasm instance leak for failed module")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active")
	}
}

// ---------------------------------------------------------------------------
// W-HMR-08..13: failure preservation + provider integration.
// ---------------------------------------------------------------------------

// W-HMR-08 — Factory failure during replacement: old preserved, new cleaned.
func TestWHMR08FactoryFailure(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, builtinArtifact("cam-fail", "industrial.camera", "builtin://cam-fail", "2"))
	e.regBuiltin(t, "builtin://cam-fail", &adapterFactory{build: func(config.ComponentConfig) (runtime.Component, error) {
		return nil, errors.New("factory refuses")
	}})

	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("cam-fail", "industrial.camera", "builtin://cam-fail", "2")})
	if err == nil {
		t.Fatal("Replace succeeded despite factory failure")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active")
	}
	if e.ld.Usage().InUse("cam-fail") || e.ld.Has("cam-fail") {
		t.Fatal("failed module leaked")
	}
	b, _ := e.h.CurrentBinding("cam")
	if b.Fiber != f1 {
		t.Fatal("binding changed")
	}
}

// W-HMR-09 — New Fiber failure (Apply fails): replacement fails; old Active.
func TestWHMR09NewFiberFailure(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, builtinArtifact("cam-apply-fail", "industrial.camera", "builtin://cam-apply-fail", "2"))
	e.regBuiltin(t, "builtin://cam-apply-fail", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, true), nil // Apply fails
	}})

	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("cam-apply-fail", "industrial.camera", "builtin://cam-apply-fail", "2")})
	if err == nil {
		t.Fatal("Replace succeeded despite new fiber failure")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active")
	}
	if e.ld.Has("cam-apply-fail") || e.ld.Usage().InUse("cam-apply-fail") {
		t.Fatal("failed new module leaked")
	}
}

// W-HMR-10 — Old cleanup after success: old fiber Gone, old Module usage == 0,
// old Module unloaded, old WASM instance destroyed.
func TestWHMR10OldCleanup(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-v2", "camera-v2.wasm", "2"))
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))

	eventually(t, 5*time.Second, "old instance destroyed", func() bool { return e.obs.destroyedN("cam-v1") == 1 })
	if e.obs.destroyedN("cam-v2") != 0 {
		t.Fatal("new instance destroyed while active")
	}
	if f1.State() != runtime.StateGone {
		t.Fatal("old fiber not Gone")
	}
	if e.ld.Usage().InUse("cam-v1") {
		t.Fatal("old usage not released")
	}
	if e.ld.Has("cam-v1") {
		t.Fatal("old module not unloaded")
	}
}

// whRec is an ordered event recorder for provider-order assertions.
type whRec struct {
	mu sync.Mutex
	ev []string
}

func (r *whRec) add(e string) {
	r.mu.Lock()
	r.ev = append(r.ev, e)
	r.mu.Unlock()
}
func (r *whRec) index(e string) int {
	r.mu.Lock()
	defer r.mu.Unlock()
	for i, x := range r.ev {
		if x == e {
			return i
		}
	}
	return -1
}

type whTagComp struct {
	id      string
	tag     string
	consume bool
	rec     *whRec
}

func (c *whTagComp) Name() string { return "wh:" + c.id }
func (c *whTagComp) Inject() []runtime.Dependency {
	if c.consume {
		return []runtime.Dependency{runtime.Requires(capKey)}
	}
	return nil
}
func (c *whTagComp) Provide() []runtime.Capability {
	if !c.consume {
		return []runtime.Capability{capKey.Capability()}
	}
	return nil
}
func (c *whTagComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.rec != nil {
		c.rec.add("apply:" + c.id)
	}
	if c.consume {
		if _, err := runtime.Require(ctx, capKey); err != nil {
			return nil, err
		}
	} else {
		if err := runtime.Provide(ctx, capKey, capVal{tag: c.tag}); err != nil {
			return nil, err
		}
	}
	return func() error {
		if c.rec != nil {
			c.rec.add("cleanup:" + c.id)
		}
		return nil
	}, nil
}

type whTagFactory struct {
	tag     string
	consume bool
	rec     *whRec
}

func (f *whTagFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &whTagComp{id: cc.ID, tag: f.tag, consume: f.consume, rec: f.rec}, nil
}

// W-HMR-11 — Exclusive provider replacement: consumer rebinds to the new
// Provider identity via Kernel semantics.
func TestWHMR11ExclusiveProviderReplacement(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1"})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2"})

	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "prov")
	e.bind(t, "prov", "prov", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	consKit := &kit{seen: make(chan string, 8)}
	cons := consumerComp(consKit, "cons")
	cf, err := e.rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}

	e.replaceOK(t, "prov", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	b := e.binding(t, "prov")
	if b.Fiber.ID() == pf.ID() {
		t.Fatal("provider fiber reused")
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new provider not Active")
	}
	if pf.State() != runtime.StateGone {
		t.Fatalf("old provider state = %v, want Gone", pf.State())
	}
	// Consumer recovers onto the new provider identity.
	eventually(t, 8*time.Second, "consumer recovered", func() bool { return cf.State() == runtime.StateActive })
	if e.ld.Usage().InUse("m1") || e.ld.Has("m1") {
		t.Fatal("old provider module leaked")
	}
	if !e.ld.Usage().InUse("m2") {
		t.Fatal("new provider usage not held")
	}
}

// W-HMR-12 — Consumer-first withdrawal: the consumer unwinds before the old
// provider is gone during the destructive fallback.
func TestWHMR12ConsumerFirstWithdrawal(t *testing.T) {
	e := newWHEnv(t)
	rec := &whRec{}
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1", rec: rec})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2", rec: rec})
	e.regBuiltin(t, "builtin://cons", &whTagFactory{consume: true, rec: rec})

	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "prov")
	e.bind(t, "prov", "prov", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	mc := e.load(t, builtinArtifact("mc", "industrial.consumer", "builtin://cons", "1"))
	cf := e.mount(t, mc, "cons")
	if cf.State() != runtime.StateActive {
		t.Fatal("consumer not active")
	}

	e.replaceOK(t, "prov", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	// During the Kernel-driven withdrawal the consumer must unwind before the
	// old provider (dependency semantics), so cleanup:cons < cleanup:prov.
	eventually(t, 8*time.Second, "consumer recovered", func() bool { return cf.State() == runtime.StateActive })
	ci := rec.index("cleanup:cons")
	pi := rec.index("cleanup:prov")
	if ci < 0 || pi < 0 {
		t.Fatalf("missing cleanups in %v", rec.ev)
	}
	if !(ci < pi) {
		t.Fatalf("consumer cleanup (%d) not before provider cleanup (%d): %v", ci, pi, rec.ev)
	}
}

// W-HMR-13 — Multi-level dependency P -> A -> B replacement.
func TestWHMR13MultiLevelDependency(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1"})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2"})

	aKit := &kit{}
	bKit := &kit{}
	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "p")
	e.bind(t, "p", "p", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	af, err := e.rt.Load(&aComp{kit: aKit, id: "a"})
	if err != nil {
		t.Fatal(err)
	}
	bf, err := e.rt.Load(&bComp{kit: bKit, id: "b"})
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, af, bf)
	if aKit.applies.Load() < 1 || bKit.applies.Load() < 1 {
		t.Fatalf("A/B not active: %d %d", aKit.applies.Load(), bKit.applies.Load())
	}

	e.replaceOK(t, "p", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))
	// A and B unwind and reactivate onto the new provider (new identities).
	eventually(t, 10*time.Second, "A/B recovered", func() bool {
		return af.State() == runtime.StateActive && bf.State() == runtime.StateActive &&
			aKit.applies.Load() >= 2 && bKit.applies.Load() >= 2
	})
	b := e.binding(t, "p")
	if b.Fiber.ID() == pf.ID() || b.Fiber.State() != runtime.StateActive {
		t.Fatal("provider replacement invalid")
	}
}

// ---------------------------------------------------------------------------
// W-HMR-14..17: concurrency, close, stale completion.
// ---------------------------------------------------------------------------

// W-HMR-14 — Same-target serialization: many concurrent replacements converge
// to exactly one valid binding with no leaked usage/fibers.
func TestWHMR14SameTargetConcurrency(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://v0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	m0 := e.load(t, builtinArtifact("v0", "industrial.camera", "builtin://v0", "0"))
	f0 := e.mount(t, m0, "cam")
	e.bind(t, "cam", "cam", m0, f0, builtinArtifact("v1", "industrial.camera", "builtin://v1", "1"))

	const g = 6
	const perG = 8
	errs := make([]error, g*perG)
	var wg sync.WaitGroup
	for gid := 0; gid < g; gid++ {
		for i := 0; i < perG; i++ {
			wg.Add(1)
			go func(gid, i int) {
				defer wg.Done()
				id := fmt.Sprintf("v%d-%d", gid, i)
				src := "builtin://" + id
				_ = e.ld.RegisterBuiltin(src, func() config.Factory {
					return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
						return independentComp(&kit{}, cc.ID, false), nil
					}}
				})
				errs[gid*perG+i] = e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact(id, "industrial.camera", src, "1")})
			}(gid, i)
		}
	}
	wg.Wait()
	for i, err := range errs {
		if err != nil {
			t.Fatalf("replace %d: %v", i, err)
		}
	}

	b := e.binding(t, "cam")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("final binding not Active")
	}
	if f0.State() != runtime.StateGone {
		t.Fatal("initial fiber not Gone")
	}
	// Exactly one module remains loaded and in use (the final binding).
	if len(e.ld.Snapshot()) != 1 {
		mods := e.ld.Snapshot()
		ids := make([]string, 0, len(mods))
		for _, m := range mods {
			ids = append(ids, m.ID)
		}
		t.Fatalf("leaked modules: %v", ids)
	}
	if !e.ld.Usage().InUse(b.Module.ID) {
		t.Fatal("final module usage missing")
	}
}

// W-HMR-15 — Different-target concurrency: failures on one target never affect
// another.
func TestWHMR15DifferentTargetConcurrency(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://a0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	e.regBuiltin(t, "builtin://b0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	ma := e.load(t, builtinArtifact("a0", "industrial.camera", "builtin://a0", "0"))
	fa := e.mount(t, ma, "a")
	e.bind(t, "A", "a", ma, fa, builtinArtifact("a1", "industrial.camera", "builtin://a1", "1"))
	mb := e.load(t, builtinArtifact("b0", "industrial.sensor", "builtin://b0", "0"))
	fb := e.mount(t, mb, "b")
	e.bind(t, "B", "b", mb, fb, builtinArtifact("b1", "industrial.sensor", "builtin://b1", "1"))

	const n = 30
	var wg sync.WaitGroup
	var aErr, bErr int32
	for i := 0; i < n; i++ {
		wg.Add(2)
		go func(i int) { // Target A: always succeeds.
			defer wg.Done()
			id := fmt.Sprintf("a%d", i+1)
			src := "builtin://" + id
			_ = e.ld.RegisterBuiltin(src, func() config.Factory {
				return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
					return independentComp(&kit{}, cc.ID, false), nil
				}}
			})
			if err := e.h.Replace(ctxT(t), hmr.Target{ID: "A", ComponentID: "a", Artifact: builtinArtifact(id, "industrial.camera", src, "1")}); err != nil {
				atomic.AddInt32(&aErr, 1)
			}
		}(i)
		go func(i int) { // Target B: always fails (unknown source); must be isolated.
			defer wg.Done()
			id := fmt.Sprintf("bmissing%d", i)
			if err := e.h.Replace(ctxT(t), hmr.Target{ID: "B", ComponentID: "b", Artifact: builtinArtifact(id, "industrial.sensor", "builtin://missing-"+id, "1")}); err == nil {
				atomic.AddInt32(&bErr, 1)
			}
		}(i)
	}
	wg.Wait()

	if aErr != 0 {
		t.Fatalf("target A had %d failures", aErr)
	}
	if bErr != 0 {
		t.Fatalf("target B replacements unexpectedly succeeded %d times (failure isolation broken)", bErr)
	}
	ba := e.binding(t, "A")
	if ba.Fiber.State() != runtime.StateActive {
		t.Fatal("A binding invalid")
	}
	bb := e.binding(t, "B")
	if bb.Module.ID != "b0" || bb.Fiber != fb || fb.State() != runtime.StateActive {
		t.Fatal("B binding changed despite failures")
	}
}

// W-HMR-16 — Close during an in-flight replacement: CloseContext times out
// (no fake Closed), then finishes truthfully after the replacement aborts.
func TestWHMR16CloseDuringReplacement(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://cam-v0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	m0 := e.load(t, builtinArtifact("cam-v0", "industrial.camera", "builtin://cam-v0", "0"))
	f0 := e.mount(t, m0, "cam")
	e.bind(t, "cam", "cam", m0, f0, builtinArtifact("pend", "industrial.camera", "builtin://pend", "1"))
	// The pending replacement module requires a provider that never appears.
	e.regBuiltin(t, "builtin://pend", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return consumerComp(&kit{seen: make(chan string, 4)}, cc.ID), nil
	}})

	replaceCtx, cancelReplace := context.WithCancel(context.Background())
	replaceDone := make(chan error, 1)
	go func() {
		replaceDone <- e.h.Replace(replaceCtx, hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("pend", "industrial.camera", "builtin://pend", "1")})
	}()
	// Wait until the replacement is in-flight (new usage acquired) before Close.
	eventually(t, 8*time.Second, "replacement in flight", func() bool { return e.ld.Usage().InUse("pend") })

	shortCtx, shortCancel := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer shortCancel()
	first := e.h.CloseContext(shortCtx)
	if first == nil {
		t.Fatal("CloseContext claimed Closed while a replacement was in flight (fake Closed)")
	}

	cancelReplace()
	if err := <-replaceDone; err == nil {
		t.Fatal("in-flight replacement unexpectedly succeeded")
	}
	// The replacement abort must clean its own resources.
	eventually(t, 8*time.Second, "pending module cleaned", func() bool {
		return !e.ld.Usage().InUse("pend") && !e.ld.Has("pend")
	})

	// Now the controller can finish closing truthfully.
	clctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := e.h.CloseContext(clctx); err != nil {
		t.Fatalf("final CloseContext: %v", err)
	}
	if err := e.h.Replace(context.Background(), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("x", "industrial.camera", "builtin://x", "1")}); !errors.Is(err, hmr.ErrHMRClosed) {
		t.Fatalf("Replace after Close = %v, want ErrHMRClosed", err)
	}
}

// W-HMR-17 — Same-target concurrent replacement has a deterministic
// linearization: the final binding equals the last completed Replace; the
// loser is fully cleaned and never overwrites the winner.
func TestWHMR17StaleCompletionIsolation(t *testing.T) {
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-v2", "camera-v2.wasm", "2"))

	order := make(chan string, 2)
	done := make(chan struct{}, 2)
	run := func(id, file string) {
		defer func() { done <- struct{}{} }()
		err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact(id, file, "2")})
		if err != nil {
			t.Errorf("replace %s: %v", id, err)
			return
		}
		order <- id
	}
	go run("cam-v2", "camera-v2.wasm")
	go run("cam-v3", "camera-v3.wasm")
	<-done
	<-done
	close(order)

	last := ""
	for id := range order {
		last = id
	}
	b := e.binding(t, "cam")
	if b.Module.ID != last {
		t.Fatalf("binding module %q, want last-completed %q", b.Module.ID, last)
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("winner not Active")
	}
	loser := "cam-v2"
	if last == "cam-v2" {
		loser = "cam-v3"
	}
	if e.ld.Usage().InUse(loser) || e.ld.Has(loser) {
		t.Fatal("loser module leaked")
	}
	if e.obs.destroyedN(loser) != 1 {
		t.Fatal("loser wasm instance not destroyed")
	}
	// Winner instance alive; exactly one live binding.
	if e.obs.destroyedN(last) != 0 {
		t.Fatal("winner instance destroyed while active")
	}
	// No ambiguous double-active: initial fiber Gone.
	if f1.State() != runtime.StateGone {
		t.Fatalf("initial fiber state = %v, want Gone", f1.State())
	}
}

// ---------------------------------------------------------------------------
// E2E-WH-01..12 — end-to-end WASM+HMR chain assertions.
// ---------------------------------------------------------------------------

func e2eSetupCamera(t *testing.T) (*whEnv, *runtime.Fiber) {
	t.Helper()
	e := newWHEnv(t)
	m1 := e.load(t, wArtifact("cam-v1", "valid.wasm", "1"))
	f1 := e.mount(t, m1, "cam")
	e.bind(t, "cam", "cam", m1, f1, wArtifact("cam-v2", "camera-v2.wasm", "2"))
	return e, f1
}

// E2E-WH-01 — WASM v1 -> v2 end to end.
func TestE2EWH01WASMV1ToV2(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	b := e.binding(t, "cam")
	if b.Fiber.ID() == f1.ID() || b.Fiber.State() != runtime.StateActive || f1.State() != runtime.StateGone {
		t.Fatal("replacement chain broken")
	}
}

// E2E-WH-02 — WASM identity change.
func TestE2EWH02WASMIdentityChange(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	if e.obs.createdN("cam-v1") != 1 || e.obs.createdN("cam-v2") != 1 || e.obs.destroyedN("cam-v1") != 1 {
		t.Fatal("activation/instance identity not preserved")
	}
	if b := e.binding(t, "cam"); b.ModuleType != "industrial.camera" {
		t.Fatal("logical type lost")
	}
}

// E2E-WH-03 — backend switch builtin -> wasm end to end.
func TestE2EWH03BackendSwitch(t *testing.T) {
	// dedicated env: builtin v1 -> wasm v2 (see W-HMR-04); assert module-level.
	e2 := newWHEnv(t)
	e2.regBuiltin(t, "builtin://cam-go", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	m1 := e2.load(t, builtinArtifact("cam-go", "industrial.camera", "builtin://cam-go", "1"))
	f1 := e2.mount(t, m1, "cam")
	e2.bind(t, "cam", "cam", m1, f1, wArtifact("cam-wasm", "valid.wasm", "2"))
	e2.replaceOK(t, "cam", wArtifact("cam-wasm", "valid.wasm", "2"))
	b := e2.binding(t, "cam")
	if b.Fiber.Component().Name() != "wasm:cam" || b.ModuleType != "industrial.camera" {
		t.Fatal("backend switch broken")
	}
}

// E2E-WH-04 — incompatible ModuleType preservation end to end.
func TestE2EWH04IncompatibleModuleType(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("sen", "sensor.wasm", "1")})
	if !errors.Is(err, hmr.ErrIncompatibleModule) {
		t.Fatalf("Replace = %v", err)
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old not Active")
	}
	if e.ld.Has("sen") {
		t.Fatal("incompatible module leaked")
	}
}

// E2E-WH-05 — invalid WASM preservation end to end.
func TestE2EWH05InvalidWASMPreservation(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("bad", "invalid.wasm", "2")})
	if !errors.Is(err, wasm.ErrInvalidWASM) {
		t.Fatalf("Replace = %v", err)
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old not Active")
	}
}

// E2E-WH-06 — missing export preservation end to end.
func TestE2EWH06MissingExportPreservation(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("bad", "missing-create.wasm", "2")})
	if !errors.Is(err, wasm.ErrWASMABI) {
		t.Fatalf("Replace = %v", err)
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old not Active")
	}
	if e.ld.Has("bad") || e.ld.Usage().InUse("bad") {
		t.Fatal("leak after missing export")
	}
}

// E2E-WH-07 — factory failure preservation end to end.
func TestE2EWH07FactoryFailurePreservation(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	e.regBuiltin(t, "builtin://fail", &adapterFactory{build: func(config.ComponentConfig) (runtime.Component, error) {
		return nil, errors.New("boom")
	}})
	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("fail", "industrial.camera", "builtin://fail", "2")})
	if err == nil || f1.State() != runtime.StateActive {
		t.Fatalf("factory failure not preserved: %v / %v", err, f1.State())
	}
}

// E2E-WH-08 — new Fiber failure preservation end to end.
func TestE2EWH08NewFiberFailurePreservation(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	e.regBuiltin(t, "builtin://afail", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, true), nil
	}})
	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("afail", "industrial.camera", "builtin://afail", "2")})
	if err == nil || f1.State() != runtime.StateActive {
		t.Fatalf("new fiber failure not preserved: %v / %v", err, f1.State())
	}
	if e.ld.Has("afail") {
		t.Fatal("failed new module leaked")
	}
}

// E2E-WH-09 — old Module unload only after old Fiber Gone.
func TestE2EWH09OldModuleUnloadAfterGone(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	if f1.State() != runtime.StateGone {
		t.Fatal("old fiber not Gone")
	}
	if e.ld.Has("cam-v1") || e.ld.Usage().InUse("cam-v1") {
		t.Fatal("old module released before Gone")
	}
}

// E2E-WH-10 — exclusive Provider replacement end to end (builtin provider,
// consumer; an unrelated wasm component is unaffected).
func TestE2EWH10ExclusiveProviderReplacement(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1"})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2"})
	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "prov")
	e.bind(t, "prov", "prov", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	// Independent wasm fiber untouched by provider churn.
	wm := e.load(t, wArtifact("sensor", "sensor.wasm", "1"))
	wf := e.mount(t, wm, "sensor")

	e.regBuiltin(t, "builtin://cons", &whTagFactory{consume: true})
	mc := e.load(t, builtinArtifact("mcons", "industrial.consumer", "builtin://cons", "1"))
	cf2 := e.mount(t, mc, "cons")

	e.replaceOK(t, "prov", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))
	if e.binding(t, "prov").Fiber.State() != runtime.StateActive || pf.State() != runtime.StateGone {
		t.Fatal("provider replacement broken")
	}
	eventually(t, 8*time.Second, "consumer recovered", func() bool { return cf2.State() == runtime.StateActive })
	if wf.State() != runtime.StateActive {
		t.Fatal("independent wasm fiber disturbed by provider replacement")
	}
}

// E2E-WH-11 — consumer rebinding: consumer receives the new provider tag.
func TestE2EWH11ConsumerRebinding(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1"})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2"})
	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "prov")
	e.bind(t, "prov", "prov", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	consKit := &kit{seen: make(chan string, 8)}
	cons := consumerComp(consKit, "cons")
	cf, err := e.rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if got := <-consKit.seen; got != "v1" {
		t.Fatalf("initial tag = %q", got)
	}

	e.replaceOK(t, "prov", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))
	eventually(t, 8*time.Second, "consumer rebinding", func() bool { return cf.State() == runtime.StateActive })
	select {
	case tag := <-consKit.seen:
		if tag != "v2" {
			t.Fatalf("rebound tag = %q, want v2", tag)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("consumer never saw the new provider")
	}
}

// E2E-WH-12 — multi-level dependency replacement end to end.
func TestE2EWH12MultiLevelDependency(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1"})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2"})
	aKit := &kit{}
	bKit := &kit{}
	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "p")
	e.bind(t, "p", "p", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))
	af, err := e.rt.Load(&aComp{kit: aKit, id: "a"})
	if err != nil {
		t.Fatal(err)
	}
	bf, err := e.rt.Load(&bComp{kit: bKit, id: "b"})
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, af, bf)

	e.replaceOK(t, "p", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))
	eventually(t, 10*time.Second, "chain recovered", func() bool {
		return af.State() == runtime.StateActive && bf.State() == runtime.StateActive &&
			aKit.applies.Load() >= 2 && bKit.applies.Load() >= 2
	})
	if e.binding(t, "p").Fiber.ID() == pf.ID() {
		t.Fatal("provider fiber reused")
	}
}

// ---------------------------------------------------------------------------
// E2E-WH-13..20 — concurrency / close / conservation / isolation.
// ---------------------------------------------------------------------------

// E2E-WH-13 — same-target concurrency converges.
func TestE2EWH13SameTargetConcurrency(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	const n = 24
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			id := fmt.Sprintf("v2x%d", i)
			_ = e.ld.RegisterBuiltin("builtin://"+id, func() config.Factory {
				return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
					return independentComp(&kit{}, cc.ID, false), nil
				}}
			})
			if err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact(id, "industrial.camera", "builtin://"+id, "1")}); err != nil {
				t.Errorf("replace %d: %v", i, err)
			}
		}(i)
	}
	wg.Wait()
	b := e.binding(t, "cam")
	if b.Fiber.State() != runtime.StateActive || len(e.ld.Snapshot()) != 1 {
		t.Fatalf("final binding invalid; modules=%d", len(e.ld.Snapshot()))
	}
}

// E2E-WH-14 — different-target concurrency isolates failures.
func TestE2EWH14DifferentTargetConcurrency(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://a0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	e.regBuiltin(t, "builtin://b0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	ma := e.load(t, builtinArtifact("a0", "industrial.camera", "builtin://a0", "0"))
	fa := e.mount(t, ma, "a")
	e.bind(t, "A", "a", ma, fa, builtinArtifact("a1", "industrial.camera", "builtin://a1", "1"))
	mb := e.load(t, builtinArtifact("b0", "industrial.sensor", "builtin://b0", "0"))
	fb := e.mount(t, mb, "b")
	e.bind(t, "B", "b", mb, fb, builtinArtifact("b1", "industrial.sensor", "builtin://b1", "1"))

	const n = 16
	var wg sync.WaitGroup
	var failA atomic.Int32
	var aErrs []string
	var aErrMu sync.Mutex
	for i := 0; i < n; i++ {
		wg.Add(2)
		go func(i int) {
			defer wg.Done()
			id := fmt.Sprintf("a%d", i+1)
			_ = e.ld.RegisterBuiltin("builtin://"+id, func() config.Factory {
				return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
					return independentComp(&kit{}, cc.ID, false), nil
				}}
			})
			if err := e.h.Replace(ctxT(t), hmr.Target{ID: "A", ComponentID: "a", Artifact: builtinArtifact(id, "industrial.camera", "builtin://"+id, "1")}); err != nil {
				failA.Add(1)
				aErrMu.Lock()
				aErrs = append(aErrs, fmt.Sprintf("a%d: %v", i, err))
				aErrMu.Unlock()
			}
		}(i)
		go func(i int) {
			defer wg.Done()
			_ = e.h.Replace(ctxT(t), hmr.Target{ID: "B", ComponentID: "b", Artifact: builtinArtifact(fmt.Sprintf("miss%d", i), "industrial.sensor", "builtin://miss-"+fmt.Sprint(i), "1")})
		}(i)
	}
	wg.Wait()
	if failA.Load() != 0 {
		t.Fatalf("A had %d failures: %v", failA.Load(), aErrs)
	}
	if e.binding(t, "A").Fiber.State() != runtime.StateActive {
		t.Fatal("A broken")
	}
	bb := e.binding(t, "B")
	if bb.Module.ID != "b0" || fb.State() != runtime.StateActive {
		t.Fatal("B disturbed by A traffic or its own failures")
	}
}

// E2E-WH-15 — stale completion cannot overwrite the newest binding.
func TestE2EWH15StaleCompletion(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	order := make(chan string, 2)
	done := make(chan struct{}, 2)
	run := func(id, file string) {
		defer func() { done <- struct{}{} }()
		if err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact(id, file, "2")}); err == nil {
			order <- id
		}
	}
	go run("cam-v2", "camera-v2.wasm")
	go run("cam-v3", "camera-v3.wasm")
	<-done
	<-done
	close(order)
	last := ""
	for id := range order {
		last = id
	}
	b := e.binding(t, "cam")
	if b.Module.ID != last || b.Fiber.State() != runtime.StateActive {
		t.Fatalf("stale overwrote newest: binding=%s last=%s", b.Module.ID, last)
	}
}

// E2E-WH-16 — Close during replacement is truthful (no fake Closed).
func TestE2EWH16CloseDuringReplacement(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://v0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	m0 := e.load(t, builtinArtifact("v0", "industrial.camera", "builtin://v0", "0"))
	f0 := e.mount(t, m0, "cam")
	e.bind(t, "cam", "cam", m0, f0, builtinArtifact("pend", "industrial.camera", "builtin://pend", "1"))
	e.regBuiltin(t, "builtin://pend", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return consumerComp(&kit{seen: make(chan string, 4)}, cc.ID), nil
	}})

	rctx, cancel := context.WithCancel(context.Background())
	rdone := make(chan error, 1)
	go func() {
		rdone <- e.h.Replace(rctx, hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("pend", "industrial.camera", "builtin://pend", "1")})
	}()
	eventually(t, 8*time.Second, "replace in flight", func() bool { return e.ld.Usage().InUse("pend") })

	sc, cancelSC := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancelSC()
	if err := e.h.CloseContext(sc); err == nil {
		t.Fatal("fake Closed while replace in flight")
	}
	cancel()
	<-rdone
	eventually(t, 8*time.Second, "pending cleaned", func() bool { return !e.ld.Usage().InUse("pend") && !e.ld.Has("pend") })
	cl, cancelCl := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancelCl()
	if err := e.h.CloseContext(cl); err != nil {
		t.Fatalf("final close: %v", err)
	}
}

// E2E-WH-17 — replacement resource conservation across many cycles.
func TestE2EWH17ResourceConservation(t *testing.T) {
	e := newWHEnv(t)
	cycle := []struct{ id, file string }{
		{"cam-1", "valid.wasm"}, {"cam-2", "camera-v2.wasm"}, {"cam-3", "camera-v3.wasm"},
	}
	m := e.load(t, wArtifact(cycle[0].id, cycle[0].file, "1"))
	f := e.mount(t, m, "cam")
	e.bind(t, "cam", "cam", m, f, wArtifact(cycle[1].id, cycle[1].file, "2"))
	for i := 1; i < 6; i++ {
		c := cycle[i%len(cycle)]
		prev := cycle[(i-1)%len(cycle)]
		e.replaceOK(t, "cam", wArtifact(c.id, c.file, "2"))
		if e.ld.Usage().InUse(prev.id) || e.ld.Has(prev.id) {
			t.Fatalf("cycle %d: previous module %s leaked", i, prev.id)
		}
		if len(e.ld.Snapshot()) != 1 {
			t.Fatalf("cycle %d: module registry has %d entries", i, len(e.ld.Snapshot()))
		}
	}
	b := e.binding(t, "cam")
	if !e.ld.Usage().InUse(b.Module.ID) {
		t.Fatal("final usage missing")
	}
	// Six activations total; five ended, exactly one (the current) stays alive.
	totalCreated := e.obs.createdN(cycle[0].id) + e.obs.createdN(cycle[1].id) + e.obs.createdN(cycle[2].id)
	totalDestroyed := e.obs.destroyedN(cycle[0].id) + e.obs.destroyedN(cycle[1].id) + e.obs.destroyedN(cycle[2].id)
	if totalCreated != 6 || totalDestroyed != 5 {
		t.Fatalf("created/destroyed = %d/%d, want 6/5 (no instance leak)", totalCreated, totalDestroyed)
	}
	if e.obs.destroyedN(b.Module.ID) >= e.obs.createdN(b.Module.ID) {
		t.Fatal("current instance destroyed while active")
	}
}

// E2E-WH-18 — Module/Fiber orthogonality: usage blocks Unload; Fiber Gone does
// not unload the Module by itself.
func TestE2EWH18ModuleFiberOrthogonality(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	// While the bound fiber is Active, Unload is blocked by HMR usage.
	if err := e.ld.Unload(ctxT(t), "cam-v1"); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload(active) = %v, want ErrModuleInUse", err)
	}
	// A raw fiber disposal (not via HMR) reaches Gone but the Module stays
	// loaded and usage stays held by the HMR binding.
	f := e.binding(t, "cam").Fiber
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if !e.ld.Has("cam-v1") || !e.ld.Usage().InUse("cam-v1") {
		t.Fatal("module lifecycle followed fiber lifecycle (orthogonality broken)")
	}
	// Only explicit HMR Unregister releases usage; then Unload succeeds.
	if err := e.h.Unregister("cam"); err != nil {
		t.Fatal(err)
	}
	if e.ld.Usage().InUse("cam-v1") {
		t.Fatal("usage not released by Unregister")
	}
	if err := e.ld.Unload(ctxT(t), "cam-v1"); err != nil {
		t.Fatalf("Unload after release: %v", err)
	}
}

// E2E-WH-19 — Watch isolation: only an explicit test connector turns a Watch
// change into an HMR Replace; Watch never auto-calls HMR.
func TestE2EWH19WatchIsolation(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	dir := t.TempDir()
	path := filepath.Join(dir, "version.txt")
	if err := os.WriteFile(path, []byte("1"), 0o644); err != nil {
		t.Fatal(err)
	}
	fw := watch.NewFileWatcher()
	defer fw.Close()
	sub, err := fw.Watch(ctxT(t), watch.Source{ID: "ver", Kind: "file", URI: "file://" + path})
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("watch unsupported")
		}
		t.Fatal(err)
	}

	// The connector is test-owned: Watch change -> explicit HMR.Replace.
	connectorDone := make(chan struct{})
	go func() {
		defer close(connectorDone)
		for c := range sub.Changes() {
			if !c.Current.Exists {
				continue
			}
			data, rerr := os.ReadFile(path)
			if rerr != nil {
				continue
			}
			if strings.TrimSpace(string(data)) == "2" {
				_ = e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("cam-v2", "camera-v2.wasm", "2")})
				return
			}
		}
	}()

	// Before the change the binding is still v1 (Watch alone triggers nothing).
	if b := e.binding(t, "cam"); b.Module.ID != "cam-v1" {
		t.Fatal("unexpected auto-replacement")
	}
	// Let the native watcher register before the write (same pattern as the
	// Watch package tests).
	time.Sleep(300 * time.Millisecond)
	if err := os.WriteFile(path, []byte("2"), 0o644); err != nil {
		t.Fatal(err)
	}
	eventually(t, 8*time.Second, "connector replaced", func() bool { return e.binding(t, "cam").Module.ID == "cam-v2" })
	<-connectorDone
}

// E2E-WH-20 — Config isolation: Config Reconcile never auto-triggers HMR, and
// HMR Replace never mutates Config Applied.
func TestE2EWH20ConfigIsolation(t *testing.T) {
	// Config side owns a component of the same logical type via its own factory.
	cfgRt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer cfgRt.Close(context.Background())
	reg := config.NewFactoryRegistry()
	if err := reg.Register("industrial.camera", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}}); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(cfgRt, reg)
	defer ctrl.CloseContext(ctxT(t))
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "cfg-cam", Type: "industrial.camera"}}}); err != nil {
		t.Fatal(err)
	}
	cfgFiber := fiberOf(t, &env{ctrl: ctrl}, "cfg-cam")
	waitActive(t, cfgFiber)

	// HMR side replaces its own camera target; Config Applied must not change.
	e, _ := e2eSetupCamera(t)
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	if got := fiberOf(t, &env{ctrl: ctrl}, "cfg-cam"); got.ID() != cfgFiber.ID() {
		t.Fatal("Config fiber changed by HMR Replace")
	}
	// Config Reconcile (same desired) does not touch the HMR binding.
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "cfg-cam", Type: "industrial.camera"}}}); err != nil {
		t.Fatal(err)
	}
	if b := e.binding(t, "cam"); b.Module.ID != "cam-v2" {
		t.Fatal("HMR binding changed by Config Reconcile")
	}
}

// ---------------------------------------------------------------------------
// P-WH-01..08 — property tests.
// ---------------------------------------------------------------------------

// P-WH-01 — Failure preservation: for every replacement failure the old fiber
// stays Active and the binding is unchanged.
func TestPWH01FailurePreservation(t *testing.T) {
	e, f1 := e2eSetupCamera(t)
	failures := []loader.Artifact{
		wArtifact("sensor", "sensor.wasm", "1"), // incompatible
		wArtifact("inv", "invalid.wasm", "2"),   // invalid
		wArtifact("me", "missing-export.wasm", "2"),
	}
	e.regBuiltin(t, "builtin://ff", &adapterFactory{build: func(config.ComponentConfig) (runtime.Component, error) {
		return nil, errors.New("x")
	}})
	failures = append(failures, builtinArtifact("ff", "industrial.camera", "builtin://ff", "2"))
	for i, a := range failures {
		if err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: a}); err == nil {
			t.Fatalf("failure #%d unexpectedly succeeded", i)
		}
		if f1.State() != runtime.StateActive {
			t.Fatalf("failure #%d: old fiber not Active (%v)", i, f1.State())
		}
		b, _ := e.h.CurrentBinding("cam")
		if b.Fiber != f1 {
			t.Fatalf("failure #%d: binding changed", i)
		}
	}
}

// P-WH-02 — Resource conservation on success and failure.
func TestPWH02ResourceConservation(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	// success
	e.replaceOK(t, "cam", wArtifact("cam-v2", "camera-v2.wasm", "2"))
	if e.ld.Has("cam-v1") || e.ld.Usage().InUse("cam-v1") {
		t.Fatal("old leaked on success")
	}
	if len(e.ld.Snapshot()) != 1 {
		t.Fatal("module registry not conserved on success")
	}
	// failure
	err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact("bad", "invalid.wasm", "3")})
	if err == nil {
		t.Fatal("expected failure")
	}
	if e.ld.Has("bad") || e.ld.Usage().InUse("bad") {
		t.Fatal("new module leaked on failure")
	}
}

// P-WH-03 — Activation identity: every replacement uses a fresh Fiber and a
// fresh WASM instance; exactly one activation survives.
func TestPWH03ActivationIdentity(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	ids := []runtime.FiberID{e.binding(t, "cam").Fiber.ID()}
	for _, step := range []struct{ id, file string }{{"cam-v2", "camera-v2.wasm"}, {"cam-v3", "camera-v3.wasm"}} {
		e.replaceOK(t, "cam", wArtifact(step.id, step.file, "2"))
		ids = append(ids, e.binding(t, "cam").Fiber.ID())
	}
	if ids[0] == ids[1] || ids[1] == ids[2] || ids[0] == ids[2] {
		t.Fatalf("fiber ids not distinct: %v", ids)
	}
	for _, m := range []string{"cam-v1", "cam-v2", "cam-v3"} {
		if e.obs.createdN(m) != 1 {
			t.Fatalf("module %s: expected exactly 1 instance", m)
		}
	}
	if e.obs.destroyedN("cam-v1") != 1 || e.obs.destroyedN("cam-v2") != 1 || e.obs.destroyedN("cam-v3") != 0 {
		t.Fatal("activation instance conservation broken")
	}
}

// P-WH-04 — Module/Fiber orthogonality property.
func TestPWH04ModuleFiberOrthogonality(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	if err := e.ld.Unload(ctxT(t), "cam-v1"); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload while bound = %v", err)
	}
	f := e.binding(t, "cam").Fiber
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
	if !e.ld.Has("cam-v1") || !e.ld.Usage().InUse("cam-v1") {
		t.Fatal("Gone fiber auto-unloaded its module")
	}
	_ = e.h.Unregister("cam")
	if e.ld.Usage().InUse("cam-v1") {
		t.Fatal("usage not released")
	}
	if err := e.ld.Unload(ctxT(t), "cam-v1"); err != nil {
		t.Fatalf("unload after release: %v", err)
	}
}

// P-WH-05 — Provider rebinding property.
func TestPWH05ProviderRebinding(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://p1", &whTagFactory{tag: "v1"})
	e.regBuiltin(t, "builtin://p2", &whTagFactory{tag: "v2"})
	m1 := e.load(t, builtinArtifact("m1", "industrial.camera", "builtin://p1", "1"))
	pf := e.mount(t, m1, "prov")
	e.bind(t, "prov", "prov", m1, pf, builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))

	consKit := &kit{seen: make(chan string, 8)}
	cf, err := e.rt.Load(consumerComp(consKit, "cons"))
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if got := <-consKit.seen; got != "v1" {
		t.Fatalf("initial tag = %q, want v1", got)
	}
	e.replaceOK(t, "prov", builtinArtifact("m2", "industrial.camera", "builtin://p2", "2"))
	eventually(t, 8*time.Second, "consumer recovered", func() bool { return cf.State() == runtime.StateActive })
	if b := e.binding(t, "prov"); b.Fiber.ID() == pf.ID() || b.Fiber.State() != runtime.StateActive {
		t.Fatal("provider did not rebind")
	}
	select {
	case tag := <-consKit.seen:
		if tag != "v2" {
			t.Fatalf("rebound provider tag = %q, want v2", tag)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("consumer never observed the new provider")
	}
}

// P-WH-06 — Same-target serialization property.
func TestPWH06SameTargetSerialization(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	const n = 20
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			id := fmt.Sprintf("s%d", i)
			_ = e.ld.RegisterBuiltin("builtin://"+id, func() config.Factory {
				return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
					return independentComp(&kit{}, cc.ID, false), nil
				}}
			})
			if err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact(id, "industrial.camera", "builtin://"+id, "1")}); err != nil {
				t.Errorf("replace %d: %v", i, err)
			}
		}(i)
	}
	wg.Wait()
	b := e.binding(t, "cam")
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("serialized binding not active")
	}
	if len(e.ld.Snapshot()) != 1 || !e.ld.Usage().InUse(b.Module.ID) {
		t.Fatal("usage/module conservation broken")
	}
}

// P-WH-07 — Stale completion isolation property.
func TestPWH07StaleCompletionIsolation(t *testing.T) {
	e, _ := e2eSetupCamera(t)
	done := make(chan struct{}, 2)
	var orderMu sync.Mutex
	order := []string{}
	run := func(id, file string) {
		defer func() { done <- struct{}{} }()
		if err := e.h.Replace(ctxT(t), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: wArtifact(id, file, "2")}); err == nil {
			orderMu.Lock()
			order = append(order, id)
			orderMu.Unlock()
		}
	}
	go run("cam-v2", "camera-v2.wasm")
	go run("cam-v3", "camera-v3.wasm")
	<-done
	<-done
	orderMu.Lock()
	last := order[len(order)-1]
	orderMu.Unlock()
	b := e.binding(t, "cam")
	if b.Module.ID != last || b.Fiber.State() != runtime.StateActive {
		t.Fatalf("stale completion won: binding=%s last=%s", b.Module.ID, last)
	}
}

// P-WH-08 — Close truthfulness property.
func TestPWH08CloseTruthfulness(t *testing.T) {
	e := newWHEnv(t)
	e.regBuiltin(t, "builtin://v0", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	m0 := e.load(t, builtinArtifact("v0", "industrial.camera", "builtin://v0", "0"))
	f0 := e.mount(t, m0, "cam")
	e.bind(t, "cam", "cam", m0, f0, builtinArtifact("pend", "industrial.camera", "builtin://pend", "1"))
	e.regBuiltin(t, "builtin://pend", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return consumerComp(&kit{seen: make(chan string, 4)}, cc.ID), nil
	}})

	rctx, cancel := context.WithCancel(context.Background())
	rdone := make(chan error, 1)
	go func() {
		rdone <- e.h.Replace(rctx, hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("pend", "industrial.camera", "builtin://pend", "1")})
	}()
	eventually(t, 8*time.Second, "in flight", func() bool { return e.ld.Usage().InUse("pend") })

	short, cancelS := context.WithTimeout(context.Background(), 200*time.Millisecond)
	defer cancelS()
	if err := e.h.CloseContext(short); err == nil {
		t.Fatal("fake Closed while in flight")
	}
	cancel()
	<-rdone
	eventually(t, 8*time.Second, "cleaned", func() bool { return !e.ld.Usage().InUse("pend") && !e.ld.Has("pend") })
	fin, cancelF := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancelF()
	if err := e.h.CloseContext(fin); err != nil {
		t.Fatalf("final close: %v", err)
	}
	if err := e.h.Replace(context.Background(), hmr.Target{ID: "cam", ComponentID: "cam", Artifact: builtinArtifact("x", "industrial.camera", "builtin://x", "1")}); !errors.Is(err, hmr.ErrHMRClosed) {
		t.Fatalf("post-close Replace = %v", err)
	}
}

var _ = config.ComponentConfig{}
