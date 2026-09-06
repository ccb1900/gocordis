package wasm_test

import (
	"context"
	"errors"
	"fmt"
	"path/filepath"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/runtime"
)

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// observer implements wasm.Observer: a thread-safe diagnostics recorder.
type observer struct {
	mu        sync.Mutex
	created   []wasmEvent
	destroyed []wasmEvent
}

type wasmEvent struct {
	module   string
	instance uint64
}

func newObserver() *observer { return &observer{} }

func (o *observer) InstanceCreated(moduleID string, instanceID uint64) {
	o.mu.Lock()
	defer o.mu.Unlock()
	o.created = append(o.created, wasmEvent{module: moduleID, instance: instanceID})
}

func (o *observer) InstanceDestroyed(moduleID string, instanceID uint64) {
	o.mu.Lock()
	defer o.mu.Unlock()
	o.destroyed = append(o.destroyed, wasmEvent{module: moduleID, instance: instanceID})
}

func (o *observer) createdFor(module string) []wasmEvent {
	o.mu.Lock()
	defer o.mu.Unlock()
	var out []wasmEvent
	for _, e := range o.created {
		if e.module == module {
			out = append(out, e)
		}
	}
	return out
}

func (o *observer) destroyedFor(module string) []wasmEvent {
	o.mu.Lock()
	defer o.mu.Unlock()
	var out []wasmEvent
	for _, e := range o.destroyed {
		if e.module == module {
			out = append(out, e)
		}
	}
	return out
}

// env bundles the real Runtime + Loader with a registered WASM Backend.
type env struct {
	rt  *runtime.Runtime
	ld  *loader.BuiltinLoader
	wb  *wasm.Backend
	obs *observer
}

func newEnv(t *testing.T) *env {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	ld := loader.NewBuiltinLoader()
	obs := newObserver()
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		t.Fatalf("RegisterBackend: %v", err)
	}
	e := &env{rt: rt, ld: ld, wb: wb, obs: obs}
	t.Cleanup(func() {
		_ = rt.Close(context.Background())
		clctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ld.CloseContext(clctx)
		_ = wb.Close()
	})
	return e
}

// src builds a file:// URI for a testdata fixture.
func src(t *testing.T, name string) string {
	t.Helper()
	abs, err := filepath.Abs(filepath.Join("testdata", name))
	if err != nil {
		t.Fatalf("abs path: %v", err)
	}
	return "file://" + abs
}

func art(id, source, ver string) loader.Artifact {
	return loader.Artifact{ID: id, BackendType: loader.BackendWASM, Source: source, Version: ver}
}

func (e *env) load(t *testing.T, a loader.Artifact) *loader.Module {
	t.Helper()
	m, err := e.ld.Load(ctxT(t), a)
	if err != nil {
		t.Fatalf("Load(%+v): %v", a, err)
	}
	return m
}

// install creates a component from a module and mounts it as a Fiber.
func (e *env) install(t *testing.T, m *loader.Module, componentID string, cfg map[string]any) *runtime.Fiber {
	t.Helper()
	comp, err := m.Factory.Create(config.ComponentConfig{ID: componentID, Type: m.Type, Config: cfg})
	if err != nil {
		t.Fatalf("Factory.Create: %v", err)
	}
	f, err := e.rt.Load(comp)
	if err != nil {
		t.Fatalf("rt.Load: %v", err)
	}
	return f
}

// W-01 — Valid WASM: Load -> Module Loaded.
func TestW01ValidWASMLoad(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w1", src(t, "valid.wasm"), "1.0"))
	if m.ID != "w1" || m.Type != "industrial.camera" || m.Version != "1.0" {
		t.Fatalf("module = %+v", m)
	}
	if !e.ld.Has("w1") {
		t.Fatal("module not registered")
	}
	if got := e.obs.createdFor("w1"); len(got) != 0 {
		t.Fatalf("Load must not create instances: %v", got)
	}
}

// W-02 — Invalid WASM: ErrInvalidWASM; registry unchanged.
func TestW02InvalidWASMRegistryUnchanged(t *testing.T) {
	e := newEnv(t)
	ok := e.load(t, art("ok", src(t, "valid.wasm"), "1"))
	before := len(e.ld.Snapshot())

	_, err := e.ld.Load(ctxT(t), art("bad", src(t, "invalid.wasm"), "1"))
	if !errors.Is(err, wasm.ErrInvalidWASM) {
		t.Fatalf("Load(invalid) = %v, want ErrInvalidWASM", err)
	}
	if e.ld.Has("bad") {
		t.Fatal("invalid module entered the registry")
	}
	if got := len(e.ld.Snapshot()); got != before {
		t.Fatalf("registry changed after failed load: %d -> %d", before, got)
	}
	got, present := e.ld.Get("ok")
	if !present || got.Factory != ok.Factory {
		t.Fatal("existing module disturbed by a failed load")
	}
}

