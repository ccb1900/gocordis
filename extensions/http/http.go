// Package http provides a small net/http-based client as a Runtime-managed
// capability. Interceptors run in registration order through a next-chain,
// outside every service lock; the service owns its transport so disposal can
// close idle connections as a reversible effect.
//
// Proxies are identity-based registrations (mirroring the JS
// ctx.http.proxy([schemes], factory)): unregistering one contribution restores
// the previously winning one for its schemes instead of clearing a global.
//
// Ownership has three distinct completions, deliberately not conflated:
//
//   - Request Completed: Do/DoStream/RoundTrip returned and Active decreased.
//     For a streaming call this happens as soon as response headers arrive.
//   - Stream Closed: the Body returned by DoStream/RoundTrip was Closed; only
//     then is that call's WaitGroup slot released and OpenStreams decreased.
//   - Service Closed: Close stopped new calls, cancelled live ones and waited
//     for all owned work up to CloseTimeout. A timeout is observable through
//     ErrCloseTimeout and State.CloseTimedOut; it is NOT an absolute join.
//
// Close behaves like an HTTP server shutdown: stop new requests, cancel active
// ones, wait (bounded), then release all transports.
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

	"github.com/gorilla/websocket"

	"dynamic-runtime/runtime"
	h12socks "h12.io/socks"
)

