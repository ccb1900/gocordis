package http_test

import (
	"context"
	"errors"
	"io"
	"net"
	stdhttp "net/http"
	"net/http/httptest"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

type capture struct {
	ch chan exthttp.Service
}

func (c *capture) Name() string { return "capture" }
func (c *capture) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(exthttp.ServiceKey)}
}
func (c *capture) Provide() []runtime.Capability { return nil }
func (c *capture) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	s, err := runtime.Require(ctx, exthttp.ServiceKey)
	if err != nil {
		return nil, err
	}
	c.ch <- s
	return nil, nil
}

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func loadService(t *testing.T) (exthttp.Service, *runtime.Fiber) {
	t.Helper()
	return loadServiceWithConfig(t, exthttp.Config{})
}

func loadServiceWithConfig(t *testing.T, cfg exthttp.Config) (exthttp.Service, *runtime.Fiber) {
	t.Helper()
	ctx := testCtx(t)
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() { _ = rt.Close(testCtx(t)) })

	cap := &capture{ch: make(chan exthttp.Service, 1)}
	f, err := rt.Load(&exthttp.Component{Config: cfg})
	if err != nil {
		t.Fatalf("load provider: %v", err)
	}
	cf, err := rt.Load(cap)
	if err != nil {
		t.Fatalf("load consumer: %v", err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("provider Ready: %v", err)
	}
	if err := cf.Ready(ctx); err != nil {
		t.Fatalf("consumer Ready: %v", err)
	}
	select {
	case s := <-cap.ch:
		return s, f
	case <-ctx.Done():
		t.Fatal("service capability never delivered")
		return nil, nil
	}
}

func dispose(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
}

func mustRecv[T any](t *testing.T, ch <-chan T, d time.Duration) T {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(d):
		t.Fatalf("timed out after %v", d)
		var zero T
		return zero
	}
}

func TestGetPostAndDoRoundTrip(t *testing.T) {
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		switch r.URL.Path {
		case "/echo":
			body, _ := io.ReadAll(r.Body)
			w.Header().Set("X-Method", r.Method)
			w.Header().Set("X-From", r.Header.Get("X-From"))
			w.WriteHeader(stdhttp.StatusCreated)
			_, _ = w.Write(body)
		default:
			w.Header().Set("Content-Type", "text/plain")
			_, _ = w.Write([]byte("hello"))
		}
	}))
	defer srv.Close()

	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	got, err := svc.Get(ctx, srv.URL+"/hello")
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if got.StatusCode != stdhttp.StatusOK || string(got.Body) != "hello" {
		t.Fatalf("Get response = %d %q", got.StatusCode, got.Body)
	}
	if got.Header.Get("Content-Type") != "text/plain" {
		t.Fatalf("Get header = %q", got.Header.Get("Content-Type"))
	}

	post, err := svc.Post(ctx, srv.URL+"/echo", []byte("payload"))
	if err != nil {
		t.Fatalf("Post: %v", err)
	}
	if post.StatusCode != stdhttp.StatusCreated || string(post.Body) != "payload" {
		t.Fatalf("Post response = %d %q", post.StatusCode, post.Body)
	}
	if post.Header.Get("X-Method") != "POST" {
		t.Fatalf("Post method header = %q", post.Header.Get("X-Method"))
	}

	hdr := stdhttp.Header{}
	hdr.Set("X-From", "do-test")
	custom, err := svc.Do(ctx, exthttp.Request{
		Method: stdhttp.MethodPut,
		URL:    srv.URL + "/echo",
		Header: hdr,
		Body:   []byte("custom"),
	})
	if err != nil {
		t.Fatalf("Do: %v", err)
	}
	if custom.Header.Get("X-Method") != "PUT" || custom.Header.Get("X-From") != "do-test" || string(custom.Body) != "custom" {
		t.Fatalf("Do round-trip mismatch: %+v body=%q", custom.Header, custom.Body)
	}
}

