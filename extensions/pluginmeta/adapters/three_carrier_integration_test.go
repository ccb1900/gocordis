package adapters_test

// Three-carrier integration coverage (audit P1-6).
//
// This file drives all three pluginmeta carrier ecosystems through their REAL
// discovery/loading/activation paths. Every comment marked REAL is executed by
// the Runtime / Loader / Config / proc runtime; STRUCTURAL means descriptor or
// identity mapping only. STRUCTURAL assertions never claim a fiber activated.
//
//  - native: REAL adapter mapping + REAL config.FactoryRegistry + REAL
//    Controller.Reconcile -> Active fiber + provider in the snapshot.
//  - wasm:   REAL adapter mapping + REAL WASM bytes (valid.wasm) through the
//    REAL wasm.Backend -> Module identity -> Factory -> Reconcile -> Active
//    fiber (the demo wasm component exposes no capability, so 0 providers).
//  - proc:   REAL procplugin.DiscoverPlugins output + REAL adapter mapping +
//    REAL proc.Backend load (Module.Type == "proc") + REAL process activation
//    via the test-binary helper. The EXACT BOUNDARY asserted is that the proc
//    backend's Module.Type is the BackendType "proc", NOT the descriptor's
//    logical ModuleType; pluginmeta.Manifest itself cannot load anything.

import (
	"context"
	"encoding/json"
	"os"
	"path/filepath"
	"testing"
	"time"

	toml "github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/procplugin"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/proc"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/extensions/pluginmeta/adapters"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// shared harness
// ---------------------------------------------------------------------------

func threeCarrierCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newThreeCarrierEnv(t *testing.T) (*runtime.Runtime, config.FactoryRegistry, *config.Controller) {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	t.Cleanup(func() {
		closeCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(closeCtx)
		_ = rt.Close(closeCtx)
	})
	return rt, reg, ctrl
}

func requireCarrierActive(t *testing.T, ctrl *config.Controller, id string, ctx context.Context) *runtime.Fiber {
	t.Helper()
	for _, o := range ctrl.Owned() {
		if o.ID == id && o.Fiber != nil {
			if err := o.Fiber.Ready(ctx); err != nil {
				t.Fatalf("fiber %q not ready: %v", id, err)
			}
			if o.Fiber.State() != runtime.StateActive {
				t.Fatalf("fiber %q state = %v, want Active", id, o.Fiber.State())
			}
			return o.Fiber
		}
	}
	t.Fatalf("component %q not owned", id)
	return nil
}

// assertCarrierSnapshot asserts the REAL snapshot fact anchor after activation.
// wantProviderKey == "" means the module exposes no capability (the wasm demo).
func assertCarrierSnapshot(t *testing.T, rt *runtime.Runtime, ctx context.Context, wantFiberName, wantProviderKey string) {
	t.Helper()
	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Fibers) != 1 {
		t.Fatalf("snapshot fibers = %d, want 1: %+v", len(snap.Fibers), snap.Fibers)
	}
	if snap.Fibers[0].Name != wantFiberName {
		t.Fatalf("snapshot fiber name = %q, want %q", snap.Fibers[0].Name, wantFiberName)
	}
	if snap.Fibers[0].State != runtime.StateActive {
		t.Fatalf("snapshot fiber state = %v, want Active", snap.Fibers[0].State)
	}
	if wantProviderKey == "" {
		if len(snap.Providers) != 0 {
			t.Fatalf("snapshot providers = %d, want 0: %+v", len(snap.Providers), snap.Providers)
		}
		return
	}
	found := false
	for _, p := range snap.Providers {
		if p.Key == wantProviderKey && p.State == runtime.StateActive {
			found = true
		}
	}
	if !found {
		t.Fatalf("snapshot missing Active provider %q: %+v", wantProviderKey, snap.Providers)
	}
}

// ---------------------------------------------------------------------------
// native carrier component (REAL runtime.Component)
// ---------------------------------------------------------------------------

type nativeCarrierComponent struct {
	key runtime.Key[string]
}

