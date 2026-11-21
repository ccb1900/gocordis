// Package static contributes a read-only file route to a server.Service,
// reading resources through the fs extension's Service.
package static

import (
	"errors"
	"mime"
	"net/http"
	"os"
	"path"
	"strings"

	"dynamic-runtime/extensions/fs"
	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

// pathParam is the wildcard name used in the Route+"/{path...}" pattern. The
// standard library captures every remaining segment (including nested
// directories) under this name, readable through r.PathValue("path").
const pathParam = "path"

// Component serves files under Root at Route+"/{path...}" while Active.
// Root is an fs resource-name prefix (e.g. "file:///tmp/assets") that must be
// mounted in the fs.Service by the composition root; the component only reads
// through the capability and never owns that mount.
type Component struct {
	Route string
	Root  string
}

func (c *Component) Name() string { return "server-static" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(server.ServiceKey),
		runtime.Requires(fs.ServiceKey),
	}
}

func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	fsSvc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	if err := ctx.Effect(func() (func() error, error) {
		return svc.Handle(http.MethodGet, c.Route+"/{"+pathParam+"...}", func(w http.ResponseWriter, r *http.Request) error {
			return c.serve(fsSvc, w, r)
		})
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

func (c *Component) serve(fsSvc fs.Service, w http.ResponseWriter, r *http.Request) error {
	rel, ok := safeRelative(r.PathValue(pathParam))
	if !ok {
		http.NotFound(w, r)
		return nil
	}
	data, err := fsSvc.ReadFile(r.Context(), strings.TrimRight(c.Root, "/")+"/"+rel)
	if err != nil {
		if errors.Is(err, os.ErrNotExist) {
			http.NotFound(w, r)
			return nil
		}
		return err
	}
	if ct := mime.TypeByExtension(path.Ext(rel)); ct != "" {
		w.Header().Set("Content-Type", ct)
	}
	_, err = w.Write(data)
	return err
}

// safeRelative rejects traversal, absolute paths and foreign URL schemes.
// Every ".." segment is refused outright; cleaning a slash-prefixed value then
// keeps the result inside the mount even for percent-encoded input.
func safeRelative(name string) (string, bool) {
	if name == "" || strings.ContainsAny(name, "\\\x00") || strings.Contains(name, "://") {
		return "", false
	}
	for _, seg := range strings.Split(name, "/") {
		if seg == ".." {
			return "", false
		}
	}
	rel := strings.TrimPrefix(path.Clean("/"+name), "/")
	if rel == "" || rel == "." || rel == ".." || strings.HasPrefix(rel, "../") {
		return "", false
	}
	return rel, true
}
