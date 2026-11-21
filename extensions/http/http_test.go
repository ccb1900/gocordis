package http_test

import (
	"bytes"
	"context"
	"errors"
	"io"
	"net"
	stdhttp "net/http"
	"net/http/httptest"
	"net/url"
	"reflect"
	"strings"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"github.com/gorilla/websocket"

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

// The new capability is deliberately a separate interface so that existing
// third-party Service implementations (which only know Do/Get/Post/...) keep
// compiling. A real service always implements it.
func asStreamService(t *testing.T, svc exthttp.Service) exthttp.StreamService {
	t.Helper()
	ss, ok := svc.(exthttp.StreamService)
	if !ok {
		t.Fatalf("service %T does not implement StreamService", svc)
	}
	return ss
}

func loadStreamService(t *testing.T) (exthttp.StreamService, *runtime.Fiber) {
	t.Helper()
	svc, f := loadService(t)
	return asStreamService(t, svc), f
}

// r1.md HTTP equivalence: a stream response must be handed to the caller
// before the whole body arrives. The server writes one chunk, flushes and
// blocks; the test reads that first chunk while the server is still blocked.
func TestDoStreamStreamsBodyInChunks(t *testing.T) {
	const chunks = 8
	chunk := strings.Repeat("x", 64*1024)
	wroteFirst := make(chan struct{})
	release := make(chan struct{})
	var releaseOnce sync.Once
	unblock := func() { releaseOnce.Do(func() { close(release) }) }
	defer unblock()

	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		w.Header().Set("Content-Type", "application/octet-stream")
		w.WriteHeader(stdhttp.StatusOK)
		_, _ = io.WriteString(w, chunk)
		w.(stdhttp.Flusher).Flush()
		close(wroteFirst)
		<-release
		for i := 1; i < chunks; i++ {
			_, _ = io.WriteString(w, chunk)
		}
	}))
	defer srv.Close()

	ss, f := loadStreamService(t)
	defer dispose(t, f)

	type streamResult struct {
		resp *exthttp.StreamResponse
		err  error
	}
	res := make(chan streamResult, 1)
	go func() {
		resp, err := ss.DoStream(testCtx(t), exthttp.Request{Method: stdhttp.MethodGet, URL: srv.URL})
		res <- streamResult{resp, err}
	}()
	got := mustRecv(t, res, 2*time.Second)
	if got.err != nil {
		t.Fatalf("DoStream: %v", got.err)
	}
	resp := got.resp
	defer resp.Body.Close()
	if resp.StatusCode != stdhttp.StatusOK || resp.Header.Get("Content-Type") != "application/octet-stream" {
		t.Fatalf("stream status/header = %d %q", resp.StatusCode, resp.Header.Get("Content-Type"))
	}
	mustRecv(t, wroteFirst, time.Second)

	first := make([]byte, len(chunk))
	firstErr := make(chan error, 1)
	go func() {
		_, err := io.ReadFull(resp.Body, first)
		firstErr <- err
	}()
	if err := mustRecv(t, firstErr, 2*time.Second); err != nil {
		t.Fatalf("first chunk did not arrive while server blocked: %v", err)
	}
	if string(first) != chunk {
		t.Fatal("first chunk mismatch")
	}
	unblock()
	rest, err := io.ReadAll(resp.Body)
	if err != nil {
		t.Fatalf("read remaining chunks: %v", err)
	}
	if len(rest) != len(chunk)*(chunks-1) {
		t.Fatalf("remaining body = %d bytes, want %d", len(rest), len(chunk)*(chunks-1))
	}
}

