package market_test

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"reflect"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/market"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// fixtures
// ---------------------------------------------------------------------------

func sha256File(t *testing.T, path string) string {
	t.Helper()
	b, err := os.ReadFile(path)
	if err != nil {
		t.Fatal(err)
	}
	sum := sha256.Sum256(b)
	return hex.EncodeToString(sum[:])
}

func writeFile(t *testing.T, path, body string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
}

func wasmManifest(id, version, checksum, runtimeExpr string) string {
	return fmt.Sprintf(`id = %q
name = %q
version = %q
description = "camera wasm module"
backend = "wasm"
module_type = "industrial.camera"
[compatibility]
runtime = %q
api = ">=1.0.0"
[source]
kind = "wasm"
location = "camera.wasm"
checksum = %q
`, id, id, version, runtimeExpr, checksum)
}

func procManifest(id, version, checksum, apiExpr string) string {
	return fmt.Sprintf(`{
  "id": %q,
  "name": %q,
  "version": %q,
  "description": "modbus gateway",
  "backend": "proc",
  "module_type": "modbus",
  "compatibility": {"runtime": ">=1.0.0", "api": %q},
  "provides": [
    {"key": "query:modbus.status", "description": "status"},
    {"key": "command:modbus.write", "description": "write"}
  ],
  "source": {"kind": "proc", "location": "bin/modbus", "checksum": %q}
}`, id, id, version, apiExpr, checksum)
}

func nativeManifest(id, version string) string {
	return fmt.Sprintf(`id = %q
name = %q
version = %q
description = "built-in component"
backend = "native"
module_type = "settings"
`, id, id, version)
}

// recordingRuntime delegates Load to a real Runtime and records every call, so
// tests can prove that market reaches the kernel ONLY through Controller.Reconcile.
type recordingRuntime struct {
	inner  *runtime.Runtime
	mu     sync.Mutex
	loads  int
	fibers []*runtime.Fiber
}

func (r *recordingRuntime) Load(c runtime.Component) (*runtime.Fiber, error) {
	f, err := r.inner.Load(c)
	if err == nil {
		r.mu.Lock()
		r.loads++
		r.fibers = append(r.fibers, f)
		r.mu.Unlock()
	}
	return f, err
}

func (r *recordingRuntime) loadCount() int {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.loads
}

type markerComponent struct{ name string }

func (c *markerComponent) Name() string                  { return c.name }
func (c *markerComponent) Inject() []runtime.Dependency  { return nil }
func (c *markerComponent) Provide() []runtime.Capability { return nil }
func (c *markerComponent) Apply(*runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}

type serviceEnv struct {
	dir  string
	rt   *recordingRuntime
	ctrl *config.Controller
	svc  market.Service
}

func newServiceEnv(t *testing.T, dir string, versions ...string) *serviceEnv {
	t.Helper()
	runtimeVersion, apiVersion := "1.2.0", "1.0.0"
	if len(versions) > 0 {
		runtimeVersion = versions[0]
	}
	if len(versions) > 1 {
		apiVersion = versions[1]
	}
	inner, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	rt := &recordingRuntime{inner: inner}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = inner.Close(ctx)
	})
	reg := config.NewFactoryRegistry()
	registerMarkers(t, reg)
	ctrl := config.NewController(rt, reg)
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(ctx)
	})
	svc, err := market.NewService(market.Options{
		Controller:     ctrl,
		CatalogDirs:    []string{dir},
		RuntimeVersion: runtimeVersion,
		APIVersion:     apiVersion,
		Trust:          "local",
	})
	if err != nil {
		t.Fatal(err)
	}
	return &serviceEnv{dir: dir, rt: rt, ctrl: ctrl, svc: svc}
}

func registerMarkers(t *testing.T, reg config.FactoryRegistry) {
	t.Helper()
	for _, typ := range []string{"industrial.camera", "proc-plugin", "settings"} {
		typ := typ
		if err := reg.Register(typ, config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
			return &markerComponent{name: typ + ":" + cc.ID}, nil
		})); err != nil {
			t.Fatal(err)
		}
	}
}

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// ---------------------------------------------------------------------------
// catalog discovery + exact DTO
// ---------------------------------------------------------------------------

