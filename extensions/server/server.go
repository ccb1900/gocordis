// Package server is the HTTP-server extension for dynamic-runtime.
//
// It is an implementation-layer capability (paper §5), not kernel semantics:
// a Component provides a Service capability; applications compose that
// service with Serve, which binds a real listener as a reversible effect. The
// listener, the serving goroutine and the graceful shutdown all belong to one
// activation — no second lifecycle authority, no package-global state.
package server

import (
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"

	"dynamic-runtime/runtime"
)

// Handler is one route handler. Returning an error raises a 500 unless the
// handler has already written a response (the response is never written twice).
type Handler func(http.ResponseWriter, *http.Request) error

// Service is the reversible HTTP service provided by Component.
//
// Handle and Use return idempotent disposers that remove ONLY the registration
// they were returned for. A registration lives exactly as long as the effect
// that installed it; Close is the activation-level inverse.
type Service interface {
	Handle(method, pattern string, h Handler) (func() error, error)
	// All registers a handler for every HTTP method on pattern. Exact-method
	// routes registered through Handle take precedence over an All route.
	All(pattern string, h Handler) (func() error, error)
	Use(mw func(http.Handler) http.Handler) (func() error, error)
	ServeHTTP(http.ResponseWriter, *http.Request)
	Close() error
}

// ServiceKey is the capability a server Component provides while Active.
var ServiceKey = runtime.NewKey[Service]("extension.server.service")

var errServiceClosed = errors.New("server: service closed")

// ---- named route params --------------------------------------------------

// Param returns a named path parameter captured by the matching route, e.g.
// Param(r, "id") for a pattern "/users/{id}", or Param(r, "path") for the
// catch-all "/static/{path...}" (which captures every remaining segment).
// Values come from the standard library's PathValue; outside a routed request
// or for an unknown name the result is "".
func Param(r *http.Request, name string) string {
	if r == nil {
		return ""
	}
	return r.PathValue(name)
}

// ---- route representation ------------------------------------------------

type route struct {
	id      uint64
	method  string // "" for All routes, which match every method
	pattern string // Go 1.22 ServeMux path pattern, e.g. "/users/{id}"
	handler Handler
}

type middleware struct {
	id uint64
	fn func(http.Handler) http.Handler
}

type router struct {
	mu          sync.RWMutex
	nextID      uint64
	routes      []*route
	middlewares []*middleware
	closed      bool
	mux         *http.ServeMux // rebuilt whenever a registration changes
}

// newRouter builds an empty, open router. Unexported on purpose: the only
// public way to obtain a Service is Component + ServiceKey.
func newRouter() Service { return &router{mux: http.NewServeMux()} }

// buildMux compiles the registration list into a fresh ServeMux. Path matching,
// wildcards ({id}, {path...}) and method precedence are entirely the standard
// library's job; there is no hand-written matcher here. The first registration
// for a given pattern wins (matching the old dispatch order). ServeMux panics
// on an invalid or conflicting pattern, so that panic is converted into an
// error: a bad declaration must fail registration, never crash the process.
func buildMux(routes []*route) (mux *http.ServeMux, err error) {
	mux = http.NewServeMux()
	defer func() {
		if v := recover(); v != nil {
			err = fmt.Errorf("server: invalid route registration: %v", v)
		}
	}()
	seen := make(map[string]struct{}, len(routes))
	for _, rt := range routes {
		pattern := rt.pattern
		if rt.method != "" {
			pattern = rt.method + " " + pattern
		}
		if _, duplicate := seen[pattern]; duplicate {
			continue
		}
		seen[pattern] = struct{}{}
		h := rt.handler
		mux.HandleFunc(pattern, func(w http.ResponseWriter, req *http.Request) {
			runHandler(w, req, h)
		})
	}
	return mux, nil
}

func (r *router) Handle(method, pattern string, h Handler) (func() error, error) {
	if method == "" {
		return nil, errors.New("server: empty method")
	}
	return r.register(strings.ToUpper(method), pattern, h)
}

func (r *router) All(pattern string, h Handler) (func() error, error) {
	return r.register("", pattern, h)
}

