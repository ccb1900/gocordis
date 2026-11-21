package server

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/gorilla/websocket"

	"dynamic-runtime/runtime"
)

// ---- helpers -------------------------------------------------------------

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
		defer cancel()
		if err := rt.Close(ctx); err != nil {
			t.Errorf("rt.Close: %v", err)
		}
	})
	return rt
}

func freeAddr(t *testing.T) string {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatalf("probe listen: %v", err)
	}
	addr := ln.Addr().String()
	if err := ln.Close(); err != nil {
		t.Fatalf("probe close: %v", err)
	}
	return addr
}

// ---- routing / params ----------------------------------------------------

func TestRouterRoutingAndParams(t *testing.T) {
	svc := newRouter()
	if _, err := svc.Handle(http.MethodGet, "/users/{id}", func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, "user="+Param(r, "id"))
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}
	if _, err := svc.Handle(http.MethodGet, "/users/{id}/posts/{post}", func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, Param(r, "id")+":"+Param(r, "post"))
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}

	cases := []struct {
		path string
		want string
	}{
		{"/users/42", "user=42"},
		{"/users/abc/posts/xyz", "abc:xyz"},
	}
	for _, tc := range cases {
		rec := httptest.NewRecorder()
		svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, tc.path, nil))
		if rec.Code != http.StatusOK {
			t.Fatalf("%s: status %d, want 200", tc.path, rec.Code)
		}
		if got := rec.Body.String(); got != tc.want {
			t.Fatalf("%s: body %q, want %q", tc.path, got, tc.want)
		}
	}

	// Param outside a handler / unknown name is empty, never a panic.
	if got := Param(httptest.NewRequest(http.MethodGet, "/", nil), "id"); got != "" {
		t.Fatalf("Param outside route = %q, want empty", got)
	}
}

// ---- catch-all: {path...} spans the remaining segments -------------------

func TestRouterCatchAllMatchesNestedPath(t *testing.T) {
	svc := newRouter()
	if _, err := svc.Handle(http.MethodGet, "/static/{path...}", func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, "path="+Param(r, "path"))
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}

	cases := []struct {
		path string
		want string
	}{
		{"/static/app.css", "path=app.css"},
		{"/static/css/app.css", "path=css/app.css"},
		{"/static/css/theme/dark.css", "path=css/theme/dark.css"},
		{"/static/a%2Ecss", "path=a.css"}, // percent-escaped path
	}
	for _, tc := range cases {
		rec := httptest.NewRecorder()
		svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, tc.path, nil))
		if rec.Code != http.StatusOK {
			t.Fatalf("%s: status %d, want 200", tc.path, rec.Code)
		}
		if got := rec.Body.String(); got != tc.want {
			t.Fatalf("%s: body %q, want %q", tc.path, got, tc.want)
		}
	}
}

func TestRouterCatchAllExactMethodWinsOverAll(t *testing.T) {
	svc := newRouter()
	disposeAll, err := svc.All("/static/{path...}", func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, "all:"+r.Method)
		return err
	})
	if err != nil {
		t.Fatalf("All: %v", err)
	}
	disposeGet, err := svc.Handle(http.MethodGet, "/static/{path...}", func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, "get:"+Param(r, "path"))
		return err
	})
	if err != nil {
		t.Fatalf("Handle: %v", err)
	}

	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/static/css/a.css", nil))
	if rec.Body.String() != "get:css/a.css" {
		t.Fatalf("GET prefer exact catch-all: %q", rec.Body.String())
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodPost, "/static/css/a.css", nil))
	if rec.Body.String() != "all:POST" {
		t.Fatalf("POST should use All catch-all: %q", rec.Body.String())
	}

	if err := disposeGet(); err != nil {
		t.Fatalf("disposeGet: %v", err)
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/static/css/a.css", nil))
	if rec.Body.String() != "all:GET" {
		t.Fatalf("GET after dispose: %q", rec.Body.String())
	}
	if err := disposeAll(); err != nil {
		t.Fatalf("disposeAll: %v", err)
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/static/css/a.css", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("after both disposed: code=%d, want 404", rec.Code)
	}
}

func TestRouterRejectsInvalidPattern(t *testing.T) {
	svc := newRouter()
	for _, pattern := range []string{"relative", "/x/{", "/x/{a}/{a}"} {
		if _, err := svc.Handle(http.MethodGet, pattern, func(http.ResponseWriter, *http.Request) error { return nil }); err == nil {
			t.Fatalf("Handle(%q): want error, got nil", pattern)
		}
	}
}

// ---- 404 / 405 -----------------------------------------------------------

