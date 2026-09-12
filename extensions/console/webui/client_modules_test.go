package webui_test

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	appui "dynamic-runtime/extensions/console/registry"
	"dynamic-runtime/extensions/console/webui"
)

func newModuleServer(t *testing.T) (*webui.Server, string, appui.Registry) {
	t.Helper()
	dir := t.TempDir()
	if err := os.MkdirAll(filepath.Join(dir, "lib"), 0o755); err != nil {
		t.Fatal(err)
	}
	mod := filepath.Join(dir, "ui.js")
	if err := os.WriteFile(mod, []byte("import * as echarts from './lib/echarts.esm.min.js';\nexport default function register(m) { void m; }"), 0o644); err != nil {
		t.Fatal(err)
	}
	lib := filepath.Join(dir, "lib", "echarts.esm.min.js")
	if err := os.WriteFile(lib, []byte("export const init = () => 1;"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "..", "secret.txt"), []byte("top secret"), 0o644); err != nil {
		t.Fatal(err)
	}
	reg := appui.NewRegistry(func() {})
	_, err := reg.RegisterClientModule(
		appui.ContributionOwner{PluginID: "ui-client", ComponentID: "alarm", ActivationID: "a1"},
		appui.ClientModule{Name: "demo-ui", Path: mod},
	)
	if err != nil {
		t.Fatal(err)
	}
	adapter := host.NewHost(nil, reg, nil)
	s := webui.New(adapter, nil)
	return s, dir, reg
}

func TestClientModulesManifestAndServing(t *testing.T) {
	s, _, _ := newModuleServer(t)

	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/ui/client-modules", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("manifest status %d: %s", rec.Code, rec.Body.String())
	}
	want := `{"data":{"modules":[{"name":"demo-ui","url":"/client-modules/demo-ui/ui.js"}]}}`
	if got := strings.TrimSpace(rec.Body.String()); got != want {
		t.Fatalf("manifest = %s, want %s", got, want)
	}

	// The entry file at its directory-shaped URL: relative imports of
	// vendored libraries resolve inside the plugin directory.
	rec = httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/client-modules/demo-ui/ui.js", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("module status %d", rec.Code)
	}
	if ct := rec.Header().Get("Content-Type"); ct != "text/javascript; charset=utf-8" {
		t.Fatalf("content type = %q", ct)
	}

	// Vendored library files under the plugin directory are served too.
	rec = httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/client-modules/demo-ui/lib/echarts.esm.min.js", nil))
	if rec.Code != http.StatusOK || rec.Body.String() != "export const init = () => 1;" {
		t.Fatalf("vendored lib status %d body %q", rec.Code, rec.Body.String())
	}
}

func TestClientModulesTraversalImpossible(t *testing.T) {
	s, dir, _ := newModuleServer(t)
	for _, path := range []string{
		"/client-modules/..%2f..%2fetc%2fpasswd",
		"/client-modules/demo-ui/../secret.txt",
		"/client-modules/demo-ui/%2e%2e/secret.txt",
		"/client-modules/demo-ui/lib/../../../secret.txt",
		"/client-modules/nope",
		"/client-modules/nope/lib/x.js",
	} {
		rec := httptest.NewRecorder()
		s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, path, nil))
		if rec.Code == http.StatusOK {
			t.Fatalf("GET %s must not succeed", path)
		}
	}
	// The secret really is next to the plugin dir — the guard held.
	if _, err := os.Stat(filepath.Join(dir, "..", "secret.txt")); err != nil {
		t.Fatal(err)
	}
}

func TestClientModulesMissingFileIs404(t *testing.T) {
	s, _, reg := newModuleServer(t)
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "ui.js"), []byte("export default 1"), 0o644); err != nil {
		t.Fatal(err)
	}
	_, err := reg.RegisterClientModule(
		appui.ContributionOwner{PluginID: "ui-client", ComponentID: "gone", ActivationID: "a1"},
		appui.ClientModule{Name: "gone", Path: filepath.Join(dir, "ui.js")},
	)
	if err != nil {
		t.Fatal(err)
	}
	_ = os.Remove(filepath.Join(dir, "ui.js"))
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/client-modules/gone/ui.js", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("missing module status = %d, want 404", rec.Code)
	}
}

// The manifest comes from the composition registry: unregistering the
// owning contribution (uninstall/disable of the ui-client component)
// empties it — existence on disk is never enough.
func TestClientModulesGovernedByComposition(t *testing.T) {
	s, _, reg := newModuleServer(t)

	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/ui/client-modules", nil))
	if !strings.Contains(rec.Body.String(), `"demo-ui"`) {
		t.Fatalf("registered module must be listed: %s", rec.Body.String())
	}

	// Duplicate module name is rejected.
	if _, err := reg.RegisterClientModule(
		appui.ContributionOwner{PluginID: "ui-client", ComponentID: "second", ActivationID: "a2"},
		appui.ClientModule{Name: "demo-ui", Path: filepath.Join(t.TempDir(), "ui.js")},
	); err == nil {
		t.Fatal("duplicate module name must be rejected")
	}

	// A second module registers; its cleanup removes only its own row.
	cleanup, err := reg.RegisterClientModule(
		appui.ContributionOwner{PluginID: "ui-client", ComponentID: "second", ActivationID: "a2"},
		appui.ClientModule{Name: "other", Path: filepath.Join(t.TempDir(), "ui.js")},
	)
	if err != nil {
		t.Fatal(err)
	}
	if err := cleanup(); err != nil {
		t.Fatal(err)
	}
	rec = httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/api/ui/client-modules", nil))
	if !strings.Contains(rec.Body.String(), `"demo-ui"`) || strings.Contains(rec.Body.String(), `"other"`) {
		t.Fatalf("cleanup must remove only its own module: %s", rec.Body.String())
	}
}

func TestClientModuleComponentDefaults(t *testing.T) {
	mk := func(cfg map[string]any) config.ComponentConfig {
		return config.ComponentConfig{ID: "alarm-demo", Type: "ui-client", Config: cfg}
	}
	// Convention: module name = component id, entry = plugins/<name>/ui.js.
	c, err := host.NewClientModuleComponent(mk(nil))
	if err != nil {
		t.Fatal(err)
	}
	_ = c
	// Explicit module name and path override the convention.
	if _, err := host.NewClientModuleComponent(mk(map[string]any{
		"module": "alt", "path": "/opt/alt/ui.js",
	})); err != nil {
		t.Fatal(err)
	}
	// Invalid module names fail loudly.
	if _, err := host.NewClientModuleComponent(mk(map[string]any{"module": "a/b"})); err == nil {
		t.Fatal("separator in module name must be rejected")
	}
	if _, err := host.NewClientModuleComponent(mk(map[string]any{"module": ".."})); err == nil {
		t.Fatal("traversal module name must be rejected")
	}
}