func TestInterceptorsOrderMutationShortCircuitAndRemoval(t *testing.T) {
	var hits atomic.Int32
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		hits.Add(1)
		_, _ = w.Write([]byte(r.Header.Get("X-Mutated")))
	}))
	defer srv.Close()

	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	var mu sync.Mutex
	var order []string
	mk := func(name string, mutate bool) exthttp.Interceptor {
		return func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
			mu.Lock()
			order = append(order, name)
			mu.Unlock()
			if mutate {
				if req.Header == nil {
					req.Header = stdhttp.Header{}
				}
				req.Header.Set("X-Mutated", "yes")
			}
			return next(ctx, req)
		}
	}
	_, err := svc.AddInterceptor(mk("first", true))
	if err != nil {
		t.Fatalf("AddInterceptor first: %v", err)
	}
	_, err = svc.AddInterceptor(mk("second", false))
	if err != nil {
		t.Fatalf("AddInterceptor second: %v", err)
	}

	resp, err := svc.Get(ctx, srv.URL)
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if string(resp.Body) != "yes" {
		t.Fatalf("mutation not seen by server: body=%q", resp.Body)
	}
	mu.Lock()
	gotOrder := strings.Join(order, ",")
	order = nil
	mu.Unlock()
	if gotOrder != "first,second" {
		t.Fatalf("interceptor order = %q, want first,second", gotOrder)
	}

	before := hits.Load()
	remove, err := svc.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
		mu.Lock()
		order = append(order, "short")
		mu.Unlock()
		return &exthttp.Response{StatusCode: 299, Header: stdhttp.Header{}, Body: []byte("short-circuit")}, nil
	})
	if err != nil {
		t.Fatalf("AddInterceptor short-circuit: %v", err)
	}
	resp, err = svc.Get(ctx, srv.URL)
	if err != nil {
		t.Fatalf("Get (short-circuit): %v", err)
	}
	if resp.StatusCode != 299 || string(resp.Body) != "short-circuit" {
		t.Fatalf("short-circuit response = %d %q", resp.StatusCode, resp.Body)
	}
	if hits.Load() != before {
		t.Fatal("short-circuit interceptor still reached the server")
	}
	mu.Lock()
	gotOrder = strings.Join(order, ",")
	order = nil
	mu.Unlock()
	if gotOrder != "first,second,short" {
		t.Fatalf("interceptor order = %q, want first,second,short", gotOrder)
	}

	if err := remove(); err != nil {
		t.Fatalf("remove interceptor: %v", err)
	}
	if err := remove(); err != nil {
		t.Fatalf("remove interceptor must be idempotent: %v", err)
	}
	resp, err = svc.Get(ctx, srv.URL)
	if err != nil {
		t.Fatalf("Get (after removal): %v", err)
	}
	if string(resp.Body) != "yes" || resp.StatusCode != stdhttp.StatusOK {
		t.Fatalf("after removal response = %d %q", resp.StatusCode, resp.Body)
	}
}

func TestSetProxyAndInvalidURL(t *testing.T) {
	var proxied atomic.Int32
	proxy := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		proxied.Add(1)
		_, _ = w.Write([]byte(r.RequestURI))
	}))
	defer proxy.Close()

	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	if err := svc.SetProxy(proxy.URL); err != nil {
		t.Fatalf("SetProxy: %v", err)
	}
	resp, err := svc.Get(ctx, "http://example.invalid/proxied")
	if err != nil {
		t.Fatalf("Get via proxy: %v", err)
	}
	if proxied.Load() != 1 {
		t.Fatalf("proxy hit count = %d, want 1", proxied.Load())
	}
	if string(resp.Body) != "http://example.invalid/proxied" {
		t.Fatalf("proxy saw request URI %q", resp.Body)
	}

	for _, bad := range []string{"://bad", "not a url", "http://"} {
		if err := svc.SetProxy(bad); err == nil {
			t.Fatalf("SetProxy(%q) should fail", bad)
		}
	}
	// The last valid proxy is still in effect after rejected updates.
	if _, err := svc.Get(ctx, "http://example.invalid/again"); err != nil {
		t.Fatalf("Get via still-configured proxy: %v", err)
	}
	if proxied.Load() != 2 {
		t.Fatalf("proxy hit count = %d, want 2", proxied.Load())
	}
}