func TestRouter404And405(t *testing.T) {
	svc := newRouter()
	handler := func(w http.ResponseWriter, _ *http.Request) error {
		w.WriteHeader(http.StatusOK)
		return nil
	}
	for _, m := range []string{http.MethodGet, http.MethodPut} {
		if _, err := svc.Handle(m, "/users/{id}", handler); err != nil {
			t.Fatalf("Handle(%s): %v", m, err)
		}
	}

	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodPost, "/users/7", nil))
	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("POST /users/7: status %d, want 405", rec.Code)
	}
	allow := rec.Header().Get("Allow")
	if !strings.Contains(allow, "GET") || !strings.Contains(allow, "PUT") {
		t.Fatalf("Allow = %q, want GET and PUT", allow)
	}

	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/nothing", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("GET /nothing: status %d, want 404", rec.Code)
	}
}

// ---- middleware ----------------------------------------------------------

func TestMiddlewareRegistrationOrderAndDisposer(t *testing.T) {
	svc := newRouter()
	if _, err := svc.Handle(http.MethodGet, "/x", func(w http.ResponseWriter, _ *http.Request) error {
		w.WriteHeader(http.StatusOK)
		return nil
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}

	mw := func(tag string) func(http.Handler) http.Handler {
		return func(next http.Handler) http.Handler {
			return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
				w.Header().Add("X-Mw", tag)
				next.ServeHTTP(w, r)
			})
		}
	}
	disposeA, err := svc.Use(mw("a"))
	if err != nil {
		t.Fatalf("Use a: %v", err)
	}
	if _, err := svc.Use(mw("b")); err != nil {
		t.Fatalf("Use b: %v", err)
	}

	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/x", nil))
	if got := rec.Header().Values("X-Mw"); strings.Join(got, ",") != "a,b" {
		t.Fatalf("middleware order = %v, want [a b]", got)
	}

	// Repeated dispose is a no-op; removing a removes only a.
	disposeA()
	if err := disposeA(); err != nil {
		t.Fatalf("second dispose: %v", err)
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/x", nil))
	if got := rec.Header().Values("X-Mw"); strings.Join(got, ",") != "b" {
		t.Fatalf("middleware after dispose = %v, want [b]", got)
	}
}

func TestRouteDisposerRemovesOnlyThatRoute(t *testing.T) {
	svc := newRouter()
	h := func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "ok")
		return err
	}
	_, _ = svc.Handle(http.MethodGet, "/a", h)
	disposeB, err := svc.Handle(http.MethodGet, "/b", h)
	if err != nil {
		t.Fatalf("Handle /b: %v", err)
	}
	if err := disposeB(); err != nil {
		t.Fatalf("dispose: %v", err)
	}
	if err := disposeB(); err != nil {
		t.Fatalf("dispose twice: %v", err)
	}

	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/a", nil))
	if rec.Code != http.StatusOK {
		t.Fatalf("/a status %d, want 200", rec.Code)
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/b", nil))
	if rec.Code != http.StatusNotFound {
		t.Fatalf("/b status %d, want 404", rec.Code)
	}
}

// ---- handler error -> single 500 ----------------------------------------

func TestHandlerErrorWrites500Once(t *testing.T) {
	svc := newRouter()
	_, _ = svc.Handle(http.MethodGet, "/boom", func(http.ResponseWriter, *http.Request) error {
		return io.ErrUnexpectedEOF
	})
	_, _ = svc.Handle(http.MethodGet, "/partial", func(w http.ResponseWriter, _ *http.Request) error {
		_, _ = io.WriteString(w, "partial")
		return io.ErrUnexpectedEOF
	})
	_, _ = svc.Handle(http.MethodGet, "/flushed", func(w http.ResponseWriter, _ *http.Request) error {
		w.(http.Flusher).Flush() // wrapped handler keeps Flusher
		return io.ErrUnexpectedEOF
	})

	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/boom", nil))
	if rec.Code != http.StatusInternalServerError {
		t.Fatalf("/boom status %d, want 500", rec.Code)
	}
	if strings.Count(rec.Body.String(), "Internal Server Error") != 1 {
		t.Fatalf("/boom body written more than once: %q", rec.Body.String())
	}

	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/partial", nil))
	if rec.Code != http.StatusOK || rec.Body.String() != "partial" {
		t.Fatalf("/partial wrote twice: code=%d body=%q", rec.Code, rec.Body.String())
	}

	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/flushed", nil))
	if rec.Code == http.StatusInternalServerError {
		t.Fatalf("/flushed wrote 500 after a flush")
	}
}

// ---- Serve: Active <=> listener bound; Gone releases port ----------------

type serveComponent struct {
	addr  string
	svcCh chan Service
}

func (c *serveComponent) Name() string { return "test-serve" }
func (c *serveComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(ServiceKey)}
}
func (c *serveComponent) Provide() []runtime.Capability { return nil }
func (c *serveComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, ServiceKey)
	if err != nil {
		return nil, err
	}
	if _, err := svc.Handle(http.MethodGet, "/ping", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "pong")
		return err
	}); err != nil {
		return nil, err
	}
	if err := Serve(ctx, svc, c.addr); err != nil {
		return nil, err
	}
	if c.svcCh != nil {
		select {
		case c.svcCh <- svc:
		default:
		}
	}
	return nil, nil
}

