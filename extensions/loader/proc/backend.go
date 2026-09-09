// Package proc implements the process-external plugin backend for the Loader
// (paper §6.2 Cross-process invocation): a plugin is an independent OS process
// that hosts its own logic and speaks a small JSON-RPC protocol over
// stdin/stdout; a coordinating component in the host links it, treating it as
// a remote provider.
//
// Why a process boundary: Go native code cannot be installed or unloaded at
// runtime (the plugin package cannot unload and locks the toolchain), while a
// process boundary makes install/uninstall = process lifecycle with native
// performance. The paper's caveat applies: a cross-process call incurs latency
// and may fail mid-flight, so the exposed interface MUST be designed against
// an asynchronous-tolerant contract (errors on every call path; no in-order
// delivery assumptions).
//
// Trust boundary: a process boundary is NOT a security sandbox — the plugin
// runs with the host's user privileges. For untrusted code use the WASM
// backend (paper §6.3: sandboxing requires an external mechanism).
//
// Lifecycle (mirrors the WASM backend): each activation materializes exactly
// one fresh plugin process owned by that activation. Apply spawns the process,
// performs the handshake, and commits spawn+shutdown as one reversible effect
// — Fiber Active <=> the plugin process is up and answered the handshake; the
// activation unwind stops the process before the fiber can finish. The
// Backend never decides when a plugin process lives or dies, never touches the
// Module Registry, and never starts or disposes a Fiber.
package proc

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

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// BackendType is the loader backend name ("proc").
const BackendType = loader.BackendProc

// Protocol constants (full wire format in README.md).
const (
	// HandshakeHeader is the first line a plugin process writes on stdout.
	HandshakeHeader = "GORIDIS-PROC-PLUGIN 1"
	// MethodShutdown is the host->plugin graceful-stop request. The plugin
	// should exit after receiving it; the host force-kills after its
	// shutdown timeout regardless.
	MethodShutdown = "shutdown"
	// DefaultHandshakeWait bounds the handshake wait.
	DefaultHandshakeWait = 5 * time.Second
	// DefaultShutdownTimeout bounds the graceful-stop wait.
	DefaultShutdownTimeout = 2 * time.Second
)

// Errors. Compare with errors.Is.
var (
	ErrProcBackendClosed    = errors.New("proc backend closed")
	ErrProcBackendPanic     = errors.New("proc backend panic")
	ErrInvalidProcArtifact  = errors.New("invalid proc artifact")
	ErrProcStart            = errors.New("proc plugin start failed")
	ErrProcHandshake        = errors.New("proc plugin handshake failed")
	ErrProcFactory          = errors.New("proc factory failed")
	ErrProcContractRequired = errors.New("proc backend requires a contract (key + bind)")
	ErrPluginUnavailable    = errors.New("proc plugin unavailable")
	ErrPluginProtocol       = errors.New("proc plugin protocol violation")
)

// Caller is one host-side RPC connection to a plugin process. Calls are
// sequential (one in-flight request per connection, v1); every failure mode —
// crash, protocol violation, cancellation — surfaces as a non-nil error so
// callers honor the paper's asynchronous-tolerant contract.
type Caller interface {
	// Call invokes the remote method with JSON params and decodes the JSON
	// result into result (nil discards it).
	Call(ctx context.Context, method string, params any, result any) error
	// Notify sends a one-way notification; errors cover local write failures.
	Notify(method string, params any) error
}

// Option configures a Backend.
type Option func(*backendOptions)

type backendOptions struct {
	args            []string
	env             []string
	stderrFn        func() io.Writer
	handshakeWait   time.Duration
	shutdownTimeout time.Duration
}

// WithArgs appends arguments passed to every spawned plugin process.
func WithArgs(args ...string) Option {
	return func(o *backendOptions) { o.args = append(o.args, args...) }
}

// WithEnv appends environment variables (KEY=VALUE) for spawned processes.
func WithEnv(kv ...string) Option {
	return func(o *backendOptions) { o.env = append(o.env, kv...) }
}