// W-03 — Missing ABI export: ErrWASMABI; module never registered.
func TestW03MissingABI(t *testing.T) {
	e := newEnv(t)
	_, err := e.ld.Load(ctxT(t), art("noabi", src(t, "missing-export.wasm"), "1"))
	if !errors.Is(err, wasm.ErrWASMABI) {
		t.Fatalf("Load(missing-export) = %v, want ErrWASMABI", err)
	}
	if e.ld.Has("noabi") {
		t.Fatal("ABI-violating module entered the registry")
	}
}

// W-04 — Duplicate Module ID: first success, second ErrModuleExists, original
// untouched.
func TestW04DuplicateModule(t *testing.T) {
	e := newEnv(t)
	m1 := e.load(t, art("dup", src(t, "valid.wasm"), "1"))
	_, err := e.ld.Load(ctxT(t), art("dup", src(t, "valid.wasm"), "2"))
	if !errors.Is(err, loader.ErrModuleExists) {
		t.Fatalf("duplicate Load = %v, want ErrModuleExists", err)
	}
	got, ok := e.ld.Get("dup")
	if !ok || got.Factory != m1.Factory || got.Version != "1" {
		t.Fatal("original module replaced or modified by duplicate load")
	}
}

// W-05 — Factory: Module.Factory.Create -> Component.
func TestW05Factory(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w5", src(t, "valid.wasm"), "1"))
	comp, err := m.Factory.Create(config.ComponentConfig{ID: "c5", Type: m.Type})
	if err != nil {
		t.Fatalf("Create: %v", err)
	}
	if comp == nil {
		t.Fatal("Create returned nil component")
	}
	if comp.Name() != "wasm:c5" {
		t.Fatalf("component name = %q, want wasm:c5", comp.Name())
	}
}

