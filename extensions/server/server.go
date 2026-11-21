// Package server is the HTTP-server extension for dynamic-runtime.
//
// It is an implementation-layer capability (paper §5), not kernel semantics:
// a Component provides a Service capability; applications compose that
// service with Serve, which binds a real listener as a reversible effect. The
// listener, the serving goroutine and the graceful shutdown all belong to one
// activation — no second lifecycle authority, no package-global state.
package server

import (
	"bufio"
	"context"
	"errors"
	"fmt"
	"net"
	"net/http"
	"net/url"
	"sort"
	"strconv"
	"strings"
	"sync"
	"time"

	"github.com/gorilla/websocket"

	"dynamic-runtime/runtime"
)

// Handler is one route handler. Returning an error raises a 500 unless the
// handler has already written a response (the response is never written twice).
type Handler func(http.ResponseWriter, *http.Request) error

// Service is the reversible HTTP service provided by Component.
//
// Handle, All, WS and Use return idempotent disposers that remove ONLY the
// registration they were returned for. A registration lives exactly as long as
// the effect that installed it; Close is the activation-level inverse.
type Service interface {
	Handle(method, pattern string, h Handler) (func() error, error)
	// All registers a handler for every HTTP method on pattern. Exact-method
	// routes registered through Handle take precedence over an All route.
	All(pattern string, h Handler) (func() error, error)
	// WS registers a WebSocket upgrade handler on a ServeMux pattern (e.g.
	// "/ws/{id}"). Like All it has no method qualifier, so exact-method HTTP
	// routes registered through Handle take precedence. The disposer removes
	// only this route and closes/joins every live connection it accepted.
	//
	// A callback MUST NOT call Close, nor its own disposer: both JOIN handler
	// goroutines and would therefore wait for the callback that called them.
	// Inside a callback use (*WSConn).Shutdown (own registration) or Shutdown
	// (whole service); both are non-joining.
	WS(pattern string, cb func(*WSConn) error) (func() error, error)
	Use(mw func(http.Handler) http.Handler) (func() error, error)
	// AddRouteCheck registers a dispatch-time route veto. It is effect-owned:
	// the returned idempotent disposer removes ONLY this check and is the
	// inverse a component should commit through ctx.Effect. It never changes a
	// Handle/All/WS registration or its disposer.
	AddRouteCheck(check RouteCheck) (func() error, error)
	// SetCheckOrigin overrides the policy used to verify the Origin header on
	// WebSocket upgrades. nil restores the same-origin default. It affects
	// upgrades performed after the call.
	SetCheckOrigin(fn func(*http.Request) bool)
	ServeHTTP(http.ResponseWriter, *http.Request)
	// Close is the activation-level inverse. It removes all registrations,
	// closes every live socket and JOINS all handler goroutines before it
	// returns, so Gone implies no surviving socket or handler. Never call it
	// from a WS callback (self-wait) — use Shutdown there.
	Close() error
	// Shutdown is the non-joining counterpart of Close: it removes all
	// registrations and closes every live socket without waiting for handler
	// goroutines. It is safe to call from inside a WS callback; the component
	// inverse still uses Close so the Gone guarantee is preserved.
	Shutdown() error
}

// RouteCheck is a dispatch-time predicate over one registration. pattern is
// the registration key: "GET /x" for Handle, "/x" for All and WS. Returning
// false vetoes that route for this request; when no route survives, the
// service answers 404 (never 403/405). Checks are additive: with none
// registered, Handle/All/WS dispatch exactly as before.
type RouteCheck func(req *http.Request, pattern string) bool

// ServiceKey is the capability a server Component provides while Active.
var ServiceKey = runtime.NewKey[Service]("extension.server.service")

var errServiceClosed = errors.New("server: service closed")

// ---- WebSocket public surface --------------------------------------------

// WSConn is the narrow view of an upgraded connection handed to a WS callback.
// It is implemented by a wrapper around *websocket.Conn; gorilla owns the
// handshake, framing and close protocol — nothing is re-implemented here.
//
// Close closes ONLY this socket. A callback that wants to dispose its
// registration must call Shutdown instead: Close's sibling APIs on Service (and
// the WS disposer) join handler goroutines and deadlock if called from a
// callback's own goroutine.
type WSConn interface {
	ReadMessage() (int, []byte, error)
	WriteMessage(int, []byte) error
	Close() error
	// Shutdown disposes the registration that accepted this connection: it
	// removes the route and closes all its sockets, including this one, WITHOUT
	// joining handler goroutines. Safe inside the callback. It is the only
	// disposal path a callback may use for its own registration.
	Shutdown() error
}

