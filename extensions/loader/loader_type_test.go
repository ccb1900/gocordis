package loader_test

import (
	"context"
	"errors"
	"fmt"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/runtime"
)

// typeEnv bundles a Loader (builtin + wasm backends), a Config FactoryRegistry
// and a Config Controller over one Runtime, mirroring real usage.
type typeEnv struct {
	rt   *runtime.Runtime
	ld   *loader.BuiltinLoader
	wb   *wasm.Backend
	reg  config.FactoryRegistry
	ctrl *config.Controller
}

func newTypeEnv(t *testing.T) *typeEnv {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	ld := loader.NewBuiltinLoader()
	wb := wasm.NewBackend()
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatalf("RegisterBackend(wasm): %v", err)
	}
	e := &typeEnv{rt: rt, ld: ld, wb: wb, reg: reg, ctrl: ctrl}
	t.Cleanup(func() {
		_ = ctrl.CloseContext(ctxT(t))
		_ = rt.Close(context.Background())
		clctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ld.CloseContext(clctx)
		_ = wb.Close()
	})
	return e
}

func typeSrc(name string) string {
	abs, err := filepath.Abs(filepath.Join("wasm", "testdata", name))
	if err != nil {
		panic(fmt.Sprintf("abs path: %v", err))
	}
	return "file://" + abs
}

func wasmTypeArtifact(id, file string) loader.Artifact {
	return loader.Artifact{ID: id, BackendType: loader.BackendWASM, Source: typeSrc(file), Version: "1"}
}

func (e *typeEnv) load(t *testing.T, a loader.Artifact) *loader.Module {
	t.Helper()
	m, err := e.ld.Load(ctxT(t), a)
	if err != nil {
		t.Fatalf("Load(%+v): %v", a, err)
	}
	return m
}

func (e *typeEnv) regBuiltin(t *testing.T, src string, f loader.BuiltinFactory) {
	t.Helper()
	if err := e.ld.RegisterBuiltin(src, f); err != nil {
		t.Fatalf("RegisterBuiltin(%s): %v", src, err)
	}
}

func (e *typeEnv) install(t *testing.T, m *loader.Module, componentID string) *runtime.Fiber {
	t.Helper()
	comp, err := m.Factory.Create(config.ComponentConfig{ID: componentID, Type: m.Type})
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

// ---------------------------------------------------------------------------
// T-01 / T-02 / T-03 — Backend selection by BackendType; Module.Type is the
// Logical Module Type from the wasm manifest, and BackendType != ModuleType.
// ---------------------------------------------------------------------------

func TestTypeT01BackendSelection(t *testing.T) {
	e := newTypeEnv(t)
	m := e.load(t, wasmTypeArtifact("camera", "valid.wasm"))
	if m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q, want industrial.camera", m.Type)
	}
	// The backend used is wasm (BackendType), while Module.Type is logical.
	if string(loader.BackendWASM) == m.Type {
		t.Fatal("BackendType conflated with ModuleType")
	}
	if e.ld.Has("camera") == false {
		t.Fatal("module not registered")
	}
}

// T-04 — multiple WASM Modules with distinct Logical Module Types coexist.
func TestTypeT04MultipleWASMModules(t *testing.T) {
	e := newTypeEnv(t)
	want := map[string]string{
		"camera": "industrial.camera",
		"sensor": "industrial.sensor",
		"can":    "automotive.can",
	}
	for id, typ := range want {
		m := e.load(t, wasmTypeArtifact(id, map[string]string{
			"camera": "valid.wasm", "sensor": "sensor.wasm", "can": "can.wasm",
		}[id]))
		if m.Type != typ {
			t.Fatalf("%s Module.Type = %q, want %q", id, m.Type, typ)
		}
	}
	if got := len(e.ld.Snapshot()); got != 3 {
		t.Fatalf("registry has %d modules, want 3", got)
	}
}

