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
	"dynamic-runtime/extensions/console/webui"
)

func newModuleServer(t *testing.T) (*webui.Server, string) {
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
	s := webui.New(host.NewHost(nil, nil, nil), nil)
	s.SetClientModules([]host.ClientModule{{Name: "demo-ui", Path: mod}})
	return s, dir
}

func TestClientModulesManifestAndServing(t *testing.T) {
	s, _ := newModuleServer(t)

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
	s, dir := newModuleServer(t)
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
	s, _ := newModuleServer(t)
	s.SetClientModules([]host.ClientModule{{Name: "gone", Path: filepath.Join(t.TempDir(), "missing.js")}})
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/client-modules/gone/ui.js", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("missing module status = %d, want 404", rec.Code)
	}
}

func TestNewConsoleParsesClientModules(t *testing.T) {
	mk := func(rows any) config.ComponentConfig {
		return config.ComponentConfig{ID: "ui", Type: "ui", Config: map[string]any{"client_modules": rows}}
	}
	c, err := host.NewConsole(mk([]any{
		map[string]any{"name": "demo", "path": "./x/demo.js"},
		map[string]any{"path": "./x/other.mjs"},
	}))
	if err != nil {
		t.Fatal(err)
	}
	mods := c.ClientModules()
	if len(mods) != 2 || mods[0].Name != "demo" || mods[1].Name != "other" {
		t.Fatalf("modules = %+v", mods)
	}

	// Invalid shapes fail loudly.
	if _, err := host.NewConsole(mk([]any{map[string]any{"name": "x"}})); err == nil {
		t.Fatal("missing path must be rejected")
	}
	if _, err := host.NewConsole(mk([]any{map[string]any{"path": "./x.js", "name": "a/b"}})); err == nil {
		t.Fatal("separator in name must be rejected")
	}
	if _, err := host.NewConsole(mk([]any{
		map[string]any{"path": "./x.js", "name": "dup"},
		map[string]any{"path": "./y.js", "name": "dup"},
	})); err == nil {
		t.Fatal("duplicate names must be rejected")
	}
	if _, err := host.NewConsole(mk("not-an-array")); err == nil {
		t.Fatal("non-array client_modules must be rejected")
	}
}
