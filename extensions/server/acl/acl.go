// Package acl provides a host allow/deny middleware contribution to a
// server.Service. The component registers the middleware through the service's
// reversible Use and installs the returned disposer as a ctx.Effect inverse.
package acl

import (
	"net"
	"net/http"
	"strings"

	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

// Component installs a host-based access-control middleware while Active.
// Allow is a set of exact hosts or "*.suffix" wildcards; an empty Allow admits
// every host. Deny is checked first and always wins.
type Component struct {
	Allow []string
	Deny  []string
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
	// Effect installs Use and commits its disposer as the inverse: LIFO unwind
	// withdraws exactly this middleware registration.
	if err := ctx.Effect(func() (func() error, error) {
		return svc.Use(Middleware(c.Allow, c.Deny))
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

// Middleware returns an HTTP middleware that admits a request only when its
// host matches an allow rule (or Allow is empty) and matches no deny rule.
// Host matching is case-insensitive, ignores an optional port, and supports a
// leading "*." wildcard that requires at least one extra label.
func Middleware(allow, deny []string) func(http.Handler) http.Handler {
	return func(next http.Handler) http.Handler {
		return http.HandlerFunc(func(w http.ResponseWriter, r *http.Request) {
			host := requestHost(r)
			if matchesAny(host, deny) || (len(allow) > 0 && !matchesAny(host, allow)) {
				http.Error(w, "Forbidden", http.StatusForbidden)
				return
			}
			next.ServeHTTP(w, r)
		})
	}
}

func requestHost(r *http.Request) string {
	if r == nil {
		return ""
	}
	host := r.Host
	if h, _, err := net.SplitHostPort(host); err == nil {
		host = h
	}
	return strings.ToLower(strings.TrimSuffix(host, "."))
}

func matchesAny(host string, rules []string) bool {
	for _, rule := range rules {
		rule = strings.ToLower(strings.TrimSuffix(strings.TrimSpace(rule), "."))
		if strings.HasPrefix(rule, "*.") {
			suffix := rule[2:]
			if suffix != "" && strings.HasSuffix(host, "."+suffix) {
				return true
			}
			continue
		}
		if rule != "" && host == rule {
			return true
		}
	}
	return false
}