func (c *nativeCarrierComponent) Name() string                 { return "three-carrier-native" }
func (c *nativeCarrierComponent) Inject() []runtime.Dependency { return nil }
func (c *nativeCarrierComponent) Provide() []runtime.Capability {
	return []runtime.Capability{c.key.Capability()}
}
func (c *nativeCarrierComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, c.key, "native-ready")
}

// ---------------------------------------------------------------------------
// proc carrier contract + real helper process (the test binary itself)
// ---------------------------------------------------------------------------

const threeCarrierProcHelperEnv = "GORIDIS_THREE_CARRIER_PROC_HELPER"

// TestThreeCarrierProcPluginHelper is the plugin process for the proc subtest:
// the test binary is re-invoked with -test.run=this and the env gate set. This
// is REAL cross-process execution via proc.Serve (not a model).
func TestThreeCarrierProcPluginHelper(t *testing.T) {
	if os.Getenv(threeCarrierProcHelperEnv) == "" {
		return
	}
	methods := map[string]proc.Handler{
		"echo": func(_ context.Context, params json.RawMessage) (any, error) {
			var in struct {
				Msg string `json:"msg"`
			}
			if err := json.Unmarshal(params, &in); err != nil {
				return nil, err
			}
			return map[string]string{"out": "echo:" + in.Msg}, nil
		},
	}
	if err := proc.Serve(methods); err != nil {
		os.Exit(1)
	}
	os.Exit(0)
}

type threeCarrierCodec interface {
	Echo(msg string) (string, error)
}

var threeCarrierCodecKey = runtime.NewKey[threeCarrierCodec]("pluginmeta.adapters.three.carrier.codec")

func bindThreeCarrierCodec(call proc.Caller) threeCarrierCodec {
	return &threeCarrierRemoteCodec{call: call}
}

type threeCarrierRemoteCodec struct{ call proc.Caller }

func (r *threeCarrierRemoteCodec) Echo(msg string) (string, error) {
	var out struct {
		Out string `json:"out"`
	}
	if err := r.call.Call(context.Background(), "echo", map[string]string{"msg": msg}, &out); err != nil {
		return "", err
	}
	return out.Out, nil
}

// ---------------------------------------------------------------------------
// the three-carrier test
// ---------------------------------------------------------------------------

