// Package http provides a small net/http-based client as a Runtime-managed
// capability. Interceptors run in registration order through a next-chain,
// outside every service lock; the service owns its transport so disposal can
// close idle connections as a reversible effect.
//
// Proxies are identity-based registrations (mirroring the JS
// ctx.http.proxy([schemes], factory)): unregistering one contribution restores
// the previously winning one for its schemes instead of clearing a global.
// The service also owns every in-flight request so Close behaves like an HTTP
// server shutdown: stop new requests, cancel active ones, wait (bounded), then
// release all transports.
package http

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net"
	stdhttp "net/http"
	"net/url"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"dynamic-runtime/runtime"
	h12socks "h12.io/socks"
)

// Request is the transport-agnostic request passed through the interceptor
// chain. Interceptors may mutate it; Header and Body are cloned at Do entry so
// a caller's Request is never modified.
type Request struct {
	Method  string
	URL     string
	Header  stdhttp.Header
	Body    []byte
	Timeout time.Duration
	// ExpectStatus validates the response status. Nil means 2xx is accepted.
	ExpectStatus func(int) bool
}

// Response is the materialized response (the body has already been read and
// the underlying net/http body closed).
type Response struct {
	StatusCode int
	Header     stdhttp.Header
	Body       []byte
}

// Interceptor is one link in a next-chain. It may short-circuit by returning
// without calling next, or mutate req before delegating.
type Interceptor func(ctx context.Context, req *Request, next func(context.Context, *Request) (*Response, error)) (*Response, error)

// ProxyDialer dials a target through a proxy. Its signature matches
// net.Dialer.DialContext so it can be installed on http.Transport.DialContext.
type ProxyDialer func(ctx context.Context, network, addr string) (net.Conn, error)

// Service is the HTTP capability consumed by components.
type Service interface {
	Do(context.Context, Request) (*Response, error)
	Get(context.Context, string) (*Response, error)
	Post(context.Context, string, []byte) (*Response, error)
	AddInterceptor(Interceptor) (func() error, error)
	// RegisterProxy registers proxyURL for the given request URL schemes
	// (e.g. "http", "https"); an empty list means every scheme. The latest
	// registration wins per scheme. The returned inverse removes ONLY this
	// registration, restoring the previously winning one for its schemes.
	RegisterProxy(schemes []string, proxyURL string) (func() error, error)
	// RegisterProxyDialer is RegisterProxy with a caller-built dial function.
	// It is useful for proxy protocols a URL alone cannot express in net/http.
	RegisterProxyDialer(schemes []string, dial ProxyDialer) (func() error, error)
	// SetProxy is a compatibility wrapper backed by a sentinel (all-schemes)
	// registration. Use RegisterProxy for composable contributions.
	SetProxy(string) error
	// Close stops new requests, cancels active ones, waits (bounded) and
	// releases all transports. It is idempotent.
	Close() error
}

// ServiceKey is the capability identity of the HTTP service.
var ServiceKey = runtime.NewKey[Service]("extension.http.service")

var (
	// ErrClosed is returned by Do/registration methods after Close.
	ErrClosed = errors.New("http service closed")
	// errClosed is kept as an internal alias for readability.
	errClosed = ErrClosed

	// ErrTimeout wraps deadline/cancellation failures from Do. Use errors.Is.
	ErrTimeout = errors.New("http: request timeout")
	// ErrStatus wraps a rejected response status. Use errors.Is together with
	// errors.As for the accompanying *StatusError.
	ErrStatus = errors.New("http: unexpected status")
)

// StatusError carries the status code rejected by Request.ExpectStatus.
type StatusError struct {
	Code int
}

func (e *StatusError) Error() string {
	return fmt.Sprintf("http: unexpected status %d", e.Code)
}

type interceptorEntry struct {
	id uint64
	fn Interceptor
}