// WSRequestConn is optionally implemented by the WSConn value passed to a WS
// callback. It exposes the handshake request so handlers can read path
// parameters and headers. Use WSParam for the common path-parameter case.
type WSRequestConn interface {
	WSConn
	Request() *http.Request
}

// WSParam returns a named path parameter captured by the route that accepted
// the WebSocket connection (e.g. "id" for "/ws/{id}"). It is "" when conn does
// not expose a handshake request or the name is unknown.
func WSParam(conn WSConn, name string) string {
	if rc, ok := conn.(WSRequestConn); ok {
		return Param(rc.Request(), name)
	}
	return ""
}

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

type routeCheckReg struct {
	id uint64
	fn RouteCheck
}

type router struct {
	mu          sync.RWMutex
	nextID      uint64
	routes      []*route
	middlewares []*middleware
	checks      []*routeCheckReg
	closed      bool
	mux         *http.ServeMux // rebuilt whenever a registration changes

	checkOrigin func(*http.Request) bool // nil => same-origin default
	upgrader    *websocket.Upgrader
	wsRegs      map[*wsRegistration]struct{}
	closeOnce   sync.Once
	joinOnce    sync.Once
	closeDone   chan struct{}
	closingRegs []*wsRegistration
}

// newRouter builds an empty, open router. Unexported on purpose: the only
// public way to obtain a Service is Component + ServiceKey.
func newRouter() Service {
	r := &router{
		mux:       http.NewServeMux(),
		wsRegs:    make(map[*wsRegistration]struct{}),
		closeDone: make(chan struct{}),
	}
	r.upgrader = &websocket.Upgrader{
		ReadBufferSize:  1024,
		WriteBufferSize: 1024,
		CheckOrigin:     r.checkSameOrigin,
	}
	return r
}

// ---- WebSocket connection tracking ---------------------------------------

const wsWriteWait = time.Second

// closeScope records whether an inverse may wait for handler goroutines. It is
// the code-level boundary for the self-wait hazard: callback-facing surfaces
// (WSConn.Shutdown, Service.Shutdown) are structurally limited to scopeCallback
// and can never reach wg.Wait. Only activation-level inverses use scopeInverse.
type closeScope int

const (
	scopeCallback closeScope = iota // initiate shutdown only; safe in a callback
	scopeInverse                    // initiate AND join; inverses only
)

// wsConn wraps gorilla's connection so Close is idempotent and the handshake
// request stays reachable for path-parameter lookup. Gorilla still performs
// the actual handshake, framing and close handshake.
type wsConn struct {
	*websocket.Conn
	req  *http.Request
	reg  *wsRegistration
	once sync.Once
}

func (c *wsConn) Request() *http.Request { return c.req }

func (c *wsConn) Close() error {
	var err error
	c.once.Do(func() { err = c.Conn.Close() })
	return err
}

// Shutdown is the callback-safe disposal path. It only initiates shutdown of
// this connection's registration; it never joins, so a callback can call it
// without waiting for its own goroutine to finish.
func (c *wsConn) Shutdown() error {
	if c.reg != nil {
		c.reg.shutdown()
	}
	return nil
}

// shutdown sends a normal close frame before closing the socket, so a client
// observes a clean closure. WriteControl is safe to call concurrently with the
// callback's reads/writes (gorilla guarantees this).
func (c *wsConn) shutdown() {
	_ = c.Conn.WriteControl(
		websocket.CloseMessage,
		websocket.FormatCloseMessage(websocket.CloseNormalClosure, "server closed"),
		time.Now().Add(wsWriteWait),
	)
	_ = c.Close()
}

// wsRegistration is the per-WS-route owner of every live connection it
// accepted. shutdown is non-joining (safe inside a callback); close with
// scopeInverse additionally joins all handler goroutines.
type wsRegistration struct {
	cb     func(*WSConn) error
	mu     sync.Mutex
	closed bool
	conns  map[*wsConn]struct{}
	wg     sync.WaitGroup
	once   sync.Once
}

func newWSRegistration(cb func(*WSConn) error) *wsRegistration {
	return &wsRegistration{cb: cb, conns: make(map[*wsConn]struct{})}
}

// track registers a new connection. It returns false once the registration is
// closed, so no connection can be added after (or race with) a close/join.
func (g *wsRegistration) track(c *wsConn) bool {
	g.mu.Lock()
	defer g.mu.Unlock()
	if g.closed {
		return false
	}
	g.conns[c] = struct{}{}
	g.wg.Add(1)
	return true
}

func (g *wsRegistration) untrack(c *wsConn) {
	g.mu.Lock()
	delete(g.conns, c)
	g.mu.Unlock()
	g.wg.Done()
}

