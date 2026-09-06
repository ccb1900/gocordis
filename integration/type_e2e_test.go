package integration

import (
	"context"
	"errors"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/runtime"
)

// E2E-TYPE-01 — WASM Backend -> industrial.camera.
func TestE2EType01WASMToLogicalCamera(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "type-01", "valid.wasm")
	if m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q, want industrial.camera", m.Type)
	}
	if string(loader.BackendWASM) == m.Type {
		t.Fatal("BackendType == ModuleType (conflation)")
	}
}

// E2E-TYPE-02 — WASM Backend -> industrial.sensor.
func TestE2EType02WASMToLogicalSensor(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "type-02", "sensor.wasm")
	if m.Type != "industrial.sensor" {
		t.Fatalf("Module.Type = %q, want industrial.sensor", m.Type)
	}
}

// E2E-TYPE-03 — Builtin Backend -> industrial.camera.
func TestE2EType03BuiltinToLogicalCamera(t *testing.T) {
	e := newWASMEnv(t)
	if err := e.ld.RegisterBuiltin("builtin://cam-go", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return independentComp(&kit{}, cc.ID, false), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	m, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "type-03", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://cam-go", Version: "1"})
	if err != nil {
		t.Fatalf("builtin Load: %v", err)
	}
	if m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q, want industrial.camera", m.Type)
	}
}

// E2E-TYPE-04 — WASM + Builtin with the same logical type coexist.
func TestE2EType04SameLogicalTypeAcrossBackends(t *testing.T) {
	e := newWASMEnv(t)
	if err := e.ld.RegisterBuiltin("builtin://cam-go", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return independentComp(&kit{}, cc.ID, false), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	bm, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "type-04b", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://cam-go", Version: "1"})
	if err != nil {
		t.Fatal(err)
	}
	wm := e.loadWASM(t, "type-04w", "valid.wasm")
	if bm.Type != "industrial.camera" || wm.Type != "industrial.camera" {
		t.Fatalf("types: %q vs %q", bm.Type, wm.Type)
	}
	if len(e.ld.Snapshot()) != 2 {
		t.Fatalf("registry has %d modules, want 2", len(e.ld.Snapshot()))
	}
}

// E2E-TYPE-05 — three WASM logical types concurrently drive three Fibers.
func TestE2EType05ThreeWASMLogicalTypesConcurrent(t *testing.T) {
	e := newWASMEnv(t)
	mods := []struct {
		id, file, typ, comp string
	}{
		{"type-05a", "valid.wasm", "industrial.camera", "cam"},
		{"type-05b", "sensor.wasm", "industrial.sensor", "sen"},
		{"type-05c", "can.wasm", "automotive.can", "can"},
	}
	for _, m := range mods {
		mod := e.loadWASM(t, m.id, m.file)
		if mod.Type != m.typ {
			t.Fatalf("%s type = %q", m.id, mod.Type)
		}
	}
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatalf("RegisterFactories: %v", err)
	}
	desired := config.Config{Components: []config.ComponentConfig{
		{ID: "cam", Type: "industrial.camera"},
		{ID: "sen", Type: "industrial.sensor"},
		{ID: "can", Type: "automotive.can"},
	}}
	if err := e.ctrl.Reconcile(ctxT(t), desired); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	if got := len(e.ctrl.Owned()); got != 3 {
		t.Fatalf("owned components = %d, want 3", got)
	}
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		all := true
		for _, o := range e.ctrl.Owned() {
			if o.Fiber.State() != runtime.StateActive {
				all = false
			}
		}
		if all {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatal("not all three fibers reached Active")
}

// E2E-TYPE-06 — Logical Module Type -> Config Factory (ComponentConfig.Type).
func TestE2EType06LogicalTypeToConfigFactory(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "type-06", "valid.wasm")
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatal(err)
	}
	if _, ok := e.reg.Lookup("industrial.camera"); !ok {
		t.Fatal("factory not registered under the logical type")
	}
	if _, ok := e.reg.Lookup(string(loader.BackendWASM)); ok {
		t.Fatal("factory keyed by BackendType")
	}
	// ComponentConfig.Type is the logical type; Config never sees the backend.
	cc := config.ComponentConfig{ID: "cam", Type: "industrial.camera"}
	comp, err := m.Factory.Create(cc)
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if comp == nil {
		t.Fatal("nil component")
	}
}

// E2E-TYPE-07 — Logical type -> Component -> Fiber (real Runtime, Active).
func TestE2EType07LogicalTypeToFiber(t *testing.T) {
	e := newWASMEnv(t)
	m := e.loadWASM(t, "type-07", "valid.wasm")
	f := e.deployViaConfig(t, m, "type-07-c")
	waitActive(t, f)
	if f.State() != runtime.StateActive {
		t.Fatalf("fiber state = %v, want Active", f.State())
	}
	if f.Component().Name() != "wasm:type-07-c" {
		t.Fatalf("component name = %q", f.Component().Name())
	}
}