// proxyRegistration is one identity-bearing proxy contribution.
type proxyRegistration struct {
	id       uint64
	schemes  map[string]struct{} // nil/empty means all schemes
	proxyURL *url.URL            // non-nil for URL-based proxies (http/https/socks5)
	dial     ProxyDialer         // non-nil for dialer-based proxies (e.g. socks4)
}

type proxyDialerKey struct{}

type service struct {
	mu           sync.RWMutex
	closed       bool
	interceptors []interceptorEntry
	nextID       uint64
	proxies      []proxyRegistration // in registration order; last wins
	sentinelID   uint64              // SetProxy's registration, 0 when absent
	transport    *stdhttp.Transport
	// retired holds every transport replaced by a proxy change. A request using
	// an older transport may still be in flight, so each one stays reachable
	// and is closed on every swap and again on service disposal.
	retired      []*stdhttp.Transport
	ctx          context.Context
	cancel       context.CancelFunc
	wg           sync.WaitGroup
	active       atomic.Int64
	closeTimeout time.Duration
	closeOnce    sync.Once
	closeDone    chan struct{}
}

// DefaultCloseTimeout bounds the graceful wait in Close.
const DefaultCloseTimeout = 5 * time.Second

func newService() *service {
	ctx, cancel := context.WithCancel(context.Background())
	s := &service{
		ctx:          ctx,
		cancel:       cancel,
		closeTimeout: DefaultCloseTimeout,
		closeDone:    make(chan struct{}),
	}
	s.transport = s.buildTransport(nil)
	return s
}

func normalizeScheme(s string) string {
	return strings.ToLower(strings.TrimSuffix(strings.TrimSpace(s), ":"))
}

// winningRegistration returns the most recently registered proxy matching the
// request scheme. The caller must hold s.mu (or operate on a stable snapshot).
func winningRegistration(regs []proxyRegistration, scheme string) *proxyRegistration {
	for i := len(regs) - 1; i >= 0; i-- {
		reg := &regs[i]
		if len(reg.schemes) == 0 {
			return reg
		}
		if _, ok := reg.schemes[scheme]; ok {
			return reg
		}
	}
	return nil
}

// buildTransport creates a transport whose proxy decision is frozen from a
// snapshot of registrations. A dialer-based winner is carried per request via
// a context value (set in Do), keeping connection pooling coherent because a
// new transport is built on every registration change.
func (s *service) buildTransport(snapshot []proxyRegistration) *stdhttp.Transport {
	tr := stdhttp.DefaultTransport.(*stdhttp.Transport).Clone()
	baseDial := tr.DialContext
	tr.DialContext = func(ctx context.Context, network, addr string) (net.Conn, error) {
		if d, ok := ctx.Value(proxyDialerKey{}).(ProxyDialer); ok && d != nil {
			return d(ctx, network, addr)
		}
		if baseDial != nil {
			return baseDial(ctx, network, addr)
		}
		return (&net.Dialer{}).DialContext(ctx, network, addr)
	}
	tr.Proxy = func(req *stdhttp.Request) (*url.URL, error) {
		scheme := ""
		if req.URL != nil {
			scheme = normalizeScheme(req.URL.Scheme)
		}
		reg := winningRegistration(snapshot, scheme)
		if reg == nil || reg.dial != nil {
			// A dialer-based winner is applied through DialContext; returning
			// its URL would ask net/http to dial a protocol it does not know
			// (e.g. socks4://).
			return nil, nil
		}
		return reg.proxyURL, nil
	}
	return tr
}

// rebuildTransportLocked retires the current transport and rebuilds it from
// the current registrations. Caller holds s.mu.
func (s *service) rebuildTransportLocked() {
	snapshot := make([]proxyRegistration, len(s.proxies))
	copy(snapshot, s.proxies)
	old := s.transport
	s.transport = s.buildTransport(snapshot)
	if old != nil {
		s.retired = append(s.retired, old)
	}
	all := make([]*stdhttp.Transport, 0, len(s.retired))
	all = append(all, s.retired...)
	for _, tr := range all {
		tr.CloseIdleConnections()
	}
}

