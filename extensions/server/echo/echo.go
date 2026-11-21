// Package echo contributes a simple GET/POST echo route to a server.Service.
package echo

import (
	"io"
	"net/http"
	"strings"

	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

// Component registers GET and POST handlers at Prefix+"echo" while Active.
// GET echoes the "msg" query parameter; POST echoes the request body.
type Component struct{ Prefix string }

func (c *Component) Name() string { return "server-echo" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(server.ServiceKey)}
}

func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	pattern := c.Prefix + "echo"
	if !strings.HasPrefix(pattern, "/") {
		pattern = "/" + pattern
	}
	// Each Effect carries one registration and commits its disposer as inverse.
	if err := ctx.Effect(func() (func() error, error) {
		return svc.Handle(http.MethodGet, pattern, echoQuery)
	}); err != nil {
		return nil, err
	}
	if err := ctx.Effect(func() (func() error, error) {
		return svc.Handle(http.MethodPost, pattern, echoBody)
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

func echoQuery(w http.ResponseWriter, r *http.Request) error {
	_, err := io.WriteString(w, r.URL.Query().Get("msg"))
	return err
}

func echoBody(w http.ResponseWriter, r *http.Request) error {
	if r.Body == nil {
		return nil
	}
	body, err := io.ReadAll(r.Body)
	if err != nil {
		return err
	}
	_, err = w.Write(body)
	return err
}
