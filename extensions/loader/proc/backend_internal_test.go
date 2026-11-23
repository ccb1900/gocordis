package proc

import (
	"context"
	"errors"
	"os"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/loader/proc/transport"
	"dynamic-runtime/runtime"
)

// TestMapCallErrKeepsPublicContract pins the adapter boundary after the
// transport extraction: raw pipe/write failures must still satisfy
// errors.Is(err, ErrPluginUnavailable), while typed RPC errors and caller
// cancellation keep their own identity.
func TestMapCallErrKeepsPublicContract(t *testing.T) {
	writeErr := errors.New("write /dev/stdin: broken pipe")
	err := mapCallErr(writeErr)
	if !errors.Is(err, ErrPluginUnavailable) {
		t.Fatalf("raw write error = %v, want ErrPluginUnavailable", err)
	}
	if !strings.Contains(err.Error(), "broken pipe") {
		t.Fatalf("raw write error lost its cause: %v", err)
	}

	err = mapCallErr(transport.ErrProtocol)
	if !errors.Is(err, ErrPluginProtocol) || errors.Is(err, ErrPluginUnavailable) {
		t.Fatalf("protocol error = %v, want only ErrPluginProtocol", err)
	}

	rpc := &transport.RPCError{Code: 42, Message: "boom"}
	err = mapCallErr(rpc)
	var gotRPC *transport.RPCError
	if !errors.As(err, &gotRPC) || gotRPC.Code != 42 || errors.Is(err, ErrPluginUnavailable) {
		t.Fatalf("RPC error = %v, want typed rpc error preserved", err)
	}

	err = mapCallErr(context.DeadlineExceeded)
	if !errors.Is(err, context.DeadlineExceeded) || errors.Is(err, ErrPluginUnavailable) {
		t.Fatalf("deadline = %v, want caller deadline preserved", err)
	}
}

// TestBackendPassesShutdownWaitToTransport verifies WithShutdownTimeout still
// bounds the graceful-shutdown write after the shared-transport extraction.
func TestBackendPassesShutdownWaitToTransport(t *testing.T) {
	exe, err := os.Executable()
	if err != nil {
		t.Fatal(err)
	}
	const want = 137 * time.Millisecond
	b := NewBackend(
		runtime.NewKey[string]("proc.internal.shutdownwait"),
		func(Caller) string { return "" },
		WithArgs("-test.run=TestProcPluginHelper", "-test.timeout=60s"),
		WithEnv("GORIDIS_PROC_PLUGIN_HELPER=1"),
		WithHandshakeWait(10*time.Second),
		WithShutdownTimeout(want),
	)
	defer func() { _ = b.Close() }()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	p, err := b.spawn(exe, "shutdown-wait-probe", ctx)
	if err != nil {
		t.Fatalf("spawn: %v", err)
	}
	defer func() { _ = p.Stop(2 * time.Second) }()
	if got := p.ShutdownWait(); got != want {
		t.Fatalf("transport shutdown wait = %v, want %v", got, want)
	}
}
