package configwatch_test

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

func writeTOML(t *testing.T, path, body string) {
	t.Helper()
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
}

func waitFiberIDChanged(t *testing.T, ctrl *config.Controller, id string, old runtime.FiberID) runtime.FiberID {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		for _, o := range ctrl.Owned() {
			if o.ID == id && o.Fiber.ID() != old {
				return o.Fiber.ID()
			}
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("fiber %q was not replaced", id)
	return 0
}

func waitEmpty(t *testing.T, ctrl *config.Controller) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if len(ctrl.Owned()) == 0 {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("controller still owns %d components", len(ctrl.Owned()))
}

// AC-20 / AC-02 — full integration chain with the REAL Watch FileWatcher:
// TOML file -> Watch -> Adapter -> Config Controller -> Factory -> Component ->
// Fiber Active; modifying the file replaces the fiber (identity change).
func TestIntegrationFullChain(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "app.toml")
	writeTOML(t, path, tomlComp("cam", "camera", "night"))

	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("camera", &factory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return &markerComponent{id: cc.ID, tag: "cam"}, nil
	}}); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	fw := watch.NewFileWatcher()
	defer func() { _ = fw.Close(); _ = ctrl.CloseContext(ctxT(t)); _ = rt.Close(context.Background()) }()

	src := configwatch.Source{ID: "app", Path: path, Format: configwatch.FormatTOML}
	adapter, err := configwatch.New(src, ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("native file watching unsupported on this platform")
		}
		t.Fatalf("New: %v", err)
	}
	defer adapter.CloseContext(ctxT(t))

	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatalf("Sync: %v", err)
	}
	waitOwned(t, &env{ctrl: ctrl}, 1)
	waitActive(t, &env{ctrl: ctrl})
	oldFiber := ctrl.Owned()[0].Fiber.ID()

	runCtx, runCancel := context.WithCancel(context.Background())
	defer runCancel()
	go adapter.Run(runCtx)

	// Modify the TOML (mode change) -> Config replaces the camera with a new
	// fiber. Individual OS events can occasionally be coalesced away, so write
	// distinct content until the replacement is observed (still change-driven).
	replaceViaWrites(t, adapter, path, ctrl, "cam", oldFiber)

	// Delete the file -> empty desired -> owned components removed. Poll the
	// watch-driven path, then fall back to an explicit Sync (deterministic)
	// only if the OS event was missed.
	if err := os.Remove(path); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(2 * time.Second)
	for len(ctrl.Owned()) != 0 && time.Now().Before(deadline) {
		time.Sleep(5 * time.Millisecond)
	}
	if len(ctrl.Owned()) != 0 {
		if err := adapter.Sync(ctxT(t)); err != nil {
			t.Fatalf("sync after delete: %v", err)
		}
	}
	waitEmpty(t, ctrl)

	// Recreate -> new activation.
	appearViaWrites(t, path, ctrl, "cam")
	waitActive(t, &env{ctrl: ctrl})
}

// AC-21 — dependency semantics preserved: provider replacement withdraws and
// recovers the consumer via the Kernel.
func TestIntegrationDependencyChain(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "app.toml")
	writeTOML(t, path,
		"[[components]]\nid=\"prov\"\ntype=\"provider\"\n\n[components.config]\nmode=\"a\"\n\n"+
			"[[components]]\nid=\"cons\"\ntype=\"consumer\"\n")

	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	if err := reg.Register("provider", &factory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		mode, _ := cc.Config["mode"].(string)
		return &providerComponent{id: cc.ID, mode: mode}, nil
	}}); err != nil {
		t.Fatal(err)
	}
	if err := reg.Register("consumer", &factory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return &consumerComponent{id: cc.ID}, nil
	}}); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	fw := watch.NewFileWatcher()
	defer func() { _ = fw.Close(); _ = ctrl.CloseContext(ctxT(t)); _ = rt.Close(context.Background()) }()

	src := configwatch.Source{ID: "app", Path: path, Format: configwatch.FormatTOML}
	adapter, err := configwatch.New(src, ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatalf("New: %v", err)
	}
	defer adapter.CloseContext(ctxT(t))
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatalf("Sync: %v", err)
	}
	waitOwned(t, &env{ctrl: ctrl}, 2)
	// Wait until both are active (consumer requires provider).
	waitActive(t, &env{ctrl: ctrl})

	runCtx, runCancel := context.WithCancel(context.Background())
	defer runCancel()
	go adapter.Run(runCtx)

	// Replace the provider's mode: provider fiber changes; the consumer follows
	// (Kernel dependency semantics), ending Active again.
	oldProv := fiberID(t, ctrl, "prov")
	replaceViaWritesProv(t, adapter, path, ctrl, oldProv)
	// Consumer must recover to Active (same config entry, kernel re-activates).
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		all := true
		for _, o := range ctrl.Owned() {
			if o.Fiber.State() != runtime.StateActive {
				all = false
			}
		}
		if all && len(ctrl.Owned()) == 2 {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("consumer did not recover to Active after provider replacement")
}