func (s *service) Do(ctx context.Context, req Request) (*Response, error) {
	r := req
	if r.Header == nil {
		r.Header = make(stdhttp.Header)
	} else {
		r.Header = r.Header.Clone()
	}
	if r.Method == "" {
		r.Method = stdhttp.MethodGet
	}
	scheme := ""
	if u, err := url.Parse(r.URL); err == nil && u != nil {
		scheme = normalizeScheme(u.Scheme)
	}

	s.mu.RLock()
	if s.closed {
		s.mu.RUnlock()
		return nil, ErrClosed
	}
	// Add before releasing the read lock; Close sets closed under the write
	// lock before it waits, so no Add can race with Wait.
	s.wg.Add(1)
	s.active.Add(1)
	tr := s.transport
	chain := make([]Interceptor, len(s.interceptors))
	for i, e := range s.interceptors {
		chain[i] = e.fn
	}
	var dial ProxyDialer
	if reg := winningRegistration(s.proxies, scheme); reg != nil {
		dial = reg.dial
	}
	s.mu.RUnlock()
	defer func() {
		s.active.Add(-1)
		s.wg.Done()
	}()

	// Derive the call context from both the caller and the service lifetime so
	// Close can cancel requests that are still in flight.
	callCtx := ctx
	if s.cancel != nil {
		var cancel context.CancelFunc
		callCtx, cancel = context.WithCancel(ctx)
		defer cancel()
		stop := context.AfterFunc(s.ctx, cancel)
		defer stop()
	}
	if r.Timeout > 0 {
		var cancelTimeout context.CancelFunc
		callCtx, cancelTimeout = context.WithTimeout(callCtx, r.Timeout)
		defer cancelTimeout()
	}
	if dial != nil {
		callCtx = context.WithValue(callCtx, proxyDialerKey{}, dial)
	}

	next := func(c context.Context, rr *Request) (*Response, error) {
		return s.roundTrip(c, tr, rr)
	}
	for i := len(chain) - 1; i >= 0; i-- {
		ic := chain[i]
		inner := next
		next = func(c context.Context, rr *Request) (*Response, error) {
			return ic(c, rr, inner)
		}
	}
	resp, err := next(callCtx, &r)
	if err != nil {
		if errors.Is(err, context.DeadlineExceeded) || errors.Is(callCtx.Err(), context.DeadlineExceeded) {
			return resp, fmt.Errorf("%w: %w", ErrTimeout, err)
		}
		return resp, err
	}
	if resp == nil {
		return nil, nil
	}
	check := r.ExpectStatus
	if check == nil {
		check = func(code int) bool { return code >= 200 && code < 300 }
	}
	if !check(resp.StatusCode) {
		return resp, fmt.Errorf("%w: %w", ErrStatus, &StatusError{Code: resp.StatusCode})
	}
	return resp, nil
}

func (s *service) roundTrip(ctx context.Context, tr *stdhttp.Transport, req *Request) (*Response, error) {
	var body io.Reader
	if len(req.Body) > 0 {
		body = bytes.NewReader(req.Body)
	}
	hr, err := stdhttp.NewRequestWithContext(ctx, req.Method, req.URL, body)
	if err != nil {
		return nil, err
	}
	if req.Header != nil {
		hr.Header = req.Header.Clone()
	}
	client := &stdhttp.Client{Transport: tr}
	resp, err := client.Do(hr)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()
	data, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}
	return &Response{StatusCode: resp.StatusCode, Header: resp.Header.Clone(), Body: data}, nil
}

func (s *service) Get(ctx context.Context, url string) (*Response, error) {
	return s.Do(ctx, Request{Method: stdhttp.MethodGet, URL: url})
}

func (s *service) Post(ctx context.Context, url string, body []byte) (*Response, error) {
	return s.Do(ctx, Request{Method: stdhttp.MethodPost, URL: url, Body: body})
}