func TestTimeoutAndCancellation(t *testing.T) {
	entered := make(chan struct{}, 2)
	release := make(chan struct{})
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		select {
		case entered <- struct{}{}:
		default:
		}
		select {
		case <-release:
			_, _ = w.Write([]byte("late"))
		case <-r.Context().Done():
		}
	}))
	defer srv.Close()
	defer close(release)

	svc, f := loadService(t)
	defer dispose(t, f)

	_, err := svc.Do(testCtx(t), exthttp.Request{
		Method:  stdhttp.MethodGet,
		URL:     srv.URL + "/slow",
		Timeout: 60 * time.Millisecond,
	})
	mustRecv(t, entered, time.Second)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("Timeout error = %v, want context deadline exceeded", err)
	}
	if !errors.Is(err, exthttp.ErrTimeout) {
		t.Fatalf("Timeout error = %v, want ErrTimeout wrapper", err)
	}

	ctx, cancel := context.WithCancel(testCtx(t))
	done := make(chan error, 1)
	go func() {
		_, err := svc.Do(ctx, exthttp.Request{Method: stdhttp.MethodGet, URL: srv.URL + "/cancel"})
		done <- err
	}()
	mustRecv(t, entered, time.Second)
	cancel()
	if err := mustRecv(t, done, time.Second); !errors.Is(err, context.Canceled) {
		t.Fatalf("cancellation error = %v, want context canceled", err)
	}
}

func TestDisposeClosesIdleConnections(t *testing.T) {
	idle := make(chan struct{}, 8)
	closed := make(chan struct{}, 8)
	srv := httptest.NewUnstartedServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("ok"))
	}))
	srv.Config.ConnState = func(_ net.Conn, st stdhttp.ConnState) {
		switch st {
		case stdhttp.StateIdle:
			select {
			case idle <- struct{}{}:
			default:
			}
		case stdhttp.StateClosed:
			select {
			case closed <- struct{}{}:
			default:
			}
		}
	}
	srv.Start()
	defer srv.Close()

	svc, f := loadService(t)
	resp, err := svc.Get(testCtx(t), srv.URL)
	if err != nil {
		t.Fatalf("Get: %v", err)
	}
	if string(resp.Body) != "ok" {
		t.Fatalf("body = %q", resp.Body)
	}
	mustRecv(t, idle, time.Second) // keep-alive connection is idle

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	mustRecv(t, closed, time.Second) // disposal closed the idle connection
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
}

// TestConcurrentUse is mainly a race-detector exercise: interceptors are
// snapshotted under RLock and invoked outside the lock while requests overlap.
func TestConcurrentUse(t *testing.T) {
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("ok"))
	}))
	defer srv.Close()

	svc, f := loadService(t)
	defer dispose(t, f)

	_, err := svc.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
		return next(ctx, req)
	})
	if err != nil {
		t.Fatalf("AddInterceptor: %v", err)
	}

	const n = 8
	done := make(chan struct{}, n+1)
	for i := 0; i < n; i++ {
		go func() {
			defer func() { done <- struct{}{} }()
			resp, err := svc.Get(testCtx(t), srv.URL)
			if err != nil || string(resp.Body) != "ok" {
				t.Errorf("concurrent Get: %v %v", resp, err)
			}
		}()
	}
	go func() {
		defer func() { done <- struct{}{} }()
		for i := 0; i < n; i++ {
			remove, err := svc.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
				return next(ctx, req)
			})
			if err != nil {
				t.Errorf("concurrent AddInterceptor: %v", err)
				return
			}
			if err := remove(); err != nil {
				t.Errorf("concurrent remove: %v", err)
			}
		}
	}()
	for i := 0; i < n+1; i++ {
		mustRecv(t, done, 5*time.Second)
	}
}

type proxyProbe struct {
	srv     *httptest.Server
	name    string
	idle    chan struct{}
	closed  chan struct{}
	entered chan struct{}
	release chan struct{}
}

func newProxyProbe(t *testing.T, name string, block bool) *proxyProbe {
	t.Helper()
	p := &proxyProbe{
		name:    name,
		idle:    make(chan struct{}, 8),
		closed:  make(chan struct{}, 8),
		entered: make(chan struct{}, 8),
		release: make(chan struct{}),
	}
	p.srv = httptest.NewUnstartedServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		if block {
			select {
			case p.entered <- struct{}{}:
			default:
			}
			<-p.release
		}
		_, _ = w.Write([]byte(name))
	}))
	p.srv.Config.ConnState = func(_ net.Conn, st stdhttp.ConnState) {
		switch st {
		case stdhttp.StateIdle:
			select {
			case p.idle <- struct{}{}:
			default:
			}
		case stdhttp.StateClosed:
			select {
			case p.closed <- struct{}{}:
			default:
			}
		}
	}
	p.srv.Start()
	t.Cleanup(p.srv.Close)
	return p
}

