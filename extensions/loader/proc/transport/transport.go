// Package transport is the SHARED low-level host side of the process plugin
// protocol (paper §6.2, cross-process invocation):
//
//   - OS process start / stdio wiring / exit detection / graceful shutdown;
//   - the handshake line "GORIDIS-PROC-PLUGIN 1";
//   - line-delimited JSON-RPC 2.0 with a pending-request map;
//   - ONE write lock for every stdin write (RPC calls, notifications and the
//     shutdown request) so protocol frames can never interleave.
//
// It deliberately knows nothing about what a capability or a console hub is.
// The two business adapters stay distinct and own their own vocabulary:
//
//   - extensions/loader/proc maps a Conn to a Go Capability (typed Key[T]);
//   - extensions/console/procplugin maps a Conn to hub Query/Command handlers.
//
// A process boundary is not a security sandbox; the plugin runs with the
// host's privileges. The caller owns all lifecycle authority: Conn never
// starts or disposes a Fiber and never decides when a plugin should live.
package transport

import (
	"bufio"
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"os/exec"
	"sync"
	"sync/atomic"
	"time"
)

// Protocol constants (full wire format in extensions/loader/proc/README.md).
const (
	// HandshakeHeader is the first line a plugin process writes on stdout.
	HandshakeHeader = "GORIDIS-PROC-PLUGIN 1"
	// MethodShutdown is the host->plugin graceful-stop request.
	MethodShutdown = "shutdown"

	DefaultHandshakeWait = 5 * time.Second
	DefaultShutdownWait  = 1500 * time.Millisecond
	DefaultWaitDelay     = 5 * time.Second
)

// Neutral transport failures. Business adapters wrap these in their own
// sentinel errors (errors.Is chains survive) instead of duplicating the wire
// protocol.
var (
	ErrStart     = errors.New("proc transport: process start failed")
	ErrHandshake = errors.New("proc transport: handshake failed")
	ErrExited    = errors.New("proc transport: process exited")
	ErrProtocol  = errors.New("proc transport: protocol violation")
	ErrClosed    = errors.New("proc transport: connection closed")
)

// RPCError is the JSON-RPC error object. Both adapters expose it to callers;
// having one type keeps errors.As(err, **RPCError) stable across them.
type RPCError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
}

func (e *RPCError) Error() string { return fmt.Sprintf("plugin rpc %d: %s", e.Code, e.Message) }

// Option configures Start.
type Option func(*options)

type options struct {
	handshakeWait time.Duration
	shutdownWait  time.Duration
	waitDelay     time.Duration
	stderr        io.Writer
}

// WithHandshakeWait bounds the handshake wait (default 5s).
func WithHandshakeWait(d time.Duration) Option {
	return func(o *options) { o.handshakeWait = d }
}

// WithShutdownWait bounds the graceful shutdown request (default 1.5s).
func WithShutdownWait(d time.Duration) Option {
	return func(o *options) { o.shutdownWait = d }
}

// WithWaitDelay bounds cmd.Wait when a plugin spawns children holding the
// inherited pipes (default 5s). A zero cmd.WaitDelay set by the caller is
// replaced; an explicit non-zero value wins.
func WithWaitDelay(d time.Duration) Option {
	return func(o *options) { o.waitDelay = d }
}

// WithStderr wires plugin stderr (diagnostics; ignored when cmd.Stderr is set).
func WithStderr(w io.Writer) Option {
	return func(o *options) { o.stderr = w }
}

// Response is one decoded JSON-RPC response.
type Response struct {
	JSONRPC string          `json:"jsonrpc"`
	ID      *int            `json:"id"`
	Result  json.RawMessage `json:"result"`
	Error   *RPCError       `json:"error"`
}

type callResult struct {
	resp Response
	err  error
}

// Conn is one host-side process connection. It is safe for concurrent use:
// Calls are sequential on the wire (v1 contract) but pending results, exit
// detection and teardown may race from any goroutine.
type Conn struct {
	cmd    *exec.Cmd
	stdin  io.WriteCloser
	exited chan struct{} // closed when cmd.Wait returns

	stopOnce sync.Once

	// writeMu serializes EVERY stdin write. It is the only lock held during a
	// blocking pipe write. In particular the state lock (mu) is released
	// before writing, so a wedged-but-alive plugin cannot wedge failPending,
	// markDead or Close behind a full pipe.
	writeMu sync.Mutex

	mu      sync.Mutex // guards pending, nextID, dead
	pending map[int]chan callResult
	nextID  int
	dead    error // sticky transport failure

	closed       atomic.Bool
	shutdownWait time.Duration
}

// newConn is shared by Start and by package tests (which inject a writer to
// prove the write-lock invariant without spawning a process).
func newConn(cmd *exec.Cmd, stdin io.WriteCloser, exited chan struct{}, o options) *Conn {
	shutdownWait := o.shutdownWait
	if shutdownWait <= 0 {
		shutdownWait = DefaultShutdownWait
	}
	return &Conn{
		cmd:          cmd,
		stdin:        stdin,
		exited:       exited,
		pending:      make(map[int]chan callResult),
		shutdownWait: shutdownWait,
	}
}