// WithStderr wires plugin stderr to the writer returned per spawn
// (diagnostics; default discarded).
func WithStderr(w func() io.Writer) Option {
	return func(o *backendOptions) { o.stderrFn = w }
}

// WithHandshakeWait bounds the handshake wait (default 5s).
func WithHandshakeWait(d time.Duration) Option {
	return func(o *backendOptions) { o.handshakeWait = d }
}

// WithShutdownTimeout bounds the graceful-stop wait (default 2s).
func WithShutdownTimeout(d time.Duration) Option {
	return func(o *backendOptions) { o.shutdownTimeout = d }
}

// Backend is the process-external plugin backend. It implements loader.Backend:
// turning a "proc" Artifact (Source = executable path) into a loader.Module
// whose Factory creates Kernel Components that materialize one fresh plugin
// process per activation.
//
// The backend is bound to one CONTRACT: key is the typed capability the
// plugin provides and bind turns an RPC Caller into that capability's
// interface. The host owns the contract (interface + key live in the
// application's contracts package); the plugin process implements the same
// method names over JSON-RPC.
type Backend struct {
	mu       sync.Mutex
	closed   bool
	keyCap   runtime.Capability
	provide  func(ctx *runtime.Context, c Caller) error
	args     []string
	env      []string
	stderrFn func() io.Writer
	hsWait   time.Duration
	stopWait time.Duration
	nextPID  atomic.Uint64
}

var _ loader.Backend = (*Backend)(nil)

// NewBackend creates a running proc Backend bound to one contract: every
// module it loads provides the capability key with an implementation produced
// by bind(client). bind is called once per activation after the handshake.
func NewBackend[T any](key runtime.Key[T], bind func(Caller) T, opts ...Option) *Backend {
	o := backendOptions{handshakeWait: DefaultHandshakeWait, shutdownTimeout: DefaultShutdownTimeout}
	for _, opt := range opts {
		if opt != nil {
			opt(&o)
		}
	}
	return &Backend{
		keyCap:   key.Capability(),
		provide:  func(ctx *runtime.Context, c Caller) error { return runtime.Provide(ctx, key, bind(c)) },
		args:     o.args,
		env:      o.env,
		stderrFn: o.stderrFn,
		hsWait:   o.handshakeWait,
		stopWait: o.shutdownTimeout,
	}
}

// Load validates the artifact (executable present) and materializes a
// loader.Module (Type "proc"). Atomicity: validation completes before the
// Module is returned; the Loader commits it. Backend.Load never writes the
// Registry.
func (b *Backend) Load(ctx context.Context, artifact loader.Artifact) (m loader.Module, err error) {
	defer func() {
		if r := recover(); r != nil {
			m = loader.Module{}
			err = fmt.Errorf("%w: %v", ErrProcBackendPanic, r)
		}
	}()
	if ctx == nil {
		ctx = context.Background()
	}
	if err := b.checkOpen(); err != nil {
		return loader.Module{}, err
	}
	if artifact.BackendType != BackendType {
		return loader.Module{}, fmt.Errorf("%w: proc backend only accepts BackendType == %q, got %q", loader.ErrInvalidArtifact, BackendType, artifact.BackendType)
	}
	if artifact.Source == "" {
		return loader.Module{}, fmt.Errorf("%w: empty executable path", loader.ErrInvalidArtifact)
	}
	if err := ctx.Err(); err != nil {
		return loader.Module{}, err
	}
	if err := validateExecutable(artifact.Source); err != nil {
		return loader.Module{}, fmt.Errorf("%w: %s: %v", loader.ErrInvalidArtifact, artifact.Source, err)
	}
	return loader.Module{
		ID:      artifact.ID,
		Type:    string(BackendType),
		Version: artifact.Version,
		Factory: &factory{backend: b, exe: artifact.Source},
	}, nil
}