// TestSetProxyClosesAllRetiredTransports catches a swap policy that closes
// only the immediately previous transport: p1 goes idle AFTER being retired,
// so only a later swap that closes every retired transport will reap it.
func TestSetProxyClosesAllRetiredTransports(t *testing.T) {
	p1 := newProxyProbe(t, "p1", true)
	p2 := newProxyProbe(t, "p2", false)
	p3 := newProxyProbe(t, "p3", false)
	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	if err := svc.SetProxy(p1.srv.URL); err != nil {
		t.Fatalf("SetProxy p1: %v", err)
	}
	type result struct {
		body string
		err  error
	}
	res := make(chan result, 1)
	go func() {
		resp, err := svc.Get(ctx, "http://example.invalid/one")
		if err != nil {
			res <- result{err: err}
			return
		}
		res <- result{body: string(resp.Body)}
	}()
	mustRecv(t, p1.entered, time.Second) // request in flight on p1's transport

	if err := svc.SetProxy(p2.srv.URL); err != nil { // swap 1: p1 retired while active
		t.Fatalf("SetProxy p2: %v", err)
	}
	close(p1.release)
	r := mustRecv(t, res, time.Second)
	if r.err != nil || r.body != "p1" {
		t.Fatalf("in-flight request = %q, %v", r.body, r.err)
	}
	mustRecv(t, p1.idle, time.Second) // p1's connection became idle after retirement

	if err := svc.SetProxy(p3.srv.URL); err != nil { // swap 2
		t.Fatalf("SetProxy p3: %v", err)
	}
	mustRecv(t, p1.closed, time.Second) // p1 is NOT the immediately old transport

	resp, err := svc.Get(ctx, "http://example.invalid/three")
	if err != nil {
		t.Fatalf("Get via p3: %v", err)
	}
	if string(resp.Body) != "p3" {
		t.Fatalf("p3 body = %q", resp.Body)
	}
	mustRecv(t, p3.idle, time.Second)

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	mustRecv(t, p3.closed, time.Second) // Close reaps the current transport too
	if err := f.Gone(testCtx(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
}

func TestStatusValidationAndTypedErrors(t *testing.T) {
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		w.WriteHeader(stdhttp.StatusNotFound)
		_, _ = w.Write([]byte("missing"))
	}))
	defer srv.Close()

	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	resp, err := svc.Get(ctx, srv.URL) // nil ExpectStatus: only 2xx accepted
	if !errors.Is(err, exthttp.ErrStatus) {
		t.Fatalf("error = %v, want ErrStatus wrapper", err)
	}
	var se *exthttp.StatusError
	if !errors.As(err, &se) || se.Code != stdhttp.StatusNotFound {
		t.Fatalf("status error = %v, want StatusError{Code: 404}", err)
	}
	if resp == nil || string(resp.Body) != "missing" {
		t.Fatalf("response body lost on status rejection: %v", resp)
	}

	accepted, err := svc.Do(ctx, exthttp.Request{
		Method:       stdhttp.MethodGet,
		URL:          srv.URL,
		ExpectStatus: func(code int) bool { return code == stdhttp.StatusNotFound },
	})
	if err != nil {
		t.Fatalf("Do with custom ExpectStatus: %v", err)
	}
	if accepted.StatusCode != stdhttp.StatusNotFound {
		t.Fatalf("status = %d, want 404", accepted.StatusCode)
	}
}