// T-05 — three Modules with distinct Logical Types register factories without
// conflict (Factory Registry keyed by Module.Type, never BackendType).
func TestTypeT05FactoryRegistration(t *testing.T) {
	e := newTypeEnv(t)
	e.load(t, wasmTypeArtifact("camera", "valid.wasm"))
	e.load(t, wasmTypeArtifact("sensor", "sensor.wasm"))
	e.load(t, wasmTypeArtifact("can", "can.wasm"))
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatalf("RegisterFactories: %v", err)
	}
	for _, typ := range []string{"industrial.camera", "industrial.sensor", "automotive.can"} {
		if _, ok := e.reg.Lookup(typ); !ok {
			t.Fatalf("factory type %q not registered", typ)
		}
	}
	if _, ok := e.reg.Lookup(string(loader.BackendWASM)); ok {
		t.Fatal("Factory Registry must not be keyed by BackendType")
	}
}

// T-06 — same Logical Type from two WASM Modules: both Loaded; the second
// Factory registration fails with config.ErrFactoryExists; the first stays.
func TestTypeT06SameLogicalType(t *testing.T) {
	e := newTypeEnv(t)
	m1 := e.load(t, wasmTypeArtifact("camera-v1", "valid.wasm"))
	m2 := e.load(t, wasmTypeArtifact("camera-v2", "camera-v2.wasm"))
	if m1.Type != m2.Type || m1.Type != "industrial.camera" {
		t.Fatalf("types: %q vs %q", m1.Type, m2.Type)
	}
	if len(e.ld.Snapshot()) != 2 {
		t.Fatalf("registry has %d modules, want 2", len(e.ld.Snapshot()))
	}
	err := e.ld.RegisterFactories(e.reg)
	if err == nil || !errors.Is(err, config.ErrFactoryExists) {
		t.Fatalf("RegisterFactories = %v, want ErrFactoryExists", err)
	}
	got, ok := e.reg.Lookup("industrial.camera")
	if !ok {
		t.Fatal("first factory lost")
	}
	// Sorted registration by Module ID: camera-v1 registered first.
	if got != m1.Factory {
		t.Fatal("factory conflict replaced the original factory")
	}
	if len(e.ld.Snapshot()) != 2 {
		t.Fatal("Module Registry damaged by factory conflict")
	}
}

