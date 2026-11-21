package acl

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"testing"
	"time"

	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type svcCapture struct{ ch chan server.Service }

func (c *svcCapture) Name() string { return "test-svc-capture" }
func (c *svcCapture) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(server.ServiceKey)}
}
func (c *svcCapture) Provide() []runtime.Capability { return nil }
func (c *svcCapture) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	c.ch <- svc
	return nil, nil
}

func loadHarness(t *testing.T) (*runtime.Runtime, server.Service) {
	t.Helper()
	ctx := testCtx(t)
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() {
		if err := rt.Close(testCtx(t)); err != nil {
			t.Errorf("rt.Close: %v", err)
		}
	})
	cap := &svcCapture{ch: make(chan server.Service, 1)}
	provider, err := rt.Load(&server.Component{})
	if err != nil {
		t.Fatalf("load server provider: %v", err)
	}
	consumer, err := rt.Load(cap)
	if err != nil {
		t.Fatalf("load capture: %v", err)
	}
	if err := provider.Ready(ctx); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	if err := consumer.Ready(ctx); err != nil {
		t.Fatalf("capture Ready: %v", err)
	}
	select {
	case svc := <-cap.ch:
		return rt, svc
	case <-ctx.Done():
		t.Fatal("service never delivered")
		return nil, nil
	}
}

func okHandler() http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, "ok")
	})
}

func req(method, host, path string) *http.Request {
	r := httptest.NewRequest(method, path, nil)
	r.Host = host
	return r
}

func call(h http.Handler, host string) *httptest.ResponseRecorder {
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req(http.MethodGet, host, "/x"))
	return rec
}

// loadComponent registers one GET route and activates the ACL component.
func loadComponent(t *testing.T, c *Component) (*runtime.Runtime, server.Service) {
	t.Helper()
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	if _, err := svc.Handle(http.MethodGet, "/x", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "ok")
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}
	comp, err := rt.Load(c)
	if err != nil {
		t.Fatalf("load acl: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("acl Ready: %v", err)
	}
	return rt, svc
}

// ---- pure helpers ---------------------------------------------------------

func TestStripPort(t *testing.T) {
	cases := map[string]string{
		"EXAMPLE.com:8443": "example.com",
		"example.com.":     "example.com",
		"[::1]:8080":       "[::1]",
		"[2001:db8::1]":    "[2001:db8::1]",
		"::1":              "::1",
		"127.0.0.1:9000":   "127.0.0.1",
	}
	for in, want := range cases {
		if got := stripPort(in); got != want {
			t.Errorf("stripPort(%q) = %q, want %q", in, got, want)
		}
	}
}

func TestMiddlewareExactAndPort(t *testing.T) {
	h := Middleware([]string{"example.com"}, nil)(okHandler())
	if rec := call(h, "example.com"); rec.Code != http.StatusOK {
		t.Fatalf("allowed host: code=%d, want 200", rec.Code)
	}
	if rec := call(h, "EXAMPLE.COM:8443"); rec.Code != http.StatusOK {
		t.Fatalf("host with port / case: code=%d, want 200", rec.Code)
	}
	if rec := call(h, "other.com"); rec.Code != http.StatusNotFound {
		t.Fatalf("unknown host: code=%d, want 404 (vetoed route)", rec.Code)
	}
}

func TestMiddlewareLeadingDotMatchesApexAndSubdomains(t *testing.T) {
	h := Middleware([]string{".example.com"}, nil)(okHandler())
	for _, host := range []string{"example.com", "a.example.com", "a.b.example.com", "EXAMPLE.COM:443"} {
		if rec := call(h, host); rec.Code != http.StatusOK {
			t.Errorf("%s: code=%d, want 200", host, rec.Code)
		}
	}
	for _, host := range []string{"evil-example.com", "example.org"} {
		if rec := call(h, host); rec.Code != http.StatusNotFound {
			t.Errorf("%s: code=%d, want 404", host, rec.Code)
		}
	}
}

func TestMiddlewareWildcardAlsoIncludesApex(t *testing.T) {
	h := Middleware([]string{"*.example.com"}, nil)(okHandler())
	for _, host := range []string{"a.example.com", "example.com"} {
		if rec := call(h, host); rec.Code != http.StatusOK {
			t.Errorf("%s: code=%d, want 200", host, rec.Code)
		}
	}
	if rec := call(h, "evil-example.com"); rec.Code != http.StatusNotFound {
		t.Fatalf("lookalike: code=%d, want 404", rec.Code)
	}
}

func TestMiddlewareEmptyAllowIsLoopbackOnly(t *testing.T) {
	h := Middleware(nil, nil)(okHandler())
	for _, host := range []string{"localhost", "LOCALHOST:3000", "127.0.0.1", "127.0.0.1:8080", "::1", "[::1]:8080"} {
		if rec := call(h, host); rec.Code != http.StatusOK {
			t.Errorf("loopback %s: code=%d, want 200", host, rec.Code)
		}
	}
	if rec := call(h, "example.com"); rec.Code != http.StatusNotFound {
		t.Fatalf("external host: code=%d, want 404", rec.Code)
	}
}