func TestRegisterProxyIdentityRestoresPrevious(t *testing.T) {
	p1 := newProxyProbe(t, "p1", false)
	p2 := newProxyProbe(t, "p2", false)
	direct := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("direct"))
	}))
	defer direct.Close()

	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)
	get := func(path string) string {
		t.Helper()
		resp, err := svc.Get(ctx, "http://example.invalid/"+path)
		if err != nil {
			t.Fatalf("Get %s: %v", path, err)
		}
		return string(resp.Body)
	}

	remove1, err := svc.RegisterProxy([]string{"http:"}, p1.srv.URL)
	if err != nil {
		t.Fatalf("RegisterProxy p1: %v", err)
	}
	if got := get("one"); got != "p1" {
		t.Fatalf("body = %q, want p1", got)
	}
	remove2, err := svc.RegisterProxy([]string{"http"}, p2.srv.URL)
	if err != nil {
		t.Fatalf("RegisterProxy p2: %v", err)
	}
	if got := get("two"); got != "p2" {
		t.Fatalf("latest registration did not win: body = %q", got)
	}
	if err := remove2(); err != nil {
		t.Fatalf("remove p2: %v", err)
	}
	if err := remove2(); err != nil {
		t.Fatalf("remove p2 must be idempotent: %v", err)
	}
	if got := get("three"); got != "p1" {
		t.Fatalf("previous registration not restored: body = %q", got)
	}

	// A registration for a different scheme must not change http routing.
	removeHTTPS, err := svc.RegisterProxy([]string{"https"}, p2.srv.URL)
	if err != nil {
		t.Fatalf("RegisterProxy https: %v", err)
	}
	if got := get("four"); got != "p1" {
		t.Fatalf("https registration affected http: body = %q", got)
	}
	if err := removeHTTPS(); err != nil {
		t.Fatalf("remove https: %v", err)
	}

	if err := remove1(); err != nil {
		t.Fatalf("remove p1: %v", err)
	}
	resp, err := svc.Get(ctx, direct.URL)
	if err != nil {
		t.Fatalf("Get direct: %v", err)
	}
	if string(resp.Body) != "direct" {
		t.Fatalf("direct body = %q", resp.Body)
	}
}

func TestSetProxySentinelIsIndependentOfRegistrations(t *testing.T) {
	p1 := newProxyProbe(t, "p1", false)
	p2 := newProxyProbe(t, "p2", false)
	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)
	get := func() string {
		t.Helper()
		resp, err := svc.Get(ctx, "http://example.invalid/probe")
		if err != nil {
			t.Fatalf("Get: %v", err)
		}
		return string(resp.Body)
	}

	remove, err := svc.RegisterProxy([]string{"http"}, p1.srv.URL)
	if err != nil {
		t.Fatalf("RegisterProxy p1: %v", err)
	}
	if got := get(); got != "p1" {
		t.Fatalf("body = %q, want p1", got)
	}
	if err := svc.SetProxy(p2.srv.URL); err != nil {
		t.Fatalf("SetProxy p2: %v", err)
	}
	if got := get(); got != "p2" {
		t.Fatalf("sentinel did not win: body = %q", got)
	}
	if err := svc.SetProxy(""); err != nil {
		t.Fatalf("SetProxy clear: %v", err)
	}
	if got := get(); got != "p1" {
		t.Fatalf("clearing sentinel lost registration: body = %q", got)
	}
	if err := remove(); err != nil {
		t.Fatalf("remove p1: %v", err)
	}
}

// P1-06: Close must wait for an in-flight request (tracked by the service
// WaitGroup) before returning, and must refuse new requests immediately.
func TestCloseWaitsForInFlightAndRejectsNew(t *testing.T) {
	entered := make(chan struct{})
	release := make(chan struct{})
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("ok"))
	}))
	defer srv.Close()

	svc, f := loadService(t)
	_, err := svc.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
		close(entered)
		<-release // deliberately ignores ctx: only the test releases it
		return next(ctx, req)
	})
	if err != nil {
		t.Fatalf("AddInterceptor: %v", err)
	}

	doDone := make(chan struct{})
	go func() {
		defer close(doDone)
		// Close cancels the service context, so the request may finish with
		// context.Canceled after the interceptor is released; what matters is
		// that Close does not return until this call has finished.
		_, _ = svc.Get(testCtx(t), srv.URL)
	}()
	mustRecv(t, entered, time.Second)

	closeCalled := make(chan struct{})
	closeDone := make(chan struct{})
	go func() {
		close(closeCalled)
		_ = svc.Close()
		close(closeDone)
	}()
	<-closeCalled
	select {
	case <-closeDone:
		t.Fatal("Close returned while a request was still in flight")
	default:
	}
	if _, err := svc.Get(testCtx(t), srv.URL); !errors.Is(err, exthttp.ErrClosed) {
		t.Fatalf("new Do after Close = %v, want ErrClosed", err)
	}

	close(release)
	mustRecv(t, doDone, time.Second)
	mustRecv(t, closeDone, time.Second)
	dispose(t, f)
}