func TestDoStreamTimeoutContextAndInterceptors(t *testing.T) {
	entered := make(chan struct{}, 2)
	release := make(chan struct{})
	var releaseOnce sync.Once
	unblock := func() { releaseOnce.Do(func() { close(release) }) }
	defer unblock()
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		select {
		case entered <- struct{}{}:
		default:
		}
		select {
		case <-release:
			_, _ = io.WriteString(w, r.Header.Get("X-Stream-Interceptor"))
		case <-r.Context().Done():
		}
	}))
	defer srv.Close()

	ss, f := loadStreamService(t)
	defer dispose(t, f)

	_, err := ss.AddStreamInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.StreamResponse, error)) (*exthttp.StreamResponse, error) {
		req.Header.Set("X-Stream-Interceptor", "streamed")
		return next(ctx, req)
	})
	if err != nil {
		t.Fatalf("AddStreamInterceptor: %v", err)
	}

	// req.Timeout is honored by DoStream, with the same error wrapping as Do.
	_, err = ss.DoStream(testCtx(t), exthttp.Request{URL: srv.URL, Timeout: 60 * time.Millisecond})
	mustRecv(t, entered, time.Second)
	if !errors.Is(err, exthttp.ErrTimeout) || !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("DoStream timeout error = %v", err)
	}

	// Caller cancellation is honored too.
	ctx, cancel := context.WithCancel(testCtx(t))
	done := make(chan error, 1)
	go func() {
		_, e := ss.DoStream(ctx, exthttp.Request{URL: srv.URL})
		done <- e
	}()
	mustRecv(t, entered, time.Second)
	cancel()
	if err := mustRecv(t, done, time.Second); !errors.Is(err, context.Canceled) {
		t.Fatalf("DoStream cancel error = %v, want context.Canceled", err)
	}

	// GetStream is the convenience form and still runs the interceptor chain.
	unblock()
	resp, err := ss.GetStream(testCtx(t), srv.URL)
	if err != nil {
		t.Fatalf("GetStream: %v", err)
	}
	defer resp.Body.Close()
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		t.Fatalf("read stream body: %v", err)
	}
	if string(body) != "streamed" {
		t.Fatalf("stream interceptor body = %q, want streamed", body)
	}
}

func TestDecoderSelectionParamsLatestWinsAndRestores(t *testing.T) {
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		w.Header().Set("Content-Type", r.URL.Query().Get("ct"))
		_, _ = w.Write([]byte(`{"n":7}`))
	}))
	defer srv.Close()

	ss, f := loadStreamService(t)
	defer dispose(t, f)
	ctx := testCtx(t)
	fetch := func(ct string) *exthttp.StreamResponse {
		t.Helper()
		resp, err := ss.GetStream(ctx, srv.URL+"?ct="+url.QueryEscape(ct))
		if err != nil {
			t.Fatalf("GetStream(%q): %v", ct, err)
		}
		return resp
	}

	// Built-in application/json decoder is found via the media type when the
	// Content-Type carries parameters. The package-level helper stays compatible.
	var out struct {
		N int `json:"n"`
	}
	resp := fetch("application/json; charset=utf-8")
	if err := exthttp.Decode(resp, &out); err != nil {
		t.Fatalf("package Decode builtin json: %v", err)
	}
	if out.N != 7 {
		t.Fatalf("decoded n = %d, want 7", out.N)
	}
	out2 := struct {
		N int `json:"n"`
	}{}
	if err := ss.Decode(fetch("application/json; charset=utf-8"), &out2); err != nil || out2.N != 7 {
		t.Fatalf("service Decode builtin json: %v n=%d", err, out2.N)
	}

	customCT := "application/vnd.cordis.test+json; charset=utf-8"
	var calls int
	removeExact, err := ss.RegisterDecoder(customCT, func(r io.Reader, out any) error {
		calls++
		_, _ = io.Copy(io.Discard, r)
		*(out.(*string)) = "exact"
		return nil
	})
	if err != nil {
		t.Fatalf("RegisterDecoder exact: %v", err)
	}
	decodeString := func(ct string) string {
		t.Helper()
		var s string
		if err := ss.Decode(fetch(ct), &s); err != nil {
			t.Fatalf("Decode(%q): %v", ct, err)
		}
		return s
	}
	if got := decodeString(customCT); got != "exact" || calls != 1 {
		t.Fatalf("exact decoder = %q calls=%d", got, calls)
	}

	// An exact registration beats one for the bare media type.
	removeBare, err := ss.RegisterDecoder("application/vnd.cordis.test+json", func(r io.Reader, out any) error {
		_, _ = io.Copy(io.Discard, r)
		*(out.(*string)) = "bare"
		return nil
	})
	if err != nil {
		t.Fatalf("RegisterDecoder bare: %v", err)
	}
	if got := decodeString(customCT); got != "exact" {
		t.Fatalf("exact did not beat bare media type: %q", got)
	}

	removeLatest, err := ss.RegisterDecoder(customCT, func(r io.Reader, out any) error {
		_, _ = io.Copy(io.Discard, r)
		*(out.(*string)) = "latest"
		return nil
	})
	if err != nil {
		t.Fatalf("RegisterDecoder latest: %v", err)
	}
	if got := decodeString(customCT); got != "latest" {
		t.Fatalf("latest decoder = %q", got)
	}
	if err := removeLatest(); err != nil {
		t.Fatalf("remove latest: %v", err)
	}
	if err := removeLatest(); err != nil {
		t.Fatalf("remove latest must be idempotent: %v", err)
	}
	if got := decodeString(customCT); got != "exact" {
		t.Fatalf("previous decoder not restored: %q", got)
	}
	if err := removeExact(); err != nil {
		t.Fatalf("remove exact: %v", err)
	}
	if got := decodeString(customCT); got != "bare" {
		t.Fatalf("bare media-type decoder not restored: %q", got)
	}
	if err := removeBare(); err != nil {
		t.Fatalf("remove bare: %v", err)
	}

	unknown := &exthttp.StreamResponse{
		Header: stdhttp.Header{"Content-Type": {"application/x-unregistered-test"}},
		Body:   io.NopCloser(strings.NewReader("raw")),
	}
	if err := ss.Decode(unknown, &out); err == nil || !strings.Contains(err.Error(), "application/x-unregistered-test") {
		t.Fatalf("unknown decoder error = %v", err)
	}
}