func TestServeActiveThenGoneReleasesPort(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	addr := freeAddr(t)

	provider, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	if err := provider.Ready(ctx); err != nil {
		t.Fatalf("provider ready: %v", err)
	}

	dep := &serveComponent{addr: addr}
	consumer, err := rt.Load(dep)
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	// Ready returns only after Serve has bound the listener.
	if err := consumer.Ready(ctx); err != nil {
		t.Fatalf("consumer ready: %v", err)
	}

	resp, err := http.Get("http://" + addr + "/ping") //nolint:gosec // test-local address
	if err != nil {
		t.Fatalf("GET while Active: %v", err)
	}
	body, err := io.ReadAll(resp.Body)
	_ = resp.Body.Close()
	if err != nil || string(body) != "pong" {
		t.Fatalf("GET body = %q, err = %v", body, err)
	}

	if err := consumer.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := consumer.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}

	// The inverse joined the serve goroutine and released the port before Gone.
	ln, err := net.Listen("tcp", addr)
	if err != nil {
		t.Fatalf("rebind after Gone: %v", err)
	}
	_ = ln.Close()
}

// ---- Component provides ServiceKey; effect cleanup closes it ------------

type probeComponent struct{ svcCh chan Service }

func (c *probeComponent) Name() string { return "test-probe" }
func (c *probeComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(ServiceKey)}
}
func (c *probeComponent) Provide() []runtime.Capability { return nil }
func (c *probeComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, ServiceKey)
	if err != nil {
		return nil, err
	}
	select {
	case c.svcCh <- svc:
	default:
	}
	return nil, nil
}

func TestComponentProvidesServiceAndEffectCleanup(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)

	f, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("ready: %v", err)
	}

	probe := &probeComponent{svcCh: make(chan Service, 1)}
	pf, err := rt.Load(probe)
	if err != nil {
		t.Fatalf("load probe: %v", err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatalf("probe ready: %v", err)
	}
	svc := <-probe.svcCh

	if _, err := svc.Handle(http.MethodGet, "/live", func(http.ResponseWriter, *http.Request) error { return nil }); err != nil {
		t.Fatalf("Handle while Active: %v", err)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if _, err := svc.Handle(http.MethodGet, "/after", func(http.ResponseWriter, *http.Request) error { return nil }); err == nil {
		t.Fatal("Handle after provider Gone: want closed-service error, got nil")
	}
}

// ---- concurrent dispatch is race-clean ----------------------------------

func TestConcurrentServeHTTP(t *testing.T) {
	svc := newRouter()
	_, _ = svc.Handle(http.MethodGet, "/c/{id}", func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, Param(r, "id"))
		return err
	})
	var wg sync.WaitGroup
	for i := 0; i < 32; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			rec := httptest.NewRecorder()
			svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/c/7", nil))
			if rec.Code != http.StatusOK || rec.Body.String() != "7" {
				t.Errorf("concurrent request: code=%d body=%q", rec.Code, rec.Body.String())
			}
		}()
	}
	wg.Wait()
}

// ---- All routes + OPTIONS ------------------------------------------------

func TestAllRouteAndOPTIONS(t *testing.T) {
	svc := newRouter()
	allHandler := func(w http.ResponseWriter, r *http.Request) error {
		_, err := io.WriteString(w, "all:"+r.Method)
		return err
	}
	disposeAll, err := svc.All("/anything/{id}", allHandler)
	if err != nil {
		t.Fatalf("All: %v", err)
	}
	for _, method := range []string{http.MethodGet, http.MethodPost, http.MethodPatch} {
		rec := httptest.NewRecorder()
		svc.ServeHTTP(rec, httptest.NewRequest(method, "/anything/7", nil))
		if rec.Code != http.StatusOK || rec.Body.String() != "all:"+method {
			t.Fatalf("%s: code=%d body=%q", method, rec.Code, rec.Body.String())
		}
	}

	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodOptions, "/anything/7", nil))
	if rec.Code != http.StatusNoContent {
		t.Fatalf("OPTIONS All: code=%d, want 204", rec.Code)
	}
	if allow := rec.Header().Get("Allow"); !strings.Contains(allow, "*") {
		t.Fatalf("OPTIONS All Allow = %q, want *", allow)
	}
	if rec.Body.Len() != 0 {
		t.Fatalf("OPTIONS body = %q, want empty", rec.Body.String())
	}

	// Exact routes win over All for their method; Allow reports both.
	_, _ = svc.Handle(http.MethodGet, "/anything/{id}", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "exact")
		return err
	})
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/anything/7", nil))
	if rec.Body.String() != "exact" {
		t.Fatalf("GET prefer exact route: %q", rec.Body.String())
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodOptions, "/anything/7", nil))
	allow := rec.Header().Get("Allow")
	if !strings.Contains(allow, "GET") || !strings.Contains(allow, "*") {
		t.Fatalf("OPTIONS mixed Allow = %q, want GET and *", allow)
	}

	// An explicit OPTIONS route wins over the automatic 204.
	_, _ = svc.Handle(http.MethodOptions, "/opts", func(w http.ResponseWriter, _ *http.Request) error {
		w.WriteHeader(http.StatusAccepted)
		return nil
	})
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodOptions, "/opts", nil))
	if rec.Code != http.StatusAccepted {
		t.Fatalf("explicit OPTIONS: code=%d, want 202", rec.Code)
	}

	// Disposer removes only the All route.
	if err := disposeAll(); err != nil {
		t.Fatalf("disposeAll: %v", err)
	}
	if err := disposeAll(); err != nil {
		t.Fatalf("disposeAll twice: %v", err)
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/anything/7", nil))
	if rec.Body.String() != "exact" {
		t.Fatalf("GET after dispose: %q", rec.Body.String())
	}
	rec = httptest.NewRecorder()
	svc.ServeHTTP(rec, httptest.NewRequest(http.MethodPost, "/anything/7", nil))
	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("POST after dispose: code=%d, want 405", rec.Code)
	}
}

