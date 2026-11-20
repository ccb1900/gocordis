package hmr_test

import (
	"context"
	"errors"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// P1 fix regression tests. They pin three P1 bugs:
//
//	(1) ModuleUsage ownership granularity: ModuleUsage is an (ModuleID, OwnerID)
//	    SET, not a refcount — every Target must own its usage under a per-Target
//	    owner id. One shared owner id would let the second Target of the same
//	    module silently reuse the first's entry (its Acquire fails with
//	    ErrModuleUseExists and is ignored), and the first Target's release would
//	    free the module while the second still uses it.
//	(2) doBind rebinding must release the OLD binding's usage, or the old module
//	    can never unload.
//	(3) Runtime.Gone's contract: the fiber IS Gone even when its cleanup
//	    returned an error — HMR must treat "old fiber is Gone" as success for
//	    lifecycle purposes and always release the old binding/usage, or the
//	    stale state poisons every later replacement.

// failingCleanupComponent's Cleanup always returns an error: the fiber still
// reaches Gone (Runtime contract), with the cleanup error as its lifecycle
// error.
type failingCleanupComponent struct {
	id  string
	tag string
}

func (c *failingCleanupComponent) Name() string                  { return c.tag + ":" + c.id }
func (c *failingCleanupComponent) Inject() []runtime.Dependency  { return nil }
func (c *failingCleanupComponent) Provide() []runtime.Capability { return nil }
func (c *failingCleanupComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return func() error { return errors.New("cleanup boom") }, nil
}

type failingCleanupFactory struct{ tag string }

func (f *failingCleanupFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &failingCleanupComponent{id: cc.ID, tag: f.tag}, nil
}

// TestHMRPerTargetUsageOwnership — each Target owns its own ModuleUsage entry:
// unregistering one of two Targets bound to the SAME module must not free the
// module while the other Target still uses it.
func TestHMRPerTargetUsageOwnership(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://shared-v1", "v1", &markerFactory{tag: "v1"})
	mod := e.loadModule(t, artifact("shared-v1", "camera", "builtin://shared-v1", "1"))
	_, _ = e.install(t, "A", "camera", mod, artifact("shared-v1", "camera", "builtin://shared-v1", "1"))
	_, _ = e.install(t, "B", "camera", mod, artifact("shared-v1", "camera", "builtin://shared-v1", "1"))

	if !e.ld.Usage().InUse("shared-v1") {
		t.Fatal("module should be in use while two Targets hold it")
	}

	// Unregister A: B still uses the module, so the usage entry must survive.
	if err := e.ctrl.Unregister("A"); err != nil {
		t.Fatalf("Unregister A: %v", err)
	}
	if !e.ld.Usage().InUse("shared-v1") {
		t.Fatal("usage freed while Target B still uses the module (shared owner id)")
	}

	// Unregister B: last owner gone -> usage released and module unloadable.
	if err := e.ctrl.Unregister("B"); err != nil {
		t.Fatalf("Unregister B: %v", err)
	}
	if e.ld.Usage().InUse("shared-v1") {
		t.Fatal("usage not released after the last owner unregistered")
	}
	if err := e.ld.Unload(context.Background(), "shared-v1"); err != nil {
		t.Fatalf("module not unloadable after every Target released it: %v", err)
	}
}

// TestHMRReplaceKeepsSiblingUsage — replacing one of two Targets bound to the
// same module releases only the replacing Target's usage: the sibling keeps
// the old module in use (and loaded) until it moves away itself.
func TestHMRReplaceKeepsSiblingUsage(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://sh-v1", "v1", &markerFactory{tag: "v1"})
	e.regFactory(t, "builtin://sh-v2", "v2", &markerFactory{tag: "v2"})
	e.regFactory(t, "builtin://sh-v3", "v3", &markerFactory{tag: "v3"})
	mod1 := e.loadModule(t, artifact("sh-v1", "camera", "builtin://sh-v1", "1"))
	_, _ = e.install(t, "A", "camera", mod1, artifact("sh-v2", "camera", "builtin://sh-v2", "2"))
	_, _ = e.install(t, "B", "camera", mod1, artifact("sh-v3", "camera", "builtin://sh-v3", "3"))

	// Replace A to sh-v2: A's own usage on sh-v1 is released; B still holds it.
	if err := e.ctrl.Replace(ctxT(t), target("A", artifact("sh-v2", "camera", "builtin://sh-v2", "2"))); err != nil {
		t.Fatalf("Replace A: %v", err)
	}
	if !e.ld.Usage().InUse("sh-v1") {
		t.Fatal("old module usage freed while Target B still holds it")
	}
	if !e.ld.Has("sh-v1") {
		t.Fatal("old module unloaded while Target B still holds usage on it")
	}
	if b, _ := e.ctrl.CurrentBinding("B"); b.Module.ID != "sh-v1" {
		t.Fatalf("sibling binding disturbed: module %q, want sh-v1", b.Module.ID)
	}

	// Replace B to sh-v3: now sh-v1 has no owner left -> usage gone, module
	// unloaded.
	if err := e.ctrl.Replace(ctxT(t), target("B", artifact("sh-v3", "camera", "builtin://sh-v3", "3"))); err != nil {
		t.Fatalf("Replace B: %v", err)
	}
	if e.ld.Usage().InUse("sh-v1") {
		t.Fatal("sh-v1 usage leaked after both Targets moved away")
	}
	if e.ld.Has("sh-v1") {
		t.Fatal("sh-v1 still loaded after both Targets moved away")
	}
}

// TestHMRBindRebindReleasesOldUsage — re-Binding a Target to a different
// module releases the OLD binding's usage (otherwise the old module could
// never unload); re-Binding the SAME module keeps its single usage entry.
func TestHMRBindRebindReleasesOldUsage(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://rb-v1", "v1", &markerFactory{tag: "v1"})
	e.regFactory(t, "builtin://rb-v2", "v2", &markerFactory{tag: "v2"})
	mod1 := e.loadModule(t, artifact("rb-v1", "camera", "builtin://rb-v1", "1"))
	mod2 := e.loadModule(t, artifact("rb-v2", "camera", "builtin://rb-v2", "2"))
	_, _ = e.install(t, "cam", "camera", mod1, artifact("rb-v2", "camera", "builtin://rb-v2", "2"))

	// Application-driven rebind: build a second fiber from mod2 and Bind again.
	comp2, err := mod2.Factory.Create(config.ComponentConfig{ID: "camera", Type: mod2.Type})
	if err != nil {
		t.Fatal(err)
	}
	fiber2, err := e.rt.Load(comp2)
	if err != nil {
		t.Fatal(err)
	}
	if err := fiber2.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Bind("cam", *mod2, fiber2); err != nil {
		t.Fatalf("re-Bind: %v", err)
	}
	b, ok := e.ctrl.CurrentBinding("cam")
	if !ok || b.Fiber != fiber2 || b.Module.ID != "rb-v2" {
		t.Fatalf("binding after re-Bind = %+v ok=%v, want fiber2 on rb-v2", b, ok)
	}
	if e.ld.Usage().InUse("rb-v1") {
		t.Fatal("old module usage leaked after re-Bind")
	}

	// Re-Binding the SAME module must keep its (single) usage entry held.
	if err := e.ctrl.Bind("cam", *mod2, fiber2); err != nil {
		t.Fatalf("same-module re-Bind: %v", err)
	}
	if !e.ld.Usage().InUse("rb-v2") {
		t.Fatal("same-module re-Bind lost the usage entry")
	}
}

// TestHMRReplaceSurvivesOldCleanupError — Runtime.Gone's contract: the fiber
// IS Gone even when its cleanup returned an error (the cleanup error is kept
// as the fiber's lifecycle error and reported by the Runtime). HMR must treat
// "old fiber is Gone" as success: release the old binding/usage, bind the new
// fiber, and never poison later replacements.
func TestHMRReplaceSurvivesOldCleanupError(t *testing.T) {
	e := newEnv(t)
	if err := e.ld.RegisterBuiltin("builtin://cl-v1", func() config.Factory {
		return &failingCleanupFactory{tag: "v1"}
	}); err != nil {
		t.Fatal(err)
	}
	e.regFactory(t, "builtin://cl-v2", "v2", &markerFactory{tag: "v2"})
	e.regFactory(t, "builtin://cl-v3", "v3", &markerFactory{tag: "v3"})
	oldMod := e.loadModule(t, artifact("cl-v1", "camera", "builtin://cl-v1", "1"))

	// Install v1 inline (the shared install helper asserts *markerComponent).
	comp, err := oldMod.Factory.Create(config.ComponentConfig{ID: "camera", Type: oldMod.Type})
	if err != nil {
		t.Fatal(err)
	}
	oldFiber, err := e.rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := oldFiber.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Register(target("cam", artifact("cl-v2", "camera", "builtin://cl-v2", "2"))); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Bind("cam", *oldMod, oldFiber); err != nil {
		t.Fatal(err)
	}

	if err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cl-v2", "camera", "builtin://cl-v2", "2"))); err != nil {
		t.Fatalf("Replace must succeed even when the old fiber's cleanup failed: %v", err)
	}
	// The old fiber IS Gone despite the cleanup error, and the error stays
	// visible as its lifecycle error (Runtime-owned reporting).
	if oldFiber.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %v, want Gone", oldFiber.State())
	}
	if oldFiber.Err() == nil {
		t.Fatal("expected the cleanup error to remain visible on the old fiber")
	}
	// The old binding/usage must always be released, never retained stale.
	if e.ld.Usage().InUse("cl-v1") {
		t.Fatal("old module usage leaked (release skipped by the cleanup error)")
	}
	if e.ld.Has("cl-v1") {
		t.Fatal("old module still loaded after replacement")
	}
	if !e.ld.Usage().InUse("cl-v2") {
		t.Fatal("new module usage not held after replacement")
	}
	if b, ok := e.ctrl.CurrentBinding("cam"); !ok || b.Fiber.State() != runtime.StateActive || b.Module.ID != "cl-v2" {
		t.Fatalf("binding after replacement = %+v ok=%v, want Active fiber on cl-v2", b, ok)
	}

	// A later replacement must not repeat the stale failure.
	if err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cl-v3", "camera", "builtin://cl-v3", "3"))); err != nil {
		t.Fatalf("second Replace after a cleanup-error replacement: %v", err)
	}
	if b, _ := e.ctrl.CurrentBinding("cam"); b.Module.ID != "cl-v3" {
		t.Fatalf("binding after second replace = module %q, want cl-v3", b.Module.ID)
	}
}
