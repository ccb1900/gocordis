// Package proxy contributes a forwarding route to a server.Service, using the
// http extension's Service for the outbound request.
package proxy

import (
	"io"
	"net/http"

	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

// Component forwards every registered standard method on Route to the fixed
// Target while Active. Method, headers and request body are preserved; the
// backend's status, headers and body are copied back. A transport failure is
// reported as 502.
type Component struct {
	Route  string
	Target string
}

// The core router requires a method per registration; cover the standard HTTP
// methods so the forwarded method is the caller's original one.
var methods = []string{
	http.MethodGet, http.MethodPost, http.MethodPut, http.MethodPatch,
	http.MethodDelete, http.MethodHead, http.MethodOptions,
}

func (c *Component) Name() string { return "server-proxy" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(server.ServiceKey),
		runtime.Requires(exthttp.ServiceKey),
	}
}

func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	hc, err := runtime.Require(ctx, exthttp.ServiceKey)
	if err != nil {
		return nil, err
	}
	for _, method := range methods {
		m := method
		if err := ctx.Effect(func() (func() error, error) {
			return svc.Handle(m, c.Route, func(w http.ResponseWriter, r *http.Request) error {
				return c.forward(hc, w, r)
			})
		}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func (c *Component) forward(hc exthttp.Service, w http.ResponseWriter, r *http.Request) error {
	var body []byte
	if r.Body != nil {
		var err error
		body, err = io.ReadAll(r.Body)
		if err != nil {
			http.Error(w, "Bad Request", http.StatusBadRequest)
			return nil
		}
	}
	resp, err := hc.Do(r.Context(), exthttp.Request{
		Method: r.Method,
		URL:    c.Target,
		Header: r.Header,
		Body:   body,
	})
	if err != nil {
		http.Error(w, "Bad Gateway", http.StatusBadGateway)
		return nil
	}
	for key, values := range resp.Header {
		for _, value := range values {
			w.Header().Add(key, value)
		}
	}
	w.WriteHeader(resp.StatusCode)
	_, err = w.Write(resp.Body)
	return err
}