// Two independently loaded services must own independent decoder registries:
// a contribution in one must not be visible in the other, and each must still
// carry its own built-in JSON decoder.
func TestDecodersArePerService(t *testing.T) {
	ss1, f1 := loadStreamService(t)
	defer dispose(t, f1)
	ss2, f2 := loadStreamService(t)
	defer dispose(t, f2)

	mk := func(ct string) *exthttp.StreamResponse {
		return &exthttp.StreamResponse{
			Header: stdhttp.Header{"Content-Type": {ct}},
			Body:   io.NopCloser(strings.NewReader(`{"n":7}`)),
		}
	}
	const customCT = "application/x-only-on-service-one"
	remove, err := ss1.RegisterDecoder(customCT, func(r io.Reader, out any) error {
		_, _ = io.Copy(io.Discard, r)
		*(out.(*string)) = "one"
		return nil
	})
	if err != nil {
		t.Fatalf("RegisterDecoder on ss1: %v", err)
	}
	defer func() { _ = remove() }()

	var s string
	if err := ss1.Decode(mk(customCT), &s); err != nil || s != "one" {
		t.Fatalf("ss1 Decode = %q, %v; want one", s, err)
	}
	if err := ss2.Decode(mk(customCT), &s); err == nil || !strings.Contains(err.Error(), customCT) {
		t.Fatalf("ss2 saw ss1's decoder: %v", err)
	}

	// A contribution in ss2 is likewise private, and views inherit only their
	// own service's registry.
	remove2, err := ss2.RegisterDecoder(customCT, func(r io.Reader, out any) error {
		_, _ = io.Copy(io.Discard, r)
		*(out.(*string)) = "two"
		return nil
	})
	if err != nil {
		t.Fatalf("RegisterDecoder on ss2: %v", err)
	}
	defer func() { _ = remove2() }()
	if err := ss1.Decode(mk(customCT), &s); err != nil || s != "one" {
		t.Fatalf("ss2 registration changed ss1: %q, %v", s, err)
	}
	if err := ss2.Decode(mk(customCT), &s); err != nil || s != "two" {
		t.Fatalf("ss2 Decode = %q, %v; want two", s, err)
	}
	if err := ss1.Extend("", nil).Decode(mk(customCT), &s); err != nil || s != "one" {
		t.Fatalf("ss1 view Decode = %q, %v; want one", s, err)
	}

	// Both instances got a private copy of the built-in JSON decoder.
	for i, ss := range []exthttp.StreamService{ss1, ss2} {
		var out struct {
			N int `json:"n"`
		}
		if err := ss.Decode(mk("application/json"), &out); err != nil || out.N != 7 {
			t.Fatalf("service %d builtin json = %v n=%d", i, err, out.N)
		}
	}
}

