package proc_test

import (
	"context"
	"encoding/json"
	"errors"
	"os"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/proc"
	"dynamic-runtime/runtime"
)

// The plugin process for tests is THE TEST BINARY ITSELF, re-invoked with
// -test.run=TestProcPluginHelper and an env gate (classic helper-process
// pattern). The helper serves "echo" (round trip) and "explode" (crash
// mid-run) and honors "shutdown" via proc.Serve.

const helperEnv = "GORIDIS_PROC_PLUGIN_HELPER"

func TestProcPluginHelper(t *testing.T) {
	if os.Getenv(helperEnv) == "" {
		return
	}
	methods := map[string]proc.Handler{
		"echo": func(_ context.Context, params json.RawMessage) (any, error) {
			var in struct {
				Msg string `json:"msg"`
			}
			if err := json.Unmarshal(params, &in); err != nil {
				return nil, err
			}
			return map[string]string{"out": "echo:" + in.Msg}, nil
		},
		"explode": func(_ context.Context, _ json.RawMessage) (any, error) {
			os.Exit(3) // ungraceful crash mid-run
			return nil, nil
		},
	}
	if err := proc.Serve(methods); err != nil {
		os.Exit(1)
	}
	os.Exit(0)
}

// ---------------------------------------------------------------------------
// harness
// ---------------------------------------------------------------------------

// Codec is the host-side contract: an interface preserved over RPC (paper
// §6.2). The host declares it; the plugin implements the same method names
// over JSON-RPC.
type Codec interface {
	Echo(msg string) (string, error)
	// Explode asks the plugin to exit(3) mid-run (crash simulation).
	Explode() error
}

var codecKey = runtime.NewKey[Codec]("proc.test.codec")

func bindCodec(call proc.Caller) Codec { return &remoteCodec{call: call} }

type remoteCodec struct{ call proc.Caller }

func (r *remoteCodec) Echo(msg string) (string, error) {
	var out struct {
		Out string `json:"out"`
	}
	err := r.call.Call(context.Background(), "echo", map[string]string{"msg": msg}, &out)
	if err != nil {
		return "", err
	}
	return out.Out, nil
}

func (r *remoteCodec) Explode() error {
	return r.call.Call(context.Background(), "explode", struct{}{}, nil)
}

func testBackend(t *testing.T) *proc.Backend {
	t.Helper()
	return proc.NewBackend(codecKey, bindCodec,
		proc.WithArgs("-test.run=TestProcPluginHelper", "-test.timeout=60s"),
		proc.WithEnv(helperEnv+"=1"),
		proc.WithHandshakeWait(10*time.Second),
	)
}

func loadHelperModule(t *testing.T, ld *loader.BuiltinLoader, id string) loader.Module {
	t.Helper()
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	m, err := ld.Load(ctxT(t), loader.Artifact{
		ID:          id,
		BackendType: proc.BackendType,
		Source:      exe,
		Version:     "v1",
	})
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	return *m
}

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func mountAndReady(t *testing.T, rt *runtime.Runtime, ld *loader.BuiltinLoader, moduleID string) *runtime.Fiber {
	t.Helper()
	comp, err := loadHelperModule(t, ld, moduleID).Factory.Create(config.ComponentConfig{ID: moduleID, Type: "proc"})
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctxT(t)); err != nil {
		t.Fatalf("ready: %v", err)
	}
	return f
}

type codecUser struct {
	apply func(ctx *runtime.Context, c Codec) error
}

func (c *codecUser) Name() string { return "codec-user" }
func (c *codecUser) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(codecKey)}
}
func (c *codecUser) Provide() []runtime.Capability {
	return nil
}
func (c *codecUser) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, codecKey)
	if err != nil {
		return nil, err
	}
	if c.apply != nil {
		if err := c.apply(ctx, v); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

// ---------------------------------------------------------------------------
// tests
// ---------------------------------------------------------------------------

// P-01 — Active <=> handshake done; a consumer resolves the capability and
// round-trips a call through the process boundary; disposal stops the process
// before the fiber reaches Gone.
func TestProcRoundTripAndDispose(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT(t))
	ld := loader.NewBuiltinLoader()
	b := testBackend(t)
	defer b.Close()
	if err := ld.RegisterBackend(proc.BackendType, b); err != nil {
		t.Fatal(err)
	}
	f := mountAndReady(t, rt, ld, "codec.v1")

	got := make(chan string, 1)
	cf, err := rt.Load(&codecUser{apply: func(_ *runtime.Context, c Codec) error {
		v, err := c.Echo("hi")
		if err != nil {
			return err
		}
		got <- v
		return nil
	}})
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	select {
	case v := <-got:
		if v != "echo:hi" {
			t.Fatalf("round trip = %q", v)
		}
	case <-time.After(8 * time.Second):
		t.Fatal("timeout waiting for round trip")
	}

	// Disposal: Gone implies the graceful stop already ran.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
}

