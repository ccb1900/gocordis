package pluginmeta_test

import (
	"strings"
	"testing"

	"dynamic-runtime/extensions/pluginmeta"
)

const checksum = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"

const validJSON = `{
  "id": "camera",
  "name": "Camera Plugin",
  "version": "1.2.3",
  "description": "frame source",
  "author": "alice",
  "license": "MIT",
  "repository": "https://example.com/camera",
  "backend": "native",
  "module_type": "industrial.camera",
  "requires": [{"key": "fs", "optional": false}, {"key": "logger", "optional": true}],
  "provides": [{"key": "camera.frame", "description": "frame stream"}],
  "config": {"batch": 8},
  "compatibility": {"runtime": ">=1.2.0", "api": "<2.0.0"},
  "source": {"kind": "builtin", "location": "builtin://camera", "checksum": "` + checksum + `"}
}`

const validTOML = `id = "camera"
name = "Camera Plugin"
version = "1.2.3"
description = "frame source"
author = "alice"
license = "MIT"
repository = "https://example.com/camera"
backend = "wasm"
module_type = "industrial.camera"

[[requires]]
key = "fs"
optional = false

[[requires]]
key = "logger"
optional = true

[[provides]]
key = "camera.frame"
description = "frame stream"

[config]
batch = 8

[compatibility]
runtime = ">=1.2.0"
api = "<2.0.0"

[source]
kind = "wasm"
location = "https://example.com/camera.wasm"
checksum = "` + checksum + `"
`

func assertParsedManifest(t *testing.T, m pluginmeta.Manifest, backend pluginmeta.Backend) {
	t.Helper()
	if m.ID != "camera" || m.Name != "Camera Plugin" || m.Version != "1.2.3" {
		t.Errorf("identity fields not parsed: %+v", m)
	}
	if m.Description != "frame source" || m.Author != "alice" || m.License != "MIT" ||
		m.Repository != "https://example.com/camera" {
		t.Errorf("descriptive fields not parsed: %+v", m)
	}
	if m.Backend != backend || m.ModuleType != "industrial.camera" {
		t.Errorf("backend/module fields not parsed: %+v", m)
	}
	if len(m.Requires) != 2 || m.Requires[0].Key != "fs" || m.Requires[0].Optional ||
		m.Requires[1].Key != "logger" || !m.Requires[1].Optional {
		t.Errorf("requires not parsed: %+v", m.Requires)
	}
	if len(m.Provides) != 1 || m.Provides[0].Key != "camera.frame" ||
		m.Provides[0].Description != "frame stream" {
		t.Errorf("provides not parsed: %+v", m.Provides)
	}
	if _, ok := m.Config["batch"]; !ok {
		t.Errorf("config not parsed: %+v", m.Config)
	}
	if m.Compatibility.Runtime != ">=1.2.0" || m.Compatibility.API != "<2.0.0" {
		t.Errorf("compatibility not parsed: %+v", m.Compatibility)
	}
	if m.Source.Checksum != checksum || m.Source.Kind == "" || m.Source.Location == "" {
		t.Errorf("source not parsed: %+v", m.Source)
	}
	if err := pluginmeta.Validate(m); err != nil {
		t.Errorf("parsed manifest failed validation: %v", err)
	}
}

func TestParseJSON(t *testing.T) {
	m, err := pluginmeta.Parse([]byte(validJSON), "json")
	if err != nil {
		t.Fatalf("Parse(json) failed: %v", err)
	}
	assertParsedManifest(t, m, pluginmeta.BackendNative)
}

func TestParseTOML(t *testing.T) {
	m, err := pluginmeta.Parse([]byte(validTOML), "toml")
	if err != nil {
		t.Fatalf("Parse(toml) failed: %v", err)
	}
	assertParsedManifest(t, m, pluginmeta.BackendWASM)
}

func TestParseStrictUnknownFields(t *testing.T) {
	tests := []struct {
		name   string
		format string
		data   string
	}{
		{"json top-level", "json", strings.Replace(validJSON, `"id": "camera",`, `"id": "camera", "bogus": 1,`, 1)},
		{"json nested requires", "json", strings.Replace(validJSON, `{"key": "fs", "optional": false}`, `{"key": "fs", "bogus": true}`, 1)},
		{"json nested compatibility", "json", strings.Replace(validJSON, `"runtime": ">=1.2.0"`, `"runtime": ">=1.2.0", "bogus": "x"`, 1)},
		{"toml top-level", "toml", validTOML + "\nbogus = 1\n"},
		{"toml nested source", "toml", strings.Replace(validTOML, `kind = "wasm"`, `kind = "wasm"\nbogus = 1`, 1)},
	}
	for _, tt := range tests {
		t.Run(tt.name, func(t *testing.T) {
			if _, err := pluginmeta.Parse([]byte(tt.data), tt.format); err == nil {
				t.Fatalf("Parse(%s) accepted an unknown field", tt.format)
			}
		})
	}
}

func TestParseErrors(t *testing.T) {
	if _, err := pluginmeta.Parse([]byte(validJSON), "yaml"); err == nil {
		t.Error("Parse accepted an unsupported format")
	}
	if _, err := pluginmeta.Parse([]byte(`{"id":`), "json"); err == nil {
		t.Error("Parse accepted malformed JSON")
	}
	if _, err := pluginmeta.Parse([]byte(`id = "unterminated`), "toml"); err == nil {
		t.Error("Parse accepted malformed TOML")
	}
}