// register is the shared, id-keyed registration path for Handle and All. Every
// change rebuilds the mux from the current list. A disposer removes exactly the
// route it was returned for, idempotently, and rebuilds the mux again.
func (r *router) register(method, pattern string, h Handler) (func() error, error) {
	if h == nil {
		return nil, errors.New("server: nil handler")
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed {
		return nil, errServiceClosed
	}
	r.nextID++
	id := r.nextID
	next := append(append([]*route(nil), r.routes...), &route{
		id: id, method: method, pattern: pattern, handler: h,
	})
	mux, err := buildMux(next)
	if err != nil {
		return nil, err
	}
	r.routes, r.mux = next, mux
	return func() error {
		r.mu.Lock()
		defer r.mu.Unlock()
		for i, existing := range r.routes {
			if existing.id == id {
				remaining := append(append([]*route(nil), r.routes[:i]...), r.routes[i+1:]...)
				rebuilt, err := buildMux(remaining)
				if err != nil {
					return err
				}
				r.routes, r.mux = remaining, rebuilt
				break
			}
		}
		return nil
	}, nil
}

func (r *router) Use(mw func(http.Handler) http.Handler) (func() error, error) {
	if mw == nil {
		return nil, errors.New("server: nil middleware")
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed {
		return nil, errServiceClosed
	}
	r.nextID++
	id := r.nextID
	r.middlewares = append(r.middlewares, &middleware{id: id, fn: mw})
	return func() error {
		r.mu.Lock()
		defer r.mu.Unlock()
		for i, existing := range r.middlewares {
			if existing.id == id {
				r.middlewares = append(r.middlewares[:i], r.middlewares[i+1:]...)
				break
			}
		}
		return nil
	}, nil
}

func (r *router) Close() error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed {
		return nil
	}
	r.closed = true
	r.routes = nil
	r.middlewares = nil
	r.mux = http.NewServeMux()
	return nil
}

func (r *router) ServeHTTP(w http.ResponseWriter, req *http.Request) {
	r.mu.RLock()
	if r.closed {
		r.mu.RUnlock()
		http.Error(w, "service closed", http.StatusServiceUnavailable)
		return
	}
	mux := r.mux
	routes := append([]*route(nil), r.routes...)
	chain := make([]func(http.Handler) http.Handler, len(r.middlewares))
	for i, m := range r.middlewares {
		chain[i] = m.fn
	}
	r.mu.RUnlock()

	// Snapshot taken under the lock; user middleware/handlers run with NO lock
	// held (no broad lock across user code).
	var h http.Handler = http.HandlerFunc(func(w http.ResponseWriter, req *http.Request) {
		r.dispatch(w, req, mux, routes)
	})
	for i := len(chain) - 1; i >= 0; i-- {
		h = chain[i](h)
	}
	h.ServeHTTP(w, req)
}

func (r *router) dispatch(w http.ResponseWriter, req *http.Request, mux *http.ServeMux, routes []*route) {
	// ServeMux's own 404 and 405 responses already match the old behavior
	// (http.NotFound and http.Error(..., 405) with a sorted Allow header), so
	// normal requests are handed straight to it. The one convention ServeMux
	// does not provide is the extension's OPTIONS handling: an empty 204 with
	// the sorted Allow set, unless an explicit OPTIONS route was registered.
	if req.Method == http.MethodOptions {
		_, pattern := mux.Handler(req.Clone(req.Context()))
		if strings.HasPrefix(pattern, http.MethodOptions+" ") {
			// An explicit OPTIONS route wins over the automatic 204.
			mux.ServeHTTP(w, req)
			return
		}
		allow := allowedMethods(req, mux, routes)
		if allow == "" {
			http.NotFound(w, req)
			return
		}
		w.Header().Set("Allow", allow)
		w.WriteHeader(http.StatusNoContent)
		return
	}
	mux.ServeHTTP(w, req)
}

// allowedMethods asks the very same ServeMux which registrations would handle
// this path — no second, hand-written path matcher is introduced. Each route is
// probed with its own method (All routes with a neutral one) and is admitted
// only when the mux selects that route's exact pattern.
func allowedMethods(req *http.Request, mux *http.ServeMux, routes []*route) string {
	allowed := make(map[string]struct{})
	for _, rt := range routes {
		method := rt.method
		if method == "" {
			method = "PROBE" // an All route answers any method
		}
		probe := req.Clone(req.Context())
		probe.Method = method
		_, pattern := mux.Handler(probe)
		want := rt.pattern
		if rt.method != "" {
			want = rt.method + " " + want
		}
		if pattern != want {
			continue // skipped: duplicate, shadowed or conflicting registration
		}
		if rt.method == "" {
			allowed["*"] = struct{}{}
		} else {
			allowed[rt.method] = struct{}{}
		}
	}
	return strings.Join(sortedMethods(allowed), ", ")
}

func sortedMethods(allowed map[string]struct{}) []string {
	methods := make([]string, 0, len(allowed))
	for m := range allowed {
		methods = append(methods, m)
	}
	sort.Strings(methods)
	return methods
}

func runHandler(w http.ResponseWriter, req *http.Request, h Handler) {
	cw := &captureWriter{ResponseWriter: w}
	if err := h(cw, req); err != nil && !cw.wrote {
		http.Error(w, "Internal Server Error", http.StatusInternalServerError)
	}
}

// captureWriter records whether the handler has already produced a response
// so an error never causes a second write.
type captureWriter struct {
	http.ResponseWriter
	wrote bool
}

func (cw *captureWriter) WriteHeader(code int) {
	cw.wrote = true
	cw.ResponseWriter.WriteHeader(code)
}

func (cw *captureWriter) Write(p []byte) (int, error) {
	cw.wrote = true
	return cw.ResponseWriter.Write(p)
}

// Flush forwards the optional Flusher capability so wrapped handlers keep it.
// A flush also counts as a response write.
func (cw *captureWriter) Flush() {
	cw.wrote = true
	if f, ok := cw.ResponseWriter.(http.Flusher); ok {
		f.Flush()
	}
}

// Unwrap lets http.ResponseController reach optional capabilities of the
// underlying ResponseWriter through the capturing wrapper.
func (cw *captureWriter) Unwrap() http.ResponseWriter { return cw.ResponseWriter }

// ---- Serve: bind before commit, join before unwind completes -------------

const shutdownTimeout = 5 * time.Second

// Serve binds addr and serves svc as one reversible effect on c.
//
// Active <=> the listener is bound: net.Listen runs synchronously inside the
// effect install, and only then is the serving goroutine started. The inverse
// gracefully shuts the server down and JOINS the goroutine before returning,
// so a Fiber cannot reach Gone while the port is still held.
func Serve(c *runtime.Context, svc Service, addr string) error {
	return serveWithPorts(c, svc, addr, 1)
}

// ServeRange binds the first available port in [base, base+ports-1] derived
// from addr (e.g. "127.0.0.1:8080" with ports=4 tries 8080..8083). Binding
// still happens inside the effect install: Active <=> a port is bound, and the
// inverse shuts down and joins the serving goroutine before returning. Port 0
// keeps its usual ephemeral meaning. ports must be positive.
func ServeRange(c *runtime.Context, svc Service, addr string, ports int) error {
	if ports <= 0 {
		return errors.New("server: ports must be positive")
	}
	return serveWithPorts(c, svc, addr, ports)
}

func serveWithPorts(c *runtime.Context, svc Service, addr string, ports int) error {
	if c == nil {
		return errors.New("server: nil context")
	}
	if svc == nil {
		return errors.New("server: nil service")
	}
	if addr == "" {
		return errors.New("server: empty address")
	}
	return c.Effect(func() (func() error, error) {
		ln, err := listenRange(addr, ports)
		if err != nil {
			return nil, err
		}
		srv := &http.Server{Handler: svc}
		served := make(chan struct{})
		go func() {
			defer close(served)
			_ = srv.Serve(ln) // ErrServerClosed is the expected shutdown result
		}()

		var once sync.Once
		var stopErr error
		stop := func() error {
			once.Do(func() {
				shCtx, cancel := context.WithTimeout(context.Background(), shutdownTimeout)
				defer cancel()
				stopErr = srv.Shutdown(shCtx)
				if stopErr != nil {
					_ = srv.Close()
				}
				<-served // join: no goroutine or listener outlives the inverse
			})
			return stopErr
		}
		return stop, nil
	})
}

// listenRange tries each port in the range in order, returning the first
// successfully bound listener. No fixed waits: binding is synchronous.
func listenRange(addr string, ports int) (net.Listener, error) {
	host, portStr, err := net.SplitHostPort(addr)
	if err != nil {
		return nil, fmt.Errorf("server: invalid address %q: %w", addr, err)
	}
	base, err := strconv.Atoi(portStr)
	if err != nil || base < 0 || base > 65535 {
		return nil, fmt.Errorf("server: invalid port in %q", addr)
	}
	var lastErr error
	for i := 0; i < ports; i++ {
		port := base + i
		if port > 65535 {
			break
		}
		ln, listenErr := net.Listen("tcp", net.JoinHostPort(host, strconv.Itoa(port)))
		if listenErr == nil {
			return ln, nil
		}
		lastErr = listenErr
	}
	if lastErr == nil {
		lastErr = errors.New("empty port range")
	}
	return nil, fmt.Errorf("server: no available port in range %s..%d: %w", addr, base+ports-1, lastErr)
}

// ---- Component -----------------------------------------------------------

// Component provides one Service under ServiceKey. It holds no configuration:
// address binding is a separate effect (Serve) belonging to whichever
// component consumes the capability.
type Component struct{}

func (c *Component) Name() string                 { return "extension.server" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc := newRouter()
	// Register the activation-level inverse BEFORE the provider so LIFO unwind
	// withdraws the capability first and closes the service last.
	if err := ctx.Effect(func() (func() error, error) {
		return func() error { return svc.Close() }, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, ServiceKey, svc); err != nil {
		return nil, err
	}
	return nil, nil
}
