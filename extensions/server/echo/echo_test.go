package echo

import (
	"context"
	"net/http"
	"net/http/httptest"
	"strings"
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

func serve(svc server.Service, method, target, body string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(method, target, strings.NewReader(body))
	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, req)
	return rec
}

func TestEchoGetAndPost(t *testing.T) {
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Prefix: "/api/"})
	if err != nil {
		t.Fatalf("load echo: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("echo Ready: %v", err)
	}

	rec := serve(svc, http.MethodGet, "/api/echo?msg=hello+world", "")
	if rec.Code != http.StatusOK || rec.Body.String() != "hello world" {
		t.Fatalf("GET echo: code=%d body=%q", rec.Code, rec.Body.String())
	}
	rec = serve(svc, http.MethodPost, "/api/echo", "payload-body")
	if rec.Code != http.StatusOK || rec.Body.String() != "payload-body" {
		t.Fatalf("POST echo: code=%d body=%q", rec.Code, rec.Body.String())
	}
	// Wrong method on the same path is a 405, not a 404.
	rec = serve(svc, http.MethodPut, "/api/echo", "")
	if rec.Code != http.StatusMethodNotAllowed {
		t.Fatalf("PUT echo: code=%d, want 405", rec.Code)
	}
}

func TestEchoEmptyPrefixUsesRootPath(t *testing.T) {
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	comp, err := rt.Load(&Component{})
	if err != nil {
		t.Fatalf("load echo: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("echo Ready: %v", err)
	}
	rec := serve(svc, http.MethodGet, "/echo?msg=root", "")
	if rec.Code != http.StatusOK || rec.Body.String() != "root" {
		t.Fatalf("GET /echo: code=%d body=%q", rec.Code, rec.Body.String())
	}
}

func TestEchoDisposeRemovesHandlers(t *testing.T) {
	rt, svc := loadHarness(t)
	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Prefix: "/v1/"})
	if err != nil {
		t.Fatalf("load echo: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("echo Ready: %v", err)
	}
	if rec := serve(svc, http.MethodGet, "/v1/echo?msg=alive", ""); rec.Code != http.StatusOK {
		t.Fatalf("before dispose: code=%d", rec.Code)
	}
	if err := comp.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := comp.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	for _, m := range []string{http.MethodGet, http.MethodPost} {
		if rec := serve(svc, m, "/v1/echo", ""); rec.Code != http.StatusNotFound {
			t.Fatalf("%s after dispose: code=%d, want 404", m, rec.Code)
		}
	}
}

func TestComponentMetadata(t *testing.T) {
	c := &Component{Prefix: "/x/"}
	if c.Name() != "server-echo" {
		t.Fatalf("Name = %q", c.Name())
	}
	if c.Provide() != nil {
		t.Fatalf("Provide = %v, want nil", c.Provide())
	}
	if len(c.Inject()) != 1 || c.Inject()[0].Key != server.ServiceKey.Capability() {
		t.Fatalf("Inject = %v, want server.ServiceKey only", c.Inject())
	}
}