// Close rejects new Load work. Already-running plugin processes belong to
// their activations (Loader usage is the guard) and are stopped by the Kernel
// unwind — same contract as the WASM backend.
func (b *Backend) Close() error {
	b.mu.Lock()
	b.closed = true
	b.mu.Unlock()
	return nil
}

func (b *Backend) checkOpen() error {
	b.mu.Lock()
	defer b.mu.Unlock()
	if b.closed {
		return ErrProcBackendClosed
	}
	return nil
}

// validateExecutable checks that path exists, is a regular file, and carries
// an execute bit.
func validateExecutable(path string) error {
	st, err := os.Stat(path)
	if err != nil {
		return err
	}
	if st.IsDir() {
		return errors.New("not a regular file")
	}
	if st.Mode()&0o111 == 0 {
		return errors.New("not executable")
	}
	return nil
}

// factory is the config.Factory produced by the proc Backend for one
// validated executable. It is the only abstraction Config/Runtime sees from
// the process world. Create builds a Component; it never creates a Fiber and
// never touches Provider/Registry/Config state.
type factory struct {
	backend *Backend
	exe     string
}

func (f *factory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	if cc.ID == "" {
		return nil, fmt.Errorf("%w: empty component id", ErrProcFactory)
	}
	if f.backend.provide == nil {
		return nil, fmt.Errorf("%w: backend has no contract bound", ErrProcContractRequired)
	}
	return &pluginComponent{id: cc.ID, backend: f.backend, exe: f.exe}, nil
}

// pluginComponent is a Runtime Component whose activation materializes exactly
// one fresh plugin process owned by that activation.
//
// Lifecycle wiring (Kernel is the only authority):
//
//	Apply
//	  ├── ctx.Effect(install = spawn + handshake,
//	  │               inverse = graceful stop + kill + wait)
//	  └── runtime.Provide(key, bind(client))   // the remote provider link
type pluginComponent struct {
	id      string
	backend *Backend
	exe     string
}

func (c *pluginComponent) Name() string                 { return "proc:" + c.id }
func (c *pluginComponent) Inject() []runtime.Dependency { return nil }
func (c *pluginComponent) Provide() []runtime.Capability {
	return []runtime.Capability{c.backend.keyCap}
}

func (c *pluginComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	var client *pluginClient
	if err := ctx.Effect(func() (func() error, error) {
		p, err := c.backend.spawn(c.exe, c.id, ctx.Context())
		if err != nil {
			return nil, err
		}
		if err := p.handshake(c.backend.hsWait); err != nil {
			_ = p.stop(c.backend.stopWait) // partial cleanup: no orphan process
			return nil, err
		}
		client = p
		return func() error { return p.stop(c.backend.stopWait) }, nil
	}); err != nil {
		return nil, err
	}

	// The remote provider link (paper §6.2): provide the contract
	// implementation routed over the process channel.
	if err := c.backend.provide(ctx, client); err != nil {
		return nil, err
	}
	return nil, nil
}

// ---------------------------------------------------------------------------
// process instance + stdio JSON-RPC client
// ---------------------------------------------------------------------------

type pluginClient struct {
	id       uint64
	cmd      *exec.Cmd
	stdin    io.WriteCloser
	exited   chan struct{} // closed when the process has been waited on
	hsLine   chan string   // handshake header line (buffered 1)
	stopOnce sync.Once

	mu      sync.Mutex // serializes stdin writes and the pending map
	pending map[int]chan callResult
	nextID  int
	dead    error // sticky failure (process exit / protocol death); mu-guarded
}

// callResult is one completed RPC: either a decoded response or a transport
// failure (err carries the WRAPPED cause so errors.Is chains survive).
type callResult struct {
	resp rpcResponse
	err  error
}

type rpcResponse struct {
	JSONRPC string          `json:"jsonrpc"`
	ID      *int            `json:"id"`
	Result  json.RawMessage `json:"result"`
	Error   *rpcError       `json:"error"`
}

type rpcError struct {
	Code    int    `json:"code"`
	Message string `json:"message"`
}

