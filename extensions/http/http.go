// Package http provides a small net/http-based client as a Runtime-managed
// capability. Interceptors run in registration order through a next-chain,
// outside every service lock; the service owns its transport so disposal can
// close idle connections as a reversible effect.
package http

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	stdhttp "net/http"
	"net/url"
	"sync"
	"time"

	"dynamic-runtime/runtime"
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

// Service is the HTTP capability consumed by components.
type Service interface {
	Do(context.Context, Request) (*Response, error)
	Get(context.Context, string) (*Response, error)
	Post(context.Context, string, []byte) (*Response, error)
	AddInterceptor(Interceptor) (func() error, error)
	SetProxy(string) error
}

// ServiceKey is the capability identity of the HTTP service.
var ServiceKey = runtime.NewKey[Service]("extension.http.service")

var (
	errClosed = errors.New("http service closed")

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

type service struct {
	mu           sync.RWMutex
	closed       bool
	interceptors []interceptorEntry
	nextID       uint64
	transport    *stdhttp.Transport
	// retired holds every transport replaced by SetProxy. A request using an
	// older transport may still be in flight, so each one stays reachable and
	// is closed on every swap and again on service disposal.
	retired []*stdhttp.Transport
}

func newService() *service {
	return &service{transport: stdhttp.DefaultTransport.(*stdhttp.Transport).Clone()}
}

func (s *service) Do(ctx context.Context, req Request) (*Response, error) {
	s.mu.RLock()
	if s.closed {
		s.mu.RUnlock()
		return nil, errClosed
	}
	tr := s.transport
	chain := make([]Interceptor, len(s.interceptors))
	for i, e := range s.interceptors {
		chain[i] = e.fn
	}
	s.mu.RUnlock()

	r := req
	if r.Header == nil {
		r.Header = make(stdhttp.Header)
	} else {
		r.Header = r.Header.Clone()
	}
	if r.Method == "" {
		r.Method = stdhttp.MethodGet
	}

	callCtx := ctx
	cancel := func() {}
	if r.Timeout > 0 {
		callCtx, cancel = context.WithTimeout(ctx, r.Timeout)
	}
	defer cancel()

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
		if errors.Is(err, context.DeadlineExceeded) || callCtx.Err() == context.DeadlineExceeded {
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

func (s *service) SetProxy(raw string) error {
	var proxy func(*stdhttp.Request) (*url.URL, error)
	if raw != "" {
		u, err := url.Parse(raw)
		if err != nil {
			return fmt.Errorf("http: invalid proxy URL %q: %w", raw, err)
		}
		if u.Scheme == "" || u.Host == "" {
			return fmt.Errorf("http: invalid proxy URL %q", raw)
		}
		proxy = stdhttp.ProxyURL(u)
	}

	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return errClosed
	}
	old := s.transport
	next := old.Clone()
	next.Proxy = proxy
	s.transport = next
	s.retired = append(s.retired, old)
	// Every retired transport may still own idle connections from requests
	// that used it; close all of them, not only the immediately previous one.
	all := make([]*stdhttp.Transport, 0, len(s.retired))
	all = append(all, s.retired...)
	s.mu.Unlock()
	for _, tr := range all {
		tr.CloseIdleConnections()
	}
	return nil
}

func (s *service) Close() error {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return nil
	}
	s.closed = true
	s.interceptors = nil
	all := make([]*stdhttp.Transport, 0, len(s.retired)+1)
	all = append(all, s.transport)
	all = append(all, s.retired...)
	s.mu.Unlock()
	for _, tr := range all {
		tr.CloseIdleConnections()
	}
	return nil
}

// Component provides the HTTP service and owns its lifecycle through a
// reversible effect. It holds no lifecycle authority of its own.
type Component struct{}

func (c *Component) Name() string                 { return "http" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	s := newService()
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