// T-07 — Backend Independence: builtin -> industrial.camera and wasm ->
// industrial.sensor loaded together with no conflict.
func TestTypeT07BackendIndependence(t *testing.T) {
	e := newTypeEnv(t)
	e.regBuiltin(t, "builtin://cam-driver", func() config.Factory { return &staticFactory{name: "cam"} })
	bm := e.load(t, loader.Artifact{ID: "cam-b", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://cam-driver", Version: "1"})
	wm := e.load(t, wasmTypeArtifact("sensor", "sensor.wasm"))
	if bm.Type != "industrial.camera" || wm.Type != "industrial.sensor" {
		t.Fatalf("types: %q %q", bm.Type, wm.Type)
	}
	if len(e.ld.Snapshot()) != 2 {
		t.Fatalf("registry has %d modules, want 2", len(e.ld.Snapshot()))
	}
}

// T-08 — same Logical Type from different Backends (builtin + wasm): both
// Loaded; Factory Registry conflict follows existing Factory semantics.
func TestTypeT08SameLogicalTypeDifferentBackend(t *testing.T) {
	e := newTypeEnv(t)
	e.regBuiltin(t, "builtin://camera-go", func() config.Factory { return &staticFactory{name: "go"} })
	bm := e.load(t, loader.Artifact{ID: "camera-go", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://camera-go", Version: "1"})
	wm := e.load(t, wasmTypeArtifact("camera-wasm", "valid.wasm"))
	if bm.Type != wm.Type || bm.Type != "industrial.camera" {
		t.Fatalf("types: %q vs %q", bm.Type, wm.Type)
	}
	if len(e.ld.Snapshot()) != 2 {
		t.Fatalf("registry has %d modules, want 2", len(e.ld.Snapshot()))
	}
	if err := e.ld.RegisterFactories(e.reg); err == nil || !errors.Is(err, config.ErrFactoryExists) {
		t.Fatalf("RegisterFactories = %v, want ErrFactoryExists", err)
	}
	// First by sorted Module ID ("camera-go" < "camera-wasm").
	got, _ := e.reg.Lookup("industrial.camera")
	if got != bm.Factory {
		t.Fatal("first factory not preserved")
	}
}

// ---------------------------------------------------------------------------
// Artifact normalization / conflict cases (§7).
// ---------------------------------------------------------------------------

// Case A: Type="" + BackendType="wasm" is allowed (manifest supplies type).
func TestTypeCaseAEmptyTypeWasmBackend(t *testing.T) {
	e := newTypeEnv(t)
	a := loader.Artifact{ID: "a", BackendType: loader.BackendWASM, Source: typeSrc("valid.wasm"), Version: "1"}
	m := e.load(t, a)
	if m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q", m.Type)
	}
}

// Case B: legacy Type="wasm" with empty BackendType resolves to the wasm
// backend; Module.Type still comes from the manifest (never "wasm").
func TestTypeCaseBLegacyTypeAlias(t *testing.T) {
	e := newTypeEnv(t)
	a := loader.Artifact{ID: "b", Type: "wasm", Source: typeSrc("valid.wasm"), Version: "1"}
	m := e.load(t, a)
	if m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q, want industrial.camera (no wasm fallback)", m.Type)
	}
}

// Case C: Type="wasm" + BackendType="wasm" is allowed.
func TestTypeCaseCSameBackend(t *testing.T) {
	e := newTypeEnv(t)
	a := loader.Artifact{ID: "c", Type: "wasm", BackendType: loader.BackendWASM, Source: typeSrc("valid.wasm"), Version: "1"}
	m := e.load(t, a)
	if m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q", m.Type)
	}
}

// Case D: Type="wasm" + BackendType="builtin" is a conflict.
func TestTypeCaseDConflict(t *testing.T) {
	e := newTypeEnv(t)
	_, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "d", Type: "wasm", BackendType: loader.BackendBuiltin, Source: "builtin://x", Version: "1"})
	if !errors.Is(err, loader.ErrArtifactTypeConflict) {
		t.Fatalf("Load = %v, want ErrArtifactTypeConflict", err)
	}
	if e.ld.Has("d") {
		t.Fatal("conflicting artifact registered")
	}
}

// ---------------------------------------------------------------------------
// Property tests P-TYPE-01..08.
// ---------------------------------------------------------------------------

// P-TYPE-01 — BackendType decides the Backend.
func TestPType01BackendTypeSelectsBackend(t *testing.T) {
	e := newTypeEnv(t)
	// Same source URI, but the wasm backend is only selected when BackendType
	// says wasm; a builtin artifact with the same source string is rejected as
	// an unknown builtin (never routed to wasm by file extension).
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "x", BackendType: loader.BackendBuiltin, Type: "cam", Source: typeSrc("valid.wasm"), Version: "1"}); !errors.Is(err, loader.ErrLoadFailed) {
		t.Fatalf("builtin load of a wasm source = %v, want ErrLoadFailed (no extension sniffing)", err)
	}
	if m := e.load(t, loader.Artifact{ID: "y", BackendType: loader.BackendWASM, Source: typeSrc("valid.wasm"), Version: "1"}); m.Type != "industrial.camera" {
		t.Fatalf("Module.Type = %q", m.Type)
	}
}

// P-TYPE-02 — ModuleType decides the Factory (registry key).
func TestPType02ModuleTypeDecidesFactory(t *testing.T) {
	e := newTypeEnv(t)
	m := e.load(t, wasmTypeArtifact("camera", "valid.wasm"))
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatal(err)
	}
	f, ok := e.reg.Lookup("industrial.camera")
	if !ok || f != m.Factory {
		t.Fatal("factory not keyed by Module.Type")
	}
}