// ---- ServeRange: real listeners ------------------------------------------

type serveRangeComponent struct {
	addr  string
	ports int
}

func (c *serveRangeComponent) Name() string { return "test-serve-range" }
func (c *serveRangeComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(ServiceKey)}
}
func (c *serveRangeComponent) Provide() []runtime.Capability { return nil }
func (c *serveRangeComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, ServiceKey)
	if err != nil {
		return nil, err
	}
	if _, err := svc.Handle(http.MethodGet, "/ping", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "range-pong")
		return err
	}); err != nil {
		return nil, err
	}
	return nil, ServeRange(ctx, svc, c.addr, c.ports)
}

func portAt(t *testing.T, addr string, offset int) string {
	t.Helper()
	host, portStr, err := net.SplitHostPort(addr)
	if err != nil {
		t.Fatalf("SplitHostPort: %v", err)
	}
	port, err := strconv.Atoi(portStr)
	if err != nil {
		t.Fatalf("Atoi: %v", err)
	}
	return net.JoinHostPort(host, strconv.Itoa(port+offset))
}

func TestServeRangeBindsFirstAvailablePort(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	addr := freeAddr(t)

	// Hold the first port of the range; the effect must bind the next one.
	holder, err := net.Listen("tcp", addr)
	if err != nil {
		t.Fatalf("hold first port: %v", err)
	}
	defer holder.Close()

	provider, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	if err := provider.Ready(ctx); err != nil {
		t.Fatalf("provider ready: %v", err)
	}
	consumer, err := rt.Load(&serveRangeComponent{addr: addr, ports: 3})
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := consumer.Ready(ctx); err != nil {
		t.Fatalf("consumer ready: %v", err)
	}

	// Another test (or process) may take a port inside our range between
	// freeAddr's probe and the bind. Probe every non-held port and accept the
	// one ServeRange actually bound, instead of assuming offset 1.
	client := &http.Client{Timeout: 3 * time.Second}
	var lastErr error
	servedPort := ""
	found := false
	for offset := 1; offset < 3; offset++ {
		port := portAt(t, addr, offset)
		resp, err := client.Get("http://" + port + "/ping") //nolint:gosec // test-local
		if err != nil {
			lastErr = err
			continue
		}
		body, readErr := io.ReadAll(resp.Body)
		_ = resp.Body.Close()
		if readErr == nil && string(body) == "range-pong" {
			found = true
			servedPort = port
			break
		}
		lastErr = fmt.Errorf("body = %q, err = %v", body, readErr)
	}
	if !found {
		t.Fatalf("no fallback port served range-pong: %v", lastErr)
	}

	if err := consumer.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := consumer.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	rebound, err := net.Listen("tcp", servedPort)
	if err != nil {
		t.Fatalf("rebind fallback after Gone: %v", err)
	}
	_ = rebound.Close()
}

func TestServeRangeExhaustedFailsActivation(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	// freeAddr closes its probe, so another test can take one of the two
	// ports in between. Retry with a fresh pair instead of assuming success.
	var addr string
	var h1, h2 net.Listener
	var err error
	for attempt := 0; attempt < 20; attempt++ {
		addr = freeAddr(t)
		h1, err = net.Listen("tcp", addr)
		if err != nil {
			continue
		}
		h2, err = net.Listen("tcp", portAt(t, addr, 1))
		if err == nil {
			break
		}
		_ = h1.Close()
		h1 = nil
	}
	if h1 == nil || h2 == nil {
		t.Fatalf("could not reserve two consecutive ports: %v", err)
	}
	defer h1.Close()
	defer h2.Close()

	provider, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	if err := provider.Ready(ctx); err != nil {
		t.Fatalf("provider ready: %v", err)
	}
	consumer, err := rt.Load(&serveRangeComponent{addr: addr, ports: 2})
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := consumer.Ready(ctx); err == nil {
		t.Fatal("all ports occupied: want activation failure, got Active")
	}

	invalid, err := rt.Load(&serveRangeComponent{addr: addr, ports: 0})
	if err != nil {
		t.Fatalf("load invalid range: %v", err)
	}
	if err := invalid.Ready(ctx); err == nil {
		t.Fatal("ports=0: want activation failure")
	}
}

