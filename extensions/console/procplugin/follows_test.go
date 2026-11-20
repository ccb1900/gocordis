package procplugin

// Plugin-unit co-lifecycle via `follows`: the discovery layer attaches
// follows=<plugin> to a plugin's UI rows, which then Require the backend's
// liveness capability. Disabling the BACKEND row therefore withdraws the UI
// through the Runtime's own dependency rules (consumer-first, Active ->
// Pending, never a crash — PB-03), and re-enabling re-activates it. No
// second plugin-group lifecycle manager exists (AGENTS 铁律 7).
import (
	"context"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/runtime"
)

func TestBackendSwitchCascadesUIClient(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 60*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	hc, err := host.NewConsole(config.ComponentConfig{ID: "console-host"})
	if err != nil {
		t.Fatal(err)
	}
	hf, err := rt.Load(hc)
	if err != nil {
		t.Fatal(err)
	}
	waitProcActive(t, hf)
	adapter := hc.HostAdapter()

	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	uiJs := filepath.Join(t.TempDir(), "ui.js")
	if err := os.WriteFile(uiJs, []byte("export default function register(m){void m}"), 0o644); err != nil {
		t.Fatal(err)
	}

	os.Setenv("PROCPLUGIN_CHILD", "1")
	t.Cleanup(func() { os.Unsetenv("PROCPLUGIN_CHILD") })
	backend, err := NewComponent(config.ComponentConfig{
		ID: "fplug", Type: "proc-plugin",
		Config: map[string]any{"backend": exe, "queries": []any{"echo"}},
	})
	if err != nil {
		t.Fatal(err)
	}
	ui, err := host.NewClientModuleComponent(config.ComponentConfig{
		ID: "fplug:client", Type: "ui-client",
		Config: map[string]any{"module": "fplug", "path": uiJs, "follows": "fplug"},
	})
	if err != nil {
		t.Fatal(err)
	}

	bf, err := rt.Load(backend)
	if err != nil {
		t.Fatal(err)
	}
	uf, err := rt.Load(ui)
	if err != nil {
		t.Fatal(err)
	}
	waitProcActive(t, bf)
	waitProcState(t, uf, runtime.StateActive)
	if mods := adapter.ListClientModules(); len(mods) != 1 || mods[0].Name != "fplug" {
		t.Fatalf("modules with backend active = %+v, want [fplug]", mods)
	}

	// Disable the backend: the UI row is withdrawn by the Runtime and its
	// contribution disappears — Apply never ran again, nothing to unwind.
	if err := bf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := bf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	waitProcState(t, uf, runtime.StatePending)
	if mods := adapter.ListClientModules(); len(mods) != 0 {
		t.Fatalf("modules after backend disable = %+v, want empty", mods)
	}
	if uf.Err() != nil {
		t.Fatalf("withdrawal must not be a failure, got %v", uf.Err())
	}

	// Re-enable: the dependency is satisfied again, the Runtime re-activates
	// the consumer on its own (no manual re-load of the UI fiber).
	bf2, err := rt.Load(backend)
	if err != nil {
		t.Fatal(err)
	}
	waitProcActive(t, bf2)
	waitProcState(t, uf, runtime.StateActive)
	if mods := adapter.ListClientModules(); len(mods) != 1 {
		t.Fatalf("modules after re-enable = %+v, want [fplug]", mods)
	}
}

func waitProcState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatalf("fiber %s: state = %v, want %v (err %v)", f.Name(), f.State(), want, f.Err())
}