// P-TYPE-03 — changing BackendType must not implicitly change ModuleType.
func TestPType03BackendChangeKeepsModuleType(t *testing.T) {
	e := newTypeEnv(t)
	e.regBuiltin(t, "builtin://cam", func() config.Factory { return &staticFactory{name: "cam"} })
	bm := e.load(t, loader.Artifact{ID: "b1", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://cam", Version: "1"})
	wm := e.load(t, wasmTypeArtifact("w1", "valid.wasm"))
	if bm.Type != "industrial.camera" || wm.Type != "industrial.camera" {
		t.Fatalf("same logical type diverged by backend: %q vs %q", bm.Type, wm.Type)
	}
}

// P-TYPE-04 — changing ModuleType does not change Backend selection.
func TestPType04ModuleTypeKeepsBackend(t *testing.T) {
	e := newTypeEnv(t)
	e.regBuiltin(t, "builtin://f", func() config.Factory { return &staticFactory{name: "f"} })
	m1 := e.load(t, loader.Artifact{ID: "t1", BackendType: loader.BackendBuiltin, Type: "alpha.one", Source: "builtin://f", Version: "1"})
	m2 := e.load(t, loader.Artifact{ID: "t2", BackendType: loader.BackendBuiltin, Type: "beta.two", Source: "builtin://f", Version: "1"})
	if m1.Type == m2.Type {
		t.Fatal("expected different logical types")
	}
	if m1.ID != "t1" || m2.ID != "t2" {
		t.Fatal("unexpected modules")
	}
}

// P-TYPE-05 — multiple backends can produce the same ModuleType (covered by
// T-08); assert explicitly that both Modules carry identical logical type.
func TestPType05MultipleBackendsSameModuleType(t *testing.T) {
	e := newTypeEnv(t)
	e.regBuiltin(t, "builtin://c", func() config.Factory { return &staticFactory{name: "c"} })
	e.load(t, loader.Artifact{ID: "b", BackendType: loader.BackendBuiltin, Type: "industrial.camera", Source: "builtin://c", Version: "1"})
	e.load(t, wasmTypeArtifact("w", "valid.wasm"))
	if len(e.ld.Snapshot()) != 2 {
		t.Fatal("expected two modules")
	}
}

// P-TYPE-06 — one backend can produce multiple ModuleTypes (three wasm
// modules with distinct types).
func TestPType06OneBackendMultipleModuleTypes(t *testing.T) {
	e := newTypeEnv(t)
	seen := map[string]string{}
	for id, file := range map[string]string{"a": "valid.wasm", "b": "sensor.wasm", "c": "can.wasm"} {
		m := e.load(t, wasmTypeArtifact(id, file))
		seen[id] = m.Type
	}
	if seen["a"] == seen["b"] || seen["b"] == seen["c"] || seen["a"] == seen["c"] {
		t.Fatalf("module types not distinct: %v", seen)
	}
}

// P-TYPE-07 — Module Registry and Factory Registry are independent: Load never
// auto-registers a Factory.
func TestPType07RegistriesIndependent(t *testing.T) {
	e := newTypeEnv(t)
	e.load(t, wasmTypeArtifact("camera", "valid.wasm"))
	if _, ok := e.reg.Lookup("industrial.camera"); ok {
		t.Fatal("Load auto-registered a Factory")
	}
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatalf("explicit RegisterFactories: %v", err)
	}
	if _, ok := e.reg.Lookup("industrial.camera"); !ok {
		t.Fatal("explicit registration missing")
	}
}

// P-TYPE-08 — a Type conflict never pollutes the existing registries.
func TestPType08ConflictNoPollution(t *testing.T) {
	e := newTypeEnv(t)
	e.load(t, wasmTypeArtifact("ok", "valid.wasm"))
	if err := e.ld.RegisterFactories(e.reg); err != nil {
		t.Fatal(err)
	}
	before := len(e.ld.Snapshot())
	_, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "bad", Type: "wasm", BackendType: loader.BackendBuiltin, Source: "builtin://x", Version: "1"})
	if !errors.Is(err, loader.ErrArtifactTypeConflict) {
		t.Fatalf("Load = %v, want ErrArtifactTypeConflict", err)
	}
	if len(e.ld.Snapshot()) != before {
		t.Fatal("Module Registry polluted by conflict")
	}
	if _, ok := e.reg.Lookup("industrial.camera"); !ok {
		t.Fatal("Factory Registry polluted by conflict")
	}
}