// ---- WebSocket ------------------------------------------------------------

func newWSServer(t *testing.T) (Service, string, string) {
	t.Helper()
	svc := newRouter()
	srv := httptest.NewServer(svc)
	t.Cleanup(srv.Close)
	return svc, srv.URL, "ws" + strings.TrimPrefix(srv.URL, "http")
}

func dialWS(t *testing.T, base, path string, hdr http.Header) *websocket.Conn {
	t.Helper()
	var h http.Header
	if hdr != nil {
		h = hdr.Clone()
	}
	c, _, err := websocket.DefaultDialer.Dial(base+path, h)
	if err != nil {
		t.Fatalf("Dial %s: %v", path, err)
	}
	t.Cleanup(func() { _ = c.Close() })
	return c
}

func wsEcho(t *testing.T, c *websocket.Conn, mt int, payload []byte) (int, []byte) {
	t.Helper()
	if err := c.WriteMessage(mt, payload); err != nil {
		t.Fatalf("WriteMessage: %v", err)
	}
	if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("SetReadDeadline: %v", err)
	}
	gotMT, got, err := c.ReadMessage()
	if err != nil {
		t.Fatalf("ReadMessage: %v", err)
	}
	return gotMT, got
}

func wsEchoCallback(c *WSConn) error {
	for {
		mt, data, err := (*c).ReadMessage()
		if err != nil {
			return err
		}
		if err := (*c).WriteMessage(mt, data); err != nil {
			return err
		}
	}
}

func TestWSTextBinaryEchoAndPathParam(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	dispose, err := svc.WS("/ws/{id}", func(c *WSConn) error {
		for {
			mt, data, err := (*c).ReadMessage()
			if err != nil {
				return err
			}
			out := append([]byte(WSParam(*c, "id")+":"), data...)
			if err := (*c).WriteMessage(mt, out); err != nil {
				return err
			}
		}
	})
	if err != nil {
		t.Fatalf("WS: %v", err)
	}
	c := dialWS(t, wsBase, "/ws/42", nil)

	mt, got := wsEcho(t, c, websocket.TextMessage, []byte("hello"))
	if mt != websocket.TextMessage || string(got) != "42:hello" {
		t.Fatalf("text echo = (%d, %q), want text 42:hello", mt, got)
	}
	mt, got = wsEcho(t, c, websocket.BinaryMessage, []byte{0, 1, 2})
	if mt != websocket.BinaryMessage || string(got) != "42:\x00\x01\x02" {
		t.Fatalf("binary echo = (%d, %q), want binary 42:\\x00\\x01\\x02", mt, got)
	}
	if err := dispose(); err != nil {
		t.Fatalf("dispose: %v", err)
	}
}

func TestWSMultipleClientsCloseOnDisposeAndIdempotentDisposer(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	// A second registration that must survive disposal of the first.
	if _, err := svc.WS("/keep/{id}", wsEchoCallback); err != nil {
		t.Fatalf("WS /keep: %v", err)
	}
	dispose, err := svc.WS("/ws/{id}", wsEchoCallback)
	if err != nil {
		t.Fatalf("WS /ws: %v", err)
	}

	clients := make([]*websocket.Conn, 3)
	for i := range clients {
		clients[i] = dialWS(t, wsBase, fmt.Sprintf("/ws/%d", i), nil)
		mt, got := wsEcho(t, clients[i], websocket.TextMessage, []byte("ping"))
		if mt != websocket.TextMessage || string(got) != "ping" {
			t.Fatalf("client %d echo = (%d, %q)", i, mt, got)
		}
	}

	// Idempotent disposer; removes only its own route.
	if err := dispose(); err != nil {
		t.Fatalf("dispose: %v", err)
	}
	if err := dispose(); err != nil {
		t.Fatalf("dispose twice: %v", err)
	}
	for i, c := range clients {
		if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
			t.Fatalf("client %d SetReadDeadline: %v", i, err)
		}
		if _, _, err := c.ReadMessage(); err == nil {
			t.Fatalf("client %d: expected close on dispose", i)
		}
	}
	if _, _, err := websocket.DefaultDialer.Dial(wsBase+"/ws/0", nil); err == nil {
		t.Fatal("dial after dispose: expected route to be gone")
	}
	kc := dialWS(t, wsBase, "/keep/9", nil)
	mt, got := wsEcho(t, kc, websocket.TextMessage, []byte("alive"))
	if mt != websocket.TextMessage || string(got) != "alive" {
		t.Fatalf("/keep echo after dispose = (%d, %q)", mt, got)
	}
}