// E2E-TYPE-08 — Factory conflict isolation: same logical type from two WASM
// modules; the second registration fails, Module Registry and the first
// factory stay intact, and the first module still mounts a Fiber.
func TestE2EType08FactoryConflictIsolation(t *testing.T) {
	e := newWASMEnv(t)
	m1 := e.loadWASM(t, "type-08-v1", "valid.wasm")
	m2 := e.loadWASM(t, "type-08-v2", "camera-v2.wasm")
	if m1.Type != m2.Type || m1.Type != "industrial.camera" {
		t.Fatalf("types: %q vs %q", m1.Type, m2.Type)
	}
	err := e.ld.RegisterFactories(e.reg)
	if err == nil || !errors.Is(err, config.ErrFactoryExists) {
		t.Fatalf("RegisterFactories = %v, want ErrFactoryExists", err)
	}
	if len(e.ld.Snapshot()) != 2 {
		t.Fatal("Module Registry damaged")
	}
	got, _ := e.reg.Lookup("industrial.camera")
	if got != m1.Factory {
		t.Fatal("original factory not preserved")
	}
	// The surviving factory still drives a real fiber.
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "cam", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f, err := e.rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("fiber not active: %v", err)
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// E2E-TYPE-09 — HMR regression: replacing a builtin module with a WASM module
// of the SAME Logical Module Type is governed by HMR (replacement policy), not
// by a Type resolver; the backend change is transparent.
func TestE2EType09HMRCrossBackendSameLogicalType(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	ld := loader.NewBuiltinLoader()
	wb := wasm.NewBackend()
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatalf("RegisterBackend(wasm): %v", err)
	}
	defer wb.Close()
	h := hmr.New(rt, ld, ld.Usage())
	defer func() { _ = h.CloseContext(ctxT(t)); _ = ld.CloseContext(ctxT(t)) }()

	if err := ld.RegisterBuiltin("builtin://cam-v1", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return independentComp(&kit{}, cc.ID, false), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	m1, err := ld.Load(ctxT(t), loader.Artifact{ID: "cam-v1", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://cam-v1", Version: "1"})
	if err != nil {
		t.Fatal(err)
	}
	comp1, err := m1.Factory.Create(config.ComponentConfig{ID: "cam", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f1, err := rt.Load(comp1)
	if err != nil {
		t.Fatal(err)
	}
	if err := f1.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}

	target := hmr.Target{ID: "t", ComponentID: "cam", Artifact: loader.Artifact{ID: "cam-wasm-v2", BackendType: loader.BackendWASM, Source: wasmSrc(t, "valid.wasm"), Version: "2"}}
	if err := h.Register(target); err != nil {
		t.Fatalf("HMR Register: %v", err)
	}
	if err := h.Bind(target.ID, *m1, f1); err != nil {
		t.Fatalf("HMR Bind: %v", err)
	}
	if err := h.Replace(ctxT(t), target); err != nil {
		t.Fatalf("HMR Replace: %v", err)
	}
	b, ok := h.CurrentBinding("t")
	if !ok {
		t.Fatal("no binding")
	}
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("fiber identity unchanged")
	}
	if b.Fiber.State() != runtime.StateActive {
		t.Fatal("new fiber not Active")
	}
	if b.Fiber.Component().Name() != "wasm:cam" {
		t.Fatalf("new component name = %q, want wasm-backed", b.Fiber.Component().Name())
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %v, want Gone", f1.State())
	}
}

// E2E-TYPE-10 — Full existing integration regression: builtin + wasm modules of
// distinct logical types coexist through the whole chain (Loader -> Factory ->
// Component -> Fiber) and the runtime closes cleanly.
func TestE2EType10FullRegression(t *testing.T) {
	e := newWASMEnv(t)
	// builtin logical type
	if err := e.ld.RegisterBuiltin("builtin://metric", func() config.Factory {
		return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return independentComp(&kit{}, cc.ID, false), nil
		}}
	}); err != nil {
		t.Fatal(err)
	}
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "metric-go", BackendType: loader.BackendBuiltin, Type: "telemetry.metric", Source: "builtin://metric", Version: "1"}); err != nil {
		t.Fatal(err)
	}
	// wasm logical types
	e.loadWASM(t, "camera", "valid.wasm")
	e.loadWASM(t, "sensor", "sensor.wasm")

	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatalf("RegisterFactories: %v", err)
	}
	desired := config.Config{Components: []config.ComponentConfig{
		{ID: "metric", Type: "telemetry.metric"},
		{ID: "camera", Type: "industrial.camera"},
		{ID: "sensor", Type: "industrial.sensor"},
	}}
	if err := e.ctrl.Reconcile(ctxT(t), desired); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		all := true
		for _, o := range e.ctrl.Owned() {
			if o.Fiber.State() != runtime.StateActive {
				all = false
			}
		}
		if all && len(e.ctrl.Owned()) == 3 {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("not all fibers active: %d owned", len(e.ctrl.Owned()))
}
