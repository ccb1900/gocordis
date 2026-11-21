package proxy

import (
	"context"
	"io"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/gorilla/websocket"

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

func loadHarness(t *testing.T) (*runtime.Runtime, server.Service, exthttp.Service) {
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
		hc := <-cap.httpCh
		return rt, svc, hc
	case <-ctx.Done():
		t.Fatal("service never delivered")
		return nil, nil, nil
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

	rt, svc, _ := loadHarness(t)
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

	rt, svc, _ := loadHarness(t)
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

	rt, svc, _ := loadHarness(t)
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

// ---- JS server-proxy parity: HTTP path/query/status/body/header semantics ---

func TestProxyResolvesNestedPathAndForwardsQuery(t *testing.T) {
	type seen struct{ path, query string }
	got := make(chan seen, 1)
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		got <- seen{path: r.URL.Path, query: r.URL.RawQuery}
		_, _ = io.WriteString(w, "ok")
	}))
	defer backend.Close()

	rt, svc, _ := loadHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: backend.URL + "/base"})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}

	rec := serve(svc, http.MethodGet, "/p/a/b?x=1&y=two%20three", "", nil)
	if rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", rec.Code)
	}
	select {
	case s := <-got:
		if s.path != "/base/a/b" || s.query != "x=1&y=two%20three" {
			t.Fatalf("backend saw path=%q query=%q, want /base/a/b and x=1&y=two%%20three", s.path, s.query)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("backend never saw the request")
	}
}

func TestProxyPassesThroughUpstreamErrorStatuses(t *testing.T) {
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		switch r.URL.Path {
		case "/missing":
			http.Error(w, "not found", http.StatusNotFound)
		case "/boom":
			http.Error(w, "boom", http.StatusInternalServerError)
		default:
			_, _ = io.WriteString(w, "ok")
		}
	}))
	defer backend.Close()

	rt, svc, _ := loadHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: backend.URL})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	for _, tc := range []struct {
		path string
		want int
	}{
		{"/missing", http.StatusNotFound},
		{"/boom", http.StatusInternalServerError},
	} {
		if rec := serve(svc, http.MethodGet, "/p"+tc.path, "", nil); rec.Code != tc.want {
			t.Fatalf("GET %s: status = %d, want %d", tc.path, rec.Code, tc.want)
		}
	}
}

func TestProxyLocateRejectsPrefixEscape(t *testing.T) {
	target, err := parseTarget("http://backend.example/base")
	if err != nil {
		t.Fatalf("parseTarget: %v", err)
	}
	if u := locate(target, "a/b"); u == nil || u.String() != "http://backend.example/base/a/b" {
		t.Fatalf("locate(a/b) = %v", u)
	}
	if u := locate(target, "../secret"); u != nil {
		t.Fatalf("locate(../secret) = %v, want nil (prefix escape)", u)
	}
	if u := locate(target, "a/../../secret"); u != nil {
		t.Fatalf("locate(a/../../secret) = %v, want nil", u)
	}
	// The 400 is emitted by the HTTP handler when locate refuses the path.
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		_, _ = io.WriteString(w, "should not be reached")
	}))
	defer backend.Close()
	_, _, hc := loadHarness(t)
	comp := &Component{Route: "/p", Target: backend.URL + "/base"}
	targetURL, err := parseTarget(comp.Target)
	if err != nil {
		t.Fatalf("parseTarget: %v", err)
	}
	req := httptest.NewRequest(http.MethodGet, "/p/x", nil)
	req.SetPathValue("path", "../secret")
	rec := httptest.NewRecorder()
	if err := comp.forward(hc, targetURL, rec, req); err != nil {
		t.Fatalf("forward: %v", err)
	}
	if rec.Code != http.StatusBadRequest {
		t.Fatalf("escape status = %d, want 400", rec.Code)
	}
}

