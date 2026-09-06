package http_test

import (
	"context"
	"errors"
	"net"
	stdhttp "net/http"
	"testing"
	"time"

	httpext "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 25*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = rt.Close(context.Background()) })
	return rt
}

// freeAddr reserves a free TCP port and returns 127.0.0.1:port. Tests in this
// package run serially, so the release-to-bind window is deterministic enough.
func freeAddr(t *testing.T) string {
	t.Helper()
	ln, err := net.Listen("tcp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	addr := ln.Addr().String()
	if err := ln.Close(); err != nil {
		t.Fatal(err)
	}
	return addr
}

func cfgAt(addr string) httpext.ServerConfig {
	return httpext.ServerConfig{Network: "tcp", Address: addr, ShutdownTimeout: 5 * time.Second}
}

func newComp(t *testing.T, addr string, opts ...httpext.Option) *httpext.Component {
	t.Helper()
	comp, err := httpext.New(cfgAt(addr), opts...)
	if err != nil {
		t.Fatalf("New: %v", err)
	}
	return comp
}

func mount(t *testing.T, rt *runtime.Runtime, comp runtime.Component) *runtime.Fiber {
	t.Helper()
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("rt.Load: %v", err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("fiber not active: %v", err)
	}
	return f
}

func getBody(t *testing.T, addr string) (int, string) {
	t.Helper()
	resp, err := stdhttp.Get("http://" + addr)
	if err != nil {
		return 0, err.Error()
	}
	defer resp.Body.Close()
	buf := make([]byte, 64)
	n, _ := resp.Body.Read(buf)
	return resp.StatusCode, string(buf[:n])
}

func getStatus(t *testing.T, addr string) int {
	t.Helper()
	code, _ := getBody(t, addr)
	return code
}

func expectUnavailable(t *testing.T, addr string) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		conn, err := net.DialTimeout("tcp", addr, 200*time.Millisecond)
		if err != nil {
			return
		}
		_ = conn.Close()
		time.Sleep(20 * time.Millisecond)
	}
	t.Fatalf("address %s still reachable", addr)
}

// HTTP-01 — Basic start: Fiber Active and the endpoint really responds.
func TestHTTP01BasicStart(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr)
	f := mount(t, rt, comp)
	if f.State() != runtime.StateActive {
		t.Fatalf("state = %v", f.State())
	}
	if comp.Addr() != addr {
		t.Fatalf("addr = %q, want %q", comp.Addr(), addr)
	}
	if code, body := getBody(t, addr); code != 200 || body != "ok" {
		t.Fatalf("got %d %q", code, body)
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// HTTP-02 — Stop: Dispose makes the endpoint unavailable and the Fiber Gone.
func TestHTTP02Stop(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr)
	f := mount(t, rt, comp)
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	expectUnavailable(t, addr)
}

// HTTP-03 — Port conflict: F2 fails, F1 unchanged and still serving.
func TestHTTP03PortConflict(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp1 := newComp(t, addr)
	f1 := mount(t, rt, comp1)

	comp2 := newComp(t, addr)
	f2, err := rt.Load(comp2)
	if err != nil {
		t.Fatal(err)
	}
	if err := f2.Ready(ctxT(t)); err == nil {
		t.Fatal("second fiber on the same address became Active")
	}
	if f2.State() != runtime.StateFailed {
		t.Fatalf("f2 state = %v, want Failed", f2.State())
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("f1 disturbed by conflict")
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatalf("f1 no longer serving: status %d", got)
	}
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))
	_ = f2.Dispose()
	_ = f2.Gone(ctxT(t))
}