func TestCatalogDiscoveryAndExactDTO(t *testing.T) {
	dir := t.TempDir()
	wasmPath := filepath.Join(dir, "alpha", "camera.wasm")
	writeFile(t, wasmPath, "wasm-bytes")
	sum := sha256File(t, wasmPath)
	writeFile(t, filepath.Join(dir, "alpha", "plugin.toml"), wasmManifest("alpha", "1.0.0", sum, ">=1.0.0 <2.0.0"))
	procPath := filepath.Join(dir, "beta", "bin", "modbus")
	writeFile(t, procPath, "proc-bytes")
	psum := sha256File(t, procPath)
	writeFile(t, filepath.Join(dir, "beta", "plugin.json"), procManifest("beta", "2.1.0", psum, ">=1.0.0"))

	env := newServiceEnv(t, dir)
	cat, err := env.svc.Catalog(ctxT(t))
	if err != nil {
		t.Fatalf("Catalog: %v", err)
	}
	raw, err := json.Marshal(cat)
	if err != nil {
		t.Fatal(err)
	}
	var top map[string]json.RawMessage
	if err := json.Unmarshal(raw, &top); err != nil {
		t.Fatal(err)
	}
	if len(top) != 2 || top["entries"] == nil || top["sequence"] == nil {
		t.Fatalf("DTO keys = %v, want exactly entries+sequence: %s", keysOf(top), raw)
	}
	var entries []map[string]json.RawMessage
	if err := json.Unmarshal(top["entries"], &entries); err != nil {
		t.Fatal(err)
	}
	if len(entries) != 2 {
		t.Fatalf("entries = %d, want 2: %s", len(entries), raw)
	}
	// keysOf sorts for a stable comparison (JSON field order is not semantic).
	wantKeys := []string{"backend", "compatible", "description", "id", "installed", "moduleType", "name", "source", "trust", "version"}
	for i, e := range entries {
		if got := keysOf(e); !reflect.DeepEqual(got, wantKeys) {
			t.Fatalf("entry[%d] keys = %v, want %v", i, got, wantKeys)
		}
	}
	byID := map[string]map[string]json.RawMessage{}
	for _, e := range entries {
		var id string
		_ = json.Unmarshal(e["id"], &id)
		byID[id] = e
	}
	alpha := byID["alpha"]
	if alpha == nil || strField(t, alpha, "backend") != "wasm" || strField(t, alpha, "moduleType") != "industrial.camera" ||
		strField(t, alpha, "source") != "camera.wasm" || strField(t, alpha, "trust") != "local" ||
		strField(t, alpha, "version") != "1.0.0" || boolField(t, alpha, "compatible") != true ||
		boolField(t, alpha, "installed") != false {
		t.Fatalf("alpha entry = %v", alpha)
	}
	beta := byID["beta"]
	if beta == nil || strField(t, beta, "backend") != "proc" || strField(t, beta, "moduleType") != "modbus" ||
		boolField(t, beta, "compatible") != true {
		t.Fatalf("beta entry = %v", beta)
	}
}

func strField(t *testing.T, m map[string]json.RawMessage, key string) string {
	t.Helper()
	var s string
	if err := json.Unmarshal(m[key], &s); err != nil {
		t.Fatalf("field %q: %v", key, err)
	}
	return s
}

func boolField(t *testing.T, m map[string]json.RawMessage, key string) bool {
	t.Helper()
	var b bool
	if err := json.Unmarshal(m[key], &b); err != nil {
		t.Fatalf("field %q: %v", key, err)
	}
	return b
}

func keysOf(m map[string]json.RawMessage) []string {
	out := make([]string, 0, len(m))
	for k := range m {
		out = append(out, k)
	}
	sort.Strings(out)
	return out
}

// ---------------------------------------------------------------------------
// invalid / duplicate entries are reported with a reason
// ---------------------------------------------------------------------------

