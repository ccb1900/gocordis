package procplugin

// Backend liveness observation: an unexpected process death must surface on
// the console observation stream while the fiber stays Active and
// registrations stay (degrade-not-block; recovery is the operator's
// declaration switch, never platform self-retry — 铁律 5). Expected teardown
// (dispose) stays silent. The plugin child reuses TestMain's PROCPLUGIN_CHILD
// mode: the component's backend points at the test binary itself.
import (
	"context"
	"os"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	consolehost "dynamic-runtime/extensions/console/host"
	"dynamic-runtime/runtime"
)

func TestUnexpectedBackendExitIsObserved(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	hc, err := consolehost.NewConsole(config.ComponentConfig{ID: "console-host"})
	if err != nil {
		t.Fatal(err)
	}
	hf, err := rt.Load(hc)
	if err != nil {
		t.Fatal(err)
	}
	waitProcActive(t, hf)

	seen := make(chan consolehost.UIObservation, 64)
	unsub, err := hc.OnObservation(func(ev consolehost.UIObservation) {
		select {
		case seen <- ev:
		default:
		}
	})
	if err != nil {
		t.Fatal(err)
	}
	defer unsub()

	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	// The child IS this binary; the component builds its command inside the
	// (async) Apply from the inherited env — so PROCPLUGIN_CHILD stays set
	// for the whole test, mirroring the re-exec contract.
	os.Setenv("PROCPLUGIN_CHILD", "1")
	t.Cleanup(func() { os.Unsetenv("PROCPLUGIN_CHILD") })
	comp, err := NewComponent(config.ComponentConfig{
		ID: "p1", Type: "proc-plugin",
		Config: map[string]any{"backend": exe, "queries": []any{"echo"}},
	})
	if err != nil {
		t.Fatal(err)
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitProcActive(t, f)
	if !comp.Running() {
		t.Fatal("backend should be running after activation")
	}

	// Kill the backend behind the component's back — the crash case.
	comp.mu.Lock()
	proc := comp.client.cmd.Process
	comp.mu.Unlock()
	if err := proc.Kill(); err != nil {
		t.Fatal(err)
	}

	var gotExit bool
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) && !gotExit {
		select {
		case ev := <-seen:
			if ev.Type == "plugin.exited" && ev.SourceID == "p1" {
				gotExit = true
			}
		case <-time.After(50 * time.Millisecond):
		}
	}
	if !gotExit {
		t.Fatal("unexpected backend death must publish a plugin.exited observation")
	}

	// The fiber survives (degrade-not-block): still Active after the death.
	if state := f.State(); state != runtime.StateActive {
		t.Fatalf("fiber after backend death = %v, want Active", state)
	}

	// Expected teardown is silent: dispose, then drain — no new plugin.exited.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	drainDone := time.Now().Add(400 * time.Millisecond)
	for time.Now().Before(drainDone) {
		select {
		case ev := <-seen:
			if ev.Type == "plugin.exited" {
				t.Fatalf("expected teardown must not observe plugin.exited, got %+v", ev)
			}
		case <-time.After(20 * time.Millisecond):
		}
	}
}

func waitProcActive(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == runtime.StateActive {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}
	t.Fatalf("fiber never became Active (state %v err %v)", f.State(), f.Err())
}
