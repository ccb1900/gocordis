// Package procplugin: the GENERIC out-of-process plugin contract for console
// plugins. Where loader/proc binds compile-time Go interfaces, this package
// speaks a runtime, name-discovered contract: a plugin process serves hub
// named queries and commands as JSON-RPC methods over stdin/stdout, and one
// ProcPluginComponent forwards them into the console hub. Protocol shapes
// match loader/proc's Serve side exactly (handshake GORIDIS-PROC-PLUGIN 1,
// line-delimited JSON-RPC 2.0, one in-flight call per connection, built-in
// shutdown) — a plugin main is just proc.Serve(methods).
package procplugin

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	"sync"
	"sync/atomic"
	"time"
)

// HandshakeHeader is the first line a plugin process writes on stdout
// (identical to loader/proc so both SDKs speak one protocol).
const HandshakeHeader = "GORIDIS-PROC-PLUGIN 1"

// MethodShutdown stops the plugin's serve loop (handled by proc.Serve).
const MethodShutdown = "shutdown"

var (
	ErrHandshake  = errors.New("procplugin: handshake failed")
	ErrProcExited = errors.New("procplugin: plugin process exited")
	ErrClosed     = errors.New("procplugin: client closed")
)

type rpcError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
}

func (e *rpcError) Error() string { return fmt.Sprintf("plugin rpc %d: %s", e.Code, e.Message) }

type callResult struct {
	result json.RawMessage
	err    error
}

// Client is one plugin process connection. Calls are sequential (v1
// contract); every failure — crash, protocol violation, deadline — surfaces
// as a non-nil error.
type Client struct {
	cmd     *exec.Cmd
	exited  chan struct{}
	stdin   io.WriteCloser
	nextID  atomic.Int64
	mu      sync.Mutex
	pending map[int]chan callResult
	closed  atomic.Bool
}

// Start wires pipes onto cmd, starts the process, and performs the
// handshake. Build the command yourself (path, args, env, working dir);
// stderr defaults to inherited so plugin diagnostics land in the
// application log stream.
func Start(ctx context.Context, cmd *exec.Cmd, handshakeWait time.Duration) (*Client, error) {
	if cmd.Stderr == nil {
		cmd.Stderr = os.Stderr
	}
	stdin, err := cmd.StdinPipe()
	if err != nil {
		return nil, err
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		return nil, err
	}
	cmd.Stderr = os.Stderr
	if err := cmd.Start(); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrProcExited, err)
	}
	c := &Client{
		cmd:     cmd,
		exited:  make(chan struct{}),
		stdin:   stdin,
		pending: make(map[int]chan callResult),
	}
	hs := make(chan string, 1)
	go func() {
		defer close(c.exited)
		_ = cmd.Wait()
	}()
	go func() {
		r := bufio.NewReader(stdout)
		line, err := r.ReadString('\n')
		if err == nil {
			hs <- line
		}
		close(hs)
		c.readLoop(r)
	}()
	select {
	case line := <-hs:
		trimmed := trimCR(line)
		if trimmed != HandshakeHeader {
			_ = cmd.Process.Kill()
			return nil, fmt.Errorf("%w: got %q, want %q", ErrHandshake, trimmed, HandshakeHeader)
		}
	case <-c.exited:
		return nil, fmt.Errorf("%w: during handshake", ErrHandshake)
	case <-time.After(handshakeWait):
		_ = cmd.Process.Kill()
		return nil, fmt.Errorf("%w: timeout", ErrHandshake)
	}
	return c, nil
}

func (c *Client) readLoop(r *bufio.Reader) {
	for {
		line, err := r.ReadString('\n')
		if err != nil {
			c.failPending(ErrProcExited)
			return
		}
		var msg struct {
			JSONRPC string          `json:"jsonrpc"`
			ID      *int            `json:"id"`
			Result  json.RawMessage `json:"result"`
			Error   *rpcError       `json:"error"`
		}
		if jsonErr := json.Unmarshal([]byte(trimCR(line)), &msg); jsonErr != nil || msg.ID == nil {
			continue // notification or noise: a v1 connection has no server-initiated requests
		}
		c.mu.Lock()
		ch, ok := c.pending[*msg.ID]
		delete(c.pending, *msg.ID)
		c.mu.Unlock()
		if !ok {
			continue
		}
		switch {
		case msg.Error != nil:
			ch <- callResult{err: msg.Error}
		default:
			ch <- callResult{result: msg.Result}
		}
		close(ch)
	}
}

func (c *Client) failPending(err error) {
	c.mu.Lock()
	defer c.mu.Unlock()
	for id, ch := range c.pending {
		ch <- callResult{err: err}
		close(ch)
		delete(c.pending, id)
	}
}

// Call invokes one remote method; params is marshaled as the request's
// "params", the response "result" is decoded into result (nil discards).
func (c *Client) Call(ctx context.Context, method string, params any, result any) error {
	if c.closed.Load() {
		return ErrClosed
	}
	id := int(c.nextID.Add(1))
	raw, err := json.Marshal(params)
	if err != nil {
		return err
	}
	ch := make(chan callResult, 1)
	c.mu.Lock()
	if _, dup := c.pending[id]; dup {
		c.mu.Unlock()
		return errors.New("procplugin: duplicate call id")
	}
	c.pending[id] = ch
	req, _ := json.Marshal(map[string]any{
		"jsonrpc": "2.0", "id": id, "method": method, "params": json.RawMessage(raw),
	})
	if _, werr := c.stdin.Write(append(req, '\n')); werr != nil {
		delete(c.pending, id)
		c.mu.Unlock()
		return werr
	}
	c.mu.Unlock()

	select {
	case res := <-ch:
		if res.err != nil {
			return res.err
		}
		if result == nil {
			return nil
		}
		return json.Unmarshal(res.result, result)
	case <-ctx.Done():
		c.mu.Lock()
		delete(c.pending, id)
		c.mu.Unlock()
		return ctx.Err()
	case <-c.exited:
		return fmt.Errorf("%w: during %q", ErrProcExited, method)
	}
}

// Close asks the plugin to shut down and reverts the process.
func (c *Client) Close() error {
	if c.closed.Swap(true) {
		return nil
	}
	c.mu.Lock()
	req, _ := json.Marshal(map[string]any{"jsonrpc": "2.0", "id": int(c.nextID.Add(1)), "method": MethodShutdown})
	_, _ = c.stdin.Write(append(req, '\n'))
	c.mu.Unlock()
	select {
	case <-c.exited:
		return nil
	case <-time.After(2 * time.Second):
		return c.cmd.Process.Kill()
	}
}

func trimCR(s string) string {
	for len(s) > 0 && (s[len(s)-1] == '\n' || s[len(s)-1] == '\r') {
		s = s[:len(s)-1]
	}
	return s
}