func (s *service) AddInterceptor(ic Interceptor) (func() error, error) {
	if ic == nil {
		return nil, errors.New("http: nil interceptor")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil, errClosed
	}
	s.nextID++
	id := s.nextID
	s.interceptors = append(s.interceptors, interceptorEntry{id: id, fn: ic})

	var once sync.Once
	return func() error {
		once.Do(func() {
			s.mu.Lock()
			for i, e := range s.interceptors {
				if e.id == id {
					s.interceptors = append(s.interceptors[:i], s.interceptors[i+1:]...)
					break
				}
			}
			s.mu.Unlock()
		})
		return nil
	}, nil
}

func parseProxyURL(raw string) (*url.URL, error) {
	u, err := url.Parse(raw)
	if err != nil {
		return nil, fmt.Errorf("http: invalid proxy URL %q: %w", raw, err)
	}
	if u.Scheme == "" || u.Host == "" {
		return nil, fmt.Errorf("http: invalid proxy URL %q", raw)
	}
	return u, nil
}

// NewSOCKSProxyDialer builds a h12.io/socks-backed dialer for a SOCKS proxy
// URL. socks and socks5h are mapped to socks5; socks4 and socks4a select their
// respective h12.io/socks protocols. Non-SOCKS or malformed URLs are rejected.
func NewSOCKSProxyDialer(raw string) (ProxyDialer, error) {
	u, err := parseProxyURL(raw)
	if err != nil {
		return nil, err
	}
	switch u.Scheme {
	case "socks", "socks4", "socks4a", "socks5", "socks5h":
	default:
		return nil, fmt.Errorf("http: unsupported SOCKS proxy scheme %q", u.Scheme)
	}
	if u.User != nil {
		user := u.User.Username()
		pass, _ := u.User.Password()
		if (user == "") != (pass == "") || len(user) > 255 || len(pass) > 255 {
			return nil, fmt.Errorf("http: invalid proxy credentials in %q", raw)
		}
	}
	if t := u.Query().Get("timeout"); t != "" {
		if _, err := time.ParseDuration(t); err != nil {
			return nil, fmt.Errorf("http: invalid proxy timeout in %q: %w", raw, err)
		}
	}
	if u.Scheme == "socks" || u.Scheme == "socks5h" {
		u.Scheme = "socks5"
	}
	d := h12socks.Dial(u.String())
	return func(ctx context.Context, network, addr string) (net.Conn, error) {
		if err := ctx.Err(); err != nil {
			return nil, err
		}
		return d(network, addr)
	}, nil
}

func (s *service) registerProxy(schemes []string, u *url.URL, dial ProxyDialer) (func() error, error) {
	var set map[string]struct{}
	if len(schemes) > 0 {
		set = make(map[string]struct{}, len(schemes))
	}
	for _, sc := range schemes {
		n := normalizeScheme(sc)
		if n == "" || n == "*" {
			set = nil // wildcard
			break
		}
		set[n] = struct{}{}
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil, errClosed
	}
	s.nextID++
	id := s.nextID
	s.proxies = append(s.proxies, proxyRegistration{id: id, schemes: set, proxyURL: u, dial: dial})
	s.rebuildTransportLocked()

	var once sync.Once
	return func() error {
		once.Do(func() {
			s.mu.Lock()
			for i, reg := range s.proxies {
				if reg.id == id {
					s.proxies = append(s.proxies[:i], s.proxies[i+1:]...)
					if id == s.sentinelID {
						s.sentinelID = 0
					}
					s.rebuildTransportLocked()
					break
				}
			}
			s.mu.Unlock()
		})
		return nil
	}, nil
}

func (s *service) RegisterProxy(schemes []string, proxyURL string) (func() error, error) {
	u, err := parseProxyURL(proxyURL)
	if err != nil {
		return nil, err
	}
	if u.Scheme == "socks" {
		u.Scheme = "socks5" // bare alias; net/http understands socks5 URLs
	}
	var dial ProxyDialer
	switch u.Scheme {
	case "http", "https", "socks5", "socks5h":
		// net/http dials these directly from the proxy URL.
	case "socks4", "socks4a":
		dial, err = NewSOCKSProxyDialer(proxyURL)
		if err != nil {
			return nil, err
		}
	default:
		return nil, fmt.Errorf("http: unsupported proxy scheme %q", u.Scheme)
	}
	return s.registerProxy(schemes, u, dial)
}

