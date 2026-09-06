package loader_test

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// staticFactory builds a Config-semantics Factory producing a named component.
type staticFactory struct {
	name    string
	applies *atomic.Int32
}

func (f *staticFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	c := &testComponent{name: cc.ID}
	if f.applies != nil {
		c.applies = f.applies
	}
	return c, nil
}

type testComponent struct {
	name    string
	applies *atomic.Int32
}

func (c *testComponent) Name() string                  { return c.name }
func (c *testComponent) Inject() []runtime.Dependency  { return nil }
func (c *testComponent) Provide() []runtime.Capability { return nil }
func (c *testComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.applies != nil {
		c.applies.Add(1)
	}
	return nil, nil
}

func newLoader(t *testing.T) *loader.BuiltinLoader {
	t.Helper()
	l := loader.NewBuiltinLoader()
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = l.CloseContext(ctx)
	})
	return l
}

func regBuiltin(t *testing.T, l *loader.BuiltinLoader, src string, f loader.BuiltinFactory) {
	t.Helper()
	if err := l.RegisterBuiltin(src, f); err != nil {
		t.Fatalf("RegisterBuiltin(%q): %v", src, err)
	}
}

func staticBuiltin(tag string) loader.BuiltinFactory {
	return func() config.Factory { return &staticFactory{name: tag} }
}

func art(id, typ, src, ver string) loader.Artifact {
	return loader.Artifact{ID: id, Type: typ, Source: src, Version: ver}
}