func (e *rpcError) err() error { return fmt.Errorf("plugin rpc %d: %s", e.Code, e.Message) }

func (b *Backend) spawn(exe, id string, actCtx context.Context) (*pluginClient, error) {
	// The activation context is the backstop: cancellation kills the process
	// even if something wedges (exec.CommandContext + Cancel).
	cmd := exec.CommandContext(actCtx, exe, b.args...)
	cmd.Env = append(os.Environ(), b.env...)
	var stderr io.Writer = io.Discard
	if b.stderrFn != nil {
		stderr = b.stderrFn()
	}
	cmd.Stderr = stderr
	stdin, err := cmd.StdinPipe()
	if err != nil {
		return nil, fmt.Errorf("%w: stdin: %v", ErrProcStart, err)
	}
	stdout, err := cmd.StdoutPipe()
	if err != nil {
		return nil, fmt.Errorf("%w: stdout: %v", ErrProcStart, err)
	}
	cmd.Cancel = func() error {
		// Force-kill on cancellation: a wedged plugin must not outlive the
		// activation (graceful stop has its own bounded path in stop()).
		return cmd.Process.Kill()
	}

	if err := cmd.Start(); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrProcStart, err)
	}
	p := &pluginClient{
		id:      b.nextPID.Add(1),
		cmd:     cmd,
		stdin:   stdin,
		exited:  make(chan struct{}),
		hsLine:  make(chan string, 1),
		pending: make(map[int]chan callResult),
	}
	go func() {
		_ = cmd.Wait() // exit error surfaced via dead/pending failures
		close(p.exited)
	}()
	go p.readLoop(bufio.NewReader(stdout))
	return p, nil
}

func (p *pluginClient) handshake(wait time.Duration) error {
	line, err := p.readLine(wait)
	if err != nil {
		return fmt.Errorf("%w: %v", ErrProcHandshake, err)
	}
	if line != HandshakeHeader {
		return fmt.Errorf("%w: got %q, want %q", ErrProcHandshake, line, HandshakeHeader)
	}
	return nil
}

// readLine reads one protocol line from the reader goroutine's channel with a
// deadline; the goroutine pushes lines until the handshake is consumed, after
// which it switches to JSON dispatch.
func (p *pluginClient) readLine(wait time.Duration) (string, error) {
	// The reader goroutine is the sole owner of stdout; the handshake is the
	// first line it reads, delivered through hsLine.
	select {
	case line := <-p.hsLine:
		return line, nil
	case <-p.exited:
		return "", fmt.Errorf("%w: process exited during handshake", ErrProcHandshake)
	case <-time.After(wait):
		return "", errors.New("handshake timeout")
	}
}

type rpcMsg struct {
	JSONRPC string          `json:"jsonrpc"`
	ID      *int            `json:"id"`
	Method  string          `json:"method"`
	Params  json.RawMessage `json:"params"`
	Result  json.RawMessage `json:"result"`
	Error   *rpcError       `json:"error"`
}

func debugTrace(what, method string) {
	if os.Getenv("GORIDIS_PROC_TRACE") != "" {
		fmt.Fprintln(os.Stderr, "[proc-trace]", what, method)
	}
}

