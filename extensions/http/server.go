package http

import (
	"context"
	"fmt"
	"io"
	"net"
	stdhttp "net/http"
	"sync"
	"time"
)

// server is the per-activation HTTP resource: a dedicated listener + server
// with its own mux (never http.DefaultServeMux) plus its graceful stop
// function. It is instance-scoped; nothing here is package-global.
type server struct {
	srv     *stdhttp.Server
	ln      net.Listener
	serveCh chan error

	stopOnce sync.Once
	stopErr  error
}

// startServer binds addr and begins serving on a dedicated mux. It returns
// only once a readiness probe observed the server actually responding, so a
// successful return means the resource is serving (Fiber Active <=> serving).
// Any failure closes everything already created (no orphan listener).
//
// handler is the server's implementation data (§11); nil means the built-in
// "ok" handler. The readiness probe uses the reserved "/__ready" path so it
// never blocks behind a user handler.
func startServer(cfg ServerConfig, handler stdhttp.Handler) (*server, error) {
	ln, err := net.Listen(cfg.Network, cfg.Address)
	if err != nil {
		return nil, fmt.Errorf("%w: %v", ErrListen, err)
	}

	mux := stdhttp.NewServeMux()
	if handler == nil {
		mux.HandleFunc("/", func(w stdhttp.ResponseWriter, _ *stdhttp.Request) {
			w.WriteHeader(stdhttp.StatusOK)
			_, _ = io.WriteString(w, "ok")
		})
	} else {
		mux.Handle("/", handler)
	}
	mux.HandleFunc("/__ready", func(w stdhttp.ResponseWriter, _ *stdhttp.Request) {
		w.WriteHeader(stdhttp.StatusOK)
	})

	s := &server{
		srv:     &stdhttp.Server{Handler: mux},
		ln:      ln,
		serveCh: make(chan error, 1),
	}
	go func() { s.serveCh <- s.srv.Serve(ln) }()

	if cfg.Network != "unix" {
		if err := waitServing(ln.Addr().String() + "/__ready"); err != nil {
			_ = s.srv.Close()
			_ = ln.Close()
			return nil, fmt.Errorf("%w: %v", ErrServe, err)
		}
	}
	return s, nil
}

// stop gracefully shuts the server down: it stops accepting new connections,
// waits for in-flight handlers within cfg.ShutdownTimeout, and only after a
// timeout force-closes the underlying server (resource cleanup, never a
// goroutine kill). stop is idempotent and waits for the Serve goroutine to
// return so no serving goroutine leaks.
func (s *server) stop(cfg ServerConfig) error {
	s.stopOnce.Do(func() {
		shCtx, cancel := context.WithTimeout(context.Background(), cfg.ShutdownTimeout)
		defer cancel()
		if err := s.srv.Shutdown(shCtx); err != nil {
			s.stopErr = fmt.Errorf("%w: %v", ErrShutdown, err)
			_ = s.srv.Close()
		}
		<-s.serveCh
	})
	return s.stopErr
}

// waitServing probes the bound address until an HTTP 200 is observed.
func waitServing(urlPath string) error {
	base := "http://" + urlPath
	deadline := time.Now().Add(5 * time.Second)
	var lastErr error
	for time.Now().Before(deadline) {
		resp, err := stdhttp.Get(base) //nolint:gosec // test-grade local probe
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