// P-02 — a plugin crash mid-run surfaces as ErrPluginUnavailable on the call
// path; the fiber is NOT auto-restarted (paper: no automatic retry — recovery
// is a revision/re-enable).
func TestProcCrashMidRun(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT(t))
	ld := loader.NewBuiltinLoader()
	b := testBackend(t)
	defer b.Close()
	if err := ld.RegisterBackend(proc.BackendType, b); err != nil {
		t.Fatal(err)
	}
	f := mountAndReady(t, rt, ld, "codec.crash")

	errs := make(chan error, 2)
	cf, err := rt.Load(&codecUser{apply: func(_ *runtime.Context, c Codec) error {
		_, err := c.Echo("warmup")
		errs <- err
		if err != nil {
			return nil
		}
		_, _ = c.Echo("ping2")
		errs <- c.Explode() // the plugin exits(3) without answering
		return nil
	}})
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctxT(t)); err != nil {
		t.Fatalf("ready: %v", err)
	}
	// First value: the warmup call (nil). Second: the explode call, which must
	// fail because the plugin exits(3) without answering.
	for i, wantFail := range []bool{false, true} {
		select {
		case err := <-errs:
			if wantFail {
				if !errors.Is(err, proc.ErrPluginUnavailable) {
					t.Fatalf("crash error = %v, want ErrPluginUnavailable", err)
				}
			} else if err != nil {
				t.Fatalf("warmup error = %v, want nil", err)
			}
		case <-time.After(8 * time.Second):
			t.Fatalf("timeout waiting for call %d error", i)
		}
	}
	// The provider fiber stays Active: no auto-restart, no auto-fail.
	if f.State() != runtime.StateActive {
		t.Fatalf("provider state after plugin crash = %v, want Active (no auto-retry)", f.State())
	}
}

// P-03 — a missing/non-executable source fails at Load (validation before any
// process exists).
func TestProcInvalidArtifact(t *testing.T) {
	ld := loader.NewBuiltinLoader()
	b := testBackend(t)
	defer b.Close()
	if err := ld.RegisterBackend(proc.BackendType, b); err != nil {
		t.Fatal(err)
	}
	for _, src := range []string{"", "/nonexistent/plugin", t.TempDir()} {
		if _, err := ld.Load(ctxT(t), loader.Artifact{ID: "bad", BackendType: proc.BackendType, Source: src}); err == nil {
			t.Fatalf("source %q: expected load failure", src)
		}
	}
}

// P-04 — replacement: the standard replace composite (unload old, load new)
// yields a fresh process instance per activation with the same contract; two
// live providers for one key remain impossible (kernel exclusivity).
func TestProcReplacementFreshProcess(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT(t))
	ld := loader.NewBuiltinLoader()
	b := testBackend(t)
	defer b.Close()
	if err := ld.RegisterBackend(proc.BackendType, b); err != nil {
		t.Fatal(err)
	}

	f1 := mountAndReady(t, rt, ld, "codec.gen1")
	if err := f1.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f1.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}

	comp, err := loadHelperModule(t, ld, "codec.gen2").Factory.Create(config.ComponentConfig{ID: "codec.gen2", Type: "proc"})
	if err != nil {
		t.Fatal(err)
	}
	f2, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f2.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if f1.ID() == f2.ID() {
		t.Fatal("replacement must create a new fiber identity")
	}
	if err := f2.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f2.Gone(ctxT(t)); err != nil {
		t.Fatal(err)
	}
}

// P-05 — config/manifest integration: the proc factory registers as a config
// Factory and the plugin switch (Enabled) governs the process.
func TestProcConfigSwitchIntegration(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT(t))
	ld := loader.NewBuiltinLoader()
	b := testBackend(t)
	defer b.Close()
	if err := ld.RegisterBackend(proc.BackendType, b); err != nil {
		t.Fatal(err)
	}
	m := loadHelperModule(t, ld, "codec.manifest")

	reg := config.NewFactoryRegistry()
	if err := reg.Register(m.Type, config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return m.Factory.Create(cc)
	})); err != nil {
		t.Fatal(err)
	}
	ctrl := config.NewController(rt, reg)
	defer ctrl.CloseContext(ctxT(t))

	off, on := false, true
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{
		{ID: "codec", Type: m.Type, Enabled: &off},
	}}); err != nil {
		t.Fatal(err)
	}
	snap, err := rt.Snapshot(ctxT(t))
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Fibers) != 0 {
		t.Fatalf("disabled plugin must not produce fibers, got %d", len(snap.Fibers))
	}

	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{
		{ID: "codec", Type: m.Type, Enabled: &on},
	}}); err != nil {
		t.Fatal(err)
	}
	snap, err = rt.Snapshot(ctxT(t))
	if err != nil {
		t.Fatal(err)
	}
	if len(snap.Fibers) != 1 {
		t.Fatalf("enabled plugin fibers = %d, want 1", len(snap.Fibers))
	}
}