func TestWSExactMethodHTTPRouteWins(t *testing.T) {
	svc, httpBase, wsBase := newWSServer(t)
	if _, err := svc.WS("/ws/{id}", wsEchoCallback); err != nil {
		t.Fatalf("WS: %v", err)
	}
	if _, err := svc.Handle(http.MethodGet, "/ws/42", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "http")
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}

	resp, err := http.Get(httpBase + "/ws/42") //nolint:gosec // test-local
	if err != nil {
		t.Fatalf("GET /ws/42: %v", err)
	}
	body, _ := io.ReadAll(resp.Body)
	_ = resp.Body.Close()
	if string(body) != "http" {
		t.Fatalf("exact-method route body = %q, want http", body)
	}

	// A path with no exact-method route still upgrades to WebSocket.
	c := dialWS(t, wsBase, "/ws/43", nil)
	mt, got := wsEcho(t, c, websocket.TextMessage, []byte("ws"))
	if mt != websocket.TextMessage || string(got) != "ws" {
		t.Fatalf("ws echo = (%d, %q)", mt, got)
	}
}

func TestWSRejectsNonWebSocketRequestWith400(t *testing.T) {
	svc, httpBase, _ := newWSServer(t)
	if _, err := svc.WS("/ws", wsEchoCallback); err != nil {
		t.Fatalf("WS: %v", err)
	}
	resp, err := http.Get(httpBase + "/ws") //nolint:gosec // test-local
	if err != nil {
		t.Fatalf("GET /ws: %v", err)
	}
	defer resp.Body.Close()
	if resp.StatusCode != http.StatusBadRequest {
		t.Fatalf("non-WebSocket GET status = %d, want 400", resp.StatusCode)
	}
}

func TestWSCheckOriginConfigurable(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	if _, err := svc.WS("/ws", wsEchoCallback); err != nil {
		t.Fatalf("WS: %v", err)
	}
	hdr := http.Header{"Origin": []string{"http://evil.example"}}
	if _, resp, err := websocket.DefaultDialer.Dial(wsBase+"/ws", hdr); err == nil {
		if resp != nil && resp.Body != nil {
			_ = resp.Body.Close()
		}
		t.Fatal("cross-origin dial: want default same-origin rejection")
	}

	svc.SetCheckOrigin(func(*http.Request) bool { return true })
	c, _, err := websocket.DefaultDialer.Dial(wsBase+"/ws", hdr)
	if err != nil {
		t.Fatalf("dial after SetCheckOrigin: %v", err)
	}
	_ = c.Close()
}

func TestWSCallbackFailureContained(t *testing.T) {
	svc, httpBase, wsBase := newWSServer(t)
	if _, err := svc.Handle(http.MethodGet, "/health", func(w http.ResponseWriter, _ *http.Request) error {
		_, err := io.WriteString(w, "ok")
		return err
	}); err != nil {
		t.Fatalf("Handle: %v", err)
	}
	if _, err := svc.WS("/panic", func(*WSConn) error { panic("boom") }); err != nil {
		t.Fatalf("WS /panic: %v", err)
	}
	if _, err := svc.WS("/fail", func(*WSConn) error { return errors.New("nope") }); err != nil {
		t.Fatalf("WS /fail: %v", err)
	}

	pc := dialWS(t, wsBase, "/panic", nil)
	if err := pc.WriteMessage(websocket.TextMessage, []byte("x")); err != nil {
		t.Fatalf("write to panicking callback: %v", err)
	}
	if err := pc.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("SetReadDeadline: %v", err)
	}
	if _, _, err := pc.ReadMessage(); err == nil {
		t.Fatal("expected connection closed after callback panic")
	}

	fc := dialWS(t, wsBase, "/fail", nil)
	if err := fc.WriteMessage(websocket.TextMessage, []byte("y")); err != nil {
		t.Fatalf("write to erroring callback: %v", err)
	}
	resp, err := http.Get(httpBase + "/health") //nolint:gosec // test-local
	if err != nil {
		t.Fatalf("GET /health: %v", err)
	}
	body, _ := io.ReadAll(resp.Body)
	_ = resp.Body.Close()
	if string(body) != "ok" {
		t.Fatalf("/health body = %q, want ok", body)
	}
}

type wsServeComponent struct {
	addr   string
	exited chan struct{}
	once   sync.Once
}

func (c *wsServeComponent) Name() string { return "test-ws-serve" }
func (c *wsServeComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(ServiceKey)}
}
func (c *wsServeComponent) Provide() []runtime.Capability { return nil }
func (c *wsServeComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, ServiceKey)
	if err != nil {
		return nil, err
	}
	if _, err := svc.WS("/ws/{id}", func(conn *WSConn) error {
		if c.exited != nil {
			defer c.once.Do(func() { close(c.exited) })
		}
		return wsEchoCallback(conn)
	}); err != nil {
		return nil, err
	}
	return nil, Serve(ctx, svc, c.addr)
}

