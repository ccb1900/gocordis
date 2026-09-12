package procplugin

import (
	"context"
	"encoding/json"
	"errors"
	"os"
	"os/exec"
	"testing"
	"time"

	"dynamic-runtime/extensions/loader/proc"
)

// TestMain doubles as the plugin child process: the test binary re-invokes
// itself with PROCPLUGIN_CHILD=1 and serves the contract via loader/proc's
// Serve — which also proves this package's client is wire-compatible with
// the plugin-side SDK.
func TestMain(m *testing.M) {
	if os.Getenv("PROCPLUGIN_CHILD") == "1" {
		methods := map[string]proc.Handler{
			"echo": func(_ context.Context, params json.RawMessage) (any, error) {
				var v any
				if err := json.Unmarshal(params, &v); err != nil {
					return nil, err
				}
				return v, nil
			},
			"fail": func(_ context.Context, _ json.RawMessage) (any, error) {
				return nil, &proc.RPCError{Code: 42, Message: "boom"}
			},
		}
		_ = proc.Serve(methods)
		return
	}
	os.Exit(m.Run())
}

func startChild(t *testing.T) *Client {
	t.Helper()
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	cmd := exec.Command(exe)
	cmd.Env = append(os.Environ(), "PROCPLUGIN_CHILD=1")
	// Start spawns via its own path; reuse it by pointing the executable at
	// the test binary through a wrapper script is overkill — call Start with
	// the test binary and the env already set.
	c, err := Start(context.Background(), cmd, 5*time.Second)
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = c.Close() })
	return c
}

func TestClientCallRoundTrip(t *testing.T) {
	c := startChild(t)
	ctx := context.Background()

	var back map[string]any
	if err := c.Call(ctx, "echo", map[string]any{"hello": "插件", "n": 3}, &back); err != nil {
		t.Fatal(err)
	}
	if back["hello"] != "插件" || back["n"] != float64(3) {
		t.Fatalf("echo = %+v", back)
	}

	// Sequential calls on one connection.
	var second map[string]any
	if err := c.Call(ctx, "echo", map[string]any{"n": 7}, &second); err != nil {
		t.Fatal(err)
	}
	if second["n"] != float64(7) {
		t.Fatalf("second = %+v", second)
	}
}

func TestClientRPCError(t *testing.T) {
	c := startChild(t)
	err := c.Call(context.Background(), "fail", map[string]any{}, nil)
	var re *rpcError
	if !errors.As(err, &re) || re.Code != 42 {
		t.Fatalf("err = %v, want rpcError 42", err)
	}
}

func TestClientUnknownMethod(t *testing.T) {
	c := startChild(t)
	err := c.Call(context.Background(), "nope", map[string]any{}, nil)
	if err == nil {
		t.Fatal("unknown method must fail")
	}
}