func TestBaseURLParamsAndExtendLayering(t *testing.T) {
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		w.Header().Set("X-Path", r.URL.Path)
		w.Header().Set("X-Query", r.URL.RawQuery)
		w.Header().Set("X-Layer", r.Header.Get("X-Layer"))
		w.Header().Set("X-Default", r.Header.Get("X-Default"))
	}))
	defer srv.Close()

	ss, f := loadStreamService(t)
	defer dispose(t, f)
	ctx := testCtx(t)

	req := exthttp.Request{
		Method:  stdhttp.MethodGet,
		URL:     "/items?keep=1",
		BaseURL: srv.URL + "/api",
		Params:  url.Values{"page": {"2"}, "tag": {"a", "b"}},
		Header:  stdhttp.Header{"X-Layer": {"req"}},
	}
	wantURL, wantBase := req.URL, req.BaseURL
	wantParams := make(url.Values)
	for k, vs := range req.Params {
		wantParams[k] = append([]string(nil), vs...)
	}
	wantHeader := req.Header.Clone()
	resp, err := ss.Do(ctx, req)
	if err != nil {
		t.Fatalf("Do with BaseURL/Params: %v", err)
	}
	if got := resp.Header.Get("X-Path"); got != "/api/items" {
		t.Fatalf("resolved path = %q, want /api/items", got)
	}
	q := resp.Header.Get("X-Query")
	for _, want := range []string{"keep=1", "page=2", "tag=a", "tag=b"} {
		if !strings.Contains(q, want) {
			t.Fatalf("query %q missing %q", q, want)
		}
	}
	if req.URL != wantURL || req.BaseURL != wantBase || !reflect.DeepEqual(req.Params, wantParams) || !reflect.DeepEqual(req.Header, wantHeader) {
		t.Fatalf("Do mutated caller's Request: %+v", req)
	}

	baseHeader := stdhttp.Header{}
	baseHeader.Set("X-Layer", "base")
	baseHeader.Set("X-Default", "base-default")
	view := ss.Extend(srv.URL+"/api", baseHeader)
	childHeader := stdhttp.Header{}
	childHeader.Set("X-Default", "child")
	view2 := view.Extend(srv.URL+"/api/v2", childHeader)

	r2, err := view2.Do(ctx, exthttp.Request{URL: "/items", Header: stdhttp.Header{"X-Layer": {"req"}}})
	if err != nil {
		t.Fatalf("view2.Do: %v", err)
	}
	if r2.Header.Get("X-Path") != "/api/v2/items" || r2.Header.Get("X-Layer") != "req" || r2.Header.Get("X-Default") != "child" {
		t.Fatalf("layered response = %v", r2.Header)
	}
	r1, err := view.Do(ctx, exthttp.Request{URL: "/items"})
	if err != nil {
		t.Fatalf("view.Do: %v", err)
	}
	if r1.Header.Get("X-Path") != "/api/items" || r1.Header.Get("X-Layer") != "base" || r1.Header.Get("X-Default") != "base-default" {
		t.Fatalf("base view response = %v", r1.Header)
	}
	// Extending must not change the parent view's defaults.
	if baseHeader.Get("X-Layer") != "base" {
		t.Fatal("Extend mutated the caller's header")
	}

	// A view shares the parent lifecycle: closing it closes the one owner.
	if err := view.Close(); err != nil {
		t.Fatalf("view.Close: %v", err)
	}
	if _, err := ss.Do(ctx, exthttp.Request{URL: srv.URL}); !errors.Is(err, exthttp.ErrClosed) {
		t.Fatalf("parent after view.Close = %v, want ErrClosed", err)
	}
	if _, err := view2.Do(ctx, exthttp.Request{URL: srv.URL}); !errors.Is(err, exthttp.ErrClosed) {
		t.Fatalf("view after view.Close = %v, want ErrClosed", err)
	}
}

