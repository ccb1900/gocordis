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

func call(h http.Handler, host string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(http.MethodGet, "http://"+host+"/x", nil)
	rec := httptest.NewRecorder()
	h.ServeHTTP(rec, req)
	return rec
}

func TestMiddlewareAllowExactAndPort(t *testing.T) {
	h := Middleware([]string{"example.com"}, nil)(okHandler())
	if rec := call(h, "example.com"); rec.Code != http.StatusOK || rec.Body.String() != "ok" {
		t.Fatalf("allowed host: code=%d body=%q", rec.Code, rec.Body.String())
	}
	if rec := call(h, "example.com:8443"); rec.Code != http.StatusOK {
		t.Fatalf("host with port: code=%d, want 200", rec.Code)
	}
	if rec := call(h, "other.com"); rec.Code != http.StatusForbidden {
		t.Fatalf("unknown host: code=%d, want 403", rec.Code)
	}
}

func TestMiddlewareWildcard(t *testing.T) {
	h := Middleware([]string{"*.example.com"}, nil)(okHandler())
	if rec := call(h, "a.example.com"); rec.Code != http.StatusOK {
		t.Fatalf("subdomain: code=%d, want 200", rec.Code)
	}
	if rec := call(h, "a.b.example.com"); rec.Code != http.StatusOK {
		t.Fatalf("deep subdomain: code=%d, want 200", rec.Code)
	}
	if rec := call(h, "example.com"); rec.Code != http.StatusForbidden {
		t.Fatalf("bare apex: code=%d, want 403", rec.Code)
	}
	if rec := call(h, "evil-example.com"); rec.Code != http.StatusForbidden {
		t.Fatalf("lookalike: code=%d, want 403", rec.Code)
	}
}

func TestMiddlewareDenyWins(t *testing.T) {
	h := Middleware([]string{"*.example.com"}, []string{"bad.example.com"})(okHandler())
	if rec := call(h, "bad.example.com"); rec.Code != http.StatusForbidden {
		t.Fatalf("denied host also allowed: code=%d, want 403", rec.Code)
	}
	if rec := call(h, "good.example.com"); rec.Code != http.StatusOK {
		t.Fatalf("allowed host: code=%d, want 200", rec.Code)
	}

	// Empty allow means "allow all except denied".
	h = Middleware(nil, []string{"blocked.test"})(okHandler())
	if rec := call(h, "blocked.test"); rec.Code != http.StatusForbidden {
		t.Fatalf("deny with no allow: code=%d, want 403", rec.Code)
	}
	if rec := call(h, "any.test"); rec.Code != http.StatusOK {
		t.Fatalf("non-denied with no allow: code=%d, want 200", rec.Code)
	}
}

func TestComponentInstallsAndDisposesMiddleware(t *testing.T) {
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
	if rec := call(svc, "example.com"); rec.Code != http.StatusOK {
		t.Fatalf("allowed after install: code=%d, want 200", rec.Code)
	}
	if rec := call(svc, "evil.test"); rec.Code != http.StatusForbidden {
		t.Fatalf("denied after install: code=%d, want 403", rec.Code)
	}

	if err := comp.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := comp.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if rec := call(svc, "evil.test"); rec.Code != http.StatusOK {
		t.Fatalf("after dispose: code=%d, want 200 (middleware withdrawn)", rec.Code)
	}
}
