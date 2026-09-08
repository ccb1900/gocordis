// Command httpd is the reference demo for binding an EXTERNAL resource (an
// HTTP listener) to the Runtime — transferred from extensions/http (R4 review,
// 2026-09-09: the package was an instance of this pattern, not paper semantics;
// its doc calls itself an example and the convergence matrix marked it "P2
// 示例能力").
//
// The pattern it demonstrates (transferable to any long-running external
// resource — DB pool, queue consumer, gRPC server):
//
//  1. Apply listens and reaches a serving state BEFORE committing through
//     ctx.Effect, so Fiber Active <=> the server is truly serving;
//  2. the activation unwind gracefully shuts the server down before the fiber
//     can finish (the stop function is the effect's inverse);
//  3. authority stays split: Kernel owns Fiber/Activation/Effect/Ownership,
//     this file owns listen/serve/shutdown only — no second lifecycle system,
//     no package-global HTTP state.
//
// The paper link: this is a §5-style implementation-layer capability (how a
// host binds real resources to the calculus), not part of the core semantics.
//
// Run: go run ./cmd/httpd ; then curl localhost:8080
package main

import (
	"context"
	"fmt"
	"io"
	"net"
	stdhttp "net/http"
	"os"
	"os/signal"
	"sync"
	"time"

	"dynamic-runtime/runtime"
)

// ServerConfig describes where one HTTP server binds.
type ServerConfig struct {
	Network         string        // "tcp" (default), "tcp4", "tcp6", "unix"
	Address         string        // e.g. "127.0.0.1:8080"
	ShutdownTimeout time.Duration // graceful-shutdown bound; 0 = 5s default
}

func (c ServerConfig) resolved() ServerConfig {
	if c.Network == "" {
		c.Network = "tcp"
	}
	if c.ShutdownTimeout == 0 {
		c.ShutdownTimeout = 5 * time.Second
	}
	return c
}

func (c ServerConfig) validate() error {
	if c.Address == "" {
		return fmt.Errorf("empty address")
	}
	switch c.Network {
	case "tcp", "tcp4", "tcp6", "unix":
		return nil
	default:
		return fmt.Errorf("unsupported network %q", c.Network)
	}
}

// ServerComponent is an HTTP server as a Runtime Component: one Activation
// owns exactly one listener + server + dedicated mux.
type ServerComponent struct {
	name string
	cfg  ServerConfig

	mu   sync.Mutex
	addr string
}

// NewServer builds one server component (configuration validated eagerly).
func NewServer(name, address string) (*ServerComponent, error) {
	cfg := ServerConfig{Address: address}.resolved()
	if err := cfg.validate(); err != nil {
		return nil, err
	}
	return &ServerComponent{name: name, cfg: cfg}, nil
}

func (c *ServerComponent) Name() string                  { return c.name }
func (c *ServerComponent) Inject() []runtime.Dependency  { return nil }
func (c *ServerComponent) Provide() []runtime.Capability { return nil }
func (c *ServerComponent) Addr() string {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.addr
}

// Apply binds, serves, and registers the graceful-stop inverse — returning
// only after a readiness probe observed the server actually responding.
func (c *ServerComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	cfg := c.cfg.resolved()
	ln, err := net.Listen(cfg.Network, cfg.Address)
	if err != nil {
		return nil, fmt.Errorf("listen: %w", err)
	}

	mux := stdhttp.NewServeMux()
	mux.HandleFunc("/", func(w stdhttp.ResponseWriter, _ *stdhttp.Request) {
		w.WriteHeader(stdhttp.StatusOK)
		_, _ = io.WriteString(w, "ok")
	})
	mux.HandleFunc("/__ready", func(w stdhttp.ResponseWriter, _ *stdhttp.Request) {
		w.WriteHeader(stdhttp.StatusOK)
	})
	srv := &stdhttp.Server{Handler: mux}
	serveCh := make(chan error, 1)
	go func() { serveCh <- srv.Serve(ln) }()

	// Readiness: Fiber Active <=> serving. Any failure closes the listener.
	if cfg.Network != "unix" {
		if err := probeServing("http://" + ln.Addr().String() + "/__ready"); err != nil {
			_ = srv.Close()
			_ = ln.Close()
			return nil, fmt.Errorf("serve: %w", err)
		}
	}
	c.mu.Lock()
	c.addr = ln.Addr().String()
	c.mu.Unlock()
	fmt.Printf("[httpd] %s serving on %s\n", c.name, c.addr)

	// The inverse: graceful shutdown within the timeout, then force-close;
	// idempotent, and it waits for the Serve goroutine (no leak).
	if err := ctx.Effect(func() (func() error, error) {
		return func() error {
			shCtx, cancel := context.WithTimeout(context.Background(), cfg.ShutdownTimeout)
			defer cancel()
			if err := srv.Shutdown(shCtx); err != nil {
				_ = srv.Close()
				return err
			}
			<-serveCh
			fmt.Printf("[httpd] %s stopped\n", c.name)
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

func probeServing(url string) error {
	deadline := time.Now().Add(5 * time.Second)
	var lastErr error
	for time.Now().Before(deadline) {
		resp, err := stdhttp.Get(url) //nolint:gosec // demo-local probe
		if err == nil {
			_, _ = io.Copy(io.Discard, resp.Body)
			_ = resp.Body.Close()
			if resp.StatusCode == stdhttp.StatusOK {
				return nil
			}
			lastErr = fmt.Errorf("unexpected status %d", resp.StatusCode)
		} else {
			lastErr = err
		}
		time.Sleep(5 * time.Millisecond)
	}
	return lastErr
}

func main() {
	rt, err := runtime.New()
	if err != nil {
		fmt.Fprintln(os.Stderr, "runtime:", err)
		os.Exit(1)
	}

	srv, err := NewServer("demo", "127.0.0.1:8080")
	if err != nil {
		fmt.Fprintln(os.Stderr, "config:", err)
		os.Exit(1)
	}
	f, err := rt.Load(srv)
	if err != nil {
		fmt.Fprintln(os.Stderr, "load:", err)
		os.Exit(1)
	}
	if err := f.Ready(context.Background()); err != nil {
		fmt.Fprintln(os.Stderr, "ready:", err)
		os.Exit(1)
	}

	fmt.Println("[httpd] ctrl-c to shut down")
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt)
	<-sig

	// Dispose -> the effect inverse gracefully stops the server BEFORE the
	// fiber reaches Gone.
	_ = f.Dispose()
	gone, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	_ = f.Gone(gone)
	_ = rt.Close(gone)
}
