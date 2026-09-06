package http_test

import (
	"context"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	httpext "dynamic-runtime/extensions/http"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// httpModFactory builds an HTTP server Component for a fixed address.
type httpModFactory struct {
	addr string
}

func (f *httpModFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return httpext.New(cfgAt(f.addr))
}

func httpModule(t *testing.T, ld *loader.BuiltinLoader, id, addr string) *loader.Module {
	t.Helper()
	regHTTPFactory(t, ld, id, addr)
	m, err := ld.Load(ctxT(t), loader.Artifact{ID: id, BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://" + id, Version: "1"})
	if err != nil {
		t.Fatalf("Load(%s): %v", id, err)
	}
	return m
}

// regHTTPFactory registers a builtin source WITHOUT loading its module, so an
// HMR Replace can load it later.
func regHTTPFactory(t *testing.T, ld *loader.BuiltinLoader, id, addr string) {
	t.Helper()
	if err := ld.RegisterBuiltin("builtin://"+id, func() config.Factory { return &httpModFactory{addr: addr} }); err != nil {
		t.Fatalf("RegisterBuiltin(%s): %v", id, err)
	}
}

func hmrEnv(t *testing.T) (*runtime.Runtime, *loader.BuiltinLoader, *hmr.Controller) {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	t.Cleanup(func() { _ = h.CloseContext(ctxT(t)) })
	t.Cleanup(func() { _ = ld.CloseContext(ctxT(t)) })
	t.Cleanup(func() { _ = rt.Close(context.Background()) })
	return rt, ld, h
}

func hmrInstall(t *testing.T, rt *runtime.Runtime, h *hmr.Controller, mod *loader.Module, componentID string) *runtime.Fiber {
	t.Helper()
	comp, err := mod.Factory.Create(config.ComponentConfig{ID: componentID, Type: mod.Type})
	if err != nil {
		t.Fatalf("create: %v", err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("rt.Load: %v", err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("Ready: %v", err)
	}
	tg := hmr.Target{ID: "http", ComponentID: componentID, Artifact: loader.Artifact{ID: mod.ID + "-next", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://" + mod.ID + "-next", Version: "2"}}
	if err := h.Register(tg); err != nil {
		t.Fatal(err)
	}
	if err := h.Bind("http", *mod, f); err != nil {
		t.Fatal(err)
	}
	return f
}

// HTTP-16 — Different-port warm replacement: v2 serves while v1 is retired.
func TestHTTP16DifferentPortWarmReplace(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	p1, p2 := freeAddr(t), freeAddr(t)
	m1 := httpModule(t, ld, "http-v1", p1)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	if got := getStatus(t, p1); got != 200 {
		t.Fatalf("v1 not serving: %d", got)
	}
	regHTTPFactory(t, ld, "http-v2", p2)
	if err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "http-v2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://http-v2", Version: "2"}}); err != nil {
		t.Fatalf("Replace: %v", err)
	}
	b, _ := h.CurrentBinding("http")
	if b.Fiber.ID() == f1.ID() || b.Fiber.State() != runtime.StateActive {
		t.Fatal("replacement invalid")
	}
	if got := getStatus(t, p2); got != 200 {
		t.Fatalf("v2 not serving: %d", got)
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("v1 state = %v, want Gone", f1.State())
	}
	expectUnavailable(t, p1)
}

// HTTP-17 — Same-port warm replacement fails safely: v1 stays Active.
func TestHTTP17SamePortFailurePreservation(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	addr := freeAddr(t)
	m1 := httpModule(t, ld, "http-s1", addr)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	regHTTPFactory(t, ld, "http-s2", addr)
	err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "http-s2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://http-s2", Version: "2"}})
	if err == nil {
		t.Fatal("same-port warm replace unexpectedly succeeded")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not Active after failed same-port replace")
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatalf("old server no longer responding: %d", got)
	}
	b, _ := h.CurrentBinding("http")
	if b.Fiber != f1 {
		t.Fatal("binding changed")
	}
	if e := ld.Usage().InUse("http-s1"); !e {
		t.Fatal("old module usage lost")
	}
}

// HTTP-18 — HMR instance identity: Fiber1 != Fiber2, Server1 != Server2.
func TestHTTP18HMRInstanceIdentity(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	p1, p2 := freeAddr(t), freeAddr(t)
	m1 := httpModule(t, ld, "http-i1", p1)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	regHTTPFactory(t, ld, "http-i2", p2)
	if err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "http-i2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://http-i2", Version: "2"}}); err != nil {
		t.Fatal(err)
	}
	b, _ := h.CurrentBinding("http")
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("fiber reused")
	}
	// v1 server must be stopped; v2 serving: distinct server instances.
	expectUnavailable(t, p1)
	if got := getStatus(t, p2); got != 200 {
		t.Fatalf("v2 not serving: %d", got)
	}
}