func TestCloseJoinsInFlightStreamAndRejectsNew(t *testing.T) {
	entered := make(chan struct{})
	var enteredOnce sync.Once
	release := make(chan struct{})
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = w.Write([]byte("ok"))
	}))
	defer srv.Close()

	ss, f := loadStreamService(t)
	_, err := ss.AddStreamInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.StreamResponse, error)) (*exthttp.StreamResponse, error) {
		// A post-Close probe may race into this interceptor before `closed`
		// is observed; only the first request signals the barrier.
		enteredOnce.Do(func() { close(entered) })
		<-release // deliberately ignores ctx: only this test releases it
		return next(ctx, req)
	})
	if err != nil {
		t.Fatalf("AddStreamInterceptor: %v", err)
	}

	doDone := make(chan error, 1)
	go func() {
		_, e := ss.DoStream(testCtx(t), exthttp.Request{URL: srv.URL})
		doDone <- e
	}()
	mustRecv(t, entered, time.Second)

	closeCalled := make(chan struct{})
	closeDone := make(chan struct{})
	go func() {
		close(closeCalled)
		_ = ss.Close()
		close(closeDone)
	}()
	<-closeCalled
	select {
	case <-closeDone:
		t.Fatal("Close returned while a stream request was still in flight")
	default:
	}
	close(release)
	mustRecv(t, doDone, time.Second)
	mustRecv(t, closeDone, time.Second)
	dispose(t, f)
}

func TestDoStreamAfterCloseRejected(t *testing.T) {
	ss, f := loadStreamService(t)
	if err := ss.Close(); err != nil {
		t.Fatal(err)
	}
	if _, err := ss.DoStream(testCtx(t), exthttp.Request{URL: "http://127.0.0.1:1"}); !errors.Is(err, exthttp.ErrClosed) {
		t.Fatalf("DoStream after Close = %v, want ErrClosed", err)
	}
	dispose(t, f)
}

// --- r1.md HTTP equivalence: full method family + WebSocket ---------------

func asMethodService(t *testing.T, svc exthttp.Service) exthttp.MethodService {
	t.Helper()
	ms, ok := svc.(exthttp.MethodService)
	if !ok {
		t.Fatalf("service %T does not implement MethodService", svc)
	}
	return ms
}

func asWebSocketService(t *testing.T, svc exthttp.Service) exthttp.WebSocketService {
	t.Helper()
	wss, ok := svc.(exthttp.WebSocketService)
	if !ok {
		t.Fatalf("service %T does not implement WebSocketService", svc)
	}
	return wss
}

func wsURL(httpURL string) string { return "ws" + strings.TrimPrefix(httpURL, "http") }

