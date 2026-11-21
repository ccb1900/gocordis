// Package static contributes a read-only file route to a server.Service,
// reading resources through the fs extension's Service.
package static

import (
	"context"
	"net/http"
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
			c.serve(fsSvc, w, r)
			return nil
		})
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

// serve validates the wildcard once, then hands the request to the standard
// library file server. The io/fs adapter is bound to this request's context and
// scoped to Root by rootedService, so files are streamed through http.File's
// Read/Seek instead of being materialized with Service.ReadFile. MIME typing,
// Range, directory indexes and conditional headers are all stdlib concerns.
func (c *Component) serve(fsSvc fs.Service, w http.ResponseWriter, r *http.Request) {
	rel, ok := safeRelative(r.PathValue(pathParam))
	if !ok {
		http.NotFound(w, r)
		return
	}
	adapter := fs.NewFS(rootedService{Service: fsSvc, root: c.Root}, r.Context())
	fileServer := http.FileServerFS(adapter)
	clone := r.Clone(r.Context())
	clone.URL.Path = rel
	clone.URL.RawPath = "" // drop the original encoded path; Path now holds the safe value
	fileServer.ServeHTTP(w, clone)
}

// rootedService qualifies adapter-validated io/fs names with the component's
// Root before delegating to the mounted, mount-routed Service. Embedding keeps
// every unrelated Service method available without reimplementing them; only
// the read operations used by io/fs need the prefix.
type rootedService struct {
	fs.Service
	root string
}

func (s rootedService) qualify(name string) string {
	// A bare-root prefix such as "file:///" or "memory:///" must keep its
	// authority slashes: TrimRight would collapse it to "file:" / "memory:",
	// yielding "memory:/name" instead of "memory:///name" (no mount match).
	if strings.HasSuffix(s.root, "/") {
		return s.root + name
	}
	return s.root + "/" + name
}

func (s rootedService) Open(ctx context.Context, name string) (fs.File, error) {
	return s.Service.Open(ctx, s.qualify(name))
}

func (s rootedService) Stat(ctx context.Context, name string) (fs.FileInfo, error) {
	return s.Service.Stat(ctx, s.qualify(name))
}

func (s rootedService) ReadDir(ctx context.Context, name string) ([]fs.FileInfo, error) {
	return s.Service.ReadDir(ctx, s.qualify(name))
}

func (s rootedService) ReadFile(ctx context.Context, name string) ([]byte, error) {
	return s.Service.ReadFile(ctx, s.qualify(name))
}

// safeRelative rejects traversal, absolute paths, backslashes and foreign URL
// schemes before anything reaches the adapter or the mount table. Every ".."
// segment is refused outright; cleaning stays inside the mount even for
// percent-encoded input (PathValue is already unescaped by ServeMux).
func safeRelative(name string) (string, bool) {
	if name == "" || strings.HasPrefix(name, "/") || strings.ContainsAny(name, "\\\x00") ||
		strings.Contains(name, "://") || hasScheme(name) {
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
	// Keep a trailing slash so http.FileServerFS serves the directory index
	// (index.html or a listing) instead of redirecting to the slash form.
	if strings.HasSuffix(name, "/") {
		rel += "/"
	}
	return rel, true
}

// hasScheme reports whether name starts with a URI scheme (RFC 3986), e.g.
// "file:" or "memory://", which must never be treated as a relative resource.
func hasScheme(name string) bool {
	for i := 0; i < len(name); i++ {
		c := name[i]
		alpha := c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z'
		if i == 0 {
			if !alpha {
				return false
			}
			continue
		}
		if alpha || c >= '0' && c <= '9' || c == '+' || c == '-' || c == '.' {
			continue
		}
		return c == ':'
	}
	return false
}
