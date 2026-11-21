package proxy

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type services struct {
	serverCh chan server.Service
	httpCh   chan exthttp.Service
}

func (c *services) Name() string { return "test-services-capture" }
func (c *services) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(server.ServiceKey),
		runtime.Requires(exthttp.ServiceKey),
	}
}
func (c *services) Provide() []runtime.Capability { return nil }
func (c *services) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	hc, err := runtime.Require(ctx, exthttp.ServiceKey)
	if err != nil {
		return nil, err
	}
	c.serverCh <- svc
	c.httpCh <- hc
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
	cap := &services{serverCh: make(chan server.Service, 1), httpCh: make(chan exthttp.Service, 1)}
	for _, c := range []runtime.Component{&server.Component{}, &exthttp.Component{}, cap} {
		f, err := rt.Load(c)
		if err != nil {
			t.Fatalf("load %s: %v", c.Name(), err)
		}
		if err := f.Ready(ctx); err != nil {
			t.Fatalf("%s Ready: %v", c.Name(), err)
		}
	}
	select {
	case svc := <-cap.serverCh:
		<-cap.httpCh
		return rt, svc
	case <-ctx.Done():
		t.Fatal("service never delivered")
		return nil, nil
	}
}

func serve(svc server.Service, method, target, body string, header http.Header) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, target, strings.NewReader(body))
	for k, vs := range header {
		for _, v := range vs {
			req.Header.Add(k, v)
		}
	}
	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, req)
	return rec
}

func TestProxyForwardsMethodHeadersBodyAndResponse(t *testing.T) {
	type seen struct {
		method string
		header string
		body   string
	}
	got := make(chan seen, 2)
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		got <- seen{method: r.Method, header: r.Header.Get("X-Client"), body: string(b)}
		w.Header().Set("X-Backend", "present")
		w.WriteHeader(http.StatusCreated)
		_, _ = io.WriteString(w, "from-backend")
	}))
	defer backend.Close()

	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: backend.URL})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}

	hdr := http.Header{}
	hdr.Set("X-Client", "value-1")
	rec := serve(svc, http.MethodPost, "/p", "request-body", hdr)
	if rec.Code != http.StatusCreated {
		t.Fatalf("status = %d, want 201", rec.Code)
	}
	if rec.Header().Get("X-Backend") != "present" {
		t.Fatalf("response headers = %v", rec.Header())
	}
	if rec.Body.String() != "from-backend" {
		t.Fatalf("body = %q", rec.Body.String())
	}
	s := <-got
	if s.method != http.MethodPost || s.header != "value-1" || s.body != "request-body" {
		t.Fatalf("backend saw %+v", s)
	}

	rec = serve(svc, http.MethodGet, "/p", "", nil)
	if rec.Code != http.StatusCreated {
		t.Fatalf("GET status = %d, want 201", rec.Code)
	}
	if s = <-got; s.method != http.MethodGet {
		t.Fatalf("backend method = %q, want GET", s.method)
	}
}

func TestProxyTransportErrorIsBadGateway(t *testing.T) {
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
	}))
	target := backend.URL
	backend.Close() // no fixed sleeps: every subsequent Do must fail

	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: target})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	rec := serve(svc, http.MethodGet, "/p", "", nil)
	if rec.Code != http.StatusBadGateway {
		t.Fatalf("status = %d, want 502", rec.Code)
	}
}

func TestProxyDisposeRemovesRoute(t *testing.T) {
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, "ok")
	}))
	defer backend.Close()

	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/gone", Target: backend.URL})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	if rec := serve(svc, http.MethodGet, "/gone", "", nil); rec.Code != http.StatusOK {
		t.Fatalf("before dispose: code=%d", rec.Code)
	}
	if err := comp.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := comp.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if rec := serve(svc, http.MethodGet, "/gone", "", nil); rec.Code != http.StatusNotFound {
		t.Fatalf("after dispose: code=%d, want 404", rec.Code)
	}
}

func TestComponentMetadata(t *testing.T) {
	c := &Component{Route: "/p", Target: "http://example.invalid"}
	if c.Name() != "server-proxy" {
		t.Fatalf("Name = %q", c.Name())
	}
	if c.Provide() != nil {
		t.Fatalf("Provide = %v, want nil", c.Provide())
	}
	if len(c.Inject()) != 2 {
		t.Fatalf("Inject len = %d, want 2", len(c.Inject()))
	}
}