func TestCatalogInvalidAndDuplicatesReported(t *testing.T) {
	dir := t.TempDir()
	wasm := filepath.Join(dir, "ok", "camera.wasm")
	writeFile(t, wasm, "bytes")
	sum := sha256File(t, wasm)
	writeFile(t, filepath.Join(dir, "ok", "plugin.toml"), wasmManifest("ok", "1.0.0", sum, ">=1.0.0"))
	// strict parse: unknown field must be reported, not ignored.
	writeFile(t, filepath.Join(dir, "bad", "plugin.json"), `{"id":"bad","name":"Bad","version":"1.0.0","backend":"wasm","surprise":true}`)
	// intrinsic validation: invalid semantic version.
	writeFile(t, filepath.Join(dir, "badver", "plugin.toml"), wasmManifest("badver", "not-semver", sum, ">=1.0.0"))
	// duplicate id of "ok".
	writeFile(t, filepath.Join(dir, "dupe", "plugin.toml"), wasmManifest("ok", "9.9.9", sum, ">=1.0.0"))

	env := newServiceEnv(t, dir)
	cat, err := env.svc.Catalog(ctxT(t))
	if !errors.Is(err, market.ErrInvalidCatalog) {
		t.Fatalf("Catalog error = %v, want ErrInvalidCatalog", err)
	}
	msg := err.Error()
	for _, want := range []string{"unknown field", "not-semver", "duplicate"} {
		if !strings.Contains(msg, want) {
			t.Fatalf("error %q missing reason %q", msg, want)
		}
	}
	if len(cat.Entries) != 1 || cat.Entries[0].ID != "ok" {
		t.Fatalf("valid entries = %+v, want only ok", cat.Entries)
	}
}

// ---------------------------------------------------------------------------
// compatibility uses pluginmeta.ValidateAgainst with both config versions
// ---------------------------------------------------------------------------

func TestCatalogCompatibility(t *testing.T) {
	dir := t.TempDir()
	wasm := filepath.Join(dir, "a", "camera.wasm")
	writeFile(t, wasm, "bytes")
	sum := sha256File(t, wasm)
	// runtime 1.2.0 / api 2.0.0 supplied via Component config.
	writeFile(t, filepath.Join(dir, "a", "plugin.toml"), wasmManifest("fit", "1.0.0", sum, ">=1.0.0 <2.0.0"))       // api >=1.0.0, fits
	writeFile(t, filepath.Join(dir, "b", "plugin.toml"), wasmManifest("runtime-mismatch", "1.0.0", sum, ">=2.0.0")) // runtime fails
	writeFile(t, filepath.Join(dir, "c", "plugin.json"), procManifest("api-mismatch", "1.0.0", sum, ">=3.0.0"))     // api fails

	env := newServiceEnv(t, dir, "1.2.0", "2.0.0")
	cat, err := env.svc.Catalog(ctxT(t))
	if err != nil {
		t.Fatalf("Catalog: %v", err)
	}
	got := map[string]bool{}
	for _, e := range cat.Entries {
		got[e.ID] = e.Compatible
	}
	want := map[string]bool{"fit": true, "runtime-mismatch": false, "api-mismatch": false}
	if !reflect.DeepEqual(got, want) {
		t.Fatalf("compatibility = %v, want %v", got, want)
	}
}

// ---------------------------------------------------------------------------
// checksum verification
// ---------------------------------------------------------------------------

func TestInstallRejectsChecksumMismatch(t *testing.T) {
	dir := t.TempDir()
	wasm := filepath.Join(dir, "alpha", "camera.wasm")
	writeFile(t, wasm, "real-bytes")
	writeFile(t, filepath.Join(dir, "alpha", "plugin.toml"), wasmManifest("alpha", "1.0.0", strings.Repeat("0", 64), ">=1.0.0"))

	env := newServiceEnv(t, dir)
	err := env.svc.Install(ctxT(t), "alpha")
	if !errors.Is(err, market.ErrChecksumMismatch) {
		t.Fatalf("Install error = %v, want ErrChecksumMismatch", err)
	}
	if env.rt.loadCount() != 0 {
		t.Fatalf("loads = %d, want 0 (no declaration reached the controller)", env.rt.loadCount())
	}
}

