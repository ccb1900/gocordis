package webui

import (
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"testing/fstest"
)

// The console bootstrap is embedded so `go run ./cmd/platform` serves a
// working browser UI on a fresh clone (web/console/dist is gitignored). These
// tests pin the two sides of that contract:
//
//   - a missing/empty dist falls back to the embedded bootstrap at / and on
//     static paths;
//   - a real built dist keeps priority, so `npm ci && npm run build` is a
//     pure upgrade with no regression.
//
// They are intentionally black-box (httptest over ServeHTTP) so a future
// change of the embed mechanism cannot silently drop the fallback.

const bootstrapMarker = "gocordis-bootstrap"

func staticGet(t *testing.T, s *Server, path string) *httptest.ResponseRecorder {
	t.Helper()
	req := httptest.NewRequest(http.MethodGet, path, nil)
	rec := httptest.NewRecorder()
	s.ServeHTTP(rec, req)
	return rec
}

func TestBootstrapServedWhenDistMissing(t *testing.T) {
	s := New(nil, nil) // nil assets = no built SPA (fresh clone)

	rec := staticGet(t, s, "/")
	if rec.Code != http.StatusOK {
		t.Fatalf("GET / = %d, want 200; body=%s", rec.Code, rec.Body.String())
	}
	if ct := rec.Header().Get("Content-Type"); !strings.HasPrefix(ct, "text/html") {
		t.Fatalf("GET / Content-Type = %q, want text/html", ct)
	}
	if !strings.Contains(rec.Body.String(), bootstrapMarker) {
		t.Fatalf("GET / is not the embedded bootstrap (missing %q): %s", bootstrapMarker, rec.Body.String())
	}

	// Static paths of the bootstrap must resolve too (otherwise the page
	// renders unstyled and without JS, i.e. not functional).
	for _, path := range []string{"/app.js", "/styles.css"} {
		rec = staticGet(t, s, path)
		if rec.Code != http.StatusOK {
			t.Fatalf("GET %s = %d, want 200", path, rec.Code)
		}
		if rec.Body.Len() == 0 {
			t.Fatalf("GET %s returned an empty body", path)
		}
	}
}

func TestBootstrapServedWhenDistHasNoIndex(t *testing.T) {
	// A configured GOCORDIS_WEB_DIST may point at a directory that exists but
	// holds no build output (e.g. a stale path). That must also fall back.
	dist := fstest.MapFS{"stale.txt": &fstest.MapFile{Data: []byte("not a build")}}
	s := New(nil, dist)

	rec := staticGet(t, s, "/")
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), bootstrapMarker) {
		t.Fatalf("GET / with stale dist = %d %s, want bootstrap", rec.Code, rec.Body.String())
	}
}

func TestBootstrapServedWhenConfiguredDirIsAbsent(t *testing.T) {
	// os.DirFS on a non-existent path is exactly what defaultAssets returns
	// when GOCORDIS_WEB_DIST names a directory that is not there.
	missing := filepath.Join(t.TempDir(), "no-such-dist")
	if _, err := os.Stat(missing); !os.IsNotExist(err) {
		t.Fatalf("test precondition: path should not exist: %v", err)
	}
	s := New(nil, os.DirFS(missing))

	rec := staticGet(t, s, "/")
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), bootstrapMarker) {
		t.Fatalf("GET / with absent dir = %d %s, want bootstrap", rec.Code, rec.Body.String())
	}
}

func TestRealDistWinsWhenPresent(t *testing.T) {
	const fullSpa = "FULL-SPA-BUILD-MARKER"
	dist := fstest.MapFS{
		"index.html":    &fstest.MapFile{Data: []byte("<html>" + fullSpa + "</html>")},
		"assets/app.js": &fstest.MapFile{Data: []byte("console.log('full spa');")},
	}
	s := New(nil, dist)

	rec := staticGet(t, s, "/")
	if rec.Code != http.StatusOK {
		t.Fatalf("GET / = %d, want 200", rec.Code)
	}
	body := rec.Body.String()
	if !strings.Contains(body, fullSpa) {
		t.Fatalf("GET / did not serve the real dist: %s", body)
	}
	if strings.Contains(body, bootstrapMarker) {
		t.Fatalf("GET / served bootstrap over an existing dist: %s", body)
	}

	// A real asset must be served from the real dist verbatim.
	rec = staticGet(t, s, "/assets/app.js")
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "full spa") {
		t.Fatalf("GET /assets/app.js = %d %s, want real dist asset", rec.Code, rec.Body.String())
	}
}

func TestBootstrapUsesSafeDOMAPIs(t *testing.T) {
	// Runtime names/keys are untrusted: the bootstrap must write dynamic
	// values with textContent, never innerHTML (regression guard for XSS).
	s := New(nil, nil)
	rec := staticGet(t, s, "/app.js")
	js := rec.Body.String()
	if strings.Contains(js, "innerHTML") {
		t.Fatal("bootstrap JS uses innerHTML; dynamic values must use textContent")
	}
	if !strings.Contains(js, "textContent") {
		t.Fatal("bootstrap JS does not use textContent")
	}
	// Declared routes must stay local: reject protocol-relative, backslash and
	// control-character URLs (browsers normalize /\n/evil.com off-origin).
	if !strings.Contains(js, "indexOf('\\\\')") || !strings.Contains(js, "\\u0000-\\u001f") {
		t.Fatal("bootstrap JS route guard misses backslash or control characters")
	}
	// The two queries it is required to consume must be wired in.
	for _, path := range []string{"/api/ui/pages", "/api/query/runtime/snapshot", "/api/query/framework/dashboard"} {
		if !strings.Contains(js, path) {
			t.Fatalf("bootstrap JS does not fetch %s", path)
		}
	}
}