// L1 — Load: Absent -> Loaded.
func TestL1Load(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://camera", staticBuiltin("cam"))
	m, err := l.Load(ctxT(t), art("camera-driver", "camera", "builtin://camera", "1.2"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	if m.ID != "camera-driver" || m.Type != "camera" || m.Version != "1.2" {
		t.Fatalf("module = %+v", m)
	}
	if !l.Has("camera-driver") {
		t.Fatal("module not registered")
	}
}

// L2 — Load failure leaves the registry unchanged.
func TestL2LoadFailureRegistryUnchanged(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	if _, err := l.Load(ctxT(t), art("A", "a", "builtin://a", "")); err != nil {
		t.Fatalf("load A: %v", err)
	}
	before := len(l.Snapshot())

	_, err := l.Load(ctxT(t), art("B", "b", "builtin://does-not-exist", ""))
	if !errors.Is(err, loader.ErrLoadFailed) {
		t.Fatalf("Load(B) = %v, want ErrLoadFailed", err)
	}
	if l.Has("B") {
		t.Fatal("failed module must not be registered")
	}
	if got := len(l.Snapshot()); got != before {
		t.Fatalf("registry changed after failed load: %d -> %d", before, got)
	}
	if !l.Has("A") {
		t.Fatal("existing module affected by a failed load")
	}
}

// L3 — Duplicate Load: ErrModuleExists; original untouched.
func TestL3DuplicateLoad(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://camera", staticBuiltin("cam"))
	m1, err := l.Load(ctxT(t), art("cam", "camera", "builtin://camera", "1"))
	if err != nil {
		t.Fatalf("first Load: %v", err)
	}
	_, err = l.Load(ctxT(t), art("cam", "camera", "builtin://camera", "2"))
	if !errors.Is(err, loader.ErrModuleExists) {
		t.Fatalf("duplicate Load = %v, want ErrModuleExists", err)
	}
	got, ok := l.Get("cam")
	if !ok || got.Factory != m1.Factory || got.Version != "1" {
		t.Fatal("original module was replaced or modified")
	}
}

// L4 — Invalid Artifact: no mutation.
func TestL4InvalidArtifact(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	invalid := []loader.Artifact{
		{Type: "a", Source: "builtin://a"}, // empty ID
		{ID: "x", Source: "builtin://a"},   // empty Type
		{ID: "x", Type: "a"},               // empty Source
	}
	for i, a := range invalid {
		if _, err := l.Load(ctxT(t), a); !errors.Is(err, loader.ErrInvalidArtifact) {
			t.Fatalf("invalid artifact #%d = %v, want ErrInvalidArtifact", i, err)
		}
	}
	if len(l.Snapshot()) != 0 {
		t.Fatal("registry mutated by invalid artifacts")
	}
}

// L5 — Invalid Module (nil factory from builtin) never enters the registry.
func TestL5InvalidModule(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://nil", func() config.Factory { return nil })
	_, err := l.Load(ctxT(t), art("bad", "bad", "builtin://nil", ""))
	if !errors.Is(err, loader.ErrInvalidModule) {
		t.Fatalf("Load = %v, want ErrInvalidModule", err)
	}
	if l.Has("bad") {
		t.Fatal("invalid module entered the registry")
	}
}

// L6 — Factory panic -> ErrModuleLoadPanic; Loader survives.
func TestL6FactoryPanic(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://boom", func() config.Factory { panic("boom") })
	_, err := l.Load(ctxT(t), art("x", "x", "builtin://boom", ""))
	if !errors.Is(err, loader.ErrModuleLoadPanic) {
		t.Fatalf("Load = %v, want ErrModuleLoadPanic", err)
	}
	if l.Has("x") {
		t.Fatal("panicking load left a module")
	}
	// Loader still works.
	regBuiltin(t, l, "builtin://ok", staticBuiltin("ok"))
	if _, err := l.Load(ctxT(t), art("ok", "ok", "builtin://ok", "")); err != nil {
		t.Fatalf("loader did not survive factory panic: %v", err)
	}
}

// L7 — Unload: Loaded -> Absent.
func TestL7Unload(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	if _, err := l.Load(ctxT(t), art("A", "a", "builtin://a", "")); err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := l.Unload(ctxT(t), "A"); err != nil {
		t.Fatalf("Unload: %v", err)
	}
	if l.Has("A") {
		t.Fatal("module still present after Unload")
	}
}

// L8 — Unload missing.
func TestL8UnloadMissing(t *testing.T) {
	l := newLoader(t)
	if err := l.Unload(ctxT(t), "nope"); !errors.Is(err, loader.ErrModuleNotFound) {
		t.Fatalf("Unload(missing) = %v, want ErrModuleNotFound", err)
	}
}

// L9 — Unload in use fails and keeps the module loaded.
func TestL9UnloadInUse(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	if _, err := l.Load(ctxT(t), art("A", "a", "builtin://a", "")); err != nil {
		t.Fatalf("Load: %v", err)
	}
	if err := l.Usage().Acquire("A", "consumer-1"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	err := l.Unload(ctxT(t), "A")
	if !errors.Is(err, loader.ErrModuleInUse) {
		t.Fatalf("Unload(in use) = %v, want ErrModuleInUse", err)
	}
	if !l.Has("A") {
		t.Fatal("in-use module was unloaded")
	}
}

// L10/L11 — Usage Acquire / Release lifecycle.
func TestL10L11UsageAcquireRelease(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	if _, err := l.Load(ctxT(t), art("A", "a", "builtin://a", "")); err != nil {
		t.Fatalf("Load: %v", err)
	}
	if l.Usage().InUse("A") {
		t.Fatal("InUse before acquire")
	}
	if err := l.Usage().Acquire("A", "owner"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	if !l.Usage().InUse("A") {
		t.Fatal("InUse false after acquire")
	}
	if err := l.Usage().Release("A", "owner"); err != nil {
		t.Fatalf("Release: %v", err)
	}
	if l.Usage().InUse("A") {
		t.Fatal("InUse true after releasing last owner")
	}
	if err := l.Unload(ctxT(t), "A"); err != nil {
		t.Fatalf("Unload after release: %v", err)
	}
}

// L12 — Duplicate Acquire for the same (module, owner) fails.
func TestL12DuplicateUsage(t *testing.T) {
	u := loader.NewModuleUsage()
	if err := u.Acquire("M", "O"); err != nil {
		t.Fatalf("Acquire: %v", err)
	}
	if err := u.Acquire("M", "O"); !errors.Is(err, loader.ErrModuleUseExists) {
		t.Fatalf("duplicate Acquire = %v, want ErrModuleUseExists", err)
	}
}

// L13 — Usage isolation: releasing a foreign owner does not affect real owners.
func TestL13UsageIsolation(t *testing.T) {
	u := loader.NewModuleUsage()
	_ = u.Acquire("M", "o1")
	_ = u.Acquire("M", "o2")
	if err := u.Release("M", "stranger"); !errors.Is(err, loader.ErrModuleUseNotFound) {
		t.Fatalf("Release(stranger) = %v, want ErrModuleUseNotFound", err)
	}
	if !u.InUse("M") {
		t.Fatal("real owners were affected by foreign release")
	}
	_ = u.Release("M", "o1")
	if !u.InUse("M") {
		t.Fatal("o2 should still hold the module")
	}
	_ = u.Release("M", "o2")
	if u.InUse("M") {
		t.Fatal("module still in use after all owners released")
	}
}

// L14 — Snapshot is immutable.
func TestL14SnapshotImmutability(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://a", staticBuiltin("a"))
	if _, err := l.Load(ctxT(t), art("A", "a", "builtin://a", "1")); err != nil {
		t.Fatalf("Load: %v", err)
	}
	snap := l.Snapshot()

	// Load another module and unload the first: old snapshot unchanged.
	if _, err := l.Load(ctxT(t), art("B", "b", "builtin://a", "2")); err != nil {
		t.Fatalf("Load B: %v", err)
	}
	if err := l.Unload(ctxT(t), "A"); err != nil {
		t.Fatalf("Unload A: %v", err)
	}
	if len(snap) != 1 || snap[0].ID != "A" {
		t.Fatalf("snapshot mutated by later registry changes: %+v", snap)
	}
	// Mutating the returned snapshot does not affect the registry.
	snap[0].Version = "tampered"
	got, present := l.Get("B")
	if !present || got.Version != "2" {
		t.Fatalf("registry affected by snapshot mutation: %+v", got)
	}
}

// L17 — Module identity.
func TestL17ModuleIdentity(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://c", staticBuiltin("c"))
	m, err := l.Load(ctxT(t), art("cam", "camera", "builtin://c", "1.2"))
	if err != nil {
		t.Fatalf("Load: %v", err)
	}
	id := m.Identity()
	if id.ID != "cam" || id.Version != "1.2" {
		t.Fatalf("identity = %+v", id)
	}
	other, _ := l.Load(ctxT(t), art("scan", "scanner", "builtin://c", "2.0"))
	if other.Identity() == id {
		t.Fatal("distinct modules must have distinct identities")
	}
}

// L19 — Load isolation: one failing load never affects other modules.
func TestL19LoadIsolation(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://ok", staticBuiltin("ok"))
	regBuiltin(t, l, "builtin://boom", func() config.Factory { panic("x") })

	if _, err := l.Load(ctxT(t), art("M1", "t1", "builtin://ok", "")); err != nil {
		t.Fatalf("load M1: %v", err)
	}
	if _, err := l.Load(ctxT(t), art("M2", "t2", "builtin://boom", "")); err == nil {
		t.Fatal("M2 load should fail")
	}
	if _, err := l.Load(ctxT(t), art("M3", "t3", "builtin://missing", "")); err == nil {
		t.Fatal("M3 load should fail")
	}
	if !l.Has("M1") {
		t.Fatal("M1 affected by other loads")
	}
	if l.Has("M2") || l.Has("M3") {
		t.Fatal("failed modules registered")
	}
}