// HTTP-04 — Apply failure cleanup: a bind conflict unwinds fully and, after
// releasing the owner, the address is genuinely free (no orphan listener).
func TestHTTP04ApplyFailureCleanup(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp1 := newComp(t, addr)
	f1 := mount(t, rt, comp1)

	comp2 := newComp(t, addr)
	f2, err := rt.Load(comp2)
	if err != nil {
		t.Fatal(err)
	}
	if err := f2.Ready(ctxT(t)); err == nil {
		t.Fatal("f2 unexpectedly active")
	}
	_ = f2.Dispose()
	_ = f2.Gone(ctxT(t))

	if err := f1.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f1.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	expectUnavailable(t, addr)

	comp3 := newComp(t, addr)
	f3 := mount(t, rt, comp3)
	if got := getStatus(t, addr); got != 200 {
		t.Fatalf("rebind failed: status %d", got)
	}
	_ = f3.Dispose()
	_ = f3.Gone(ctxT(t))
}

// blockedHandler waits on release before responding.
type blockedHandler struct {
	release chan struct{}
	started chan struct{}
}

func (h *blockedHandler) ServeHTTP(w stdhttp.ResponseWriter, _ *stdhttp.Request) {
	close(h.started)
	<-h.release
	w.WriteHeader(200)
}

// HTTP-05 — Graceful request: an in-flight request finishes during Dispose
// before the Fiber reaches Gone.
func TestHTTP05GracefulRequest(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	h := &blockedHandler{release: make(chan struct{}), started: make(chan struct{})}
	comp := newComp(t, addr, httpext.WithHandler(h))
	f := mount(t, rt, comp)

	done := make(chan struct{})
	go func() {
		defer close(done)
		_, _ = stdhttp.Get("http://" + addr)
	}()
	<-h.started // the handler is now in-flight

	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	// While the handler is still in-flight the listener must stop accepting,
	// but the Fiber must NOT be Gone yet.
	expectUnavailable(t, addr)
	if f.State() == runtime.StateGone {
		t.Fatal("fiber Gone while handler still in flight")
	}
	close(h.release)
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatalf("Gone after request finished: %v", err)
	}
	<-done
}

// HTTP-06 — Shutdown timeout: when a handler outlives the shutdown deadline,
// cleanup reports ErrShutdown (no false success) and the force-close still
// finishes cleanup so the Fiber truthfully ends.
func TestHTTP06ShutdownTimeout(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	h := &blockedHandler{release: make(chan struct{}), started: make(chan struct{})}
	cfg := httpext.ServerConfig{Network: "tcp", Address: addr, ShutdownTimeout: 50 * time.Millisecond}
	comp, err := httpext.New(cfg, httpext.WithHandler(h))
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	done := make(chan struct{})
	go func() {
		defer close(done)
		_, _ = stdhttp.Get("http://" + addr)
	}()
	<-h.started

	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	err = f.Gone(ctxT(t))
	if err == nil || !errors.Is(err, httpext.ErrShutdown) {
		t.Fatalf("Gone = %v, want ErrShutdown (timeout must be reported)", err)
	}
	close(h.release)
	<-done
	expectUnavailable(t, addr)
}

// HTTP-07 — Restart: after F1 is Gone the SAME address can be owned again.
func TestHTTP07Restart(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr)
	f1 := mount(t, rt, comp)
	id1 := comp.HandleID()
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))
	expectUnavailable(t, addr)

	if err := f1.Load(); err != nil {
		t.Fatal(err)
	}
	if err := f1.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("fiber not reactivated")
	}
	if comp.HandleID() == id1 {
		t.Fatal("server instance reused across activations")
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatalf("restart not serving: %d", got)
	}
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))
}

// HTTP-08 — Activation identity: every activation has a fresh server instance.
func TestHTTP08ActivationIdentity(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr, httpext.WithProvider())
	f1 := mount(t, rt, comp)
	idA := comp.HandleID()
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))

	if err := f1.Load(); err != nil {
		t.Fatal(err)
	}
	if err := f1.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	idB := comp.HandleID()
	if idA == idB || idB == 0 {
		t.Fatalf("server ids %d/%d not distinct", idA, idB)
	}
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))
}