// Start spawns cmd, wires stdin/stdout, performs the handshake and returns a
// live Conn. Any handshake failure kills the half-started process so no orphan
// survives. The caller supplies the command (path/args/env/dir) — the adapter,
// not the transport, decides which executable implements which contract.
func Start(ctx context.Context, cmd *exec.Cmd, opts ...Option) (*Conn, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	o := options{
		handshakeWait: DefaultHandshakeWait,
		shutdownWait:  DefaultShutdownWait,
		waitDelay:     DefaultWaitDelay,
	}
	for _, opt := range opts {
		if opt != nil {
			opt(&o)
		}
	}
	if o.handshakeWait <= 0 {
		o.handshakeWait = DefaultHandshakeWait
	}
	if cmd.Stderr == nil && o.stderr != nil {
		cmd.Stderr = o.stderr
	}
	stdin, err := cmd.StdinPipe()
	if err != nil {
		return nil, fmt.Errorf("%w: stdin: %v", ErrStart, err)
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		// Without Start(), exec's failure cleanup never runs — close the
		// stdin write-end we already own or the fd leaks.
		_ = stdin.Close()
		return nil, fmt.Errorf("%w: stdout: %v", ErrStart, err)
	}
	if cmd.WaitDelay == 0 {
		cmd.WaitDelay = o.waitDelay
	}
	exited := make(chan struct{})
	c := newConn(cmd, stdin, exited, o)

	// Graceful-then-kill on context cancellation: a bare Kill would fire
	// before the unwind's Stop could speak, making the shutdown RPC dead code
	// on every runtime-driven unload. Only CommandContext installs a watcher
	// that calls Cancel (and exec rejects a non-nil Cancel without one), so
	// plain exec.Command callers keep their default behavior; Stop still
	// speaks the shutdown request for them. WaitDelay is the kill backstop.
	if cmd.Cancel != nil {
		cmd.Cancel = func() error {
			c.RequestShutdown()
			return nil
		}
	}
	if err := cmd.Start(); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrStart, err)
	}
	go func() {
		_ = cmd.Wait()
		close(exited)
	}()

	hs := make(chan string, 1)
	go func() {
		r := bufio.NewReader(stdout)
		line, err := readLine(r)
		if err != nil {
			c.markDead(fmt.Errorf("%w: %v", ErrHandshake, err))
			close(hs)
			return
		}
		hs <- trimCR(line)
		c.readLoop(r)
	}()

	select {
	case line, ok := <-hs:
		if !ok {
			_ = c.forceKill()
			return nil, fmt.Errorf("%w: process exited during handshake", ErrHandshake)
		}
		if line != HandshakeHeader {
			_ = c.forceKill()
			return nil, fmt.Errorf("%w: got %q, want %q", ErrHandshake, line, HandshakeHeader)
		}
	case <-exited:
		return nil, fmt.Errorf("%w: process exited during handshake", ErrHandshake)
	case <-ctx.Done():
		_ = c.forceKill()
		return nil, fmt.Errorf("%w: canceled: %v", ErrHandshake, ctx.Err())
	case <-time.After(o.handshakeWait):
		_ = c.forceKill()
		return nil, fmt.Errorf("%w: timeout", ErrHandshake)
	}
	return c, nil
}

func (c *Conn) forceKill() error {
	c.closed.Store(true)
	if c.cmd != nil && c.cmd.Process != nil {
		return c.cmd.Process.Kill()
	}
	return nil
}

func readLine(r *bufio.Reader) (string, error) { return r.ReadString('\n') }

func trimCR(s string) string {
	for len(s) > 0 && (s[len(s)-1] == '\n' || s[len(s)-1] == '\r') {
		s = s[:len(s)-1]
	}
	return s
}

// readLoop is the sole reader of plugin stdout: JSON-RPC response dispatch.
func (c *Conn) readLoop(r *bufio.Reader) {
	for {
		line, err := readLine(r)
		if err != nil {
			c.markDead(fmt.Errorf("%w: %v", ErrExited, err))
			return
		}
		var msg struct {
			JSONRPC string          `json:"jsonrpc"`
			ID      *int            `json:"id"`
			Method  string          `json:"method"`
			Result  json.RawMessage `json:"result"`
			Error   *RPCError       `json:"error"`
		}
		if err := json.Unmarshal([]byte(trimCR(line)), &msg); err != nil {
			c.markDead(fmt.Errorf("%w: %v", ErrProtocol, err))
			return
		}
		if msg.Method != "" {
			// Plugin->host calls are not part of v1: answer method-not-found
			// so a misbehaving plugin never hangs. This write takes the same
			// writeMu as every other frame.
			if msg.ID != nil {
				_ = c.write(map[string]any{
					"jsonrpc": "2.0", "id": *msg.ID,
					"error": map[string]any{"code": -32601, "message": "host does not serve methods"},
				})
			}
			continue
		}
		if msg.ID == nil {
			continue // stray response without id
		}
		c.mu.Lock()
		ch := c.pending[*msg.ID]
		delete(c.pending, *msg.ID)
		c.mu.Unlock()
		if ch != nil {
			ch <- callResult{resp: Response{JSONRPC: msg.JSONRPC, ID: msg.ID, Result: msg.Result, Error: msg.Error}}
			close(ch)
		}
	}
}

