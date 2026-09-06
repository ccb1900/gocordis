package http_test

import (
	"context"
	stdhttp "net/http"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/hmr"
	httpext "dynamic-runtime/extensions/http"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

func stdhttpGet(addr string) (int, error) {
	resp, err := stdhttp.Get("http://" + addr)
	if err != nil {
		return 0, err
	}
	defer resp.Body.Close()
	return resp.StatusCode, nil
}

// ---------------------------------------------------------------------------
// HTTP-27..30 — concurrency.
// ---------------------------------------------------------------------------

// HTTP-27 — Concurrent start on one address: exactly one owner, deterministic.
func TestHTTP27ConcurrentStart(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	const n = 60
	comps := make([]runtime.Component, n)
	for i := 0; i < n; i++ {
		comps[i], _ = httpext.New(cfgAt(addr))
	}
	fs := make([]*runtime.Fiber, n)
	errs := make([]error, n)
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			f, err := rt.Load(comps[i])
			if err != nil {
				errs[i] = err
				return
			}
			fs[i] = f
			errs[i] = f.Ready(ctxT(t))
		}(i)
	}
	wg.Wait()
	active := 0
	for i := 0; i < n; i++ {
		if fs[i] != nil && fs[i].State() == runtime.StateActive {
			active++
		}
	}
	if active != 1 {
		t.Fatalf("active owners = %d, want exactly 1", active)
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatalf("owner not serving: %d", got)
	}
	for i := 0; i < n; i++ {
		if fs[i] != nil {
			_ = fs[i].Dispose()
			_ = fs[i].Gone(ctxT(t))
		}
	}
}

// HTTP-28 — Concurrent Start/Stop obeys Kernel linearization.
func TestHTTP28ConcurrentStartStop(t *testing.T) {
	rt := newRT(t)
	const n = 24
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			addr := freeAddr(t)
			comp, err := httpext.New(cfgAt(addr))
			if err != nil {
				t.Error(err)
				return
			}
			f, err := rt.Load(comp)
			if err != nil {
				t.Error(err)
				return
			}
			if err := f.Ready(ctxT(t)); err != nil {
				t.Error(err)
				return
			}
			_ = f.Dispose()
			_ = f.Gone(ctxT(t))
		}()
	}
	wg.Wait()
}

// HTTP-29 — HMR replacement concurrent with requests: after cleanup the new
// server responds and the old one no longer owns the resource.
func TestHTTP29HMRRequestConcurrent(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	p1, p2 := freeAddr(t), freeAddr(t)
	m1 := httpModule(t, ld, "http-h1", p1)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	regHTTPFactory(t, ld, "http-h2", p2)

	stop := make(chan struct{})
	var hits int32
	var wg sync.WaitGroup
	wg.Add(1)
	go func() {
		defer wg.Done()
		for {
			select {
			case <-stop:
				return
			default:
			}
			resp, err := stdhttpGet(p1)
			if err == nil && resp == 200 {
				hits++
			}
			time.Sleep(2 * time.Millisecond)
		}
	}()

	// Give the hammer a moment, then replace.
	time.Sleep(30 * time.Millisecond)
	if err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "http-h2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://http-h2", Version: "2"}}); err != nil {
		t.Fatalf("Replace: %v", err)
	}
	close(stop)
	wg.Wait()
	_ = f1
	if got := getStatus(t, p2); got != 200 {
		t.Fatal("new server not responding after replacement")
	}
	expectUnavailable(t, p1)
}

// HTTP-30 — Close storm is idempotent and race-free.
func TestHTTP30CloseStorm(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	addr := freeAddr(t)
	comp := newComp(t, addr)
	if _, err := rt.Load(comp); err != nil {
		t.Fatal(err)
	}
	var wg sync.WaitGroup
	for i := 0; i < 12; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_ = rt.Close(context.Background())
		}()
	}
	wg.Wait()
	// After the storm the Runtime is closed: Load must fail.
	if _, err := rt.Load(newComp(t, freeAddr(t))); err == nil {
		t.Fatal("Load succeeded after Close storm")
	}
	expectUnavailable(t, addr)
}

// ---------------------------------------------------------------------------
// P-HTTP-01..10 — properties.
// ---------------------------------------------------------------------------

// P-HTTP-01 — Lifecycle conservation: Active -> Unloading -> Gone -> new
// Activation works without instance reuse.
func TestPHTTP01LifecycleConservation(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr)
	f := mount(t, rt, comp)
	id1 := comp.HandleID()
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if err := f.Load(); err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if comp.HandleID() == id1 {
		t.Fatal("server instance reused")
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// P-HTTP-02 — Resource conservation across many restarts.
func TestPHTTP02ResourceConservation(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr)
	for i := 0; i < 6; i++ {
		f := mount(t, rt, comp)
		if got := getStatus(t, addr); got != 200 {
			t.Fatalf("cycle %d not serving", i)
		}
		_ = f.Dispose()
		_ = f.Gone(ctxT(t))
		expectUnavailable(t, addr)
	}
}

