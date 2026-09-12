package webui_test

import (
	"net/http"
	"net/http/httptest"
	"strings"
	"os"
	"path/filepath"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/webui"
)

func newModuleServer(t *testing.T) (*webui.Server, string) {
	t.Helper()
	dir := t.TempDir()
	mod := filepath.Join(dir, "demo-ui.js")
	if err := os.WriteFile(mod, []byte("export default function register(m) { void m; }"), 0o644); err != nil {
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
	want := `{"data":{"modules":[{"name":"demo-ui","url":"/client-modules/demo-ui"}]}}`
	if got := strings.TrimSpace(rec.Body.String()); got != want {
		t.Fatalf("manifest = %s, want %s", got, want)
	}

	rec = httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/client-modules/demo-ui", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("module status %d", rec.Code)
	}
	if ct := rec.Header().Get("Content-Type"); ct != "text/javascript; charset=utf-8" {
		t.Fatalf("content type = %q", ct)
	}
	if body := rec.Body.String(); body != "export default function register(m) { void m; }" {
		t.Fatalf("module body = %q", body)
	}
}

func TestClientModulesTraversalImpossible(t *testing.T) {
	s, _ := newModuleServer(t)
	for _, path := range []string{
		"/client-modules/..%2f..%2fetc%2fpasswd",
		"/client-modules/../server.go",
		"/client-modules/nope",
	} {
		rec := httptest.NewRecorder()
		s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, path, nil))
		if rec.Code == http.StatusOK {
			t.Fatalf("GET %s must not succeed", path)
		}
	}
}

func TestClientModulesMissingFileIs500(t *testing.T) {
	s, _ := newModuleServer(t)
	s.SetClientModules([]host.ClientModule{{Name: "gone", Path: filepath.Join(t.TempDir(), "missing.js")}})
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/client-modules/gone", nil))
	if rec.Code != http.StatusInternalServerError {
		t.Fatalf("missing module status = %d, want 500", rec.Code)
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