// HTTP-19 — HMR cleanup: old server stopped, old listener released, old Module
// usage released and Module unloaded.
func TestHTTP19HMRCleanup(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	p1, p2 := freeAddr(t), freeAddr(t)
	m1 := httpModule(t, ld, "http-c1", p1)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	regHTTPFactory(t, ld, "http-c2", p2)
	if err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "http-c2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://http-c2", Version: "2"}}); err != nil {
		t.Fatal(err)
	}
	_ = f1
	if ld.Usage().InUse("http-c1") || ld.Has("http-c1") {
		t.Fatal("old module not released/unloaded")
	}
	expectUnavailable(t, p1)
	if !ld.Usage().InUse("http-c2") {
		t.Fatal("new module usage not held")
	}
}

// HTTP-20 — HMR failure keeps the old server responding.
func TestHTTP20HMRFailure(t *testing.T) {
	rt, ld, h := hmrEnv(t)
	addr := freeAddr(t)
	m1 := httpModule(t, ld, "http-f1", addr)
	f1 := hmrInstall(t, rt, h, m1, "srv")
	regHTTPFactory(t, ld, "http-f2", addr)
	err := h.Replace(ctxT(t), hmr.Target{ID: "http", ComponentID: "srv", Artifact: loader.Artifact{ID: "http-f2", BackendType: loader.BackendBuiltin, Type: "http.server", Source: "builtin://http-f2", Version: "2"}})
	if err == nil {
		t.Fatal("replace should have failed (same port)")
	}
	if f1.State() != runtime.StateActive {
		t.Fatal("old fiber not active")
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatal("old server stopped responding after failed HMR")
	}
}

// HTTP-21 — Config -> HTTP: a config-driven HTTP component serves.
func TestHTTP21ConfigToHTTP(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("http.server", httpext.NewConfigFactory()); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	addr := freeAddr(t)
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "web", Type: "http.server", Config: map[string]any{"address": addr}}}}); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	var fiber *runtime.Fiber
	for _, o := range ctrl.Owned() {
		if o.ID == "web" {
			fiber = o.Fiber
		}
	}
	if fiber == nil {
		t.Fatal("component not owned")
	}
	if err := fiber.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if got := getStatus(t, addr); got != 200 {
		t.Fatalf("config http not serving: %d", got)
	}
	_ = ctrl.CloseContext(ctxT(t))
	_ = rt.Close(context.Background())
}

// HTTP-22 — Invalid config preservation: an invalid desired config leaves the
// applied HTTP component unchanged.
func TestHTTP22InvalidConfigPreservation(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("http.server", httpext.NewConfigFactory()); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	addr := freeAddr(t)
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "web", Type: "http.server", Config: map[string]any{"address": addr}}}}); err != nil {
		t.Fatal(err)
	}
	var fiber *runtime.Fiber
	for _, o := range ctrl.Owned() {
		if o.ID == "web" {
			fiber = o.Fiber
		}
	}
	if fiber == nil {
		t.Fatal("no owned component")
	}
	if err := fiber.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	idBefore := fiber.ID()

	// Invalid desired (unknown type fails Config-level validation before any
	// mutation) must leave the running HTTP component untouched.
	err = ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "web", Type: "http.does-not-exist", Config: map[string]any{"address": "x"}}}})
	if err == nil {
		t.Fatal("invalid config accepted")
	}
	if fiber.State() != runtime.StateActive {
		t.Fatal("old fiber disturbed by invalid config")
	}
	var after *runtime.Fiber
	for _, o := range ctrl.Owned() {
		if o.ID == "web" {
			after = o.Fiber
		}
	}
	if after == nil || after.ID() != idBefore {
		t.Fatal("applied HTTP component replaced by invalid config")
	}
	_ = ctrl.CloseContext(ctxT(t))
	_ = rt.Close(context.Background())
}

// HTTP-23 — Config replacement: switching the address replaces the HTTP fiber
// and the old port is released.
func TestHTTP23ConfigReplacement(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("http.server", httpext.NewConfigFactory()); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	p1, p2 := freeAddr(t), freeAddr(t)
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "web", Type: "http.server", Config: map[string]any{"address": p1}}}}); err != nil {
		t.Fatal(err)
	}
	fiberOf := func() *runtime.Fiber {
		for _, o := range ctrl.Owned() {
			if o.ID == "web" {
				return o.Fiber
			}
		}
		return nil
	}
	f1 := fiberOf()
	if f1 == nil {
		t.Fatal("no web fiber")
	}
	if err := f1.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	oldID := f1.ID()

	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{{ID: "web", Type: "http.server", Config: map[string]any{"address": p2}}}}); err != nil {
		t.Fatal(err)
	}
	f2 := fiberOf()
	if f2 == nil || f2.ID() == oldID {
		t.Fatal("fiber not replaced")
	}
	if err := f2.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	expectUnavailable(t, p1)
	if got := getStatus(t, p2); got != 200 {
		t.Fatalf("new config http not serving: %d", got)
	}
	_ = ctrl.CloseContext(ctxT(t))
	_ = rt.Close(context.Background())
}
