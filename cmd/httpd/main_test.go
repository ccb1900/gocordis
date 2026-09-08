package main

import (
	"context"
	"io"
	"net/http"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Focused behavioral guarantees for the external-resource binding pattern
// (transferred from the extensions/http suite's highest-value cases):
//
//	1. Active <=> truly serving (Apply lands the listener before committing);
//	2. disposal gracefully stops the server BEFORE the fiber reaches Gone;
//	3. the port is released exactly once the fiber is Gone (same-address
//	   reload succeeds — the sequential form of the old same-port HMR case).

func httpdRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

func httpdGet(t *testing.T, url string) (int, string) {
	t.Helper()
	resp, err := http.Get(url) //nolint:gosec // demo-local probe
	if err != nil {
		t.Fatalf("get %s: %v", url, err)
	}
	defer resp.Body.Close()
	body, _ := io.ReadAll(resp.Body)
	return resp.StatusCode, string(body)
}

func TestHttpdActiveMeansServing(t *testing.T) {
	rt := httpdRT(t)
	srv, err := NewServer("s1", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(srv)
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("ready: %v", err)
	}
	if srv.Addr() == "" {
		t.Fatal("no bound address after Active")
	}
	code, body := httpdGet(t, "http://"+srv.Addr()+"/")
	if code != http.StatusOK || body != "ok" {
		t.Fatalf("serving probe = %d %q", code, body)
	}

	// Disposal: Gone implies the graceful stop already ran.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if _, err := http.Get("http://" + srv.Addr() + "/"); err == nil {
		t.Fatal("server still serving after Gone")
	}
}

func TestHttpdSameAddressReload(t *testing.T) {
	rt := httpdRT(t)
	const addr = "127.0.0.1:0"
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()

	s1, err := NewServer("gen1", addr)
	if err != nil {
		t.Fatal(err)
	}
	f1, err := rt.Load(s1)
	if err != nil {
		t.Fatal(err)
	}
	if err := f1.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	bound := s1.Addr()

	// Retire gen1, then bind the SAME address as gen2: proves the listener is
	// released exactly when the fiber finalizes (no orphan, no leak).
	if err := f1.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f1.Gone(ctx); err != nil {
		t.Fatal(err)
	}

	s2, err := NewServer("gen2", bound)
	if err != nil {
		t.Fatal(err)
	}
	f2, err := rt.Load(s2)
	if err != nil {
		t.Fatalf("same-address reload: %v (listener not released?)", err)
	}
	if err := f2.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if code, _ := httpdGet(t, "http://"+bound+"/"); code != http.StatusOK {
		t.Fatalf("reloaded server status = %d", code)
	}
	_ = f2.Dispose()
	_ = f2.Gone(ctx)
}
