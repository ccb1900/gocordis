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
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"mime"
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

	// BaseURL is an optional prefix resolved against URL by Do/DoStream. An
	// absolute URL overrides it. Params are appended to the URL's existing
	// query. Both fields are read-only to the caller: Do/DoStream work on a
	// private copy and never mutate the Request they were given.
	BaseURL string
	Params  url.Values
}

// Response is the materialized response (the body has already been read and
// the underlying net/http body closed).
type Response struct {
	StatusCode int
	Header     stdhttp.Header
	Body       []byte
}

// StreamResponse is a response whose body has NOT been read. The caller owns
// Body and must Close it; closing it also releases the service's per-request
// context (timeout and Close cancellation). The service still tracks the
// DoStream call itself in its in-flight set until DoStream returns.
type StreamResponse struct {
	StatusCode int
	Header     stdhttp.Header
	Body       io.ReadCloser
}

// StreamInterceptor is the stream-aware counterpart of Interceptor. It runs
// in registration order through a next-chain and may short-circuit by
// returning without calling next.
type StreamInterceptor func(ctx context.Context, req *Request, next func(context.Context, *Request) (*StreamResponse, error)) (*StreamResponse, error)

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

// StreamService is the additive streaming/configuration contract. It is kept
// separate from Service on purpose: external code may implement only the
// original Service interface (e.g. test doubles), so growing Service would be
// a breaking change. The concrete service and every Extend view implement both.
type StreamService interface {
	Service
	DoStream(context.Context, Request) (*StreamResponse, error)
	GetStream(context.Context, string) (*StreamResponse, error)
	AddStreamInterceptor(StreamInterceptor) (func() error, error)
	RegisterDecoder(contentType string, fn func(io.Reader, any) error) (func() error, error)
	// Decode uses this service's own decoder registry.
	Decode(*StreamResponse, any) error
	Extend(baseURL string, header stdhttp.Header) StreamService
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

type streamInterceptorEntry struct {
	id uint64
	fn StreamInterceptor
}

// decoderRegistration is an identity-bearing decoder contribution. Like
// proxies, registrations live in an ordered slice: the last matching one wins
// and removing it reveals the previous one for the same content type.
type decoderRegistration struct {
	id          uint64
	contentType string // normalized: lower-cased and trimmed
	fn          func(io.Reader, any) error
}

// builtinJSONDecoder is copied into every service's own decoder registry at
// construction; decoders are per-service state, never process-global.
func builtinJSONDecoder(r io.Reader, out any) error {
	return json.NewDecoder(r).Decode(out)
}

// defaultDecoders is an immutable, built-in-only registry backing the
// package-level Decode compatibility helper. It is never mutated, so it needs
// no lock.
var defaultDecoders = []decoderRegistration{{
	contentType: "application/json",
	fn:          builtinJSONDecoder,
}}

func normalizeContentType(ct string) string {
	return strings.ToLower(strings.TrimSpace(ct))
}

// winningDecoderIn returns the most recently registered decoder in regs whose
// content type equals one of the supplied keys (most specific key first).
func winningDecoderIn(regs []decoderRegistration, keys ...string) func(io.Reader, any) error {
	for _, key := range keys {
		if key == "" {
			continue
		}
		for i := len(regs) - 1; i >= 0; i-- {
			if regs[i].contentType == key {
				return regs[i].fn
			}
		}
	}
	return nil
}

// winningDecoder selects from this service's registry. The scan runs under the
// service lock because removal compacts the slice in place.
func (s *service) winningDecoder(keys ...string) func(io.Reader, any) error {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return winningDecoderIn(s.decoders, keys...)
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
	mu                 sync.RWMutex
	closed             bool
	interceptors       []interceptorEntry
	streamInterceptors []streamInterceptorEntry
	nextID             uint64
	decoders           []decoderRegistration // per-service; in registration order, last wins
	proxies            []proxyRegistration   // in registration order; last wins
	sentinelID         uint64                // SetProxy's registration, 0 when absent
	transport          *stdhttp.Transport
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
	// Every instance gets its own built-in decoder as the first contribution,
	// so removing custom decoders reveals it without affecting other services.
	s.nextID++
	s.decoders = []decoderRegistration{{
		id:          s.nextID,
		contentType: "application/json",
		fn:          builtinJSONDecoder,
	}}
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
	r.URL = resolveRequestURL(r.BaseURL, r.URL, r.Params)
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

// --- additive r1.md HTTP equivalence APIs ---------------------------------

// joinBaseURL prefixes rel with base using exactly one slash. An absolute rel
// is returned unchanged so a request can always opt out of a configured base.
func joinBaseURL(base, rel string) string {
	if base == "" {
		return rel
	}
	if rel == "" {
		return base
	}
	if u, err := url.Parse(rel); err == nil && u.IsAbs() {
		return rel
	}
	return strings.TrimRight(base, "/") + "/" + strings.TrimLeft(rel, "/")
}

// resolveRequestURL applies BaseURL and Params to a private copy of the URL.
// The existing query is preserved and Params are appended; the caller's
// url.Values is never written through.
func resolveRequestURL(baseURL, rawURL string, params url.Values) string {
	resolved := joinBaseURL(baseURL, rawURL)
	if len(params) == 0 {
		return resolved
	}
	u, err := url.Parse(resolved)
	if err != nil {
		return resolved // let net/http report the malformed URL
	}
	q := u.Query()
	for key, values := range params {
		for _, value := range values {
			q.Add(key, value)
		}
	}
	u.RawQuery = q.Encode()
	return u.String()
}

func cloneHeader(h stdhttp.Header) stdhttp.Header {
	out := make(stdhttp.Header)
	for k, vs := range h {
		out[stdhttp.CanonicalHeaderKey(k)] = append([]string(nil), vs...)
	}
	return out
}

// mergeHeaders layers override over base: per-request values win over Extend
// defaults, and child Extend defaults win over parent defaults.
func mergeHeaders(base, override stdhttp.Header) stdhttp.Header {
	out := cloneHeader(base)
	for k, vs := range override {
		out[stdhttp.CanonicalHeaderKey(k)] = append([]string(nil), vs...)
	}
	return out
}

// streamBody ties the per-request context cleanup (timeout cancel, service
// Close cancel and AfterFunc stop) to the lifetime of the returned Body.
type streamBody struct {
	io.ReadCloser
	once    sync.Once
	onClose func()
}

func (b *streamBody) Close() error {
	var err error
	b.once.Do(func() {
		if b.ReadCloser != nil {
			err = b.ReadCloser.Close()
		}
		b.onClose()
	})
	return err
}

// DoStream performs a request but hands the unread body to the caller. It is
// tracked by the service's in-flight set exactly like Do, so Close refuses new
// calls and waits for an in-flight DoStream to return. Status validation is
// intentionally left to the caller: StreamResponse exposes StatusCode.
func (s *service) DoStream(ctx context.Context, req Request) (*StreamResponse, error) {
	r := req
	r.Header = cloneHeader(r.Header)
	if r.Method == "" {
		r.Method = stdhttp.MethodGet
	}
	r.URL = resolveRequestURL(r.BaseURL, r.URL, r.Params)

	scheme := ""
	if u, err := url.Parse(r.URL); err == nil && u != nil {
		scheme = normalizeScheme(u.Scheme)
	}

	s.mu.RLock()
	if s.closed {
		s.mu.RUnlock()
		return nil, ErrClosed
	}
	s.wg.Add(1)
	s.active.Add(1)
	tr := s.transport
	chain := make([]Interceptor, len(s.interceptors))
	for i, e := range s.interceptors {
		chain[i] = e.fn
	}
	streamChain := make([]StreamInterceptor, len(s.streamInterceptors))
	for i, e := range s.streamInterceptors {
		streamChain[i] = e.fn
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

	// Unlike Do, the derived context cannot be cancelled on return: the Body is
	// still streaming. Cleanup is attached to Body.Close instead.
	callCtx := ctx
	var cleanups []func()
	if s.cancel != nil {
		c, cancel := context.WithCancel(ctx)
		callCtx = c
		stop := context.AfterFunc(s.ctx, cancel)
		cleanups = append(cleanups, func() { stop(); cancel() })
	}
	if r.Timeout > 0 {
		c, cancelTimeout := context.WithTimeout(callCtx, r.Timeout)
		callCtx = c
		cleanups = append(cleanups, cancelTimeout)
	}
	if dial != nil {
		callCtx = context.WithValue(callCtx, proxyDialerKey{}, dial)
	}
	var cleanupOnce sync.Once
	cleanup := func() {
		cleanupOnce.Do(func() {
			for _, fn := range cleanups {
				fn()
			}
		})
	}

	next := func(c context.Context, rr *Request) (*StreamResponse, error) {
		return s.roundTripStream(c, tr, rr)
	}
	// Ordinary interceptors apply too. They are adapted through a buffered
	// next-chain, so no materialization happens unless one is actually installed.
	for i := len(chain) - 1; i >= 0; i-- {
		ic := chain[i]
		inner := next
		next = func(c context.Context, rr *Request) (*StreamResponse, error) {
			bufferedNext := func(c2 context.Context, r2 *Request) (*Response, error) {
				sr, err := inner(c2, r2)
				if err != nil || sr == nil {
					return nil, err
				}
				data, err := io.ReadAll(sr.Body)
				_ = sr.Body.Close()
				if err != nil {
					return nil, err
				}
				return &Response{StatusCode: sr.StatusCode, Header: sr.Header, Body: data}, nil
			}
			resp, err := ic(c, rr, bufferedNext)
			if err != nil || resp == nil {
				return nil, err
			}
			return &StreamResponse{StatusCode: resp.StatusCode, Header: resp.Header, Body: io.NopCloser(bytes.NewReader(resp.Body))}, nil
		}
	}
	// Stream interceptors are outermost and run in registration order.
	for i := len(streamChain) - 1; i >= 0; i-- {
		si := streamChain[i]
		inner := next
		next = func(c context.Context, rr *Request) (*StreamResponse, error) {
			return si(c, rr, inner)
		}
	}

	sr, err := next(callCtx, &r)
	if err != nil || sr == nil {
		if sr != nil && sr.Body != nil {
			_ = sr.Body.Close()
		}
		cleanup()
		if err != nil && (errors.Is(err, context.DeadlineExceeded) || errors.Is(callCtx.Err(), context.DeadlineExceeded)) {
			return sr, fmt.Errorf("%w: %w", ErrTimeout, err)
		}
		return sr, err
	}
	if sr.Body == nil {
		sr.Body = io.NopCloser(bytes.NewReader(nil))
	}
	sr.Body = &streamBody{ReadCloser: sr.Body, onClose: cleanup}
	return sr, nil
}

func (s *service) roundTripStream(ctx context.Context, tr *stdhttp.Transport, req *Request) (*StreamResponse, error) {
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
	return &StreamResponse{StatusCode: resp.StatusCode, Header: resp.Header.Clone(), Body: resp.Body}, nil
}

func (s *service) GetStream(ctx context.Context, rawURL string) (*StreamResponse, error) {
	return s.DoStream(ctx, Request{Method: stdhttp.MethodGet, URL: rawURL})
}

func (s *service) AddStreamInterceptor(si StreamInterceptor) (func() error, error) {
	if si == nil {
		return nil, errors.New("http: nil stream interceptor")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil, errClosed
	}
	s.nextID++
	id := s.nextID
	s.streamInterceptors = append(s.streamInterceptors, streamInterceptorEntry{id: id, fn: si})

	var once sync.Once
	return func() error {
		once.Do(func() {
			s.mu.Lock()
			for i, e := range s.streamInterceptors {
				if e.id == id {
					s.streamInterceptors = append(s.streamInterceptors[:i], s.streamInterceptors[i+1:]...)
					break
				}
			}
			s.mu.Unlock()
		})
		return nil
	}, nil
}

// RegisterDecoder adds a decoder for contentType. The contribution is
// identity-bearing: the returned inverse removes only this registration and
// reveals the previously winning decoder for the same content type.
func (s *service) RegisterDecoder(contentType string, fn func(io.Reader, any) error) (func() error, error) {
	if fn == nil {
		return nil, errors.New("http: nil decoder")
	}
	ct := normalizeContentType(contentType)
	if ct == "" {
		return nil, errors.New("http: empty content type")
	}
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return nil, errClosed
	}
	s.nextID++
	id := s.nextID
	s.decoders = append(s.decoders, decoderRegistration{id: id, contentType: ct, fn: fn})

	var once sync.Once
	return func() error {
		once.Do(func() {
			s.mu.Lock()
			for i, reg := range s.decoders {
				if reg.id == id {
					s.decoders = append(s.decoders[:i], s.decoders[i+1:]...)
					break
				}
			}
			s.mu.Unlock()
		})
		return nil
	}, nil
}

type contentTypeKeys struct {
	raw, exact, mediaType string
}

func checkDecodeResponse(resp *StreamResponse) error {
	if resp == nil {
		return errors.New("http: nil stream response")
	}
	if resp.Body == nil {
		return errors.New("http: stream response has no body")
	}
	return nil
}

func contentTypeOf(resp *StreamResponse) contentTypeKeys {
	raw := ""
	if resp.Header != nil {
		raw = strings.TrimSpace(resp.Header.Get("Content-Type"))
	}
	exact := normalizeContentType(raw)
	mediaType := exact
	if raw != "" {
		if parsed, _, err := mime.ParseMediaType(raw); err == nil {
			mediaType = normalizeContentType(parsed)
		} else if i := strings.IndexByte(raw, ';'); i >= 0 {
			mediaType = normalizeContentType(raw[:i])
		}
	}
	return contentTypeKeys{raw: raw, exact: exact, mediaType: mediaType}
}

// consumeDecode closes resp.Body on every path: a decoder owns the stream once
// selected, and an unregistered type must not leak the body either.
func consumeDecode(resp *StreamResponse, out any, raw string, fn func(io.Reader, any) error) error {
	if fn == nil {
		_ = resp.Body.Close()
		return fmt.Errorf("http: no decoder registered for content type %q", raw)
	}
	defer resp.Body.Close()
	return fn(resp.Body, out)
}

// Decode is the package-level compatibility helper: it knows only the built-in
// decoders. For registrations made with RegisterDecoder, use the service-scoped
// StreamService.Decode (or a view derived from that service).
func Decode(resp *StreamResponse, out any) error {
	if err := checkDecodeResponse(resp); err != nil {
		return err
	}
	keys := contentTypeOf(resp)
	return consumeDecode(resp, out, keys.raw, winningDecoderIn(defaultDecoders, keys.exact, keys.mediaType))
}

// Decode consumes resp.Body using this service's registry: the full
// Content-Type is tried first (so a registration may include parameters), then
// the bare media type. Every instance carries its own built-in JSON decoder.
func (s *service) Decode(resp *StreamResponse, out any) error {
	if err := checkDecodeResponse(resp); err != nil {
		return err
	}
	keys := contentTypeOf(resp)
	return consumeDecode(resp, out, keys.raw, s.winningDecoder(keys.exact, keys.mediaType))
}

// Extend returns a view that layers a base URL and default headers over this
// service. It owns no transport, goroutines or lifecycle: calls delegate to the
// root service, and Close on any view closes that single owner.
func (s *service) Extend(baseURL string, header stdhttp.Header) StreamService {
	return &extendedService{root: s, baseURL: baseURL, header: cloneHeader(header)}
}

type extendedService struct {
	root    *service
	baseURL string
	header  stdhttp.Header
}

func (e *extendedService) Extend(baseURL string, header stdhttp.Header) StreamService {
	return &extendedService{
		root:    e.root,
		baseURL: joinBaseURL(e.baseURL, baseURL),
		header:  mergeHeaders(e.header, header),
	}
}

func (e *extendedService) prepare(req Request) Request {
	r := req
	r.Header = mergeHeaders(e.header, req.Header)
	r.BaseURL = joinBaseURL(e.baseURL, req.BaseURL)
	return r
}

func (e *extendedService) Do(ctx context.Context, req Request) (*Response, error) {
	return e.root.Do(ctx, e.prepare(req))
}
func (e *extendedService) DoStream(ctx context.Context, req Request) (*StreamResponse, error) {
	return e.root.DoStream(ctx, e.prepare(req))
}
func (e *extendedService) Get(ctx context.Context, rawURL string) (*Response, error) {
	return e.Do(ctx, Request{Method: stdhttp.MethodGet, URL: rawURL})
}
func (e *extendedService) Post(ctx context.Context, rawURL string, body []byte) (*Response, error) {
	return e.Do(ctx, Request{Method: stdhttp.MethodPost, URL: rawURL, Body: body})
}
func (e *extendedService) GetStream(ctx context.Context, rawURL string) (*StreamResponse, error) {
	return e.DoStream(ctx, Request{Method: stdhttp.MethodGet, URL: rawURL})
}
func (e *extendedService) AddInterceptor(ic Interceptor) (func() error, error) {
	return e.root.AddInterceptor(ic)
}
func (e *extendedService) AddStreamInterceptor(si StreamInterceptor) (func() error, error) {
	return e.root.AddStreamInterceptor(si)
}
func (e *extendedService) RegisterProxy(schemes []string, proxyURL string) (func() error, error) {
	return e.root.RegisterProxy(schemes, proxyURL)
}
func (e *extendedService) RegisterProxyDialer(schemes []string, dial ProxyDialer) (func() error, error) {
	return e.root.RegisterProxyDialer(schemes, dial)
}
func (e *extendedService) RegisterDecoder(contentType string, fn func(io.Reader, any) error) (func() error, error) {
	return e.root.RegisterDecoder(contentType, fn)
}
func (e *extendedService) Decode(resp *StreamResponse, out any) error {
	return e.root.Decode(resp, out)
}
func (e *extendedService) SetProxy(raw string) error { return e.root.SetProxy(raw) }
func (e *extendedService) Close() error              { return e.root.Close() }

var (
	_ StreamService = (*service)(nil)
	_ StreamService = (*extendedService)(nil)
)

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
		s.streamInterceptors = nil
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