func TestWSServiceCloseClosesLiveSocketsAndJoinsHandlers(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	addr := freeAddr(t)

	provider, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	if err := provider.Ready(ctx); err != nil {
		t.Fatalf("provider ready: %v", err)
	}
	dep := &wsServeComponent{addr: addr, exited: make(chan struct{})}
	consumer, err := rt.Load(dep)
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := consumer.Ready(ctx); err != nil {
		t.Fatalf("consumer ready: %v", err)
	}

	c, _, err := websocket.DefaultDialer.Dial("ws://"+addr+"/ws/7", nil)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	mt, got := wsEcho(t, c, websocket.TextMessage, []byte("hi"))
	if mt != websocket.TextMessage || string(got) != "hi" {
		t.Fatalf("echo = (%d, %q)", mt, got)
	}

	if err := consumer.Dispose(); err != nil {
		t.Fatalf("consumer Dispose: %v", err)
	}
	if err := consumer.Gone(ctx); err != nil {
		t.Fatalf("consumer Gone: %v", err)
	}
	if err := provider.Dispose(); err != nil {
		t.Fatalf("provider Dispose: %v", err)
	}
	if err := provider.Gone(ctx); err != nil {
		t.Fatalf("provider Gone: %v", err)
	}

	select {
	case <-dep.exited:
	default:
		t.Fatal("handler goroutine still running after Gone")
	}
	if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("SetReadDeadline: %v", err)
	}
	if _, _, err := c.ReadMessage(); err == nil {
		t.Fatal("client did not observe close after Service.Close")
	}
}

func TestWSConcurrentClients(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	dispose, err := svc.WS("/ws/{id}", wsEchoCallback)
	if err != nil {
		t.Fatalf("WS: %v", err)
	}
	var wg sync.WaitGroup
	for i := 0; i < 16; i++ {
		wg.Add(1)
		go func(id int) {
			defer wg.Done()
			c, _, err := websocket.DefaultDialer.Dial(fmt.Sprintf("%s/ws/%d", wsBase, id), nil)
			if err != nil {
				t.Errorf("dial %d: %v", id, err)
				return
			}
			defer c.Close()
			payload := fmt.Sprintf("client-%d", id)
			if err := c.WriteMessage(websocket.TextMessage, []byte(payload)); err != nil {
				t.Errorf("write %d: %v", id, err)
				return
			}
			if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
				t.Errorf("deadline %d: %v", id, err)
				return
			}
			_, got, err := c.ReadMessage()
			if err != nil || string(got) != payload {
				t.Errorf("echo %d = %q, err = %v", id, got, err)
			}
		}(i)
	}
	wg.Wait()
	if err := dispose(); err != nil {
		t.Fatalf("dispose: %v", err)
	}
}

type wsEffectComponent struct {
	addr      string
	handlerBy chan struct{}
	effectRan chan struct{}
}

func (c *wsEffectComponent) Name() string { return "test-ws-effect" }
func (c *wsEffectComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(ServiceKey)}
}
func (c *wsEffectComponent) Provide() []runtime.Capability { return nil }
func (c *wsEffectComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, ServiceKey)
	if err != nil {
		return nil, err
	}
	dispose, err := svc.WS("/ws/{id}", func(conn *WSConn) error {
		defer close(c.handlerBy)
		return wsEchoCallback(conn)
	})
	if err != nil {
		return nil, err
	}
	// The disposer is an ordinary reversible effect inverse.
	if err := ctx.Effect(func() (func() error, error) {
		return func() error {
			err := dispose()
			close(c.effectRan)
			return err
		}, nil
	}); err != nil {
		return nil, err
	}
	return nil, Serve(ctx, svc, c.addr)
}

func TestWSRegistrationIsReversibleEffect(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	addr := freeAddr(t)

	provider, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	if err := provider.Ready(ctx); err != nil {
		t.Fatalf("provider ready: %v", err)
	}
	dep := &wsEffectComponent{
		addr: addr, handlerBy: make(chan struct{}), effectRan: make(chan struct{}),
	}
	consumer, err := rt.Load(dep)
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := consumer.Ready(ctx); err != nil {
		t.Fatalf("consumer ready: %v", err)
	}

	c, _, err := websocket.DefaultDialer.Dial("ws://"+addr+"/ws/7", nil)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	mt, got := wsEcho(t, c, websocket.TextMessage, []byte("hi"))
	if mt != websocket.TextMessage || string(got) != "hi" {
		t.Fatalf("echo = (%d, %q)", mt, got)
	}

	if err := consumer.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := consumer.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	select {
	case <-dep.effectRan:
	default:
		t.Fatal("effect inverse did not run on disposal")
	}
	select {
	case <-dep.handlerBy:
	default:
		t.Fatal("handler goroutine not joined by the inverse")
	}
	if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("SetReadDeadline: %v", err)
	}
	if _, _, err := c.ReadMessage(); err == nil {
		t.Fatal("client did not observe close")
	}
	if _, _, err := websocket.DefaultDialer.Dial("ws://"+addr+"/ws/7", nil); err == nil {
		t.Fatal("route survived disposer")
	}
}

