package socks

import (
	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

// schemes are the request URL schemes a SOCKS proxy tunnels. A SOCKS agent
// carries both HTTP and HTTPS traffic.
var schemes = []string{"http", "https"}

// Component installs one identity-bearing SOCKS proxy registration on the HTTP
// service. The inverse removes ONLY this registration (restoring whichever
// registration previously won), so a peer's proxy is never cleared.
type Component struct {
	Scheme string
	Addr   string
}

func (c *Component) Name() string { return "http-socks" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(exthttp.ServiceKey)}
}

func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, exthttp.ServiceKey)
	if err != nil {
		return nil, err
	}
	proxyURL, err := ProxyURL(c.Scheme, c.Addr)
	if err != nil {
		return nil, err
	}
	// Register inside the Effect install: a failed install (or an unwinding
	// context) cannot leave a proxy contribution without its own inverse.
	if err := ctx.Effect(func() (func() error, error) {
		remove, err := svc.RegisterProxy(schemes, proxyURL)
		if err != nil {
			return nil, err
		}
		return remove, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}
