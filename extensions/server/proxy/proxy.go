// Package proxy contributes forwarding routes to a server.Service, using the
// http extension's Service for outbound HTTP requests and gorilla/websocket for
// outbound WebSocket connections.
package proxy

import (
	"fmt"
	"io"
	"net/http"
	"net/url"
	"strings"
	"sync"
	"time"

	"github.com/gorilla/websocket"

	exthttp "dynamic-runtime/extensions/http"
	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

// Component forwards requests registered on Route to the fixed Target while
// Active. Target is treated as a directory: the request's path under Route is
// resolved beneath it, with cross-origin / prefix-escape paths rejected (400).
// The backend's status, headers and body are copied back verbatim; only a
// transport error becomes 502. A WebSocket upgrade on any path is proxied to
// the same target with its scheme changed to ws/wss.
type Component struct {
	Route  string
	Target string
}

// The core router requires a method per registration; cover the standard HTTP
// methods so the forwarded method is the caller's original one.
var methods = []string{
	http.MethodGet, http.MethodPost, http.MethodPut, http.MethodPatch,
	http.MethodDelete, http.MethodHead, http.MethodOptions,
}

// wsRoute mirrors the JS plugin's "{/*path}" catch-all: one WS route covers
// every path. The Go router gives exact-method HTTP routes precedence over a
// method-less WS route on the same path, so upgrades below Route are handled by
// the HTTP forwarder; upgrades elsewhere (and all frame pumping) are fully
// proxied here. This is the one routing-shape difference from JS, where the
// framework dispatches HTTP and WS from the same catch-all by upgrade header.
const wsRoute = "/{path...}"

func (c *Component) Name() string { return "server-proxy" }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(server.ServiceKey),
		runtime.Requires(exthttp.ServiceKey),
	}
}

func (c *Component) Provide() []runtime.Capability { return nil }

// routePatterns yields the exact route plus a trailing catch-all so nested
// paths are resolved under the target directory. A root route needs only the
// catch-all for its subtree.
func routePatterns(route string) []string {
	route = "/" + strings.Trim(route, "/")
	if route == "/" {
		return []string{"/", "/{path...}"}
	}
	return []string{route, route + "/{path...}"}
}

// parseTarget normalizes the configured target into a directory URL: an
// absolute URL is required and its path always ends in exactly one slash, so
// resolving a relative request path cannot replace the target's last segment.
func parseTarget(raw string) (*url.URL, error) {
	u, err := url.Parse(raw)
	if err != nil {
		return nil, fmt.Errorf("proxy: invalid target %q: %w", raw, err)
	}
	if u.Scheme == "" || u.Host == "" {
		return nil, fmt.Errorf("proxy: target must be an absolute URL: %q", raw)
	}
	u.Path = strings.TrimRight(u.Path, "/") + "/"
	u.RawPath = "" // Path changed; let String() re-encode it.
	u.RawQuery = ""
	u.Fragment = ""
	return u, nil
}

// encodeSegment mirrors encodeURIComponent plus the RFC-3986 sub-delimiters
// the JS plugin restores ($&+,;=:@). The router decoded the path, so each
// segment is encoded again before being handed to the URL resolver.
func encodeSegment(segment string) string {
	const hex = "0123456789ABCDEF"
	var b strings.Builder
	for i := 0; i < len(segment); i++ {
		c := segment[i]
		keep := 'a' <= c && c <= 'z' || 'A' <= c && c <= 'Z' || '0' <= c && c <= '9'
		if !keep {
			switch c {
			case '-', '_', '.', '!', '~', '*', '\'', '(', ')',
				'$', '&', '+', ',', ':', ';', '=', '@':
				keep = true
			}
		}
		if keep {
			b.WriteByte(c)
		} else {
			b.WriteByte('%')
			b.WriteByte(hex[c>>4])
			b.WriteByte(hex[c&0xf])
		}
	}
	return b.String()
}