// Close cancels active requests via the service context, so the blocked
// handler observes r.Context().Done() and the request finishes deterministically.
func TestCloseCancelsActiveRequests(t *testing.T) {
	entered := make(chan struct{}, 1)
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		select {
		case entered <- struct{}{}:
		default:
		}
		<-r.Context().Done()
	}))
	defer srv.Close()

	svc, f := loadServiceWithConfig(t, exthttp.Config{CloseTimeout: 2 * time.Second})
	doDone := make(chan error, 1)
	go func() {
		_, err := svc.Get(testCtx(t), srv.URL)
		doDone <- err
	}()
	mustRecv(t, entered, time.Second)

	closeDone := make(chan struct{})
	go func() {
		_ = svc.Close()
		close(closeDone)
	}()
	if err := mustRecv(t, doDone, time.Second); !errors.Is(err, context.Canceled) {
		t.Fatalf("canceled request error = %v, want context.Canceled", err)
	}
	mustRecv(t, closeDone, time.Second)
	if _, err := svc.Get(testCtx(t), srv.URL); !errors.Is(err, exthttp.ErrClosed) {
		t.Fatalf("new Do after Close = %v, want ErrClosed", err)
	}
	dispose(t, f)
}

func TestCloseTimeoutBoundsGracefulWait(t *testing.T) {
	entered := make(chan struct{})
	release := make(chan struct{})
	svc, f := loadServiceWithConfig(t, exthttp.Config{CloseTimeout: 50 * time.Millisecond})
	_, err := svc.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
		close(entered)
		<-release // ignores ctx, so Close can only wait for the timeout
		return next(ctx, req)
	})
	if err != nil {
		t.Fatalf("AddInterceptor: %v", err)
	}
	doDone := make(chan struct{})
	go func() {
		defer close(doDone)
		_, _ = svc.Get(testCtx(t), "http://example.invalid/never")
	}()
	mustRecv(t, entered, time.Second)

	closeDone := make(chan struct{})
	go func() {
		_ = svc.Close()
		close(closeDone)
	}()
	mustRecv(t, closeDone, 2*time.Second) // bounded by Config.CloseTimeout
	close(release)
	mustRecv(t, doDone, time.Second)
	dispose(t, f)
}

// A dialer-based registration must override the request destination through
// the transport's DialContext, and removing it must restore direct dialing.
func TestRegisterProxyDialerRoutesAndRemoves(t *testing.T) {
	target := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("via-dialer"))
	}))
	defer target.Close()
	direct := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("direct"))
	}))
	defer direct.Close()

	svc, f := loadService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	targetAddr := strings.TrimPrefix(target.URL, "http://")
	dial := func(ctx context.Context, network, _ string) (net.Conn, error) {
		return (&net.Dialer{}).DialContext(ctx, network, targetAddr)
	}
	remove, err := svc.RegisterProxyDialer([]string{"http"}, dial)
	if err != nil {
		t.Fatalf("RegisterProxyDialer: %v", err)
	}
	resp, err := svc.Get(ctx, "http://example.invalid/routed")
	if err != nil {
		t.Fatalf("Get via dialer: %v", err)
	}
	if string(resp.Body) != "via-dialer" {
		t.Fatalf("body = %q, want via-dialer", resp.Body)
	}
	if err := remove(); err != nil {
		t.Fatalf("remove: %v", err)
	}
	resp, err = svc.Get(ctx, direct.URL)
	if err != nil {
		t.Fatalf("Get direct: %v", err)
	}
	if string(resp.Body) != "direct" {
		t.Fatalf("body = %q, want direct", resp.Body)
	}
}

func TestRegisterProxyAcceptsSocksAliases(t *testing.T) {
	svc, f := loadService(t)
	defer dispose(t, f)
	for _, raw := range []string{"socks://127.0.0.1:1080", "socks4a://127.0.0.1:1080"} {
		remove, err := svc.RegisterProxy([]string{"http"}, raw)
		if err != nil {
			t.Errorf("RegisterProxy(%q): %v", raw, err)
			continue
		}
		if err := remove(); err != nil {
			t.Errorf("remove(%q): %v", raw, err)
		}
	}
}
