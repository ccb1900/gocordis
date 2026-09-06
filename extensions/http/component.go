package http

import (
	"fmt"
	stdhttp "net/http"
	"sync"
	"sync/atomic"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/registry"
	"dynamic-runtime/runtime"
)

var nextHandleID atomic.Uint64

// Component is an HTTP Server as a Runtime Component. It owns exactly the
// Activation-scoped HTTP resource (listener + server + dedicated mux); the
// Kernel owns the Fiber/Activation lifecycle.
type Component struct {
	name string

	cfg      ServerConfig
	provider bool
	reg      *registry.Registry[Handle]
	regID    registry.MemberID

	handler stdhttp.Handler

	// Per-activation resource state (only ever touched by Apply / the Kernel
	// serialized lifecycle of this Fiber).
	mu       sync.Mutex
	addr     string
	handleID uint64
}

// Option configures a Component.
type Option func(*Component)

// WithName sets the diagnostics name (default "http:<address>").
func WithName(name string) Option { return func(c *Component) { c.name = name } }

// WithProvider makes the Component provide ServerCapability while Active.
func WithProvider() Option { return func(c *Component) { c.provider = true } }

// WithRegistryMember registers the Activation's server handle as a member of
// reg (added on Apply, removed on unwind). Membership churn never changes the
// provider identity of reg.
func WithRegistryMember(reg *registry.Registry[Handle], id registry.MemberID) Option {
	return func(c *Component) { c.reg, c.regID = reg, id }
}

// WithHandler sets the server's HTTP handler (implementation data). nil means
// the built-in "ok" handler. The readiness path is always "/__ready".
func WithHandler(h stdhttp.Handler) Option { return func(c *Component) { c.handler = h } }

// New creates an HTTP server Component for cfg. Configuration is validated
// eagerly (Factory/Create-time), and again defensively in Apply.
func New(cfg ServerConfig, opts ...Option) (*Component, error) {
	cfg = cfg.resolved()
	if err := cfg.Validate(); err != nil {
		return nil, err
	}
	c := &Component{cfg: cfg}
	for _, o := range opts {
		if o != nil {
			o(c)
		}
	}
	if c.name == "" {
		c.name = "http:" + cfg.Address
	}
	return c, nil
}

// Name returns the component name (diagnostics only).
func (c *Component) Name() string { return c.name }

// Inject declares no dependencies for a plain HTTP server.
func (c *Component) Inject() []runtime.Dependency { return nil }

// Provide declares the optional HTTP server capability.
func (c *Component) Provide() []runtime.Capability {
	if c.provider {
		return []runtime.Capability{ServerCapability.Capability()}
	}
	return nil
}

// Config returns the resolved server configuration.
func (c *Component) Config() ServerConfig { return c.cfg }

// Addr returns the bound address once the server is serving ("" before).
func (c *Component) Addr() string {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.addr
}

// HandleID returns the per-activation server instance identity (0 before the
// first activation, a fresh value for every activation afterwards).
func (c *Component) HandleID() uint64 {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.handleID
}

func (c *Component) handle() Handle {
	c.mu.Lock()
	defer c.mu.Unlock()
	return Handle{ID: c.handleID, Addr: c.addr}
}

// Apply starts one HTTP server instance and records every Runtime-managed
// consequence (listener/server, optional provider, optional registry member)
// through ctx.Effect. Apply returns only after the server is actually serving;
// a failure anywhere fully unwinds everything already created.
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	cfg := c.cfg.resolved()
	if err := cfg.Validate(); err != nil {
		return nil, fmt.Errorf("%w: %v", ErrInvalidConfig, err)
	}

	// Effect 1: bind + serve + register the graceful-stop inverse.
	if err := ctx.Effect(func() (func() error, error) {
		srv, err := startServer(cfg, c.handler)
		if err != nil {
			return nil, err
		}
		c.mu.Lock()
		c.handleID = nextHandleID.Add(1)
		c.addr = srv.ln.Addr().String()
		c.mu.Unlock()
		return func() error { return srv.stop(cfg) }, nil
	}); err != nil {
		return nil, err
	}

	// Effect 2 (optional): provide the server handle under the typed key.
	if c.provider {
		if err := runtime.Provide(ctx, ServerCapability, c.handle()); err != nil {
			return nil, err
		}
	}

	// Effect 3 (optional): add the Activation-owned registry member; the
	// inverse removes it so no stale handle survives the Activation.
	if c.reg != nil {
		if err := ctx.Effect(func() (func() error, error) {
			h := c.handle()
			if err := c.reg.Add(c.regID, h); err != nil {
				return nil, err
			}
			return func() error { return c.reg.Remove(c.regID) }, nil
		}); err != nil {
			return nil, err
		}
	}

	return nil, nil
}

// NewConfigFactory returns a config.Factory that creates HTTP Components from
// ComponentConfig.Config keys ("network", "address"; optional "provider" bool).
// It is used by Config integration and by Loader/HMR test harnesses.
func NewConfigFactory() config.Factory { return &configFactory{} }

type configFactory struct{}

func (f *configFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	if cc.ID == "" {
		return nil, fmt.Errorf("%w: empty component id", ErrFactory)
	}
	cfg := ServerConfig{}
	if v, ok := cc.Config["network"].(string); ok && v != "" {
		cfg.Network = v
	}
	addr, _ := cc.Config["address"].(string)
	if addr == "" {
		return nil, fmt.Errorf("%w: component %q has empty address", ErrFactory, cc.ID)
	}
	cfg.Address = addr
	comp, err := New(cfg, WithName(cc.ID))
	if err != nil {
		return nil, err
	}
	return comp, nil
}