// W-06 — Runtime Integration: .wasm -> Loader -> Module -> Factory ->
// Component -> Runtime.Load -> Fiber.Ready.
func TestW06RuntimeIntegration(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w6", src(t, "valid.wasm"), "1"))
	f := e.install(t, m, "c6", nil)
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("Fiber not active: %v", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("state = %v, want Active", f.State())
	}
	if got := e.obs.createdFor("w6"); len(got) != 1 {
		t.Fatalf("instances created = %v, want exactly 1", got)
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// W-07 — Fiber Lifecycle: Load -> Active -> Dispose -> Gone; the WASM instance
// follows the Activation lifecycle exactly (created once, destroyed once).
func TestW07FiberLifecycle(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w7", src(t, "valid.wasm"), "1"))
	f := e.install(t, m, "c7", nil)
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("Ready: %v", err)
	}
	if got := e.obs.createdFor("w7"); len(got) != 1 {
		t.Fatalf("created = %v, want 1 before dispose", got)
	}
	if got := e.obs.destroyedFor("w7"); len(got) != 0 {
		t.Fatalf("destroyed before dispose = %v, want none", got)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if f.State() != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", f.State())
	}
	created := e.obs.createdFor("w7")
	destroyed := e.obs.destroyedFor("w7")
	if len(created) != 1 || len(destroyed) != 1 {
		t.Fatalf("created/destroyed = %d/%d, want 1/1", len(created), len(destroyed))
	}
	if created[0].instance != destroyed[0].instance {
		t.Fatalf("destroyed instance %d != created instance %d", destroyed[0].instance, created[0].instance)
	}
}

// W-08 — Activation Freshness: a second activation of the same Fiber must
// produce a DIFFERENT WASM instance; instances are never reused.
func TestW08ActivationFreshness(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w8", src(t, "valid.wasm"), "1"))
	f := e.install(t, m, "c8", nil)
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("activation #1 Ready: %v", err)
	}
	first := e.obs.createdFor("w8")
	if len(first) != 1 {
		t.Fatalf("activation #1 created = %v, want 1", first)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	// Activation #2 on the same Fiber object.
	if err := f.Load(); err != nil {
		t.Fatalf("Load (activation #2): %v", err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("activation #2 Ready: %v", err)
	}

	created := e.obs.createdFor("w8")
	if len(created) != 2 {
		t.Fatalf("total created = %v, want 2", created)
	}
	if created[0].instance == created[1].instance {
		t.Fatalf("activation #2 reused instance %d — freshness violated", created[0].instance)
	}
	// The first instance must be gone; the second must still be live.
	destroyed := e.obs.destroyedFor("w8")
	if len(destroyed) != 1 || destroyed[0].instance != created[0].instance {
		t.Fatalf("destroyed = %v, want only instance %d", destroyed, created[0].instance)
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// W-09 — Apply Failure: a failure after instance init must unwind (destroy the
// instance) and land the Fiber in Failed with no leak.
func TestW09ApplyFailure(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w9", src(t, "valid.wasm"), "1"))
	f := e.install(t, m, "c9", map[string]any{"fail_apply": true})
	if err := f.Ready(ctxT(t)); err == nil {
		t.Fatal("Ready() = nil, want apply failure")
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", f.State())
	}
	created := e.obs.createdFor("w9")
	destroyed := e.obs.destroyedFor("w9")
	if len(created) != 1 || len(destroyed) != 1 {
		t.Fatalf("created/destroyed = %d/%d, want 1/1 (no instance leak)", len(created), len(destroyed))
	}
	if created[0].instance != destroyed[0].instance {
		t.Fatalf("destroyed %d != created %d", destroyed[0].instance, created[0].instance)
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// W-10 — Module/Fiber Orthogonality: Unload of an in-use Module is rejected
// (ErrModuleInUse); after release, Unload succeeds but never disposes the Fiber.
func TestW10ModuleFiberOrthogonality(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w10", src(t, "valid.wasm"), "1"))
	f := e.install(t, m, "c10", nil)
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("Ready: %v", err)
	}

	if err := e.ld.Usage().Acquire(m.ID, "owner"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload(in use) = %v, want ErrModuleInUse", err)
	}
	if f.State() != runtime.StateActive {
		t.Fatalf("fiber state changed while module in use: %v", f.State())
	}
	if err := e.ld.Usage().Release(m.ID, "owner"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); err != nil {
		t.Fatalf("Unload after release: %v", err)
	}
	if e.ld.Has(m.ID) {
		t.Fatal("module still present after Unload")
	}
	// Module Unload never disposes a Fiber.
	if f.State() != runtime.StateActive {
		t.Fatalf("module unload disturbed the fiber: %v", f.State())
	}
	_ = f.Dispose()
	_ = f.Gone(ctxT(t))
}

// W-11 — Usage: Acquire blocks Unload; Release allows it.
func TestW11Usage(t *testing.T) {
	e := newEnv(t)
	m := e.load(t, art("w11", src(t, "valid.wasm"), "1"))
	if err := e.ld.Usage().Acquire(m.ID, "consumer-1"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload(in use) = %v, want ErrModuleInUse", err)
	}
	if err := e.ld.Usage().Release(m.ID, "consumer-1"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if err := e.ld.Unload(ctxT(t), m.ID); err != nil {
		t.Fatalf("Unload after release: %v", err)
	}
}

// W-12 — Loader Isolation: WASM failures never affect Builtin modules and vice
// versa.
func TestW12LoaderIsolation(t *testing.T) {
	e := newEnv(t)
	// Builtin side.
	if err := e.ld.RegisterBuiltin("builtin://a", func() config.Factory {
		return &staticFactory{name: "builtin-a"}
	}); err != nil {
		t.Fatal(err)
	}
	bm, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "b1", Type: "camera", Source: "builtin://a", Version: "1"})
	if err != nil {
		t.Fatalf("builtin load: %v", err)
	}
	// WASM side.
	wm := e.load(t, art("w12", src(t, "valid.wasm"), "1"))

	// WASM failure leaves the Builtin module alone.
	if _, err := e.ld.Load(ctxT(t), art("bad", src(t, "invalid.wasm"), "1")); !errors.Is(err, wasm.ErrInvalidWASM) {
		t.Fatalf("invalid wasm load = %v, want ErrInvalidWASM", err)
	}
	if _, ok := e.ld.Get("b1"); !ok {
		t.Fatal("builtin module lost after wasm failure")
	}
	// Builtin failure leaves the WASM module alone.
	if _, err := e.ld.Load(ctxT(t), loader.Artifact{ID: "b2", Type: "camera", Source: "builtin://missing", Version: "1"}); !errors.Is(err, loader.ErrLoadFailed) {
		t.Fatalf("missing builtin load = %v, want ErrLoadFailed", err)
	}
	if _, ok := e.ld.Get("w12"); !ok {
		t.Fatal("wasm module lost after builtin failure")
	}
	// Both modules remain usable.
	if _, err := bm.Factory.Create(config.ComponentConfig{ID: "b", Type: bm.Type}); err != nil {
		t.Fatalf("builtin factory unusable: %v", err)
	}
	if _, err := wm.Factory.Create(config.ComponentConfig{ID: "w", Type: wm.Type}); err != nil {
		t.Fatalf("wasm factory unusable: %v", err)
	}
}

// W-13 — Close: after Backend.Close, Load -> ErrWASMBackendClosed; after
// Loader.Close, Load -> ErrLoaderClosed.
func TestW13Close(t *testing.T) {
	t.Run("backend closed", func(t *testing.T) {
		e := newEnv(t)
		if err := e.wb.Close(); err != nil {
			t.Fatalf("Close: %v", err)
		}
		if err := e.wb.Close(); err != nil {
			t.Fatalf("second Close not idempotent: %v", err)
		}
		if !e.wb.Closed() {
			t.Fatal("Closed() = false after Close")
		}
		_, err := e.ld.Load(ctxT(t), art("after", src(t, "valid.wasm"), "1"))
		if !errors.Is(err, wasm.ErrWASMBackendClosed) {
			t.Fatalf("Load after backend Close = %v, want ErrWASMBackendClosed", err)
		}
		if e.ld.Has("after") {
			t.Fatal("module registered despite closed backend")
		}
	})
	t.Run("loader closed", func(t *testing.T) {
		e := newEnv(t)
		clctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		if err := e.ld.CloseContext(clctx); err != nil {
			t.Fatalf("CloseContext: %v", err)
		}
		_, err := e.ld.Load(ctxT(t), art("after", src(t, "valid.wasm"), "1"))
		if !errors.Is(err, loader.ErrLoaderClosed) {
			t.Fatalf("Load after loader Close = %v, want ErrLoaderClosed", err)
		}
	})
}

// W-14 — Race: concurrent Loads (distinct and same IDs) and concurrent
// activations stay consistent; must be clean under -race.
func TestW14Race(t *testing.T) {
	e := newEnv(t)

	const n = 12
	var wg sync.WaitGroup
	errs := make([]error, n)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			id := fmt.Sprintf("race-%d", i)
			_, errs[i] = e.ld.Load(context.Background(), art(id, src(t, "valid.wasm"), "1"))
		}(i)
	}
	wg.Wait()
	for i, err := range errs {
		if err != nil {
			t.Fatalf("concurrent load race-%d: %v", i, err)
		}
	}
	if len(e.ld.Snapshot()) != n {
		t.Fatalf("registry has %d modules, want %d", len(e.ld.Snapshot()), n)
	}

	// Same-ID concurrent load: exactly one winner.
	var okCount int32
	var wg2 sync.WaitGroup
	for i := 0; i < n; i++ {
		wg2.Add(1)
		go func() {
			defer wg2.Done()
			_, err := e.ld.Load(context.Background(), art("same", src(t, "valid.wasm"), "1"))
			if err == nil {
				atomic.AddInt32(&okCount, 1)
			} else if !errors.Is(err, loader.ErrModuleExists) {
				t.Errorf("unexpected same-id load error: %v", err)
			}
		}()
	}
	wg2.Wait()
	if okCount != 1 {
		t.Fatalf("same-id winners = %d, want 1", okCount)
	}

	// Concurrent activations of several modules produce independent instances.
	mod := e.load(t, art("shared", src(t, "valid.wasm"), "1"))
	const fibers = 8
	fs := make([]*runtime.Fiber, fibers)
	var wg3 sync.WaitGroup
	for i := 0; i < fibers; i++ {
		wg3.Add(1)
		go func(i int) {
			defer wg3.Done()
			comp, err := mod.Factory.Create(config.ComponentConfig{ID: fmt.Sprintf("c%d", i), Type: mod.Type})
			if err != nil {
				t.Errorf("create %d: %v", i, err)
				return
			}
			f, err := e.rt.Load(comp)
			if err != nil {
				t.Errorf("load %d: %v", i, err)
				return
			}
			fs[i] = f
		}(i)
	}
	wg3.Wait()
	for i, f := range fs {
		if f == nil {
			continue
		}
		if err := f.Ready(ctxT(t)); err != nil {
			t.Fatalf("fiber %d not active: %v", i, err)
		}
		_ = f.Dispose()
	}
	for _, f := range fs {
		if f != nil {
			_ = f.Gone(ctxT(t))
		}
	}
	created := e.obs.createdFor("shared")
	destroyed := e.obs.destroyedFor("shared")
	if len(created) != fibers || len(destroyed) != fibers {
		t.Fatalf("created/destroyed = %d/%d, want %d/%d", len(created), len(destroyed), fibers, fibers)
	}
	seen := map[uint64]bool{}
	for _, c := range created {
		if seen[c.instance] {
			t.Fatalf("duplicate instance id %d", c.instance)
		}
		seen[c.instance] = true
	}
}

// staticFactory is a minimal config.Factory for the Builtin side of W-12.
type staticFactory struct {
	name string
}

func (f *staticFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &staticComp{name: f.name + ":" + cc.ID}, nil
}

type staticComp struct{ name string }

func (c *staticComp) Name() string                  { return c.name }
func (c *staticComp) Inject() []runtime.Dependency  { return nil }
func (c *staticComp) Provide() []runtime.Capability { return nil }
func (c *staticComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}
