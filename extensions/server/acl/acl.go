// Package acl contributes host-based access control to a server.Service.
//
// The decision is pure: stripPort + host matching are side-effect free, and
// MiddlewareWithRoutes builds a policy from the global allow/deny lists plus
// per-route overrides. The Component commits that policy as an effect-owned
// route-check, so a denial vetoes one route and the service answers 404 (the
// same response as a route that was never registered) instead of a blanket
// 403. LIFO unwind withdraws exactly that check.
package acl

import (
	"net/http"
	"strings"

	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

// LOOPBACK_HOSTS are always allowed, even with an empty allow list. Both the
// bare IPv6 form and the bracketed Host-header form are covered.
var loopbackHosts = map[string]struct{}{
	"localhost": {},
	"127.0.0.1": {},
	"::1":       {},
	"[::1]":     {},
}

// Component installs a host policy while Active.
//
// Allow and Deny are global host rules. RouteAllowedHosts optionally overrides
// Allow for a single registration, keyed exactly as it was registered:
// "METHOD /pattern" (Handle) or "/pattern" (All/WS). A key without a method
// also overrides a method-qualified route with the same path. Deny is checked
// first and always wins; an empty effective allow list means loopback-only.
type Component struct {
	Allow             []string
	Deny              []string
	RouteAllowedHosts map[string][]string
}

func (c *Component) Name() string { return "server-acl" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(server.ServiceKey)}
}

func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	policy := MiddlewareWithRoutes(c.Allow, c.Deny, c.RouteAllowedHosts)
	// Effect installs the route-check and commits its disposer as the inverse:
	// LIFO unwind withdraws exactly this veto, restoring every route.
	if err := ctx.Effect(func() (func() error, error) {
		return svc.AddRouteCheck(func(req *http.Request, pattern string) bool {
			host := ""
			if req != nil {
				host = req.Host
			}
			return policy(stripPort(host), pattern)
		})
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

// stripPort lowercases a Host header, keeps bracketed IPv6 literals intact and
// removes a trailing port. A bare IPv6 literal (more than one colon) has no
// port to remove and is preserved as-is.
func stripPort(host string) string {
	host = strings.ToLower(strings.TrimSpace(host))
	if strings.HasPrefix(host, "[") {
		if end := strings.IndexByte(host, ']'); end >= 0 {
			return strings.TrimSuffix(host[:end+1], ".")
		}
		return strings.TrimSuffix(host, ".")
	}
	if strings.Count(host, ":") > 1 {
		return strings.TrimSuffix(host, ".")
	}
	if i := strings.IndexByte(host, ':'); i >= 0 {
		host = host[:i]
	}
	return strings.TrimSuffix(host, ".")
}

// matchHost implements the host-pattern language: a leading "." matches the
// apex and every subdomain ("*.suffix" is accepted as an equivalent spelling);
// every other rule is an exact match. Matching is case-insensitive.
func matchHost(pattern, host string) bool {
	pattern = strings.ToLower(strings.TrimSuffix(strings.TrimSpace(pattern), "."))
	host = strings.ToLower(strings.TrimSuffix(host, "."))
	switch {
	case strings.HasPrefix(pattern, "."):
		suffix := pattern[1:]
		return suffix != "" && (host == suffix || strings.HasSuffix(host, pattern))
	case strings.HasPrefix(pattern, "*."):
		suffix := pattern[2:]
		return suffix != "" && (host == suffix || strings.HasSuffix(host, "."+suffix))
	default:
		return pattern != "" && host == pattern
	}
}

func matchesAny(host string, rules []string) bool {
	for _, rule := range rules {
		if matchHost(rule, host) {
			return true
		}
	}
	return false
}

// hostAllowed is the whole policy: deny first (so deny beats allow and the
// empty-allow fallback), then the loopback exemption, then an empty allow list
// (loopback-only), then the allow rules.
func hostAllowed(host string, allow, deny []string) bool {
	if matchesAny(host, deny) {
		return false
	}
	if _, loopback := loopbackHosts[host]; loopback {
		return true
	}
	if len(allow) == 0 {
		return false
	}
	return matchesAny(host, allow)
}

// HostPolicy is a pure decision: given a port-stripped host and a route
// registration key, report whether that route may serve the request.
type HostPolicy func(host, pattern string) bool

// MiddlewareWithRoutes builds the pure host policy. An entry in routes
// REPLACES the global allow list for that route key; deny still applies to
// every route. It has no server/runtime dependency and no side effects.
func MiddlewareWithRoutes(allow, deny []string, routes map[string][]string) HostPolicy {
	return func(host, pattern string) bool {
		effective := allow
		if v, ok := routes[pattern]; ok {
			effective = v
		} else if i := strings.IndexByte(pattern, ' '); i >= 0 {
			// A method-less key overrides the same path under any method.
			if v, ok := routes[pattern[i+1:]]; ok {
				effective = v
			}
		}
		return hostAllowed(host, effective, deny)
	}
}

// Handler adapts a pure policy into an HTTP middleware. A denied request sees
// 404, matching the route-check path. Use of r.Pattern (set by ServeMux, and
// available to middleware installed inside a route) keeps per-route overrides
// working without any global state.
func (p HostPolicy) Handler(next http.Handler) http.Handler {
	return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
		host, pattern := "", ""
		if r != nil {
			host = r.Host
			pattern = r.Pattern
			if pattern == "" && r.URL != nil {
				pattern = r.Method + " " + r.URL.Path
			}
		}
		if !p(stripPort(host), pattern) {
			http.NotFound(w, r)
			return
		}
		next.ServeHTTP(w, r)
	})
}

// Middleware keeps the legacy signature but delegates to the new semantics:
// loopback exemption, leading-dot apex/subdomain matching, loopback-only empty
// allow list, and a 404 (route veto) response on denial.
func Middleware(allow, deny []string) func(http.Handler) http.Handler {
	return MiddlewareWithRoutes(allow, deny, nil).Handler
}