func TestMiddlewareDenyPrecedence(t *testing.T) {
	h := Middleware([]string{".example.com"}, []string{"bad.example.com"})(okHandler())
	if rec := call(h, "bad.example.com"); rec.Code != http.StatusNotFound {
		t.Fatalf("denied host also in allow: code=%d, want 404", rec.Code)
	}
	if rec := call(h, "good.example.com"); rec.Code != http.StatusOK {
		t.Fatalf("allowed host: code=%d, want 200", rec.Code)
	}
}

func TestMiddlewareWithRoutesPureDecision(t *testing.T) {
	routes := map[string][]string{"GET /private": {"partner.test"}}
	check := MiddlewareWithRoutes([]string{"example.com"}, nil, routes)

	// The helper is pure: no server/runtime, just host + route key.
	if !check("partner.test", "GET /private") {
		t.Fatal("per-route override should admit partner.test")
	}
	if check("partner.test", "GET /public") {
		t.Fatal("override must replace the global allow list on that route only")
	}
	if !check("example.com", "GET /public") {
		t.Fatal("global allow list should apply to other routes")
	}
	if check("example.com", "GET /private") {
		t.Fatal("route override should exclude global-only hosts")
	}
}

// ---- component / route-check integration ----------------------------------

func TestComponentLoopbackAndEmptyAllow(t *testing.T) {
	_, svc := loadComponent(t, &Component{})
	for _, host := range []string{"localhost", "127.0.0.1:8080", "[::1]:8080"} {
		if rec := call(svc, host); rec.Code != http.StatusOK {
			t.Errorf("loopback %s: code=%d, want 200", host, rec.Code)
		}
	}
	if rec := call(svc, "example.com"); rec.Code != http.StatusNotFound {
		t.Fatalf("external host with empty allow: code=%d, want 404", rec.Code)
	}
}

func TestComponentDenialIs404(t *testing.T) {
	_, svc := loadComponent(t, &Component{Allow: []string{"example.com"}})
	if rec := call(svc, "evil.test"); rec.Code != http.StatusNotFound {
		t.Fatalf("denied host: code=%d, want 404 (not 403)", rec.Code)
	}
	if rec := call(svc, "example.com:8443"); rec.Code != http.StatusOK {
		t.Fatalf("allowed host: code=%d, want 200", rec.Code)
	}
}

func TestComponentDisposeWithdrawsCheck(t *testing.T) {
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	if _, err := svc.Handle(http.MethodGet, "/x", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "ok")
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}
	comp, err := rt.Load(&Component{Allow: []string{"example.com"}})
	if err != nil {
		t.Fatalf("load acl: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("acl Ready: %v", err)
	}
	if rec := call(svc, "evil.test"); rec.Code != http.StatusNotFound {
		t.Fatalf("after install: code=%d, want 404", rec.Code)
	}
	if err := comp.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := comp.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if rec := call(svc, "evil.test"); rec.Code != http.StatusOK {
		t.Fatalf("after dispose: code=%d, want 200 (check withdrawn)", rec.Code)
	}
}

func TestComponentRouteOverride(t *testing.T) {
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	handler := func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "ok")
		return err
	}
	for _, p := range []string{"/private", "/public", "/ws"} {
		if _, err := svc.Handle(http.MethodGet, p, handler); err != nil {
			t.Fatalf("Handle(%s): %v", p, err)
		}
	}
	comp, err := rt.Load(&Component{
		Allow: []string{"example.com"},
		RouteAllowedHosts: map[string][]string{
			"GET /private": {"partner.test"},
			"/ws":          {"partner.test"},
		},
	})
	if err != nil {
		t.Fatalf("load acl: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("acl Ready: %v", err)
	}

	probe := func(host, path string) int {
		rec := httptest.NewRecorder()
		svc.ServeHTTP(rec, req(http.MethodGet, host, path))
		return rec.Code
	}
	if got := probe("partner.test", "/private"); got != http.StatusOK {
		t.Errorf("override route: code=%d, want 200", got)
	}
	if got := probe("example.com", "/private"); got != http.StatusNotFound {
		t.Errorf("override excludes global host: code=%d, want 404", got)
	}
	if got := probe("partner.test", "/public"); got != http.StatusNotFound {
		t.Errorf("non-override route: code=%d, want 404", got)
	}
	if got := probe("example.com", "/public"); got != http.StatusOK {
		t.Errorf("global route: code=%d, want 200", got)
	}
	if got := probe("partner.test", "/ws"); got != http.StatusOK {
		t.Errorf("path-only override key: code=%d, want 200", got)
	}
}

func TestComponentDeniesWebSocketRoute(t *testing.T) {
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	entered := false
	if _, err := svc.WS("/ws", func(*server.WSConn) error { entered = true; return nil }); err != nil {
		t.Fatalf("WS: %v", err)
	}
	comp, err := rt.Load(&Component{Allow: []string{"example.com"}})
	if err != nil {
		t.Fatalf("load acl: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("acl Ready: %v", err)
	}
	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, req(http.MethodGet, "evil.test", "/ws"))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("denied WS upgrade: code=%d, want 404", rec.Code)
	}
	if entered {
		t.Fatal("WS callback must not run for a denied host")
	}
}

func TestComponentDeniedOptionsIs404Not405(t *testing.T) {
	_, svc := loadComponent(t, &Component{Allow: []string{"example.com"}})
	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, req(http.MethodOptions, "evil.test", "/x"))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("denied OPTIONS on sole route: code=%d, want 404 (not 204/405)", rec.Code)
	}
}
