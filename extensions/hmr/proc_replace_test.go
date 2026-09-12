package hmr_test

import (
	"context"
	"encoding/json"
	"os"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/proc"
	"dynamic-runtime/runtime"
)

// G-6: HMR replacement over the PROCESS-EXTERNAL backend - the replacement
// machinery is backend-agnostic, so replacing a proc module yields a fresh
// plugin process owned by the new fiber, the old process is stopped before
// the old fiber reaches Gone, and consumers keep resolving the capability
// across the swap.

type hmrProcCodec interface {
	Echo(msg string) (string, error)
}

var hmrProcKey = runtime.NewKey[hmrProcCodec]("hmr.proc.codec")

func bindHmrProc(call proc.Caller) hmrProcCodec {
	return &hmrProcRemote{call: call}
}

type hmrProcRemote struct{ call proc.Caller }

func (r *hmrProcRemote) Echo(msg string) (string, error) {
	var out struct {
		Out string `json:"out"`
	}
	err := r.call.Call(context.Background(), "echo", map[string]string{"msg": msg}, &out)
	if err != nil {
		return "", err
	}
	return out.Out, nil
}

// TestProcPluginHelperHMR is the helper plugin process for the HMR proc test
// (env-gated; serves "echo" like the loader/proc suite).
func TestProcPluginHelperHMR(t *testing.T) {
	if os.Getenv("GORIDIS_PROC_PLUGIN_HELPER") == "" {
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
	}
	if err := proc.Serve(methods); err != nil {
		os.Exit(1)
	}
	os.Exit(0)
}

func hmrProcBackend(t *testing.T) *proc.Backend {
	t.Helper()
	return proc.NewBackend(hmrProcKey, bindHmrProc,
		proc.WithArgs("-test.run=TestProcPluginHelperHMR", "-test.timeout=60s"),
		proc.WithEnv("GORIDIS_PROC_PLUGIN_HELPER=1"),
		proc.WithHandshakeWait(10*time.Second),
	)
}

type procEchoUser struct {
	out chan string
	msg string
}

func (c *procEchoUser) Name() string { return "proc-echo-user" }
func (c *procEchoUser) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(hmrProcKey)}
}
func (c *procEchoUser) Provide() []runtime.Capability { return nil }
func (c *procEchoUser) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	codec, err := runtime.Require(ctx, hmrProcKey)
	if err != nil {
		return nil, err
	}
	msg := c.msg
	if msg == "" {
		msg = "pre"
	}
	out, err := codec.Echo(msg)
	if err != nil {
		return nil, err
	}
	if c.out != nil {
		select {
		case c.out <- out:
		default:
		}
	}
	return nil, nil
}

// TestHMRProcReplacement - Replace over the proc backend: fresh process for
// the new fiber, old process stopped with the old fiber, consumers keep
// resolving the capability.
func TestHMRProcReplacement(t *testing.T) {
	e := newEnv(t)
	if err := e.ld.RegisterBackend(proc.BackendType, hmrProcBackend(t)); err != nil {
		t.Fatal(err)
	}

	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	procArtifact := func(id, ver string) loader.Artifact {
		return loader.Artifact{ID: id, BackendType: proc.BackendType, Source: exe, Version: ver}
	}

	m1v, err := e.ld.Load(ctxT(t), procArtifact("echo-v1", "1"))
	if err != nil {
		t.Fatalf("load v1: %v", err)
	}
	m1 := *m1v
	if m1.Type != "proc" {
		t.Fatalf("module type = %q, want proc", m1.Type)
	}
	comp1, err := m1.Factory.Create(config.ComponentConfig{ID: "echo", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f1, err := e.rt.Load(comp1)
	if err != nil {
		t.Fatal(err)
	}
	if err := f1.Ready(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Register(hmr.Target{ID: "echo", ComponentID: "echo", Artifact: procArtifact("echo-v1", "1")}); err != nil {
		t.Fatal(err)
	}
	if err := e.ctrl.Bind("echo", *m1v, f1); err != nil {
		t.Fatal(err)
	}

	seen := make(chan string, 4)
	consumer, err := e.rt.Load(&procEchoUser{out: seen})
	if err != nil {
		t.Fatal(err)
	}
	epWaitState(t, consumer, runtime.StateActive)
	select {
	case v := <-seen:
		if v != "echo:pre" {
			t.Fatalf("pre-replace echo = %q", v)
		}
	default:
		t.Fatal("no pre-replace echo")
	}

	if err := e.ctrl.Replace(ctxT(t), hmr.Target{
		ID:          "echo",
		ComponentID: "echo",
		Artifact:    procArtifact("echo-v2", "2"),
	}); err != nil {
		t.Fatalf("replace: %v", err)
	}
	b, ok := e.ctrl.CurrentBinding("echo")
	if !ok {
		t.Fatal("no binding after replace")
	}
	epWaitState(t, b.Fiber, runtime.StateActive)
	if b.Fiber.ID() == f1.ID() {
		t.Fatal("replacement must create a new fiber identity")
	}
	if f1.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %v, want Gone", f1.State())
	}

	consumer2, err := e.rt.Load(&procEchoUser{out: seen, msg: "post"})
	if err != nil {
		t.Fatal(err)
	}
	epWaitState(t, consumer2, runtime.StateActive)
	epWaitSeen(t, seen, "echo:post") // skips any re-activation leftovers
}
