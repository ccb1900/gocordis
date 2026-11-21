package adapters_test

import (
	"reflect"
	"strings"
	"testing"

	"dynamic-runtime/extensions/console/procplugin"
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/extensions/pluginmeta/adapters"
)

func TestFromNative(t *testing.T) {
	cfg := map[string]any{"batch": 8}
	m := adapters.FromNative("camera", "industrial.camera", "1.2.3", cfg,
		[]string{"fs", "logger"}, []string{"camera.frame"})

	if m.ID != "camera" || m.Name != "camera" || m.Version != "1.2.3" {
		t.Errorf("identity fields: %+v", m)
	}
	if m.Backend != pluginmeta.BackendNative || m.ModuleType != "industrial.camera" {
		t.Errorf("backend/module: %+v", m)
	}
	if !reflect.DeepEqual(m.Config, cfg) {
		t.Errorf("config not carried over: %+v", m.Config)
	}
	if len(m.Requires) != 2 || m.Requires[0].Key != "fs" || m.Requires[1].Key != "logger" {
		t.Errorf("requires: %+v", m.Requires)
	}
	if len(m.Provides) != 1 || m.Provides[0].Key != "camera.frame" {
		t.Errorf("provides: %+v", m.Provides)
	}
	if m.Source.Kind != "native" || m.Source.Location != "industrial.camera" {
		t.Errorf("source: %+v", m.Source)
	}
	if err := pluginmeta.Validate(m); err != nil {
		t.Errorf("adaptor output failed validation: %v", err)
	}
}

func TestFromWASM(t *testing.T) {
	m := adapters.FromWASM("camera-wasm", "2.0.1", "industrial.camera",
		[]string{"fs"}, []string{"camera.frame", "camera.stats"})

	if m.ID != "camera-wasm" || m.Version != "2.0.1" {
		t.Errorf("identity fields: %+v", m)
	}
	if m.Backend != pluginmeta.BackendWASM || m.ModuleType != "industrial.camera" {
		t.Errorf("backend/module: %+v", m)
	}
	if len(m.Requires) != 1 || m.Requires[0].Key != "fs" {
		t.Errorf("requires: %+v", m.Requires)
	}
	if len(m.Provides) != 2 || m.Provides[1].Key != "camera.stats" {
		t.Errorf("provides: %+v", m.Provides)
	}
	if m.Source.Kind != "wasm" {
		t.Errorf("source: %+v", m.Source)
	}
	if err := pluginmeta.Validate(m); err != nil {
		t.Errorf("adaptor output failed validation: %v", err)
	}
}

func TestFromProc(t *testing.T) {
	pm := procplugin.PluginManifest{
		Name:     "modbus",
		Title:    "Modbus Gateway",
		Backend:  "bin/modbus",
		Client:   "ui.js",
		Queries:  []string{"modbus.status"},
		Commands: []string{"modbus.write"},
		Pages: []procplugin.PluginPage{
			{ID: "overview", Title: "Overview", Route: "/modbus", Description: "live values", Renderer: "ui.js"},
		},
	}

	m := adapters.FromProc(pm)

	if m.ID != "modbus" || m.Name != "Modbus Gateway" {
		t.Errorf("identity fields: %+v", m)
	}
	if m.Backend != pluginmeta.BackendProc || m.ModuleType != "modbus" {
		t.Errorf("backend/module: %+v", m)
	}
	if m.Source.Kind != "proc" || m.Source.Location != "bin/modbus" {
		t.Errorf("source: %+v", m.Source)
	}
	if m.Config["backend"] != "bin/modbus" || m.Config["client"] != "ui.js" {
		t.Errorf("original proc metadata not retained: %+v", m.Config)
	}
	if _, ok := m.Config["pages"]; !ok {
		t.Errorf("pages not retained: %+v", m.Config)
	}
	if !strings.Contains(m.Description, "live values") || !strings.Contains(m.Description, "/modbus") {
		t.Errorf("page description not mapped: %q", m.Description)
	}
	wantKeys := []string{"query:modbus.status", "command:modbus.write", "page:overview"}
	_, gotKeys := m.CapabilityKeys()
	if !reflect.DeepEqual(gotKeys, wantKeys) {
		t.Errorf("provided keys = %v, want %v", gotKeys, wantKeys)
	}
	if err := pluginmeta.Validate(m); err != nil {
		t.Errorf("adaptor output failed validation: %v", err)
	}
}

func TestAdaptersProduceSameShape(t *testing.T) {
	manifests := []pluginmeta.Manifest{
		adapters.FromNative("n", "type.n", "1.0.0", nil, nil, []string{"cap.n"}),
		adapters.FromWASM("w", "1.0.0", "type.w", nil, []string{"cap.w"}),
		adapters.FromProc(procplugin.PluginManifest{Name: "p", Backend: "bin/p", Queries: []string{"cap.p"}}),
	}
	for _, m := range manifests {
		if m.ID == "" || m.Name == "" || m.Version == "" {
			t.Errorf("incomplete identity: %+v", m)
		}
		if m.Backend == "" || m.Source.Kind == "" {
			t.Errorf("missing backend metadata: %+v", m)
		}
		_, provides := m.CapabilityKeys()
		if len(provides) == 0 {
			t.Errorf("no provided capability keys: %+v", m)
		}
		if err := pluginmeta.Validate(m); err != nil {
			t.Errorf("manifest %q failed validation: %v", m.ID, err)
		}
	}
}