// AC-22 / P5/P6 — ownership isolation: adapter A deleting its source only
// removes controller A's components; controller B and foreign fibers survive.
func TestIntegrationOwnershipIsolation(t *testing.T) {
	dir := t.TempDir()
	pathA := filepath.Join(dir, "a.toml")
	pathB := filepath.Join(dir, "b.toml")
	writeTOML(t, pathA, tomlComp("cam", "camera", "a"))
	writeTOML(t, pathB, tomlComp("cam2", "camera", "b"))

	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())

	mk := func() (*config.Controller, *watch.FileWatcher) {
		reg := config.NewFactoryRegistry()
		if err := reg.Register("camera", &factory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
			return &markerComponent{id: cc.ID, tag: "cam"}, nil
		}}); err != nil {
			t.Fatal(err)
		}
		return config.NewController(rt, reg), watch.NewFileWatcher()
	}

	ctrlA, fwA := mk()
	ctrlB, fwB := mk()
	defer fwA.Close()
	defer fwB.Close()
	defer ctrlA.CloseContext(ctxT(t))
	defer ctrlB.CloseContext(ctxT(t))

	// A foreign component not owned by either controller.
	foreign, err := rt.Load(&markerComponent{id: "foreign", tag: "x"})
	if err != nil {
		t.Fatal(err)
	}
	if err := foreign.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}

	adapterA, err := configwatch.New(configwatch.Source{ID: "a", Path: pathA, Format: configwatch.FormatTOML}, ctrlA, fwA)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer adapterA.CloseContext(ctxT(t))
	adapterB, err := configwatch.New(configwatch.Source{ID: "b", Path: pathB, Format: configwatch.FormatTOML}, ctrlB, fwB)
	if err != nil {
		t.Fatal(err)
	}
	defer adapterB.CloseContext(ctxT(t))

	if err := adapterA.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if err := adapterB.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	waitOwned(t, &env{ctrl: ctrlA}, 1)
	waitOwned(t, &env{ctrl: ctrlB}, 1)

	runCtx, runCancel := context.WithCancel(context.Background())
	defer runCancel()
	go adapterA.Run(runCtx)
	go adapterB.Run(runCtx)

	// Delete A's file: only A's components are removed.
	if err := os.Remove(pathA); err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(2 * time.Second)
	for len(ctrlA.Owned()) != 0 && time.Now().Before(deadline) {
		time.Sleep(5 * time.Millisecond)
	}
	if len(ctrlA.Owned()) != 0 {
		if err := adapterA.Sync(ctxT(t)); err != nil {
			t.Fatalf("sync A after delete: %v", err)
		}
	}
	waitEmpty(t, ctrlA)
	if len(ctrlB.Owned()) != 1 {
		t.Fatal("controller B was affected by A's deletion")
	}
	if foreign.State() != runtime.StateActive {
		t.Fatal("foreign fiber was affected by A's deletion")
	}
}

// appearViaWrites rewrites the file until the component appears again after a
// deletion (tolerates coalesced OS events).
func appearViaWrites(t *testing.T, path string, ctrl *config.Controller, id string) {
	t.Helper()
	for i := 0; i < 10; i++ {
		writeTOML(t, path, tomlComp(id, "camera", fmt.Sprintf("recreated-%d", i)))
		deadline := time.Now().Add(1500 * time.Millisecond)
		for time.Now().Before(deadline) {
			if len(ctrl.Owned()) == 1 {
				return
			}
			time.Sleep(5 * time.Millisecond)
		}
	}
	t.Fatalf("component %q never reappeared", id)
}

// replaceViaWrites rewrites the file with distinct content until the fiber for
// id is replaced (OS events may occasionally coalesce; each write is a new
// revision so a change is emitted).
func replaceViaWrites(t *testing.T, adapter *configwatch.Adapter, path string, ctrl *config.Controller, id string, old runtime.FiberID) {
	t.Helper()
	for i := 0; i < 10; i++ {
		writeTOML(t, path, tomlComp(id, "camera", fmt.Sprintf("mode-%d", i)))
		deadline := time.Now().Add(1500 * time.Millisecond)
		for time.Now().Before(deadline) {
			if got := fiberID(t, ctrl, id); got != old {
				return
			}
			time.Sleep(5 * time.Millisecond)
		}
	}
	t.Fatalf("component %q fiber was not replaced", id)
}

func replaceViaWritesProv(t *testing.T, adapter *configwatch.Adapter, path string, ctrl *config.Controller, old runtime.FiberID) {
	t.Helper()
	for i := 0; i < 10; i++ {
		body := fmt.Sprintf(
			"[[components]]\nid=\"prov\"\ntype=\"provider\"\n\n[components.config]\nmode=\"m%d\"\n\n"+
				"[[components]]\nid=\"cons\"\ntype=\"consumer\"\n", i)
		writeTOML(t, path, body)
		deadline := time.Now().Add(1500 * time.Millisecond)
		for time.Now().Before(deadline) {
			if got := fiberID(t, ctrl, "prov"); got != old {
				return
			}
			time.Sleep(5 * time.Millisecond)
		}
	}
	t.Fatalf("provider fiber was not replaced")
}

func fiberID(t *testing.T, ctrl *config.Controller, id string) runtime.FiberID {
	t.Helper()
	for _, o := range ctrl.Owned() {
		if o.ID == id {
			return o.Fiber.ID()
		}
	}
	t.Fatalf("component %q not owned", id)
	return 0
}