func TestProxyBodyForwardedOnlyForBodyMethods(t *testing.T) {
	type seen struct{ method, body string }
	got := make(chan seen, 8)
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		b, _ := io.ReadAll(r.Body)
		got <- seen{method: r.Method, body: string(b)}
		w.WriteHeader(http.StatusOK)
	}))
	defer backend.Close()

	rt, svc, _ := loadHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: backend.URL})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	for _, m := range []string{
		http.MethodGet, http.MethodPost, http.MethodPut, http.MethodPatch,
		http.MethodDelete, http.MethodHead, http.MethodOptions,
	} {
		if rec := serve(svc, m, "/p", "payload", nil); rec.Code != http.StatusOK {
			t.Fatalf("%s: status = %d, want 200", m, rec.Code)
		}
		wantBody := ""
		if m == http.MethodPost || m == http.MethodPut || m == http.MethodPatch {
			wantBody = "payload"
		}
		select {
		case s := <-got:
			if s.method != m || s.body != wantBody {
				t.Fatalf("%s: backend saw method=%q body=%q, want body=%q", m, s.method, s.body, wantBody)
			}
		case <-time.After(2 * time.Second):
			t.Fatalf("%s: backend never saw the request", m)
		}
	}
}

func TestProxyStripsEncodingAndLengthFromResponse(t *testing.T) {
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, _ *http.Request) {
		w.Header().Set("Content-Encoding", "identity")
		w.Header().Set("Content-Length", "3")
		w.Header().Set("X-Keep", "yes")
		_, _ = io.WriteString(w, "abc")
	}))
	defer backend.Close()

	rt, svc, _ := loadHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: backend.URL})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	rec := serve(svc, http.MethodGet, "/p", "", nil)
	if rec.Code != http.StatusOK || rec.Body.String() != "abc" {
		t.Fatalf("status=%d body=%q", rec.Code, rec.Body.String())
	}
	if v := rec.Header().Get("Content-Encoding"); v != "" {
		t.Fatalf("Content-Encoding leaked: %q", v)
	}
	if v := rec.Header().Get("Content-Length"); v != "" {
		t.Fatalf("Content-Length leaked: %q", v)
	}
	if v := rec.Header().Get("X-Keep"); v != "yes" {
		t.Fatalf("X-Keep = %q, want yes", v)
	}
}

// ---- WebSocket proxy -----------------------------------------------------

func newWSUpstream(t *testing.T) (string, chan int) {
	t.Helper()
	closed := make(chan int, 4)
	upgrader := websocket.Upgrader{}
	srv := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		up, err := upgrader.Upgrade(w, r, nil)
		if err != nil {
			return
		}
		defer up.Close()
		_ = up.WriteMessage(websocket.TextMessage, []byte("greeting-from-upstream"))
		for {
			mt, data, err := up.ReadMessage()
			if err != nil {
				if ce, ok := err.(*websocket.CloseError); ok {
					closed <- ce.Code
				}
				return
			}
			if err := up.WriteMessage(mt, data); err != nil {
				return
			}
		}
	}))
	t.Cleanup(srv.Close)
	return "ws" + strings.TrimPrefix(srv.URL, "http"), closed
}

func loadWSHarness(t *testing.T) (*runtime.Runtime, server.Service, string) {
	t.Helper()
	rt, svc, _ := loadHarness(t)
	srv := httptest.NewServer(svc)
	t.Cleanup(srv.Close)
	return rt, svc, "ws" + strings.TrimPrefix(srv.URL, "http")
}

func wsRead(t *testing.T, c *websocket.Conn) (int, []byte) {
	t.Helper()
	if err := c.SetReadDeadline(time.Now().Add(3 * time.Second)); err != nil {
		t.Fatalf("SetReadDeadline: %v", err)
	}
	mt, data, err := c.ReadMessage()
	if err != nil {
		t.Fatalf("ReadMessage: %v", err)
	}
	return mt, data
}

