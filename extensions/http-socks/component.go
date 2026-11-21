package socks

import (
	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/runtime"
)

// Component installs one SOCKS proxy agent on the HTTP service. The proxy is a
// reversible effect: unwinding clears it, so the service never keeps a proxy
// that outlives this activation.
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
	url, err := ProxyURL(c.Scheme, c.Addr)
	if err != nil {
		return nil, err
	}
	// Mutate inside the Effect install so a failed install (or a context that
	// began unwinding mid-install) cannot leave the proxy set without an inverse.
	if err := ctx.Effect(func() (func() error, error) {
		if err := svc.SetProxy(url); err != nil {
			return nil, err
		}
		return func() error { return svc.SetProxy("") }, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}