// markDead latches a sticky transport failure and fails every in-flight call.
func (c *Conn) markDead(err error) {
	c.mu.Lock()
	if c.dead == nil {
		c.dead = err
	}
	for id, ch := range c.pending {
		ch <- callResult{err: err}
		close(ch)
		delete(c.pending, id)
	}
	c.mu.Unlock()
}

// write marshals and writes one protocol frame. It is the ONLY path to stdin.
func (c *Conn) write(v any) error {
	data, err := json.Marshal(v)
	if err != nil {
		return err
	}
	c.writeMu.Lock()
	defer c.writeMu.Unlock()
	c.mu.Lock()
	dead, isClosed := c.dead, c.closed.Load()
	c.mu.Unlock()
	if dead != nil {
		return dead
	}
	if isClosed {
		return ErrClosed
	}
	_, err = c.stdin.Write(append(data, '\n'))
	return err
}

// Call invokes one remote method and decodes the response into result.
func (c *Conn) Call(ctx context.Context, method string, params any, result any) error {
	if ctx == nil {
		ctx = context.Background()
	}
	c.mu.Lock()
	if c.dead != nil {
		err := c.dead
		c.mu.Unlock()
		return err
	}
	c.nextID++
	id := c.nextID
	ch := make(chan callResult, 1)
	c.pending[id] = ch
	c.mu.Unlock()

	if err := c.write(map[string]any{
		"jsonrpc": "2.0", "id": id, "method": method, "params": params,
	}); err != nil {
		c.dropPending(id)
		return err
	}

	select {
	case res := <-ch:
		if res.err != nil {
			return res.err
		}
		if res.resp.Error != nil {
			return res.resp.Error
		}
		if result != nil && len(res.resp.Result) > 0 {
			if err := json.Unmarshal(res.resp.Result, result); err != nil {
				return fmt.Errorf("%w: result decode: %v", ErrProtocol, err)
			}
		}
		return nil
	case <-ctx.Done():
		c.dropPending(id)
		return ctx.Err()
	case <-c.exited:
		c.dropPending(id)
		return ErrExited
	}
}

func (c *Conn) dropPending(id int) {
	c.mu.Lock()
	delete(c.pending, id)
	c.mu.Unlock()
}

// Notify sends a one-way notification.
func (c *Conn) Notify(method string, params any) error {
	return c.write(map[string]any{"jsonrpc": "2.0", "method": method, "params": params})
}

// RequestShutdown issues the graceful-stop RPC. It runs on exec's context
// watcher and must return quickly; exec's WaitDelay applies the kill backstop.
// The request goes through the same writeMu as normal RPC — never the state
// lock — so it cannot interleave with an in-flight frame.
func (c *Conn) RequestShutdown() {
	done := make(chan struct{})
	go func() {
		_ = c.write(map[string]any{"jsonrpc": "2.0", "id": c.nextShutdownID(), "method": MethodShutdown, "params": struct{}{}})
		close(done)
	}()
	select {
	case <-done:
	case <-time.After(c.shutdownWait):
	}
}

func (c *Conn) nextShutdownID() int {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.nextID++
	return c.nextID
}

// Stop gracefully stops the process exactly once: bounded shutdown request,
// bounded wait for exit, force kill, then wait for the reap. Gone implies the
// process has actually exited.
func (c *Conn) Stop(timeout time.Duration) error {
	c.stopOnce.Do(func() {
		// Speak the graceful request BEFORE marking closed: write() refuses
		// frames once closed is set, and a runtime unload may cancel the
		// activation context only after the effect inverse has begun.
		c.RequestShutdown()
		c.closed.Store(true)
		select {
		case <-c.exited:
		case <-time.After(timeout):
			_ = c.forceKill()
			<-c.exited
		}
	})
	return nil
}

// Exited is closed when the process has been waited on.
func (c *Conn) Exited() <-chan struct{} { return c.exited }

// Alive reports whether the process is still running. ProcessState is written
// by cmd.Wait's goroutine with no shared lock, so liveness is read from exited.
func (c *Conn) Alive() bool {
	select {
	case <-c.exited:
		return false
	default:
		return true
	}
}

// Closed reports whether teardown has begun.
func (c *Conn) Closed() bool { return c.closed.Load() }

// ShutdownWait returns the configured bound for the graceful-shutdown write.
// Adapters can assert that their configured timeout reached the transport.
func (c *Conn) ShutdownWait() time.Duration { return c.shutdownWait }