// Request is the transport-agnostic request passed through the interceptor
// chain. Interceptors may mutate it; Header is cloned at call entry so a
// caller's Request is never modified.
type Request struct {
	Method  string
	URL     string
	Header  stdhttp.Header
	Body    []byte
	Timeout time.Duration
	// ExpectStatus validates the response status. Nil means 2xx is accepted.
	ExpectStatus func(int) bool

	// BodyReader is an additive streaming alternative to Body: callers can
	// POST/PUT a stream without materializing []byte. If it implements
	// io.Closer the service closes it after the transport finishes reading,
	// under the same request ownership (including Close cancellation). When
	// Body is non-empty it takes precedence and BodyReader is ignored.
	BodyReader io.Reader
	// ContentLength optionally describes BodyReader. Zero lets net/http pick
	// (chunked for an unknown reader); -1 explicitly requests chunked encoding.
	ContentLength int64
	// Host optionally overrides the Host header sent to the peer.
	Host string

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
// Body and must Close it. Closing it releases this call's service-owned slot
// (so Close can finish), its timeout/Close cancellation and its OpenStreams
// count. DoStream/RoundTrip returning only means Request Completed, NOT
// Stream Closed.
type StreamResponse struct {
	StatusCode int
	Header     stdhttp.Header
	Body       io.ReadCloser
	// Uncompressed reports whether the transport decoded a content encoding.
	Uncompressed bool
}

// StreamInterceptor is the stream-aware counterpart of Interceptor. It runs
// in registration order through a next-chain and may short-circuit by
// returning without calling next.
type StreamInterceptor func(ctx context.Context, req *Request, next func(context.Context, *Request) (*StreamResponse, error)) (*StreamResponse, error)

// Interceptor is one link in a next-chain. It may short-circuit by returning
// without calling next, or mutate req before delegating.
//
// Buffering caveat: when DoStream/RoundTrip run an ordinary Interceptor, its
// next-chain is adapted to materialize the response with io.ReadAll before the
// interceptor observes it (the Response type is []byte). Installing even one
// ordinary interceptor therefore defeats streaming for that call. Use
// StreamInterceptor when the body must stay live.
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

// HTTPState is a linearizable, read-only view of the service's ownership.
// Active counts requests that have not yet returned; OpenStreams counts live
// bodies handed out by DoStream/RoundTrip that have not yet been Closed. The
// two are intentionally separate: a streaming call can be Completed while its
// stream is still Open.
type HTTPState struct {
	Closed        bool
	Active        int64
	OpenStreams   int64
	CloseTimeout  time.Duration
	CloseTimedOut bool
}

// StateService is the additive observation contract. It is separate from
// Service so third-party implementations of Service remain source-compatible.
type StateService interface {
	Service
	State() HTTPState
}

// RoundTripService is the additive transport-shaped contract. RoundTrip takes
// a stdlib request and returns a LIVE response body, but it is still owned by
// this service: the same WaitGroup, active count, cancellation, interceptor
// chains and identity proxy routing apply. The raw transport is never exposed.
// Closing the returned Body is what releases the call's ownership slot.
type RoundTripService interface {
	Service
	RoundTrip(ctx context.Context, req *stdhttp.Request) (*stdhttp.Response, error)
}

// MethodService is the additive full method-family contract. The base Service
// already carries Do/Get/Post; embedding it keeps those signatures identical
// while adding the remaining verbs. It is deliberately separate from Service
// so existing fake implementers of Service are not broken.
type MethodService interface {
	Service
	Put(context.Context, string, []byte) (*Response, error)
	Patch(context.Context, string, []byte) (*Response, error)
	Delete(context.Context, string) (*Response, error)
	Head(context.Context, string) (*Response, error)
}

// WSClient is the caller-facing WebSocket connection. Message type constants
// are the gorilla/websocket ones (TextMessage, BinaryMessage, ...).
type WSClient interface {
	ReadMessage() (int, []byte, error)
	WriteMessage(int, []byte) error
	Close() error
}

// WebSocketService is the additive WebSocket contract. Like MethodService it
// embeds the base Service instead of extending it, so it can be type-asserted
// from a Service capability without affecting existing implementers.
type WebSocketService interface {
	Service
	// WS dials a ws/wss URL. The returned client is registered in the owner
	// service's active set: Service.Close closes it and joins its cleanup.
	WS(ctx context.Context, url string, reqHeader stdhttp.Header) (WSClient, error)
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

	// ErrCloseTimeout is returned by Close when the graceful wait expired
	// while requests or open streams were still service-owned. State() reports
	// the timed-out state; no absolute join is claimed in that case.
	ErrCloseTimeout = errors.New("http service close timed out")
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

// lifetimeTracker is a WaitGroup whose wait can be bounded without leaving a
// blocked goroutine behind when the bound expires. It also lets Close observe
// that owned work survived the graceful wait instead of claiming a join.
type lifetimeTracker struct {
	mu   sync.Mutex
	n    int
	idle chan struct{}
}

func newLifetimeTracker() *lifetimeTracker {
	idle := make(chan struct{})
	close(idle)
	return &lifetimeTracker{idle: idle}
}

func (t *lifetimeTracker) Add(delta int) {
	t.mu.Lock()
	prev := t.n
	t.n += delta
	switch {
	case prev == 0 && t.n > 0:
		t.idle = make(chan struct{})
	case prev > 0 && t.n == 0:
		close(t.idle)
	}
	negative := t.n < 0
	t.mu.Unlock()
	if negative {
		panic("http: negative lifetime tracker count")
	}
}

func (t *lifetimeTracker) Done() { t.Add(-1) }

// WaitTimeout reports whether all owned work finished before the deadline.
func (t *lifetimeTracker) WaitTimeout(d time.Duration) bool {
	t.mu.Lock()
	idle := t.idle
	t.mu.Unlock()
	select {
	case <-idle:
		return true
	case <-time.After(d):
		return false
	}
}

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
	retired       []*stdhttp.Transport
	ctx           context.Context
	cancel        context.CancelFunc
	wg            *lifetimeTracker
	active        atomic.Int64
	openStreams   atomic.Int64
	closeTimeout  time.Duration
	closeTimedOut bool  // guarded by mu
	closeErr      error // guarded by mu
	closeOnce     sync.Once
	closeDone     chan struct{}
	// wsClients holds every live WebSocket client. Registrations and removals
	// run under s.mu; Close snapshots this set, closes each client and then
	// waits on s.wg so no socket or watcher goroutine outlives Service.Close.
	wsClients map[*wsClientConn]struct{}
}

// DefaultCloseTimeout bounds the graceful wait in Close.
const DefaultCloseTimeout = 5 * time.Second

func newService() *service {
	ctx, cancel := context.WithCancel(context.Background())
	s := &service{
		ctx:          ctx,
		cancel:       cancel,
		wg:           newLifetimeTracker(),
		closeTimeout: DefaultCloseTimeout,
		closeDone:    make(chan struct{}),
		wsClients:    make(map[*wsClientConn]struct{}),
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

// preparedCall is the single unit of service ownership shared by Do,
// DoStream and RoundTrip. Its slot is added under the same read lock Close
// uses to set `closed`, so no Add can race with Close's bounded wait.
type preparedCall struct {
	s           *service
	req         Request
	callCtx     context.Context
	tr          *stdhttp.Transport
	chain       []Interceptor
	streams     []StreamInterceptor
	dial        ProxyDialer
	cleanups    []func()
	cleanupOnce sync.Once
	activeDone  bool
	slotDone    bool
}

func (s *service) prepare(ctx context.Context, req Request) (*preparedCall, error) {
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
	streams := make([]StreamInterceptor, len(s.streamInterceptors))
	for i, e := range s.streamInterceptors {
		streams[i] = e.fn
	}
	var dial ProxyDialer
	if reg := winningRegistration(s.proxies, scheme); reg != nil {
		dial = reg.dial
	}
	s.mu.RUnlock()

	// Derive the call context from both the caller and the service lifetime so
	// Close can cancel work that is still in flight. Cleanup is deliberately
	// NOT run on return for streaming calls; it follows the Body's Close.
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
	return &preparedCall{
		s: s, req: r, callCtx: callCtx, tr: tr,
		chain: chain, streams: streams, dial: dial, cleanups: cleanups,
	}, nil
}

func (p *preparedCall) cleanup() {
	p.cleanupOnce.Do(func() {
		for _, fn := range p.cleanups {
			fn()
		}
	})
}

// endActive marks Request Completed. It always happens when the initiating
// method returns, independently of a still-open stream body.
func (p *preparedCall) endActive() {
	if !p.activeDone {
		p.activeDone = true
		p.s.active.Add(-1)
	}
}

// releaseSlot marks the end of service ownership for this call. For buffered
// calls it runs on return; for streams it runs from streamBody.Close.
func (p *preparedCall) releaseSlot() {
	if !p.slotDone {
		p.slotDone = true
		p.s.wg.Done()
	}
}

// abort is used when a call fails before a body can be handed to the caller.
func (p *preparedCall) abort() {
	p.endActive()
	p.releaseSlot()
	p.cleanup()
}

// ownStreamBody transfers ownership of the slot to the returned Body. This is
// the ONLY path that releases it for a successful streaming call.
func (p *preparedCall) ownStreamBody(sr *StreamResponse) {
	p.endActive()
	p.s.openStreams.Add(1)
	sr.Body = &streamBody{
		ReadCloser: sr.Body,
		onClose: func() {
			p.s.openStreams.Add(-1)
			p.releaseSlot()
			p.cleanup()
		},
	}
}

func wrapCallError(ctx context.Context, err error) error {
	if err == nil {
		return nil
	}
	if errors.Is(err, context.DeadlineExceeded) || errors.Is(ctx.Err(), context.DeadlineExceeded) {
		return fmt.Errorf("%w: %w", ErrTimeout, err)
	}
	return err
}

func (s *service) Do(ctx context.Context, req Request) (*Response, error) {
	p, err := s.prepare(ctx, req)
	if err != nil {
		return nil, err
	}
	defer p.abort() // buffered call: Request Completed and ownership end together

	next := func(c context.Context, rr *Request) (*Response, error) {
		return s.roundTrip(c, p.tr, rr)
	}
	for i := len(p.chain) - 1; i >= 0; i-- {
		ic := p.chain[i]
		inner := next
		next = func(c context.Context, rr *Request) (*Response, error) {
			return ic(c, rr, inner)
		}
	}
	resp, err := next(p.callCtx, &p.req)
	if err != nil {
		return resp, wrapCallError(p.callCtx, err)
	}
	if resp == nil {
		return nil, nil
	}
	check := p.req.ExpectStatus
	if check == nil {
		check = func(code int) bool { return code >= 200 && code < 300 }
	}
	if !check(resp.StatusCode) {
		return resp, fmt.Errorf("%w: %w", ErrStatus, &StatusError{Code: resp.StatusCode})
	}
	return resp, nil
}

// ownedReader keeps a caller-supplied io.Closer alive until the transport has
// finished reading it. net/http otherwise wraps unknown readers in NopCloser.
type ownedReader struct{ io.Reader }

func (r ownedReader) Close() error {
	if c, ok := r.Reader.(io.Closer); ok {
		return c.Close()
	}
	return nil
}

// newOutboundRequest is the single mapping from the capability's Request to a
// stdlib request, shared by Do, DoStream and RoundTrip.
func (s *service) newOutboundRequest(ctx context.Context, req *Request) (*stdhttp.Request, error) {
	var body io.Reader
	switch {
	case len(req.Body) > 0:
		body = bytes.NewReader(req.Body)
	case req.BodyReader != nil:
		body = ownedReader{Reader: req.BodyReader}
	}
	hr, err := stdhttp.NewRequestWithContext(ctx, req.Method, req.URL, body)
	if err != nil {
		return nil, err
	}
	if req.Header != nil {
		hr.Header = req.Header.Clone()
	}
	if req.Host != "" {
		hr.Host = req.Host
	}
	if body != nil && req.ContentLength != 0 {
		hr.ContentLength = req.ContentLength
	}
	return hr, nil
}

func (s *service) roundTrip(ctx context.Context, tr *stdhttp.Transport, req *Request) (*Response, error) {
	hr, err := s.newOutboundRequest(ctx, req)
	if err != nil {
		return nil, err
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

// executeStream builds the two interceptor chains and runs them. Stream
// interceptors are outermost so they see the LIVE response; ordinary ones are
// adapted through the documented io.ReadAll buffer. DoStream and RoundTrip
// share this function so their ownership and chain semantics cannot diverge.
func (s *service) executeStream(p *preparedCall) (*StreamResponse, error) {
	next := func(c context.Context, rr *Request) (*StreamResponse, error) {
		return s.roundTripStream(c, p.tr, rr)
	}
	// Ordinary interceptors: buffering is explicit (see Interceptor's doc).
	for i := len(p.chain) - 1; i >= 0; i-- {
		ic := p.chain[i]
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
			return &StreamResponse{
				StatusCode: resp.StatusCode, Header: resp.Header,
				Body: io.NopCloser(bytes.NewReader(resp.Body)),
			}, nil
		}
	}
	// Stream interceptors are outermost and run in registration order. They
	// receive the unread body and may stream it without materialization.
	for i := len(p.streams) - 1; i >= 0; i-- {
		si := p.streams[i]
		inner := next
		next = func(c context.Context, rr *Request) (*StreamResponse, error) {
			return si(c, rr, inner)
		}
	}

	sr, err := next(p.callCtx, &p.req)
	if err != nil || sr == nil {
		if sr != nil && sr.Body != nil {
			_ = sr.Body.Close()
		}
		return sr, err
	}
	if sr.Body == nil {
		sr.Body = io.NopCloser(bytes.NewReader(nil))
	}
	return sr, nil
}

// DoStream performs a request and hands the unread body to the caller. Its
// slot remains service-owned until Body.Close: returning only means Request
// Completed, not Stream Closed. Status validation is left to the caller.
func (s *service) DoStream(ctx context.Context, req Request) (*StreamResponse, error) {
	p, err := s.prepare(ctx, req)
	if err != nil {
		return nil, err
	}
	sr, err := s.executeStream(p)
	if err != nil || sr == nil {
		p.abort()
		return sr, wrapCallError(p.callCtx, err)
	}
	p.ownStreamBody(sr)
	return sr, nil
}

// RoundTrip is the transport-shaped, additive counterpart of DoStream. It
// returns a stdlib response whose Body is LIVE and service-owned: closing it
// releases this call's slot, exactly as for DoStream. The underlying transport
// and proxy registrations stay private to the service.
func (s *service) RoundTrip(ctx context.Context, req *stdhttp.Request) (*stdhttp.Response, error) {
	if req == nil {
		return nil, errors.New("http: nil request")
	}
	ir := Request{
		Method:        req.Method,
		Header:        cloneHeader(req.Header),
		BodyReader:    req.Body,
		ContentLength: req.ContentLength,
		Host:          req.Host,
	}
	if ir.Method == "" {
		ir.Method = stdhttp.MethodGet
	}
	if req.URL != nil {
		ir.URL = req.URL.String()
	}
	p, err := s.prepare(ctx, ir)
	if err != nil {
		return nil, err
	}
	sr, err := s.executeStream(p)
	if err != nil || sr == nil {
		p.abort()
		return nil, wrapCallError(p.callCtx, err)
	}
	p.ownStreamBody(sr)
	return &stdhttp.Response{
		Status:        fmt.Sprintf("%d %s", sr.StatusCode, stdhttp.StatusText(sr.StatusCode)),
		StatusCode:    sr.StatusCode,
		Header:        sr.Header,
		Body:          sr.Body,
		ContentLength: -1, // unknown/live: let stdlib peers stream it
		Uncompressed:  sr.Uncompressed,
		Request:       req,
	}, nil
}

func (s *service) roundTripStream(ctx context.Context, tr *stdhttp.Transport, req *Request) (*StreamResponse, error) {
	hr, err := s.newOutboundRequest(ctx, req)
	if err != nil {
		return nil, err
	}
	client := &stdhttp.Client{Transport: tr}
	resp, err := client.Do(hr)
	if err != nil {
		return nil, err
	}
	return &StreamResponse{
		StatusCode: resp.StatusCode, Header: resp.Header.Clone(),
		Body: resp.Body, Uncompressed: resp.Uncompressed,
	}, nil
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

// State observes the single root owner; a view has no lifecycle of its own.
func (e *extendedService) State() HTTPState { return e.root.State() }

// RoundTrip layers the view's base URL and default headers over a cloned
// request before delegating to the same root owner. The caller's request and
// its Body are never mutated.
func (e *extendedService) RoundTrip(ctx context.Context, req *stdhttp.Request) (*stdhttp.Response, error) {
	if req == nil {
		return e.root.RoundTrip(ctx, nil)
	}
	r := req.Clone(req.Context())
	if r.URL != nil {
		if resolved, err := url.Parse(resolveRequestURL(e.baseURL, r.URL.String(), nil)); err == nil {
			r.URL = resolved
		}
	}
	r.Header = mergeHeaders(e.header, r.Header)
	return e.root.RoundTrip(ctx, r)
}

// --- method family + WebSocket (additive r1.md HTTP equivalence) -----------

// Every verb delegates to Do with the same Request shape as Get/Post, so
// interceptors, proxy routing, in-flight tracking and Close ownership are
// identical for all methods.
func (s *service) Put(ctx context.Context, rawURL string, body []byte) (*Response, error) {
	return s.Do(ctx, Request{Method: stdhttp.MethodPut, URL: rawURL, Body: body})
}

func (s *service) Patch(ctx context.Context, rawURL string, body []byte) (*Response, error) {
	return s.Do(ctx, Request{Method: stdhttp.MethodPatch, URL: rawURL, Body: body})
}

func (s *service) Delete(ctx context.Context, rawURL string) (*Response, error) {
	return s.Do(ctx, Request{Method: stdhttp.MethodDelete, URL: rawURL})
}

func (s *service) Head(ctx context.Context, rawURL string) (*Response, error) {
	return s.Do(ctx, Request{Method: stdhttp.MethodHead, URL: rawURL})
}

func (e *extendedService) Put(ctx context.Context, rawURL string, body []byte) (*Response, error) {
	return e.Do(ctx, Request{Method: stdhttp.MethodPut, URL: rawURL, Body: body})
}

func (e *extendedService) Patch(ctx context.Context, rawURL string, body []byte) (*Response, error) {
	return e.Do(ctx, Request{Method: stdhttp.MethodPatch, URL: rawURL, Body: body})
}

func (e *extendedService) Delete(ctx context.Context, rawURL string) (*Response, error) {
	return e.Do(ctx, Request{Method: stdhttp.MethodDelete, URL: rawURL})
}

func (e *extendedService) Head(ctx context.Context, rawURL string) (*Response, error) {
	return e.Do(ctx, Request{Method: stdhttp.MethodHead, URL: rawURL})
}

// wsClientConn adapts a gorilla connection to WSClient. Close is idempotent:
// it deregisters from the owner, closes the socket, signals the ctx watcher
// and releases the service wait-group slot.
type wsClientConn struct {
	conn *websocket.Conn
	s    *service
	done chan struct{}
	once sync.Once
}

func (c *wsClientConn) ReadMessage() (int, []byte, error) { return c.conn.ReadMessage() }
func (c *wsClientConn) WriteMessage(mt int, data []byte) error {
	return c.conn.WriteMessage(mt, data)
}

func (c *wsClientConn) Close() error {
	var err error
	c.once.Do(func() {
		c.s.mu.Lock()
		delete(c.s.wsClients, c)
		c.s.mu.Unlock()
		err = c.conn.Close()
		close(c.done)
		c.s.wg.Done()
	})
	return err
}

// WS dials a ws/wss URL with gorilla/websocket. It is exposed on the additive
// WebSocketService interface, never on Service itself.
func (s *service) WS(ctx context.Context, rawURL string, reqHeader stdhttp.Header) (WSClient, error) {
	return s.ws(ctx, rawURL, reqHeader)
}

func (s *service) ws(ctx context.Context, rawURL string, reqHeader stdhttp.Header) (WSClient, error) {
	u, err := url.Parse(rawURL)
	if err != nil {
		return nil, fmt.Errorf("http: invalid websocket URL %q: %w", rawURL, err)
	}
	scheme := strings.ToLower(u.Scheme)
	if scheme != "ws" && scheme != "wss" {
		return nil, fmt.Errorf("http: websocket URL must use ws or wss scheme, got %q", u.Scheme)
	}
	if u.Host == "" {
		return nil, fmt.Errorf("http: websocket URL %q has no host", rawURL)
	}

	s.mu.RLock()
	closed := s.closed
	var proxyURL *url.URL
	var dial ProxyDialer
	if reg := winningRegistration(s.proxies, scheme); reg != nil {
		proxyURL, dial = reg.proxyURL, reg.dial
	}
	s.mu.RUnlock()
	if closed {
		return nil, ErrClosed
	}

	// Tie the dial to the service lifetime so Close also cancels a dial that
	// has not produced a live client yet. The returned client watches the
	// caller's ctx instead (see below).
	dialCtx := ctx
	if s.cancel != nil {
		var cancel context.CancelFunc
		dialCtx, cancel = context.WithCancel(ctx)
		defer cancel()
		stop := context.AfterFunc(s.ctx, cancel)
		defer stop()
	}

	dialer := websocket.Dialer{}
	if proxyURL != nil && dial == nil {
		dialer.Proxy = func(*stdhttp.Request) (*url.URL, error) { return proxyURL, nil }
	}
	if dial != nil {
		dialer.NetDialContext = func(c context.Context, network, addr string) (net.Conn, error) {
			return dial(c, network, addr)
		}
	}
	conn, resp, err := dialer.DialContext(dialCtx, u.String(), cloneHeader(reqHeader))
	if resp != nil && resp.Body != nil {
		_ = resp.Body.Close()
	}
	if err != nil {
		if ctx.Err() != nil {
			return nil, fmt.Errorf("http: websocket dial: %w", ctx.Err())
		}
		return nil, err
	}

	c := &wsClientConn{conn: conn, s: s, done: make(chan struct{})}
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		_ = conn.Close()
		return nil, ErrClosed
	}
	// One slot for the client (released by c.Close) and one for the ctx
	// watcher goroutine. Add happens under the same lock Close uses to set
	// `closed`, so no Add can race with its Wait.
	s.wg.Add(2)
	s.wsClients[c] = struct{}{}
	s.mu.Unlock()

	go func() {
		defer s.wg.Done()
		select {
		case <-ctx.Done():
			_ = c.Close()
		case <-c.done:
		}
	}()
	return c, nil
}

func (e *extendedService) WS(ctx context.Context, rawURL string, reqHeader stdhttp.Header) (WSClient, error) {
	return e.root.ws(ctx, joinBaseURL(e.baseURL, rawURL), mergeHeaders(e.header, reqHeader))
}

var (
	_ StreamService    = (*service)(nil)
	_ StreamService    = (*extendedService)(nil)
	_ StateService     = (*service)(nil)
	_ StateService     = (*extendedService)(nil)
	_ RoundTripService = (*service)(nil)
	_ RoundTripService = (*extendedService)(nil)
	_ MethodService    = (*service)(nil)
	_ MethodService    = (*extendedService)(nil)
	_ WebSocketService = (*service)(nil)
	_ WebSocketService = (*extendedService)(nil)
	_ WSClient         = (*wsClientConn)(nil)
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

// State is the additive read-only ownership view. It is linearized on the
// service mutex; the atomics for Active/OpenStreams may change immediately
// afterwards, which is expected for an observation snapshot.
func (s *service) State() HTTPState {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return HTTPState{
		Closed:        s.closed,
		Active:        s.active.Load(),
		OpenStreams:   s.openStreams.Load(),
		CloseTimeout:  s.closeTimeout,
		CloseTimedOut: s.closeTimedOut,
	}
}

// Close stops new requests, cancels active ones through the service context,
// waits up to CloseTimeout for every owned slot (requests, open stream bodies
// and WebSockets), then closes every transport including retired ones. When
// the bound expires it reports ErrCloseTimeout and State.CloseTimedOut; work
// that ignores cancellation may outlive Close, so this is not an absolute join.
// It is idempotent and safe for concurrent callers.
func (s *service) Close() error {
	s.closeOnce.Do(func() {
		s.mu.Lock()
		s.closed = true
		s.interceptors = nil
		s.streamInterceptors = nil
		// Snapshot live WebSocket clients; each one is closed (idempotently)
		// below so its socket and ctx watcher terminate before Wait returns.
		clients := make([]*wsClientConn, 0, len(s.wsClients))
		for c := range s.wsClients {
			clients = append(clients, c)
		}
		s.cancel()
		s.mu.Unlock()

		for _, c := range clients {
			_ = c.Close()
		}

		if !s.wg.WaitTimeout(s.closeTimeout) {
			s.mu.Lock()
			s.closeTimedOut = true
			s.closeErr = ErrCloseTimeout
			s.mu.Unlock()
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
	// The timeout is reported to the caller that performed/awaited this close.
	// Later idempotent calls (notably the runtime's disposal effect) observe
	// the same state through State() without re-reporting the same timeout.
	s.mu.Lock()
	err := s.closeErr
	s.closeErr = nil
	s.mu.Unlock()
	return err
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