func (s *service) RegisterProxyDialer(schemes []string, dial ProxyDialer) (func() error, error) {
	if dial == nil {
		return nil, errors.New("http: nil proxy dialer")
	}
	return s.registerProxy(schemes, nil, dial)
}

// removeRegistrationLocked removes a registration by id without rebuilding the
// transport (callers do that once per logical change).
func (s *service) removeRegistrationLocked(id uint64) {
	for i, reg := range s.proxies {
		if reg.id == id {
			s.proxies = append(s.proxies[:i], s.proxies[i+1:]...)
			return
		}
	}
}

// SetProxy keeps the legacy API working as a sentinel registration with no
// scheme restriction. Disposing it removes only the sentinel, never a proxy
// installed by another component via RegisterProxy.
func (s *service) SetProxy(raw string) error {
	if raw == "" {
		s.mu.Lock()
		defer s.mu.Unlock()
		if s.closed {
			return errClosed
		}
		if s.sentinelID != 0 {
			s.removeRegistrationLocked(s.sentinelID)
			s.sentinelID = 0
			s.rebuildTransportLocked()
		}
		return nil
	}

	u, err := parseProxyURL(raw)
	if err != nil {
		return err
	}
	if u.Scheme == "socks" {
		u.Scheme = "socks5"
	}
	var dial ProxyDialer
	switch u.Scheme {
	case "http", "https", "socks5", "socks5h":
	case "socks4", "socks4a":
		dial, err = NewSOCKSProxyDialer(raw)
		if err != nil {
			return err
		}
	default:
		return fmt.Errorf("http: unsupported proxy scheme %q", u.Scheme)
	}

	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return errClosed
	}
	if s.sentinelID != 0 {
		s.removeRegistrationLocked(s.sentinelID)
	}
	s.nextID++
	id := s.nextID
	s.sentinelID = id
	s.proxies = append(s.proxies, proxyRegistration{id: id, proxyURL: u, dial: dial})
	s.rebuildTransportLocked()
	return nil
}

// Close stops new requests, cancels active ones through the service context,
// waits up to the configured timeout, and finally closes every transport
// (including retired ones). It is idempotent and safe for concurrent callers.
func (s *service) Close() error {
	s.closeOnce.Do(func() {
		s.mu.Lock()
		s.closed = true
		s.interceptors = nil
		s.cancel()
		s.mu.Unlock()

		done := make(chan struct{})
		go func() {
			s.wg.Wait()
			close(done)
		}()
		select {
		case <-done:
		case <-time.After(s.closeTimeout):
		}

		s.mu.Lock()
		all := make([]*stdhttp.Transport, 0, len(s.retired)+1)
		if s.transport != nil {
			all = append(all, s.transport)
		}
		all = append(all, s.retired...)
		s.mu.Unlock()
		for _, tr := range all {
			tr.CloseIdleConnections()
		}
		close(s.closeDone)
	})
	<-s.closeDone
	return nil
}

// Config carries component-level HTTP settings.
type Config struct {
	// CloseTimeout bounds the graceful wait for in-flight requests in Close.
	// Zero (or negative) selects DefaultCloseTimeout.
	CloseTimeout time.Duration
}

// Component provides the HTTP service and owns its lifecycle through a
// reversible effect. It holds no lifecycle authority of its own.
type Component struct {
	Config Config
}

func (c *Component) Name() string                 { return "http" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	s := newService()
	if c.Config.CloseTimeout > 0 {
		s.closeTimeout = c.Config.CloseTimeout
	}
	if err := ctx.Effect(func() (func() error, error) {
		return s.Close, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, ServiceKey, Service(s)); err != nil {
		return nil, err
	}
	return nil, nil
}
