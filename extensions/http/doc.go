// Package http implements the HTTP/RPC Server Capability Extension v0.1: a
// real external resource (an HTTP listener/server) that strictly obeys the
// Runtime Fiber / Activation / Effect / Ownership / Dependency / Close
// semantics.
//
// Authority stays split:
//
//	Fiber/Activation/Effect/Ownership/Dependency/Provider identity — Kernel
//	HTTP listen/serve + graceful shutdown                    — this Extension
//	Artifact -> Module (HTTP implementation)                 — Loader
//	desired configuration                                    — Config
//	replacement                                             — HMR
//	server members                                          — Registry
//
// An HTTP Server is owned by one Activation: Apply listens and reaches a
// serving state BEFORE it commits (ctx.Effect), so Fiber Active means the HTTP
// server is truly serving; the activation unwind gracefully shuts the server
// down before the Fiber can finish. There is no second lifecycle system and no
// package-global HTTP state: every listener/server/mux is instance-scoped to
// the Component that created it in Apply.
package http

import (
	"errors"
	"fmt"
	"time"
)

// Errors. Compare with errors.Is.
var (
	// ErrInvalidConfig reports an invalid HTTP ServerConfig (empty address,
	// unsupported network, ...).
	ErrInvalidConfig = errors.New("http: invalid server config")
	// ErrListen reports a bind failure (e.g. address already in use).
	ErrListen = errors.New("http: listen failed")
	// ErrServe reports that the server could not reach a serving state.
	ErrServe = errors.New("http: serve failed")
	// ErrShutdown reports a graceful shutdown that did not finish within the
	// configured timeout (the underlying server is force-closed afterwards).
	ErrShutdown = errors.New("http: shutdown failed")
	// ErrFactory reports a config.Factory failure (invalid ComponentConfig).
	ErrFactory = errors.New("http: factory failed")
)

// ServerConfig describes where one HTTP server binds.
type ServerConfig struct {
	// Network is "tcp" by default; "tcp4"/"tcp6"/"unix" are also accepted.
	Network string
	// Address is the bind address, e.g. "127.0.0.1:18080" or ":18080".
	Address string
	// ShutdownTimeout bounds the graceful Shutdown wait during cleanup.
	// Zero means the 5s default.
	ShutdownTimeout time.Duration
}

// resolved applies defaults to cfg.
func (c ServerConfig) resolved() ServerConfig {
	if c.Network == "" {
		c.Network = "tcp"
	}
	if c.ShutdownTimeout <= 0 {
		c.ShutdownTimeout = 5 * time.Second
	}
	return c
}

// Validate checks the configuration without binding.
func (c ServerConfig) Validate() error {
	switch c.Network {
	case "tcp", "tcp4", "tcp6", "unix":
	default:
		return fmt.Errorf("%w: unsupported network %q", ErrInvalidConfig, c.Network)
	}
	if c.Address == "" {
		return fmt.Errorf("%w: empty address", ErrInvalidConfig)
	}
	return nil
}
