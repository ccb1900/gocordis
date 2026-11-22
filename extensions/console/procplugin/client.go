// Package procplugin: the GENERIC out-of-process plugin contract for console
// plugins. Where loader/proc binds compile-time Go interfaces, this package
// speaks a runtime, name-discovered contract: a plugin process serves hub
// named queries and commands as JSON-RPC methods over stdin/stdout, and one
// ProcPluginComponent forwards them into the console hub. Protocol shapes
// match loader/proc's Serve side exactly — both use the SHARED low-level
// transport (handshake, line-delimited JSON-RPC, pending map, exit detection,
// graceful stop, single write lock); this adapter only maps the wire to hub
// Query/Command handlers and to this package's error vocabulary.
package procplugin

import (
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"time"

	"dynamic-runtime/extensions/loader/proc/transport"
)

// Protocol constants and wire error shape are re-exported for compatibility.
const (
	HandshakeHeader = transport.HandshakeHeader
	MethodShutdown  = transport.MethodShutdown
)

type rpcError = transport.RPCError

var (
	ErrHandshake  = errors.New("procplugin: handshake failed")
	ErrProcExited = errors.New("procplugin: plugin process exited")
	ErrClosed     = errors.New("procplugin: client closed")
)

// Client is one plugin process connection. Calls are sequential (v1
// contract); every failure — crash, protocol violation, deadline — surfaces
// as a non-nil error. All stdin writes go through the shared transport's
// single write lock: a shutdown request issued from exec's Cancel hook can
// no longer interleave with an RPC frame (the old cancelShutdown wrote under
// c.mu while Call wrote under writeMu).
type Client struct {
	conn *transport.Conn
	// cmd is retained for package-level diagnostics/tests that need the OS
	// process handle; lifecycle authority stays with the shared Conn.
	cmd *exec.Cmd
}

// Start wires pipes onto cmd, starts the process and performs the handshake.
// Build the command yourself (path, args, env, working dir); stderr defaults
// to inherited so plugin diagnostics land in the application log stream.
func Start(ctx context.Context, cmd *exec.Cmd, handshakeWait time.Duration) (*Client, error) {
	var stderr io.Writer = os.Stderr
	if cmd.Stderr != nil {
		stderr = cmd.Stderr
	}
	conn, err := transport.Start(ctx, cmd,
		transport.WithStderr(stderr),
		transport.WithHandshakeWait(handshakeWait),
	)
	if err != nil {
		if errors.Is(err, transport.ErrHandshake) {
			return nil, fmt.Errorf("%w: %v", ErrHandshake, err)
		}
		return nil, fmt.Errorf("%w: %v", ErrProcExited, err)
	}
	return &Client{conn: conn, cmd: cmd}, nil
}

func mapCallErr(err error) error {
	if err == nil {
		return nil
	}
	if errors.Is(err, transport.ErrExited) {
		return fmt.Errorf("%w: %v", ErrProcExited, err)
	}
	if errors.Is(err, transport.ErrClosed) {
		return ErrClosed
	}
	return err
}

// Call invokes one remote method; params is marshaled as the request's
// "params", the response "result" is decoded into result (nil discards).
func (c *Client) Call(ctx context.Context, method string, params any, result any) error {
	if c.Closed() {
		return ErrClosed
	}
	return mapCallErr(c.conn.Call(ctx, method, params, result))
}

// Close asks the plugin to shut down and reverts the process. It is idempotent;
// Gone implies the plugin process has actually been reaped.
func (c *Client) Close() error { return c.conn.Stop(2 * time.Second) }

// Alive reports whether the plugin process is still running.
func (c *Client) Alive() bool { return c.conn.Alive() }

// Exited returns the channel closed when the plugin process terminates —
// expected (Close-driven) or unexpected (crash). Pair with Closed() to tell
// the two apart: Close marks teardown before termination is requested, so
// an exit observed with Closed()==true is ours.
func (c *Client) Exited() <-chan struct{} { return c.conn.Exited() }

// Closed reports whether teardown of this client has begun.
func (c *Client) Closed() bool { return c.conn.Closed() }