func TestThreeCarrierDiscoveryLoadingActivation(t *testing.T) {
	ctx := threeCarrierCtx(t)

	// =====================================================================
	// CARRIER 1 — NATIVE
	// =====================================================================
	t.Run("native", func(t *testing.T) {
		// REAL adapter mapping (struct + validation + descriptive keys).
		m := adapters.FromNative("native-cam", "industrial.camera", "1.2.3",
			map[string]any{"batch": 8}, nil, []string{"camera.frame"})
		if m.Backend != pluginmeta.BackendNative {
			t.Fatalf("backend = %q, want native", m.Backend)
		}
		if m.ModuleType != "industrial.camera" || m.ID != "native-cam" {
			t.Fatalf("descriptor identity = %+v", m)
		}
		if err := pluginmeta.Validate(m); err != nil {
			t.Fatalf("adapter output invalid: %v", err)
		}
		_, provides := m.CapabilityKeys()
		if len(provides) != 1 || provides[0] != "camera.frame" {
			t.Fatalf("descriptive provides = %v", provides)
		}

		// REAL activation: factory registered by logical ModuleType, reconciled.
		rt, reg, ctrl := newThreeCarrierEnv(t)
		key := runtime.NewKey[string]("pluginmeta.adapters.native.cam")
		if err := reg.Register(m.ModuleType, config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
			if cc.Type != m.ModuleType || cc.ID != m.ID {
				t.Errorf("factory saw type/id = %q/%q, want %q/%q", cc.Type, cc.ID, m.ModuleType, m.ID)
			}
			return &nativeCarrierComponent{key: key}, nil
		})); err != nil {
			t.Fatal(err)
		}
		if err := ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{{
			ID: m.ID, Type: m.ModuleType, Config: m.Config,
		}}}); err != nil {
			t.Fatalf("native Reconcile: %v", err)
		}
		requireCarrierActive(t, ctrl, m.ID, ctx)
		assertCarrierSnapshot(t, rt, ctx, "three-carrier-native", key.Capability().String())
	})

	// =====================================================================
	// CARRIER 2 — WASM (real wasm bytes, real backend, real activation)
	// =====================================================================
	t.Run("wasm", func(t *testing.T) {
		// REAL adapter mapping. moduleType is the logical module type declared
		// by valid.wasm itself; it is NOT copied from Artifact.Type.
		m := adapters.FromWASM("wasm-cam", "2.0.1", "industrial.camera",
			[]string{"fs"}, []string{"camera.frame", "camera.stats"})
		if m.Backend != pluginmeta.BackendWASM || m.ModuleType != "industrial.camera" {
			t.Fatalf("descriptor identity = %+v", m)
		}
		if err := pluginmeta.Validate(m); err != nil {
			t.Fatalf("adapter output invalid: %v", err)
		}

		rt, reg, ctrl := newThreeCarrierEnv(t)
		ld := loader.NewBuiltinLoader()
		wb := wasm.NewBackend()
		if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() {
			closeCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			_ = ld.CloseContext(closeCtx)
			_ = wb.Close()
		})

		// REAL artifact/module identity construction from the descriptor's ID
		// and carrier backend, backed by REAL WASM bytes from testdata.
		wasmPath, err := filepath.Abs(filepath.Join("..", "..", "loader", "wasm", "testdata", "valid.wasm"))
		if err != nil {
			t.Fatal(err)
		}
		if _, err := os.Stat(wasmPath); err != nil {
			t.Fatalf("real wasm bytes unavailable: %v", err)
		}
		mod, err := ld.Load(ctx, loader.Artifact{
			ID: m.ID, BackendType: loader.BackendWASM,
			Source: "file://" + wasmPath, Version: m.Version,
		})
		if err != nil {
			t.Fatalf("real WASM Load: %v", err)
		}
		// REAL: the module's own manifest declares this logical type; assert it
		// equals the adapter's descriptive ModuleType (separate from BackendType).
		if mod.Type != m.ModuleType {
			t.Fatalf("module type = %q, want adapter ModuleType %q", mod.Type, m.ModuleType)
		}
		if mod.ID != m.ID {
			t.Fatalf("module ID = %q, want %q", mod.ID, m.ID)
		}

		// REAL activation through Loader -> Factory -> Config -> Runtime.
		if err := ld.RegisterFactories(reg); err != nil {
			t.Fatalf("RegisterFactories: %v", err)
		}
		if err := ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{{
			ID: m.ID, Type: mod.Type,
		}}}); err != nil {
			t.Fatalf("wasm Reconcile: %v", err)
		}
		requireCarrierActive(t, ctrl, m.ID, ctx)
		// The demo wasm component exposes no capability (Provide() == nil):
		// 1 Active fiber, 0 providers. No fake provider is asserted.
		assertCarrierSnapshot(t, rt, ctx, "wasm:"+m.ID, "")
	})

	// =====================================================================
	// CARRIER 3 — PROC (real discovery + real load + real process activation)
	// =====================================================================
	t.Run("proc", func(t *testing.T) {
		// REAL discovery output: write a plugin dir, run procplugin.DiscoverPlugins.
		dir := t.TempDir()
		pluginDir := filepath.Join(dir, "modbus")
		if err := os.MkdirAll(pluginDir, 0o755); err != nil {
			t.Fatal(err)
		}
		manifestSrc := `name = "modbus"
title = "Modbus Gateway"
backend = "bin/modbus"
client = "ui.js"
queries = ["modbus.status"]
commands = ["modbus.write"]

[[pages]]
id = "overview"
title = "Overview"
route = "/modbus"
`
		if err := os.WriteFile(filepath.Join(pluginDir, procplugin.PluginsManifestName), []byte(manifestSrc), 0o644); err != nil {
			t.Fatal(err)
		}
		rows, err := procplugin.DiscoverPlugins(dir)
		if err != nil {
			t.Fatalf("DiscoverPlugins: %v", err)
		}
		var procRow config.ComponentConfig
		foundRow := false
		for _, r := range rows {
			if r.Type == "proc-plugin" {
				procRow, foundRow = r, true
			}
		}
		if !foundRow || procRow.ID != "modbus" {
			t.Fatalf("proc discovery row missing/mismatched: %+v", rows)
		}

		// REAL adapter mapping from the same declaration (parsed exactly as the
		// discovery path does, via go-toml into PluginManifest).
		var pm procplugin.PluginManifest
		if err := toml.Unmarshal([]byte(manifestSrc), &pm); err != nil {
			t.Fatal(err)
		}
		m := adapters.FromProc(pm)
		if m.Backend != pluginmeta.BackendProc || m.ModuleType != pm.Name || m.ID != pm.Name {
			t.Fatalf("descriptor identity = %+v", m)
		}
		if err := pluginmeta.Validate(m); err != nil {
			t.Fatalf("adapter output invalid: %v", err)
		}
		// STRUCTURAL: discovery row identity agrees with the descriptor; the
		// descriptive "query:" key is present. Neither claims activation.
		if procRow.ID != m.ID {
			t.Fatalf("discovery id %q != descriptor id %q", procRow.ID, m.ID)
		}
		_, provides := m.CapabilityKeys()
		hasQuery := false
		for _, k := range provides {
			if k == "query:modbus.status" {
				hasQuery = true
			}
		}
		if !hasQuery {
			t.Fatalf("proc provides missing query key: %v", provides)
		}

		// REAL load + activation through the proc backend.
		rt, reg, ctrl := newThreeCarrierEnv(t)
		ld := loader.NewBuiltinLoader()
		pb := proc.NewBackend(threeCarrierCodecKey, bindThreeCarrierCodec,
			proc.WithArgs("-test.run=TestThreeCarrierProcPluginHelper", "-test.timeout=60s"),
			proc.WithEnv(threeCarrierProcHelperEnv+"=1"),
			proc.WithHandshakeWait(10*time.Second),
		)
		if err := ld.RegisterBackend(loader.BackendProc, pb); err != nil {
			t.Fatal(err)
		}
		t.Cleanup(func() {
			closeCtx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
			defer cancel()
			_ = ld.CloseContext(closeCtx)
			_ = pb.Close()
		})
		exe, err := os.Executable()
		if err != nil {
			t.Fatal(err)
		}
		mod, err := ld.Load(ctx, loader.Artifact{
			ID: m.ID, BackendType: loader.BackendProc, Source: exe, Version: "v1",
		})
		if err != nil {
			t.Fatalf("real proc Load: %v", err)
		}
		// EXACT BOUNDARY: the proc backend's Module.Type is the BackendType
		// "proc", NOT the descriptor's logical ModuleType ("modbus"). The
		// descriptive manifest never becomes a runtime type by itself.
		if mod.Type != string(loader.BackendProc) || mod.Type == m.ModuleType {
			t.Fatalf("proc Module.Type = %q; want backend type %q (not logical %q)",
				mod.Type, loader.BackendProc, m.ModuleType)
		}
		if mod.ID != m.ID {
			t.Fatalf("module ID = %q, want %q", mod.ID, m.ID)
		}

		// REAL process activation: RegisterFactories keys by Module.Type ("proc").
		if err := ld.RegisterFactories(reg); err != nil {
			t.Fatalf("RegisterFactories: %v", err)
		}
		if err := ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{{
			ID: m.ID, Type: mod.Type,
		}}}); err != nil {
			t.Fatalf("proc Reconcile: %v", err)
		}
		requireCarrierActive(t, ctrl, m.ID, ctx)
		assertCarrierSnapshot(t, rt, ctx, "proc:"+m.ID, threeCarrierCodecKey.Capability().String())
	})
}