// readLoop is the sole reader of plugin stdout: handshake first, then
// line-delimited JSON-RPC dispatch.
func (p *pluginClient) readLoop(r *bufio.Reader) {
	debugTrace("readLoop start", "")
	header, err := p.readLineSync(r)
	if err != nil {
		p.markDead(fmt.Errorf("%w: %v", ErrProcHandshake, err))
		return
	}
	p.hsLine <- header
	for {
		line, err := p.readLineSync(r)
		if err != nil {
			p.markDead(fmt.Errorf("%w: %v", ErrPluginUnavailable, err))
			return
		}
		var msg rpcMsg
		if err := json.Unmarshal([]byte(line), &msg); err != nil {
			p.markDead(fmt.Errorf("%w: %v", ErrPluginProtocol, err))
			return
		}
		if msg.Method != "" {
			// Plugin->host requests are not part of protocol v1: answer with
			// a method-not-found error so the plugin never hangs.
			if msg.ID != nil {
				_ = p.write(map[string]any{
					"jsonrpc": "2.0", "id": *msg.ID,
					"error": map[string]any{"code": -32601, "message": "host does not serve methods"},
				})
			}
			continue
		}
		if msg.ID == nil {
			continue // stray response without id: ignore
		}
		p.mu.Lock()
		ch := p.pending[*msg.ID]
		delete(p.pending, *msg.ID)
		p.mu.Unlock()
		if ch != nil {
			ch <- callResult{resp: rpcResponse{JSONRPC: msg.JSONRPC, ID: msg.ID, Result: msg.Result, Error: msg.Error}}
			close(ch)
		}
	}
}

func (p *pluginClient) markDead(err error) {
	p.mu.Lock()
	if p.dead == nil {
		p.dead = err
	}
	for id, ch := range p.pending {
		ch <- callResult{err: fmt.Errorf("%w: %v", ErrPluginUnavailable, err)}
		close(ch)
		delete(p.pending, id)
	}
	p.mu.Unlock()
}

func (p *pluginClient) readLineSync(r *bufio.Reader) (string, error) {
	line, err := r.ReadString('\n')
	if err != nil {
		return "", err
	}
	// trim trailing \n (and \r for windows-authored plugins)
	for len(line) > 0 && (line[len(line)-1] == '\n' || line[len(line)-1] == '\r') {
		line = line[:len(line)-1]
	}
	return line, nil
}

func (p *pluginClient) write(v any) error {
	data, err := json.Marshal(v)
	if err != nil {
		return err
	}
	p.mu.Lock()
	defer p.mu.Unlock()
	if p.dead != nil {
		return p.dead
	}
	_, err = p.stdin.Write(append(data, '\n'))
	return err
}

// Call implements Caller.
func (p *pluginClient) Call(ctx context.Context, method string, params any, result any) error {
	debugTrace("call enter", method)
	p.mu.Lock()
	debugTrace("call registered", method)
	if p.dead != nil {
		err := p.dead
		p.mu.Unlock()
		return err
	}
	p.nextID++
	id := p.nextID
	ch := make(chan callResult, 1)
	p.pending[id] = ch
	p.mu.Unlock()

	if err := p.write(map[string]any{
		"jsonrpc": "2.0", "id": id, "method": method, "params": params,
	}); err != nil {
		p.mu.Lock()
		delete(p.pending, id)
		p.mu.Unlock()
		return fmt.Errorf("%w: write: %v", ErrPluginUnavailable, err)
	}

	debugTrace("call waiting", method)
	select {
	case res := <-ch:
		debugTrace("call got response", method)
		if res.err != nil {
			return res.err
		}
		if result != nil && len(res.resp.Result) > 0 {
			if err := json.Unmarshal(res.resp.Result, result); err != nil {
				return fmt.Errorf("%w: result decode: %v", ErrPluginProtocol, err)
			}
		}
		return nil
	case <-ctx.Done():
		p.mu.Lock()
		delete(p.pending, id)
		p.mu.Unlock()
		return ctx.Err()
	}
}

// Notify implements Caller (fire-and-forget JSON-RPC notification).
func (p *pluginClient) Notify(method string, params any) error {
	return p.write(map[string]any{
		"jsonrpc": "2.0", "method": method, "params": params,
	})
}

// stop gracefully stops the plugin exactly once: shutdown request (best
// effort), bounded wait, force kill, wait for exit. Safe to call multiple
// times.
func (p *pluginClient) stop(timeout time.Duration) error {
	p.stopOnce.Do(func() {
		_ = p.Notify(MethodShutdown, struct{}{})
		select {
		case <-p.exited:
		case <-time.After(timeout):
			_ = p.cmd.Process.Kill()
			<-p.exited
		}
	})
	return nil
}