// shutdown is the non-joining half: mark closed and close every socket, then
// return immediately. Safe from a callback because wg.Wait is never reached.
func (g *wsRegistration) shutdown() {
	g.once.Do(func() {
		g.mu.Lock()
		g.closed = true
		conns := make([]*wsConn, 0, len(g.conns))
		for c := range g.conns {
			conns = append(conns, c)
		}
		g.mu.Unlock()

		for _, c := range conns {
			c.shutdown()
		}
	})
}

// join waits for handler goroutines and is reachable ONLY via scopeInverse.
func (g *wsRegistration) join() {
	g.wg.Wait()
}

func (g *wsRegistration) close(scope closeScope) {
	g.shutdown()
	if scope == scopeInverse {
		g.join() // no handler goroutine outlives the inverse
	}
}

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
	return r.register(strings.ToUpper(method), pattern, h, nil)
}

func (r *router) All(pattern string, h Handler) (func() error, error) {
	return r.register("", pattern, h, nil)
}

// WS registers a WebSocket upgrade handler on a ServeMux pattern. It shares the
// Handle/All registry, router and middleware model; the pattern is compiled by
// the standard library, so "/ws/{id}" works and exact-method HTTP routes still
// win. The returned disposer removes only this route, then closes and joins all
// connections accepted by it.
func (r *router) WS(pattern string, cb func(*WSConn) error) (func() error, error) {
	if cb == nil {
		return nil, errors.New("server: nil websocket callback")
	}
	reg := newWSRegistration(cb)
	handler := func(w http.ResponseWriter, req *http.Request) error {
		r.serveWS(w, req, reg)
		return nil
	}
	return r.register("", pattern, handler, reg)
}

// register is the shared, id-keyed registration path for Handle, All and WS.
// Every change rebuilds the mux from the current list. A disposer removes
// exactly the registration it was returned for, idempotently.
func (r *router) register(method, pattern string, h Handler, wsReg *wsRegistration) (func() error, error) {
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
	if wsReg != nil {
		r.wsRegs[wsReg] = struct{}{}
	}
	return func() error {
		r.mu.Lock()
		for i, existing := range r.routes {
			if existing.id == id {
				remaining := append(append([]*route(nil), r.routes[:i]...), r.routes[i+1:]...)
				rebuilt, err := buildMux(remaining)
				if err != nil {
					r.mu.Unlock()
					return err
				}
				r.routes, r.mux = remaining, rebuilt
				break
			}
		}
		if wsReg != nil {
			delete(r.wsRegs, wsReg)
		}
		r.mu.Unlock()
		if wsReg != nil {
			// Joining inverse (outside r.mu): user callbacks never run under a
			// lock. A callback must not invoke this disposer — use Shutdown.
			wsReg.close(scopeInverse)
		}
		return nil
	}, nil
}

// SetCheckOrigin overrides the WebSocket origin policy for this service. A nil
// fn restores the default, which accepts only same-origin requests (and clients
// that send no Origin, e.g. native WebSocket clients).
func (r *router) SetCheckOrigin(fn func(*http.Request) bool) {
	r.mu.Lock()
	r.checkOrigin = fn
	r.mu.Unlock()
}

func (r *router) checkSameOrigin(req *http.Request) bool {
	r.mu.RLock()
	fn := r.checkOrigin
	r.mu.RUnlock()
	if fn != nil {
		return fn(req)
	}
	return sameOrigin(req)
}

// sameOrigin is the safe default: an absent Origin (native clients) is allowed,
// otherwise the Origin host must equal the request host. Rejecting a foreign
// origin prevents a browser page from hijacking a user's socket (CSWSH).
func sameOrigin(req *http.Request) bool {
	origin := req.Header.Get("Origin")
	if origin == "" {
		return true
	}
	u, err := url.Parse(origin)
	if err != nil || u.Host == "" {
		return false
	}
	return strings.EqualFold(u.Host, req.Host)
}

