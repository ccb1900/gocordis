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
	"context"
	"errors"
	"fmt"
	"io"
	"os"
	"os/exec"
	stdruntime "runtime"
	"sync"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/proc/transport"
	"dynamic-runtime/runtime"
)

// BackendType is the loader backend name ("proc").
const BackendType = loader.BackendProc

// Protocol constants (full wire format in README.md).
const (
	// Protocol constants live in the shared transport; re-exported here so
	// existing applications keep their proc.* API.
	HandshakeHeader        = transport.HandshakeHeader
	MethodShutdown         = transport.MethodShutdown
	DefaultHandshakeWait   = transport.DefaultHandshakeWait
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
// callers honor the paper's asynchronous-tolerant contract. A caller-supplied
// ctx without a deadline is wrapped with callTimeout when set (see
// WithCallTimeout): the paper's warning that cross-process calls may fail
// mid-flight implies calls must be bounded.
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
	// Windows 文件系统没有执行位（os.Stat 恒不含 0111），任何合法的
	// .exe 都会被下面的检查拒绝——proc 后端在 Windows 完全不可用。
	// Windows 上以 regular file 为准，执行位检查仅适用于 unix 系。
	if stdruntime.GOOS != "windows" && st.Mode()&0o111 == 0 {
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
		// spawn performs the handshake before returning: Active <=> the
		// process answered it. A failed handshake kills the half-started
		// process inside transport.Start, so no orphan survives.
		p, err := c.backend.spawn(c.exe, c.id, ctx.Context())
		if err != nil {
			return nil, err
		}
		client = p
		return func() error { return p.Stop(c.backend.stopWait) }, nil
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
// process instance adapter: shared transport -> Go Capability
// ---------------------------------------------------------------------------

// pluginClient is the loader-facing view of one shared transport connection.
// The low-level protocol (spawn, handshake, stdio, JSON-RPC, pending map,
// exit detection, graceful stop, write serialization) lives in transport;
// this adapter only translates neutral transport failures into the proc
// backend's public error vocabulary and satisfies the Caller interface.
type pluginClient struct{ *transport.Conn }

// mapCallErr keeps the proc backend's contract: callers can errors.Is against
// ErrPluginUnavailable / ErrPluginProtocol without importing the transport.
func mapCallErr(err error) error {
	if err == nil {
		return nil
	}
	switch {
	case errors.Is(err, transport.ErrProtocol):
		return fmt.Errorf("%w: %v", ErrPluginProtocol, err)
	case errors.Is(err, transport.ErrExited), errors.Is(err, transport.ErrClosed):
		return fmt.Errorf("%w: %v", ErrPluginUnavailable, err)
	}
	// Typed RPC failures and caller cancellation keep their own identity;
	// everything else came from the process pipe (e.g. a raw stdin write
	// error) and must still satisfy errors.Is(err, ErrPluginUnavailable) as
	// it did before the transport extraction.
	var rpcErr *transport.RPCError
	if errors.As(err, &rpcErr) || errors.Is(err, context.Canceled) || errors.Is(err, context.DeadlineExceeded) {
		return err
	}
	return fmt.Errorf("%w: %v", ErrPluginUnavailable, err)
}

func (b *Backend) spawn(exe, id string, actCtx context.Context) (*pluginClient, error) {
	// The activation context is the backstop: cancellation triggers Conn's
	// bounded graceful shutdown; exec's WaitDelay remains the kill backstop.
	cmd := exec.CommandContext(actCtx, exe, b.args...)
	cmd.Env = append(os.Environ(), b.env...)
	var stderr io.Writer = io.Discard
	if b.stderrFn != nil {
		stderr = b.stderrFn()
	}
	conn, err := transport.Start(actCtx, cmd,
		transport.WithStderr(stderr),
		transport.WithHandshakeWait(b.hsWait),
		transport.WithShutdownWait(b.stopWait),
	)
	if err != nil {
		if errors.Is(err, transport.ErrHandshake) {
			return nil, fmt.Errorf("%w: %v", ErrProcHandshake, err)
		}
		return nil, fmt.Errorf("%w: %v", ErrProcStart, err)
	}
	return &pluginClient{Conn: conn}, nil
}

// Call implements Caller.
func (p *pluginClient) Call(ctx context.Context, method string, params any, result any) error {
	return mapCallErr(p.Conn.Call(ctx, method, params, result))
}

// Notify implements Caller.
func (p *pluginClient) Notify(method string, params any) error {
	return mapCallErr(p.Conn.Notify(method, params))
}