func TestMethodFamilyDelegatesToDo(t *testing.T) {
	var mu sync.Mutex
	var got []string
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		mu.Lock()
		got = append(got, r.Method)
		mu.Unlock()
		_, _ = w.Write([]byte(r.Method))
	}))
	defer srv.Close()

	svc, f := loadService(t)
	ms := asMethodService(t, svc)

	// An interceptor sits in the Do chain; every verb must flow through it.
	var seen string
	if _, err := ms.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
		seen = req.Method
		return next(ctx, req)
	}); err != nil {
		t.Fatalf("AddInterceptor: %v", err)
	}

	calls := []struct {
		name string
		call func() error
		want string
	}{
		{"Put", func() error { _, e := ms.Put(testCtx(t), srv.URL+"/x", []byte("body")); return e }, "PUT"},
		{"Patch", func() error { _, e := ms.Patch(testCtx(t), srv.URL+"/x", []byte("body")); return e }, "PATCH"},
		{"Delete", func() error { _, e := ms.Delete(testCtx(t), srv.URL+"/x"); return e }, "DELETE"},
		{"Head", func() error { _, e := ms.Head(testCtx(t), srv.URL+"/x"); return e }, "HEAD"},
	}
	for _, c := range calls {
		if err := c.call(); err != nil {
			t.Fatalf("%s: %v", c.name, err)
		}
		if seen != c.want {
			t.Errorf("interceptor saw method %q after %s, want %q", seen, c.name, c.want)
		}
	}

	// The Extend view implements the same additive method family and layers
	// base URL + default headers before delegating to Do.
	ss := asStreamService(t, svc)
	view, ok := ss.Extend(srv.URL, stdhttp.Header{"X-Default": []string{"yes"}}).(exthttp.MethodService)
	if !ok {
		t.Fatal("Extend view does not implement MethodService")
	}
	var header string
	if _, err := view.AddInterceptor(func(ctx context.Context, req *exthttp.Request, next func(context.Context, *exthttp.Request) (*exthttp.Response, error)) (*exthttp.Response, error) {
		header = req.Header.Get("X-Default")
		return next(ctx, req)
	}); err != nil {
		t.Fatal(err)
	}
	if _, err := view.Put(testCtx(t), "/view", []byte("v")); err != nil {
		t.Fatalf("view.Put: %v", err)
	}
	if header != "yes" {
		t.Errorf("Extend default header = %q, want yes", header)
	}

	mu.Lock()
	gotMethods := append([]string(nil), got...)
	mu.Unlock()
	want := []string{"PUT", "PATCH", "DELETE", "HEAD", "PUT"}
	if !reflect.DeepEqual(gotMethods, want) {
		t.Fatalf("server methods = %v, want %v", gotMethods, want)
	}
	dispose(t, f)
}

func TestWebSocketTextAndBinaryEcho(t *testing.T) {
	headerSeen := make(chan string, 1)
	up := websocket.Upgrader{}
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		headerSeen <- r.Header.Get("X-Test")
		c, err := up.Upgrade(w, r, nil)
		if err != nil {
			return
		}
		defer c.Close()
		for {
			mt, msg, err := c.ReadMessage()
			if err != nil {
				return
			}
			if err := c.WriteMessage(mt, msg); err != nil {
				return
			}
		}
	}))
	defer srv.Close()

	svc, f := loadService(t)
	ws, err := asWebSocketService(t, svc).WS(testCtx(t), wsURL(srv.URL)+"/echo", stdhttp.Header{"X-Test": []string{"v"}})
	if err != nil {
		t.Fatalf("WS: %v", err)
	}
	if got := <-headerSeen; got != "v" {
		t.Fatalf("handshake header X-Test = %q, want v", got)
	}
	for _, tc := range []struct {
		mt   int
		data []byte
	}{
		{websocket.TextMessage, []byte("hello")},
		{websocket.BinaryMessage, []byte{0, 1, 2, 255}},
	} {
		if err := ws.WriteMessage(tc.mt, tc.data); err != nil {
			t.Fatalf("WriteMessage: %v", err)
		}
		mt, data, err := ws.ReadMessage()
		if err != nil {
			t.Fatalf("ReadMessage: %v", err)
		}
		if mt != tc.mt || !bytes.Equal(data, tc.data) {
			t.Fatalf("echo = (%d, %v), want (%d, %v)", mt, data, tc.mt, tc.data)
		}
	}
	if err := ws.Close(); err != nil {
		t.Fatalf("client Close: %v", err)
	}
	dispose(t, f)
}