// P-HTTP-03 — Ownership conservation: each activation owns a fresh server.
func TestPHTTP03OwnershipConservation(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	comp := newComp(t, addr)
	f := mount(t, rt, comp)
	first := comp.HandleID()
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
	f2, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f2.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if comp.HandleID() == first {
		t.Fatal("activation did not create a new server")
	}
	_ = f2.Dispose()
	_ = f2.Gone(ctxT(t))
}

// P-HTTP-04 — Provider withdrawal safety.
func TestPHTTP04ProviderWithdrawalSafety(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	pf := mount(t, rt, newComp(t, addr, httpext.WithProvider()))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	_ = pf.Dispose()
	if err := pf.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if cf.State() != runtime.StatePending {
		t.Fatalf("consumer state = %v after provider gone", cf.State())
	}
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}

// P-HTTP-05 — Provider recovery.
func TestPHTTP05ProviderRecovery(t *testing.T) {
	rt := newRT(t)
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	p1 := mount(t, rt, newComp(t, freeAddr(t), httpext.WithProvider()))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	_ = p1.Dispose()
	_ = p1.Gone(ctxT(t))
	_ = cf.WaitInactive(ctxT(t))
	p2 := mount(t, rt, newComp(t, freeAddr(t), httpext.WithProvider()))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal("consumer did not recover")
	}
	_ = p2.Dispose()
	_ = p2.Gone(ctxT(t))
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}

// P-HTTP-06 — HMR failure preservation.
func TestPHTTP06HMRFailurePreservation(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	addr := freeAddr(t)
	m1 := httpModule(t, ld, "ph1", addr)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	regHTTPFactory(t, ld, "ph2", addr)
	if err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "ph2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://ph2", Version: "2"}}); err == nil {
		t.Fatal("same-port replace should fail")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not preserved")
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatal("old server not preserved")
	}
}

// P-HTTP-07 — Activation isolation: independent servers do not interfere.
func TestPHTTP07ActivationIsolation(t *testing.T) {
	rt := newRT(t)
	a1, a2 := freeAddr(t), freeAddr(t)
	f1 := mount(t, rt, newComp(t, a1))
	f2 := mount(t, rt, newComp(t, a2))
	if f1.ID() == f2.ID() {
		t.Fatal("fiber ids collide")
	}
	if got := getStatus(t, a1); got != 200 {
		t.Fatal("a1 not serving")
	}
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))
	expectUnavailable(t, a1)
	if got := getStatus(t, a2); got != 200 {
		t.Fatal("a2 disturbed by a1 teardown")
	}
	_ = f2.Dispose()
	_ = f2.Gone(ctxT(t))
}

// P-HTTP-08 — Close truthfulness (see HTTP-26 for the full timeout scenario).
func TestPHTTP08CloseTruthfulness(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	addr := freeAddr(t)
	f, err := rt.Load(newComp(t, addr))
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
	expectUnavailable(t, addr)
}

// P-HTTP-09 — Address exclusivity: one address, one owner.
func TestPHTTP09AddressExclusivity(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	f1 := mount(t, rt, newComp(t, addr))
	f2, err := rt.Load(newComp(t, addr))
	if err != nil {
		t.Fatal(err)
	}
	if err := f2.Ready(ctxT(t)); err == nil {
		t.Fatal("second owner active")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("first owner disturbed")
	}
	_ = f1.Dispose()
	_ = f1.Gone(ctxT(t))
	_ = f2.Dispose()
	_ = f2.Gone(ctxT(t))
}

// P-HTTP-10 — Registry stability property.
func TestPHTTP10RegistryStability(t *testing.T) {
	rt := newRT(t)
	addr := freeAddr(t)
	cons := &hCons{id: "c"}
	cf, err := rt.Load(cons)
	if err != nil {
		t.Fatal(err)
	}
	pf := mount(t, rt, newComp(t, addr, httpext.WithProvider()))
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	before := cons.applies.Load()
	// Provider identity stable across independent server restarts is covered
	// by HTTP-13; here assert the consumer stayed bound while serving.
	if got := getStatus(t, addr); got != 200 {
		t.Fatal("server not serving")
	}
	if cons.applies.Load() != before {
		t.Fatal("consumer reactivated unexpectedly")
	}
	_ = pf.Dispose()
	_ = pf.Gone(ctxT(t))
	_ = cf.Dispose()
	_ = cf.Gone(ctxT(t))
}