// locate resolves path under target. The "./" prefix keeps an absolute or
// network-path request from changing origin; ".." segments are removed by the
// URL resolver but then fail the containment check, yielding nil (400).
func locate(target *url.URL, path string) *url.URL {
	segments := strings.Split(strings.TrimPrefix(path, "/"), "/")
	for i, s := range segments {
		segments[i] = encodeSegment(s)
	}
	raw := "./" + strings.Join(segments, "/")
	ref, err := url.Parse(raw)
	if err != nil {
		return nil
	}
	// Keep the JS encoding on the wire: Go would otherwise escape !~*'() too.
	ref.RawPath = raw
	resolved := target.ResolveReference(ref)
	if resolved.Scheme != target.Scheme || resolved.Host != target.Host ||
		!strings.HasPrefix(resolved.Path, target.Path) {
		return nil
	}
	return resolved
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	hc, err := runtime.Require(ctx, exthttp.ServiceKey)
	if err != nil {
		return nil, err
	}
	target, err := parseTarget(c.Target)
	if err != nil {
		return nil, err
	}
	for _, pattern := range routePatterns(c.Route) {
		for _, method := range methods {
			m, p := method, pattern
			if err := ctx.Effect(func() (func() error, error) {
				return svc.Handle(m, p, func(w http.ResponseWriter, r *http.Request) error {
					return c.forward(hc, target, w, r)
				})
			}); err != nil {
				return nil, err
			}
		}
	}
	if err := ctx.Effect(func() (func() error, error) {
		return svc.WS(wsRoute, func(conn *server.WSConn) error {
			return proxyWS(ctx.Done(), target, conn)
		})
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

func (c *Component) forward(hc exthttp.Service, target *url.URL, w http.ResponseWriter, r *http.Request) error {
	located := locate(target, r.PathValue("path"))
	if located == nil {
		http.Error(w, "Bad Request", http.StatusBadRequest)
		return nil
	}
	located.RawQuery = r.URL.RawQuery // forward the raw query string verbatim

	var body []byte
	if r.Body != nil && (r.Method == http.MethodPost || r.Method == http.MethodPut || r.Method == http.MethodPatch) {
		var err error
		body, err = io.ReadAll(r.Body)
		if err != nil {
			http.Error(w, "Bad Request", http.StatusBadRequest)
			return nil
		}
	}
	resp, err := hc.Do(r.Context(), exthttp.Request{
		Method: r.Method,
		URL:    located.String(),
		Header: r.Header,
		Body:   body,
		// JS http/undici accepts every status: upstream 4xx/5xx pass through.
		ExpectStatus: func(int) bool { return true },
	})
	if err != nil {
		// Do only errors here on transport failure (or a closed service).
		http.Error(w, "Bad Gateway", http.StatusBadGateway)
		return nil
	}
	for key, values := range resp.Header {
		for _, value := range values {
			w.Header().Add(key, value)
		}
	}
	// The transport already decoded the body; these no longer describe it.
	w.Header().Del("Content-Encoding")
	w.Header().Del("Content-Length")
	w.WriteHeader(resp.StatusCode)
	_, err = w.Write(resp.Body)
	return err
}

// proxyWS dials the upstream WebSocket and pumps text/binary frames in both
// directions until either side closes or the component lifetime ends. Both
// connections are then closed, so no pump goroutine outlives the activation.
func proxyWS(done <-chan struct{}, target *url.URL, downstream *server.WSConn) error {
	rc, ok := (*downstream).(server.WSRequestConn)
	if !ok {
		return fmt.Errorf("proxy: WebSocket connection exposes no handshake request")
	}
	r := rc.Request()
	located := locate(target, r.PathValue("path"))
	if located == nil {
		// Same as the JS plugin: a path outside the target gets no upgrade.
		return nil
	}
	wsURL := *located
	switch wsURL.Scheme {
	case "http":
		wsURL.Scheme = "ws"
	case "https":
		wsURL.Scheme = "wss"
	}
	wsURL.RawQuery = r.URL.RawQuery

	headers := make(http.Header)
	for key, values := range r.Header {
		switch strings.ToLower(key) {
		case "host", "connection", "upgrade", "sec-websocket-key",
			"sec-websocket-version", "sec-websocket-extensions",
			"sec-websocket-accept", "content-length":
			continue // hop-by-hop / handshake fields belong to gorilla
		}
		headers[key] = append([]string(nil), values...)
	}
	upstream, _, err := websocket.DefaultDialer.Dial(wsURL.String(), headers)
	if err != nil {
		return err
	}
	return pumpWS(done, downstream, upstream)
}

// controlWriter is implemented by gorilla's Conn and by the server's WSConn
// wrapper (which embeds a *websocket.Conn); sending a clean close frame is
// best-effort when a peer does not expose it.
type controlWriter interface {
	WriteControl(int, []byte, time.Time) error
}

const wsWriteWait = 2 * time.Second

// sanitizeWSCode follows the browser close() rule: only 1000 and 3000-4999 may
// be sent from user code; protocol-reserved codes become a normal closure.
func sanitizeWSCode(code int) int {
	if code == websocket.CloseNormalClosure || (code >= 3000 && code < 5000) {
		return code
	}
	return websocket.CloseNormalClosure
}

func writeWSClose(peer any, code int, text string) {
	if cw, ok := peer.(controlWriter); ok {
		_ = cw.WriteControl(websocket.CloseMessage,
			websocket.FormatCloseMessage(sanitizeWSCode(code), text),
			time.Now().Add(wsWriteWait))
	}
}

func pumpWS(done <-chan struct{}, downstream *server.WSConn, upstream *websocket.Conn) error {
	stop := make(chan struct{})
	var once sync.Once
	fail := func() { once.Do(func() { close(stop) }) }
	defer func() {
		fail()
		_ = upstream.Close()
		_ = (*downstream).Close()
	}()

	go func() {
		defer fail()
		for {
			mt, data, err := (*downstream).ReadMessage()
			if err != nil {
				if ce, ok := err.(*websocket.CloseError); ok {
					writeWSClose(upstream, ce.Code, ce.Text)
				}
				return
			}
			if err := upstream.WriteMessage(mt, data); err != nil {
				return
			}
		}
	}()
	go func() {
		defer fail()
		for {
			mt, data, err := upstream.ReadMessage()
			if err != nil {
				if ce, ok := err.(*websocket.CloseError); ok {
					writeWSClose(*downstream, ce.Code, ce.Text)
				}
				return
			}
			if err := (*downstream).WriteMessage(mt, data); err != nil {
				return
			}
		}
	}()

	select {
	case <-stop:
	case <-done:
	}
	return nil
}
