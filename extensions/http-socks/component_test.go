package socks

import (
	"context"
	"strings"
	"sync"
	"testing"
	"time"

	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

// fakeService records every registration call so tests can observe the
// component's reversible effect without a live HTTP server. It also tracks
// which proxy URLs are currently registered, so a test can prove that
// disposing one component never removes another component's contribution.
type fakeService struct {
	calls  chan string
	mu     sync.Mutex
	active map[string]bool
}

func newFakeService() *fakeService {
	return &fakeService{calls: make(chan string, 16), active: map[string]bool{}}
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
	f.calls <- "setproxy:" + raw
	return nil
}
func (f *fakeService) Close() error { return nil }
func (f *fakeService) RegisterProxy(schemes []string, proxyURL string) (func() error, error) {
	f.calls <- "register:" + proxyURL
	f.mu.Lock()
	f.active[proxyURL] = true
	f.mu.Unlock()
	var once sync.Once
	return func() error {
		once.Do(func() {
			f.calls <- "remove:" + proxyURL
			f.mu.Lock()
			delete(f.active, proxyURL)
			f.mu.Unlock()
		})
		return nil
	}, nil
}
func (f *fakeService) RegisterProxyDialer(schemes []string, dial exthttp.ProxyDialer) (func() error, error) {
	f.calls <- "register:dialer"
	var once sync.Once
	return func() error {
		once.Do(func() { f.calls <- "remove:dialer" })
		return nil
	}, nil
}
func (f *fakeService) isActive(url string) bool {
	f.mu.Lock()
	defer f.mu.Unlock()
	return f.active[url]
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
		t.Fatalf("timed out waiting for registration call: %v", ctx.Err())
		return ""
	}
}

func registerProvider(t *testing.T, rt *runtime.Runtime, fake *fakeService) *runtime.Fiber {
	t.Helper()
	pf, err := rt.Load(&httpProvider{svc: fake})
	if err != nil {
		t.Fatalf("load http provider: %v", err)
	}
	if err := pf.Ready(testCtx(t)); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	return pf
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

func TestComponentApplyAndDisposeRemovesOwnRegistration(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := newFakeService()
	registerProvider(t, rt, fake)

	want := "socks5://proxy.example:1080"
	c := &Component{Scheme: "socks5h", Addr: "proxy.example:1080"}
	sf, err := rt.Load(c)
	if err != nil {
		t.Fatalf("load socks component: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks Ready: %v", err)
	}

	if got := mustRecv(t, fake.calls, ctx); got != "register:"+want {
		t.Fatalf("RegisterProxy on Apply = %q, want register:%s", got, want)
	}
	if !fake.isActive(want) {
		t.Fatalf("%s not active after Apply", want)
	}

	if err := sf.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := sf.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "remove:"+want {
		t.Fatalf("inverse call = %q, want remove:%s", got, want)
	}
	if fake.isActive(want) {
		t.Fatalf("%s still active after Dispose", want)
	}
	// The inverse must never use the legacy global clear.
	select {
	case got := <-fake.calls:
		t.Fatalf("unexpected extra call %q (SetProxy clear?)", got)
	default:
	}
}

// Component.Apply must register socks4a://, not socks4://: collapsing the two
// would select the wrong h12.io/socks protocol.
func TestComponentRegistersSocks4aURL(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := newFakeService()
	registerProvider(t, rt, fake)

	want := "socks4a://proxy.example:1080"
	sf, err := rt.Load(&Component{Scheme: "socks4a", Addr: "proxy.example:1080"})
	if err != nil {
		t.Fatalf("load socks4a component: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks4a Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "register:"+want {
		t.Fatalf("registered URL = %q, want register:%s", got, want)
	}
	if !fake.isActive(want) {
		t.Fatalf("%s not active after Apply", want)
	}
}

func TestComponentAcceptsSocksAlias(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := newFakeService()
	registerProvider(t, rt, fake)

	want := "socks5://proxy.example:1080"
	sf, err := rt.Load(&Component{Scheme: "socks", Addr: "proxy.example:1080"})
	if err != nil {
		t.Fatalf("load socks component: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "register:"+want {
		t.Fatalf("registered URL = %q, want register:%s", got, want)
	}
}

// P1-03: disposing one SOCKS component must not clear a peer's proxy.
func TestDisposeOneKeepsOtherProxyRegistration(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := newFakeService()
	registerProvider(t, rt, fake)

	aURL := "socks5://a.example:1080"
	bURL := "socks4://b.example:1080"
	a, err := rt.Load(&Component{Scheme: "socks5", Addr: "a.example:1080"})
	if err != nil {
		t.Fatalf("load A: %v", err)
	}
	if err := a.Ready(ctx); err != nil {
		t.Fatalf("A Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "register:"+aURL {
		t.Fatalf("A registration = %q", got)
	}
	b, err := rt.Load(&Component{Scheme: "socks4", Addr: "b.example:1080"})
	if err != nil {
		t.Fatalf("load B: %v", err)
	}
	if err := b.Ready(ctx); err != nil {
		t.Fatalf("B Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "register:"+bURL {
		t.Fatalf("B registration = %q", got)
	}

	if err := b.Dispose(); err != nil {
		t.Fatalf("dispose B: %v", err)
	}
	if err := b.Gone(ctx); err != nil {
		t.Fatalf("B Gone: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "remove:"+bURL {
		t.Fatalf("B inverse = %q", got)
	}
	if fake.isActive(bURL) {
		t.Fatalf("%s still active after B disposal", bURL)
	}
	if !fake.isActive(aURL) {
		t.Fatalf("A's proxy %s was cleared by B's disposal", aURL)
	}
	if err := a.Dispose(); err != nil {
		t.Fatalf("dispose A: %v", err)
	}
	if err := a.Gone(ctx); err != nil {
		t.Fatalf("A Gone: %v", err)
	}
}

// Dependency loss is a withdraw to Pending (not a failure) and the reversible
// effect is unwound while the dependency is still readable; restoring the
// provider reactivates the component and reinstalls its own registration.
func TestDependencyLossWithdrawsAndRecoveryReactivates(t *testing.T) {
	ctx := testCtx(t)
	rt := newRT(t)
	fake := newFakeService()
	pf := registerProvider(t, rt, fake)

	want := "socks4a://proxy.example:1080"
	sf, err := rt.Load(&Component{Scheme: "socks4a", Addr: "proxy.example:1080"})
	if err != nil {
		t.Fatalf("load socks component: %v", err)
	}
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "register:"+want {
		t.Fatalf("RegisterProxy on Apply = %q", got)
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
	if got := mustRecv(t, fake.calls, ctx); got != "remove:"+want {
		t.Fatalf("inverse on withdraw = %q", got)
	}
	if st := sf.State(); st != runtime.StatePending {
		t.Fatalf("socks state after dependency loss = %v, want Pending", st)
	}

	registerProvider(t, rt, fake)
	if err := sf.Ready(ctx); err != nil {
		t.Fatalf("socks reactivation Ready: %v", err)
	}
	if got := mustRecv(t, fake.calls, ctx); got != "register:"+want {
		t.Fatalf("RegisterProxy on reactivation = %q", got)
	}
}

func TestComponentRejectsUnknownScheme(t *testing.T) {
	rt := newRT(t)
	fake := newFakeService()
	registerProvider(t, rt, fake)

	sf, err := rt.Load(&Component{Scheme: strings.ToUpper("http"), Addr: "proxy.example:8080"})
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if err := sf.Ready(testCtx(t)); err == nil {
		t.Fatal("Ready with unknown scheme should fail")
	}
}
