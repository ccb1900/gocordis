package main

import (
	"bytes"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

func snapshotDir(t *testing.T, dir string) map[string]string {
	t.Helper()
	out := make(map[string]string)
	err := filepath.WalkDir(dir, func(path string, d os.DirEntry, err error) error {
		if err != nil {
			return err
		}
		rel, err := filepath.Rel(dir, path)
		if err != nil {
			return err
		}
		if d.IsDir() {
			out[rel] = "<dir>"
			return nil
		}
		data, err := os.ReadFile(path)
		if err != nil {
			return err
		}
		out[rel] = string(data)
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	return out
}

func TestSandboxEmptyRuntimeAndNoFileChanges(t *testing.T) {
	path := writeManifest(t, validManifest)
	dir := filepath.Dir(path)
	before := snapshotDir(t, dir)

	var out bytes.Buffer
	if code := sandbox(path, &out); code != 0 {
		t.Fatalf("sandbox exited %d: %s", code, out.String())
	}
	text := out.String()
	if !strings.Contains(text, "requires build-time registration") {
		t.Fatalf("missing build-time registration boundary diagnostic: %s", text)
	}
	if !strings.Contains(text, "fibers: 0") || !strings.Contains(text, "providers: 0") || !strings.Contains(text, "dependencies: 0") {
		t.Fatalf("empty-runtime snapshot summary missing: %s", text)
	}

	after := snapshotDir(t, dir)
	if len(before) != len(after) {
		t.Fatalf("sandbox changed the directory: before=%v after=%v", before, after)
	}
	for k, v := range before {
		if after[k] != v {
			t.Fatalf("sandbox modified %s: %q -> %q", k, v, after[k])
		}
	}
}

func TestSandboxParseFailure(t *testing.T) {
	path := writeManifest(t, "not valid = = toml")
	var out bytes.Buffer
	if code := sandbox(path, &out); code == 0 {
		t.Fatal("parse failure did not produce a non-zero exit")
	}
	if !strings.Contains(out.String(), "PARSE_ERROR") {
		t.Fatalf("parse diagnostic missing: %s", out.String())
	}
}

type sandboxComponent struct{}

func (*sandboxComponent) Name() string                 { return "sandbox-plugin" }
func (*sandboxComponent) Inject() []runtime.Dependency { return nil }
func (*sandboxComponent) Provide() []runtime.Capability {
	return []runtime.Capability{runtime.NewKey[string]("sandbox.cap").Capability()}
}
func (*sandboxComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	key := runtime.NewKey[string]("sandbox.cap")
	return nil, runtime.Provide(ctx, key, "ready")
}

func TestSandboxWithRegisteredFactory(t *testing.T) {
	path := writeManifest(t, strings.Replace(validManifest, `id = "camera"`, `id = "sandbox-plugin"`, 1))
	factory := config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &sandboxComponent{}, nil
	})
	var out bytes.Buffer
	if code := sandboxWithFactory(path, &out, factory); code != 0 {
		t.Fatalf("sandbox with factory exited %d: %s", code, out.String())
	}
	text := out.String()
	if !strings.Contains(text, "fibers: 1") || !strings.Contains(text, "providers: 1") {
		t.Fatalf("factory-loaded component missing from snapshot: %s", text)
	}
	if strings.Contains(text, "requires build-time registration") {
		t.Fatalf("boundary diagnostic should not appear when a factory is registered: %s", text)
	}
}

func TestSandboxCommandDispatch(t *testing.T) {
	path := writeManifest(t, validManifest)
	var out, errOut bytes.Buffer
	if code := run([]string{"sandbox", path}, &out, &errOut); code != 0 {
		t.Fatalf("sandbox subcommand exited %d: %s%s", code, out.String(), errOut.String())
	}
}

func TestSandboxFactoryFlagUsesBuiltinRegistry(t *testing.T) {
	path := writeManifest(t, strings.Replace(validManifest, `id = "camera"`, `id = "factory-demo"`, 1))
	var out, errOut bytes.Buffer
	if code := run([]string{"sandbox", path, "--factory", "demo"}, &out, &errOut); code != 0 {
		t.Fatalf("sandbox --factory exited %d: %s%s", code, out.String(), errOut.String())
	}
	text := out.String()
	if !strings.Contains(text, "fibers: 1") || !strings.Contains(text, "providers: 1") {
		t.Fatalf("built-in factory did not load a component: %s", text)
	}
	if strings.Contains(text, "requires build-time registration") {
		t.Fatalf("boundary diagnostic should not appear with --factory: %s", text)
	}
}

func TestSandboxFactoryFlagRejectsUnknownName(t *testing.T) {
	path := writeManifest(t, validManifest)
	var out, errOut bytes.Buffer
	if code := run([]string{"sandbox", "--factory", "missing", path}, &out, &errOut); code == 0 {
		t.Fatal("unknown factory name was accepted")
	}
	if !strings.Contains(errOut.String()+out.String(), "UNKNOWN_FACTORY") {
		t.Fatalf("unknown-factory diagnostic missing: %s%s", out.String(), errOut.String())
	}
}