// serveWS performs the gorilla handshake, tracks the connection for the
// lifetime of its registration, and runs the user callback with NO lock held.
// A callback error or panic is contained: it only ends that one connection.
func (r *router) serveWS(w http.ResponseWriter, req *http.Request, reg *wsRegistration) {
	conn, err := r.upgrader.Upgrade(w, req, nil)
	if err != nil {
		return // gorilla has already written the rejection (400/403)
	}
	c := &wsConn{Conn: conn, req: req, reg: reg}
	if !reg.track(c) {
		_ = c.Close() // registration closed during the handshake
		return
	}
	defer func() {
		_ = c.Close()
		reg.untrack(c)
	}()
	defer func() {
		if recover() != nil {
			// Contained on purpose: a user callback must never crash the server.
		}
	}()

	var iface WSConn = c
	_ = reg.cb(&iface)
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

// AddRouteCheck registers a dispatch-time veto without touching route or
// middleware registrations. Like Use it is id-keyed and reversible: the
// disposer removes only this check, and repeated disposal is a no-op.
func (r *router) AddRouteCheck(check RouteCheck) (func() error, error) {
	if check == nil {
		return nil, errors.New("server: nil route check")
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.closed {
		return nil, errServiceClosed
	}
	r.nextID++
	id := r.nextID
	r.checks = append(r.checks, &routeCheckReg{id: id, fn: check})
	return func() error {
		r.mu.Lock()
		defer r.mu.Unlock()
		for i, existing := range r.checks {
			if existing.id == id {
				r.checks = append(r.checks[:i], r.checks[i+1:]...)
				break
			}
		}
		return nil
	}, nil
}

// checksAllow applies every registered check to one candidate route. A single
// veto removes that route from consideration for this request.
func checksAllow(req *http.Request, pattern string, checks []RouteCheck) bool {
	for _, check := range checks {
		if !check(req, pattern) {
			return false
		}
	}
	return true
}

// shutdown is the non-joining half of service disposal. It removes every
// registration and closes every socket but never waits for handlers, so it is
// safe to call from a WS callback (Service.Shutdown is the public entry).
func (r *router) shutdown() {
	r.closeOnce.Do(func() {
		r.mu.Lock()
		r.closed = true
		r.routes = nil
		r.middlewares = nil
		r.checks = nil
		r.mux = http.NewServeMux()
		r.closingRegs = make([]*wsRegistration, 0, len(r.wsRegs))
		for reg := range r.wsRegs {
			r.closingRegs = append(r.closingRegs, reg)
		}
		r.wsRegs = nil
		r.mu.Unlock()

		// Outside r.mu; a callback may be among the callers here, so do NOT join.
		for _, reg := range r.closingRegs {
			reg.shutdown()
		}
	})
}

func (r *router) close(scope closeScope) {
	r.shutdown()
	if scope == scopeInverse {
		r.joinOnce.Do(func() {
			// Activation-level inverse: join every handler before closeDone is
			// signalled, so no socket/goroutine survives Gone.
			for _, reg := range r.closingRegs {
				reg.join()
			}
			close(r.closeDone)
		})
		<-r.closeDone
	}
}

func (r *router) Close() error { r.close(scopeInverse); return nil }

func (r *router) Shutdown() error { r.close(scopeCallback); return nil }

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
	checks := make([]RouteCheck, len(r.checks))
	for i, c := range r.checks {
		checks[i] = c.fn
	}
	r.mu.RUnlock()

	// Snapshot taken under the lock; user middleware/handlers run with NO lock
	// held (no broad lock across user code).
	var h http.Handler = http.HandlerFunc(func(w http.ResponseWriter, req *http.Request) {
		r.dispatch(w, req, mux, routes, checks)
	})
	for i := len(chain) - 1; i >= 0; i-- {
		h = chain[i](h)
	}
	h.ServeHTTP(w, req)
}

func (r *router) dispatch(w http.ResponseWriter, req *http.Request, mux *http.ServeMux, routes []*route, checks []RouteCheck) {
	// A veto removes exactly one route from consideration for this request,
	// leaving other matching routes free to answer. Rebuilding the mux from the
	// surviving registrations keeps all path/method matching in the standard
	// library: a sole vetoed route yields the mux's own 404, never a middleware
	// 403 or a method-mismatch 405. With no vetoes the original mux is reused.
	if len(checks) > 0 {
		filtered := make([]*route, 0, len(routes))
		vetoed := false
		for _, rt := range routes {
			key := rt.pattern
			if rt.method != "" {
				key = rt.method + " " + rt.pattern
			}
			if checksAllow(req, key, checks) {
				filtered = append(filtered, rt)
			} else {
				vetoed = true
			}
		}
		if vetoed {
			rebuilt, err := buildMux(filtered)
			if err != nil {
				http.Error(w, "Internal Server Error", http.StatusInternalServerError)
				return
			}
			mux, routes = rebuilt, filtered
		}
	}
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
		allow := allowedMethods(req, mux, routes, checks)
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
func allowedMethods(req *http.Request, mux *http.ServeMux, routes []*route, checks []RouteCheck) string {
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
		if !checksAllow(req, want, checks) {
			continue // vetoed route: does not contribute an Allow method
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

// Hijack forwards the underlying connection so gorilla/websocket can upgrade a
// request even though runHandler wraps the ResponseWriter. Without this the
// Upgrader would see a writer without http.Hijacker and reject the upgrade.
func (cw *captureWriter) Hijack() (net.Conn, *bufio.ReadWriter, error) {
	h, ok := cw.ResponseWriter.(http.Hijacker)
	if !ok {
		return nil, nil, errors.New("server: response writer does not support hijacking")
	}
	return h.Hijack()
}

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