func TestWebSocketContextCancellation(t *testing.T) {
	disconnected := make(chan struct{})
	var once sync.Once
	up := websocket.Upgrader{}
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		c, err := up.Upgrade(w, r, nil)
		if err != nil {
			return
		}
		defer c.Close()
		if _, _, err := c.ReadMessage(); err != nil {
			once.Do(func() { close(disconnected) })
		}
	}))
	defer srv.Close()

	svc, f := loadService(t)
	ctx, cancel := context.WithCancel(testCtx(t))
	ws, err := asWebSocketService(t, svc).WS(ctx, wsURL(srv.URL), nil)
	if err != nil {
		t.Fatalf("WS: %v", err)
	}
	cancel()

	deadline := time.After(2 * time.Second)
	for {
		if _, _, err := ws.ReadMessage(); err != nil {
			break
		}
		select {
		case <-deadline:
			t.Fatal("ReadMessage still succeeded after ctx cancellation")
		default:
		}
	}
	mustRecv(t, disconnected, time.Second)
	dispose(t, f)
}

func TestWebSocketServiceCloseClosesActiveClients(t *testing.T) {
	disconnected := make(chan struct{}, 2)
	up := websocket.Upgrader{}
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		c, err := up.Upgrade(w, r, nil)
		if err != nil {
			return
		}
		defer c.Close()
		if _, _, err := c.ReadMessage(); err != nil {
			disconnected <- struct{}{}
		}
	}))
	defer srv.Close()

	svc, f := loadService(t)
	wss := asWebSocketService(t, svc)
	ws1, err := wss.WS(testCtx(t), wsURL(srv.URL), nil)
	if err != nil {
		t.Fatalf("WS 1: %v", err)
	}
	ws2, err := wss.WS(testCtx(t), wsURL(srv.URL), nil)
	if err != nil {
		t.Fatalf("WS 2: %v", err)
	}

	if err := svc.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	for i, ws := range []exthttp.WSClient{ws1, ws2} {
		if _, _, err := ws.ReadMessage(); err == nil {
			t.Fatalf("client %d ReadMessage succeeded after Service.Close", i)
		}
		mustRecv(t, disconnected, time.Second)
	}
	if _, err := wss.WS(testCtx(t), wsURL(srv.URL), nil); !errors.Is(err, exthttp.ErrClosed) {
		t.Fatalf("WS after Close = %v, want ErrClosed", err)
	}
	dispose(t, f)
}

func TestWebSocketCloseUnblocksBlockedReader(t *testing.T) {
	up := websocket.Upgrader{}
	srv := httptest.NewServer(stdhttp.HandlerFunc(func(w stdhttp.ResponseWriter, r *stdhttp.Request) {
		_, _ = up.Upgrade(w, r, nil)
	}))
	defer srv.Close()

	svc, f := loadService(t)
	ws, err := asWebSocketService(t, svc).WS(testCtx(t), wsURL(srv.URL), nil)
	if err != nil {
		t.Fatalf("WS: %v", err)
	}
	readErr := make(chan error, 1)
	go func() {
		_, _, e := ws.ReadMessage()
		readErr <- e
	}()
	// Give the reader time to actually block before Close tears the socket down.
	time.Sleep(20 * time.Millisecond)
	if err := svc.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	mustRecv(t, readErr, time.Second)
	dispose(t, f)
}

func TestWebSocketDialHonorsCanceledContext(t *testing.T) {
	svc, f := loadService(t)
	ctx, cancel := context.WithCancel(testCtx(t))
	cancel()
	if _, err := asWebSocketService(t, svc).WS(ctx, wsURL("http://127.0.0.1:1"), nil); !errors.Is(err, context.Canceled) {
		t.Fatalf("WS with canceled ctx = %v, want context.Canceled", err)
	}
	dispose(t, f)
}

func TestWebSocketRejectsNonWebSocketSchemes(t *testing.T) {
	svc, f := loadService(t)
	wss := asWebSocketService(t, svc)
	for _, raw := range []string{"http://127.0.0.1:1", "ftp://example.com", "not a url", ""} {
		_, err := wss.WS(testCtx(t), raw, nil)
		if err == nil {
			t.Errorf("WS(%q) succeeded, want scheme error", raw)
			continue
		}
		if !strings.Contains(err.Error(), "ws") {
			t.Errorf("WS(%q) error %q does not mention ws scheme", raw, err)
		}
	}
	dispose(t, f)
}