func TestProxyWebSocketBidirectional(t *testing.T) {
	upstreamWS, upstreamClosed := newWSUpstream(t)
	rt, _, downstreamWS := loadWSHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: strings.Replace(upstreamWS, "ws://", "http://", 1)})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}

	c, _, err := websocket.DefaultDialer.Dial(downstreamWS+"/ws?token=abc", nil)
	if err != nil {
		t.Fatalf("Dial downstream: %v", err)
	}
	defer c.Close()

	if mt, data := wsRead(t, c); mt != websocket.TextMessage || string(data) != "greeting-from-upstream" {
		t.Fatalf("upstream->downstream greeting = (%d, %q)", mt, data)
	}
	if err := c.WriteMessage(websocket.TextMessage, []byte("hello")); err != nil {
		t.Fatalf("WriteMessage text: %v", err)
	}
	if mt, data := wsRead(t, c); mt != websocket.TextMessage || string(data) != "hello" {
		t.Fatalf("text round trip = (%d, %q)", mt, data)
	}
	if err := c.WriteMessage(websocket.BinaryMessage, []byte{0, 1, 2}); err != nil {
		t.Fatalf("WriteMessage binary: %v", err)
	}
	if mt, data := wsRead(t, c); mt != websocket.BinaryMessage || string(data) != "\x00\x01\x02" {
		t.Fatalf("binary round trip = (%d, %q)", mt, data)
	}

	deadline := time.Now().Add(3 * time.Second)
	if err := c.WriteControl(websocket.CloseMessage,
		websocket.FormatCloseMessage(websocket.CloseNormalClosure, ""), deadline); err != nil {
		t.Fatalf("WriteControl close: %v", err)
	}
	select {
	case code := <-upstreamClosed:
		if code != websocket.CloseNormalClosure {
			t.Fatalf("upstream close code = %d, want 1000", code)
		}
	case <-time.After(3 * time.Second):
		t.Fatal("upstream never observed downstream close")
	}
}

func TestProxyWebSocketDisposeClosesConnections(t *testing.T) {
	upstreamWS, _ := newWSUpstream(t)
	rt, _, downstreamWS := loadWSHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: strings.Replace(upstreamWS, "ws://", "http://", 1)})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	c, _, err := websocket.DefaultDialer.Dial(downstreamWS+"/ws", nil)
	if err != nil {
		t.Fatalf("Dial: %v", err)
	}
	defer c.Close()
	if mt, data := wsRead(t, c); mt != websocket.TextMessage || string(data) != "greeting-from-upstream" {
		t.Fatalf("greeting = (%d, %q)", mt, data)
	}

	if err := comp.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := comp.Gone(testCtx(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	_ = c.SetReadDeadline(time.Now().Add(3 * time.Second))
	if _, _, err := c.ReadMessage(); err == nil {
		t.Fatal("downstream connection still readable after proxy disposal")
	}
}

func TestProxyReEncodesPathSegmentsOnTheWire(t *testing.T) {
	got := make(chan string, 1)
	backend := httptest.NewServer(http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		got <- r.URL.EscapedPath()
		w.WriteHeader(http.StatusOK)
	}))
	defer backend.Close()

	rt, svc, _ := loadHarness(t)
	comp, err := rt.Load(&Component{Route: "/p", Target: backend.URL + "/base"})
	if err != nil {
		t.Fatalf("load proxy: %v", err)
	}
	if err := comp.Ready(testCtx(t)); err != nil {
		t.Fatalf("proxy Ready: %v", err)
	}
	if rec := serve(svc, http.MethodGet, "/p/x%20y/a!b/c@day", "", nil); rec.Code != http.StatusOK {
		t.Fatalf("status = %d, want 200", rec.Code)
	}
	select {
	case escaped := <-got:
		if want := "/base/x%20y/a!b/c@day"; escaped != want {
			t.Fatalf("backend escaped path = %q, want %q", escaped, want)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("backend never saw the request")
	}
}