// ---------------------------------------------------------------------------
// install / upgrade / uninstall produce config rows; no direct fiber mutation
// ---------------------------------------------------------------------------

func TestInstallUpgradeUninstallConfigRows(t *testing.T) {
	dir := t.TempDir()
	wasm := filepath.Join(dir, "alpha", "camera.wasm")
	writeFile(t, wasm, "v1")
	wasmSum := sha256File(t, wasm)
	wasmManifestPath := filepath.Join(dir, "alpha", "plugin.toml")
	writeFile(t, wasmManifestPath, wasmManifest("alpha", "1.0.0", wasmSum, ">=1.0.0 <2.0.0"))
	proc := filepath.Join(dir, "beta", "bin", "modbus")
	writeFile(t, proc, "proc-v1")
	procSum := sha256File(t, proc)
	writeFile(t, filepath.Join(dir, "beta", "plugin.json"), procManifest("beta", "1.0.0", procSum, ">=1.0.0"))

	env := newServiceEnv(t, dir)
	ctx := ctxT(t)

	// A real Controller with a real Runtime: Install only submits desired rows.
	if err := env.svc.Install(ctx, "alpha"); err != nil {
		t.Fatalf("Install alpha: %v", err)
	}
	row := ownedRow(t, env, "alpha")
	if row.Type != "industrial.camera" || row.Config["version"] != "1.0.0" {
		t.Fatalf("wasm row = %+v", row)
	}
	source, _ := row.Config["source"].(string)
	wantSource := "file://" + wasm
	if source != wantSource {
		t.Fatalf("wasm source = %q, want %q", source, wantSource)
	}
	absPath, _ := row.Config["path"].(string)
	if absPath != wasm {
		t.Fatalf("wasm path = %q, want %q", absPath, wasm)
	}
	if env.rt.loadCount() != 1 {
		t.Fatalf("loads = %d, want 1 (one Reconcile, no direct Load)", env.rt.loadCount())
	}
	oldFiber := row.Fiber

	// Idempotent Install: no new declaration, no fiber churn.
	if err := env.svc.Install(ctx, "alpha"); err != nil {
		t.Fatalf("re-Install alpha: %v", err)
	}
	if env.rt.loadCount() != 1 || ownedRow(t, env, "alpha").Fiber != oldFiber {
		t.Fatal("idempotent install churned the fiber")
	}

	if err := env.svc.Install(ctx, "beta"); err != nil {
		t.Fatalf("Install beta: %v", err)
	}
	prow := ownedRow(t, env, "beta")
	if prow.Type != "proc-plugin" || prow.Config["dir"] != filepath.Join(dir, "beta") ||
		prow.Config["backend"] != "bin/modbus" || !reflect.DeepEqual(prow.Config["queries"], []any{"query:modbus.status"}) ||
		!reflect.DeepEqual(prow.Config["commands"], []any{"command:modbus.write"}) {
		t.Fatalf("proc row = %+v", prow)
	}
	if env.rt.loadCount() != 2 {
		t.Fatalf("loads = %d, want 2", env.rt.loadCount())
	}

	cat, err := env.svc.Catalog(ctx)
	if err != nil {
		t.Fatalf("Catalog: %v", err)
	}
	for _, e := range cat.Entries {
		if !e.Installed {
			t.Fatalf("entry %q installed=false after install", e.ID)
		}
	}

	// Upgrade = declaration replacement (Controller unloads old / loads new).
	writeFile(t, wasmManifestPath, wasmManifest("alpha", "1.1.0", wasmSum, ">=1.0.0 <2.0.0"))
	if err := env.svc.Upgrade(ctx, "alpha"); err != nil {
		t.Fatalf("Upgrade alpha: %v", err)
	}
	upgraded := ownedRow(t, env, "alpha")
	if upgraded.Config["version"] != "1.1.0" {
		t.Fatalf("upgraded version = %v, want 1.1.0", upgraded.Config["version"])
	}
	if env.rt.loadCount() != 3 || upgraded.Fiber == oldFiber {
		t.Fatal("Upgrade did not go through a declaration replacement")
	}

	if err := env.svc.Uninstall(ctx, "alpha"); err != nil {
		t.Fatalf("Uninstall alpha: %v", err)
	}
	if err := env.svc.Uninstall(ctx, "beta"); err != nil {
		t.Fatalf("Uninstall beta: %v", err)
	}
	if hasRow(t, env, "alpha") || hasRow(t, env, "beta") {
		t.Fatal("rows survived uninstall")
	}
	if env.rt.loadCount() != 3 {
		t.Fatalf("uninstall must not Load anything; loads = %d", env.rt.loadCount())
	}
	cat, err = env.svc.Catalog(ctx)
	if err != nil {
		t.Fatal(err)
	}
	for _, e := range cat.Entries {
		if e.Installed {
			t.Fatalf("entry %q installed=true after uninstall", e.ID)
		}
	}
	if cat.Sequence < 4 {
		t.Fatalf("sequence = %d, want >= 4 after four mutations", cat.Sequence)
	}
}