func TestWSMiddlewareAndRegistrationValidation(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	var hits atomic.Int32
	if _, err := svc.Use(func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			hits.Add(1)
			next.ServeHTTP(w, r)
		})
	}); err != nil {
		t.Fatalf("Use: %v", err)
	}
	if _, err := svc.WS("/ws", wsEchoCallback); err != nil {
		t.Fatalf("WS: %v", err)
	}
	c := dialWS(t, wsBase, "/ws", nil)
	mt, got := wsEcho(t, c, websocket.TextMessage, []byte("m"))
	if mt != websocket.TextMessage || string(got) != "m" {
		t.Fatalf("echo = (%d, %q)", mt, got)
	}
	if hits.Load() == 0 {
		t.Fatal("middleware did not run for the WebSocket upgrade")
	}

	if _, err := svc.WS("relative", wsEchoCallback); err == nil {
		t.Fatal("WS(relative): want invalid-pattern error")
	}
	if _, err := svc.WS("/nil", nil); err == nil {
		t.Fatal("WS(nil callback): want error")
	}
	if err := svc.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if _, err := svc.WS("/after", wsEchoCallback); err != errServiceClosed {
		t.Fatalf("WS after Close: got %v, want errServiceClosed", err)
	}
	if err := svc.Close(); err != nil {
		t.Fatalf("second Close: %v", err)
	}
}

// ---- callback-initiated disposal must not self-wait -----------------------

func TestWSCallbackShutdownDoesNotSelfWait(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	handlerDone := make(chan struct{})
	dispose, err := svc.WS("/ws", func(c *WSConn) error {
		defer close(handlerDone)
		if _, _, err := (*c).ReadMessage(); err != nil {
			return err
		}
		// Non-joining, registration-scoped disposal from inside the callback.
		if err := (*c).Shutdown(); err != nil {
			return err
		}
		return nil
	})
	if err != nil {
		t.Fatalf("WS: %v", err)
	}

	c := dialWS(t, wsBase, "/ws", nil)
	if err := c.WriteMessage(websocket.TextMessage, []byte("x")); err != nil {
		t.Fatalf("write: %v", err)
	}
	select {
	case <-handlerDone:
	case <-time.After(2 * time.Second):
		t.Fatal("callback deadlocked on WSConn.Shutdown")
	}
	if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
		t.Fatalf("SetReadDeadline: %v", err)
	}
	if _, _, err := c.ReadMessage(); err == nil {
		t.Fatal("client did not observe close after callback Shutdown")
	}

	// The joining inverse still works afterwards and joins handlers.
	closed := make(chan struct{})
	go func() { _ = svc.Close(); close(closed) }()
	select {
	case <-closed:
	case <-time.After(2 * time.Second):
		t.Fatal("Service.Close hung after callback Shutdown")
	}
	if err := dispose(); err != nil {
		t.Fatalf("dispose after Close: %v", err)
	}
	if err := svc.Shutdown(); err != nil {
		t.Fatalf("Shutdown after Close: %v", err)
	}
}

func TestWSCallbackServiceShutdownDoesNotSelfWait(t *testing.T) {
	svc, _, wsBase := newWSServer(t)
	aDone := make(chan struct{})
	if _, err := svc.WS("/a", func(c *WSConn) error {
		defer close(aDone)
		if _, _, err := (*c).ReadMessage(); err != nil {
			return err
		}
		return svc.Shutdown() // service-wide but still non-joining
	}); err != nil {
		t.Fatalf("WS /a: %v", err)
	}
	if _, err := svc.WS("/b", wsEchoCallback); err != nil {
		t.Fatalf("WS /b: %v", err)
	}

	ca := dialWS(t, wsBase, "/a", nil)
	cb := dialWS(t, wsBase, "/b", nil)
	if err := ca.WriteMessage(websocket.TextMessage, []byte("x")); err != nil {
		t.Fatalf("write /a: %v", err)
	}
	select {
	case <-aDone:
	case <-time.After(2 * time.Second):
		t.Fatal("callback deadlocked on Service.Shutdown")
	}
	for name, c := range map[string]*websocket.Conn{"a": ca, "b": cb} {
		if err := c.SetReadDeadline(time.Now().Add(2 * time.Second)); err != nil {
			t.Fatalf("client %s SetReadDeadline: %v", name, err)
		}
		if _, _, err := c.ReadMessage(); err == nil {
			t.Fatalf("client %s did not observe service-wide close", name)
		}
	}

	closed := make(chan struct{})
	go func() { _ = svc.Close(); close(closed) }()
	select {
	case <-closed:
	case <-time.After(2 * time.Second):
		t.Fatal("Service.Close hung after callback Service.Shutdown")
	}
}
