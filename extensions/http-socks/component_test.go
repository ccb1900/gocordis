package socks

import (
	"context"
	"testing"
	"time"

	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

// fakeService records every SetProxy call so tests can observe the component's
// reversible effect without a live HTTP server. It is provided under the real
// exthttp.ServiceKey so the socks component's declaration resolves normally.
type fakeService struct {
	calls chan string
}

func (f *fakeService) Do(context.Context, exthttp.Request) (*exthttp.Response, error) {
	return &exthttp.Response{}, nil
}
func (f *fakeService) Get(context.Context, string) (*exthttp.Response, error) {
	return &exthttp.Response{}, nil
}
func (f *fakeService) Post(context.Context, string, []byte) (*exthttp.Response, error) {
	return &exthttp.Response{}, nil
}
func (f *fakeService) AddInterceptor(exthttp.Interceptor) (func() error, error) {
	return func() error { return nil }, nil
}
func (f *fakeService) SetProxy(raw string) error {
	f.calls <- raw
	return nil
}

type httpProvider struct{ svc exthttp.Service }

func (c *httpProvider) Name() string                 { return "fake-http" }
func (c *httpProvider) Inject() []runtime.Dependency { return nil }
func (c *httpProvider) Provide() []runtime.Capability {
	return []runtime.Capability{exthttp.ServiceKey.Capability()}
}
func (c *httpProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, exthttp.ServiceKey, c.svc)
}

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

func mustRecv(t *testing.T, ch <-chan string, ctx context.Context) string {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-ctx.Done():
		t.Fatalf("timed out waiting for SetProxy call: %v", ctx.Err())
		return ""
	}
}

func TestComponentDeclarations(t *testing.T) {
	c := &Component{Scheme: "socks5", Addr: "127.0.0.1:1080"}
	if got := c.Name(); got != "http-socks" {
		t.Fatalf("Name() = %q, want http-socks", got)
	}
	if got := c.Provide(); got != nil {
		t.Fatalf("Provide() = %v, want nil", got)
	}
	inject := c.Inject()
	if len(inject) != 1 || inject[0].Key != exthttp.ServiceKey.Capability() {
		t.Fatalf("Inject() = %v, want single http.ServiceKey dependency", inject)
	}
}

func TestComponentApplyAndDisposeRestoresProxy(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := &fakeService{calls: make(chan string, 8)}

	pf, err := rt.Load(&httpProvider{svc: fake})
	if err != nil {
		t.Fatalf("load http provider: %v", err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}

	c := &Component{Scheme: "socks5h", Addr: "proxy.example:1080"}
	sf, err := rt.Load(c)
	if err != nil {
		t.Fatalf("load socks component: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks Ready: %v", err)
	}

	if got := mustRecv(t, fake.calls, ctx); got != "socks5://proxy.example:1080" {
		t.Fatalf("SetProxy on Apply = %q, want socks5://proxy.example:1080", got)
	}

	if err := sf.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := sf.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "" {
		t.Fatalf("SetProxy on Dispose = %q, want empty string", got)
	}
}

// Dependency loss is a withdraw to Pending (not a failure) and the reversible
// effect is unwound while the dependency is still readable; restoring the
// provider reactivates the component and reinstalls the proxy.
func TestDependencyLossWithdrawsAndRecoveryReactivates(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := &fakeService{calls: make(chan string, 8)}

	pf, err := rt.Load(&httpProvider{svc: fake})
	if err != nil {
		t.Fatalf("load http provider: %v", err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	sf, err := rt.Load(&Component{Scheme: "socks4a", Addr: "proxy.example:1080"})
	if err != nil {
		t.Fatalf("load socks component: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "socks4://proxy.example:1080" {
		t.Fatalf("SetProxy on Apply = %q", got)
	}

	if err := pf.Dispose(); err != nil {
		t.Fatalf("dispose provider: %v", err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatalf("provider Gone: %v", err)
	}
	if err := sf.WaitInactive(ctx); err != nil {
		t.Fatalf("socks WaitInactive: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "" {
		t.Fatalf("SetProxy on withdraw = %q, want empty string", got)
	}
	if st := sf.State(); st != runtime.StatePending {
		t.Fatalf("socks state after dependency loss = %v, want Pending", st)
	}

	pf2, err := rt.Load(&httpProvider{svc: fake})
	if err != nil {
		t.Fatalf("reload http provider: %v", err)
	}
	if err := pf2.Ready(ctx); err != nil {
		t.Fatalf("reloaded provider Ready: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks reactivation Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "socks4://proxy.example:1080" {
		t.Fatalf("SetProxy on reactivation = %q", got)
	}
}