func ownedRow(t *testing.T, env *serviceEnv, id string) config.OwnedComponent {
	t.Helper()
	for _, o := range env.ctrl.Owned() {
		if o.ID == id {
			return o
		}
	}
	t.Fatalf("row %q not owned", id)
	return config.OwnedComponent{}
}

func hasRow(t *testing.T, env *serviceEnv, id string) bool {
	t.Helper()
	for _, o := range env.ctrl.Owned() {
		if o.ID == id {
			return true
		}
	}
	return false
}

// Uninstall must retire the stored declaration even when the catalog manifest
// disappears (or turns native) after install — no stale fiber/declaration.
func TestUninstallAfterManifestDeleted(t *testing.T) {
	dir := t.TempDir()
	wasm := filepath.Join(dir, "alpha", "camera.wasm")
	writeFile(t, wasm, "bytes")
	sum := sha256File(t, wasm)
	manifest := filepath.Join(dir, "alpha", "plugin.toml")
	writeFile(t, manifest, wasmManifest("alpha", "1.0.0", sum, ">=1.0.0"))

	env := newServiceEnv(t, dir)
	if err := env.svc.Install(ctxT(t), "alpha"); err != nil {
		t.Fatalf("Install: %v", err)
	}
	if err := os.Remove(manifest); err != nil {
		t.Fatal(err)
	}
	loads := env.rt.loadCount()
	if err := env.svc.Uninstall(ctxT(t), "alpha"); err != nil {
		t.Fatalf("Uninstall after manifest deleted: %v", err)
	}
	if hasRow(t, env, "alpha") {
		t.Fatal("stale declaration survived uninstall")
	}
	if env.rt.loadCount() != loads {
		t.Fatal("uninstall loaded a component")
	}
}

// ---------------------------------------------------------------------------
// native entries are build-time, read-only
// ---------------------------------------------------------------------------

func TestNativeReadOnly(t *testing.T) {
	dir := t.TempDir()
	writeFile(t, filepath.Join(dir, "native", "plugin.toml"), nativeManifest("native", "1.0.0"))
	env := newServiceEnv(t, dir)
	ctx := ctxT(t)
	for _, op := range []struct {
		name string
		call func() error
	}{
		{"install", func() error { return env.svc.Install(ctx, "native") }},
		{"upgrade", func() error { return env.svc.Upgrade(ctx, "native") }},
		{"uninstall", func() error { return env.svc.Uninstall(ctx, "native") }},
	} {
		if err := op.call(); !errors.Is(err, market.ErrBuildTimePlugin) {
			t.Fatalf("%s native: error = %v, want ErrBuildTimePlugin", op.name, err)
		}
	}
	if env.rt.loadCount() != 0 {
		t.Fatal("native operation reached the controller")
	}
	cat, err := env.svc.Catalog(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if len(cat.Entries) != 1 || cat.Entries[0].Backend != "native" || cat.Entries[0].Compatible != true {
		t.Fatalf("native catalog = %+v", cat.Entries)
	}
}
