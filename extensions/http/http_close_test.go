package http_test

import (
	"context"
	"errors"
	stdhttp "net/http"
	"testing"
	"time"

	httpext "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

// HTTP-24 — Runtime Close shuts the HTTP server down truthfully.
func TestHTTP24RuntimeClose(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	addr := freeAddr(t)
	comp := newComp(t, addr)
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if f.State() != runtime.StateGone {
		t.Fatalf("fiber state after close = %v", f.State())
	}
	expectUnavailable(t, addr)
	if err := rt.Close(context.Background()); err != nil {
		t.Fatalf("second Close not idempotent: %v", err)
	}
}

// HTTP-25 — Close during an in-flight request waits for the request.
func TestHTTP25CloseDuringRequest(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	addr := freeAddr(t)
	h := &blockedHandler{release: make(chan struct{}), started: make(chan struct{})}
	comp, err := httpext.New(cfgAt(addr), httpext.WithHandler(h))
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
	reqDone := make(chan struct{})
	go func() {
		defer close(reqDone)
		_, _ = stdhttp.Get("http://" + addr)
	}()
	<-h.started

	closeDone := make(chan error, 1)
	go func() { closeDone <- rt.Close(context.Background()) }()

	// The listener stops accepting new connections while the handler is still
	// in flight; Close has NOT completed yet.
	expectUnavailable(t, addr)
	select {
	case <-closeDone:
		t.Fatal("Close completed while request in flight")
	case <-time.After(150 * time.Millisecond):
	}
	close(h.release)
	if err := <-closeDone; err != nil {
		t.Fatalf("Close after request finished: %v", err)
	}
	<-reqDone
}

// HTTP-26 — Close timeout is truthful: it reports ctx.Err and the Runtime is
// not Closed until real cleanup completes.
func TestHTTP26CloseTimeout(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	addr := freeAddr(t)
	h := &blockedHandler{release: make(chan struct{}), started: make(chan struct{})}
	cfg := httpext.ServerConfig{Network: "tcp", Address: addr, ShutdownTimeout: 60 * time.Second}
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
	go func() { _, _ = stdhttp.Get("http://" + addr) }()
	<-h.started

	short, cancelS := context.WithTimeout(context.Background(), 120*time.Millisecond)
	defer cancelS()
	if err := rt.Close(short); err == nil {
		t.Fatal("Close returned nil while HTTP shutdown was not finished (fake Closed)")
	} else if !errors.Is(err, context.DeadlineExceeded) && !errors.Is(err, context.Canceled) {
		t.Fatalf("Close error = %v, want context error", err)
	}
	if f.State() == runtime.StateGone {
		t.Fatal("fiber Gone while handler still in flight")
	}

	// Real cleanup completes only after the handler is released.
	close(h.release)
	fin, cancelF := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancelF()
	if err := rt.Close(fin); err != nil {
		t.Fatalf("final Close: %v", err)
	}
	expectUnavailable(t, addr)
}
