package server

import (
	"context"
	"io"
	"net"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"sync"
	"testing"
	"time"

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

	second := portAt(t, addr, 1)
	resp, err := http.Get("http://" + second + "/ping") //nolint:gosec // test-local
	if err != nil {
		t.Fatalf("GET on fallback port: %v", err)
	}
	body, err := io.ReadAll(resp.Body)
	_ = resp.Body.Close()
	if err != nil || string(body) != "range-pong" {
		t.Fatalf("body = %q, err = %v", body, err)
	}

	if err := consumer.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := consumer.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	rebound, err := net.Listen("tcp", second)
	if err != nil {
		t.Fatalf("rebind fallback after Gone: %v", err)
	}
	_ = rebound.Close()
}

func TestServeRangeExhaustedFailsActivation(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	addr := freeAddr(t)

	h1, err := net.Listen("tcp", addr)
	if err != nil {
		t.Fatalf("hold port 1: %v", err)
	}
	defer h1.Close()
	h2, err := net.Listen("tcp", portAt(t, addr, 1))
	if err != nil {
		t.Fatalf("hold port 2: %v", err)
	}
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
