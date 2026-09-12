// Package webui serves the embedded React UI and exposes the plugins/ui Host
// Adapter over plain HTTP + SSE, so the UI can be reached through a browser
// (go:embed + net/http) in addition to the Wails desktop host.
//
// The same DTO/Error/Observation Contract is used: React calls the same api
// layer; transport chooses Wails or HTTP automatically. Composition DTOs live
// above transport and are served at GET /api/ui/pages and /api/ui/panels.
package webui

import (
	"context"
	"encoding/json"
	"errors"
	"io/fs"
	"mime"
	"net/http"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"dynamic-runtime/extensions/console/explorer"
	host "dynamic-runtime/extensions/console/host"
)

// Observation event name is fixed and shared with the Wails bridge.
const ObservationEvent = "observation"

// RemovedPlugin is one uninstalled component offering an install-back action.
type RemovedPlugin struct {
	ID   string `json:"id"`
	Name string `json:"name"`
}

// PluginLifecycle is the desired-state editing boundary: the application owns
// how uninstall decisions persist; the transport only serves them.
type PluginLifecycle interface {
	Uninstall(ctx context.Context, id string) error
	Install(ctx context.Context, id string) error
	Removed(ctx context.Context) ([]RemovedPlugin, error)
	// Config returns the current configuration of one desired component as a
	// JSON-encodable object; SetConfig replaces it (validate + reconcile,
	// rolling back on failure). Optional: servers without it report
	// unavailable on the config routes.
	Config(ctx context.Context, id string) (map[string]any, error)
	SetConfig(ctx context.Context, id string, cfg map[string]any) error
}

// Server is the Web UI host: static assets + JSON Query/Command API + SSE.
type Server struct {
	adapter   *host.Host
	assets    fs.FS
	explorer  *explorer.ExplorerTransport
	lifecycle PluginLifecycle

	hostID          string
	startedAt       time.Time
	fleetPeers      []string
	fleetCache      map[string]any
	fleetCacheUntil time.Time

	mu   sync.Mutex
	subs map[chan host.UIObservation]struct{}
}

// New builds the web UI server over the given plugins/ui Host adapter.
func New(adapter *host.Host, assets fs.FS) *Server {
	return &Server{
		adapter:   adapter,
		assets:    assets,
		startedAt: time.Now(),
		subs:      map[chan host.UIObservation]struct{}{},
	}
}

// SetExplorer installs the optional Plugin Explorer transport adapter. The
// Console is still Contribution-driven: the endpoint exists only when the
// plugin-explorer component is active.
// SetPluginLifecycle installs the desired-state editing boundary for the
// console's uninstall/install actions.
func (s *Server) SetPluginLifecycle(l PluginLifecycle) {
	s.lifecycle = l
}

func (s *Server) SetExplorer(exp *explorer.ExplorerTransport) {
	s.explorer = exp
}

// Publish pushes one observation to every SSE subscriber (non-blocking).
func (s *Server) Publish(ev host.UIObservation) {
	s.mu.Lock()
	chans := make([]chan host.UIObservation, 0, len(s.subs))
	for c := range s.subs {
		chans = append(chans, c)
	}
	s.mu.Unlock()
	for _, c := range chans {
		select {
		case c <- ev:
		default: // slow subscriber: drop
		}
	}
}

func (s *Server) ServeHTTP(w http.ResponseWriter, r *http.Request) {
	if strings.HasPrefix(r.URL.Path, "/api/") {
		s.serveAPI(w, r)
		return
	}
	if strings.HasPrefix(r.URL.Path, "/client-modules/") {
		s.serveClientModule(w, r)
		return
	}
	s.serveStatic(w, r)
}

// serveClientModule serves one plugin's frontend module and the static
// assets of its plugin directory, mounted at /client-modules/<name>/.
// The entry file (ui.js) is the module URL; everything under the plugin
// directory resolves relatively — vendored libraries included. The URL is
// split into <name>/<relative file>, the file side is cleaned, and any
// escape from the plugin directory is a 404.
func (s *Server) serveClientModule(w http.ResponseWriter, r *http.Request) {
	if r.Method != http.MethodGet {
		writeAPIError(w, http.StatusMethodNotAllowed, "invalid_request", "GET only")
		return
	}
	rest := strings.TrimPrefix(r.URL.Path, "/client-modules/")
	seg, file := rest, ""
	if i := strings.IndexByte(rest, '/'); i >= 0 {
		seg, file = rest[:i], rest[i+1:]
	}
	for _, m := range s.adapter.ListClientModules() {
		if m.Name != seg {
			continue
		}
		target := m.Path
		if file != "" {
			rel := filepath.Clean(filepath.FromSlash(file))
			if rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) {
				http.NotFound(w, r)
				return
			}
			target = filepath.Join(filepath.Dir(m.Path), rel)
		}
		info, err := os.Stat(target)
		if err != nil || info.IsDir() {
			http.NotFound(w, r)
			return
		}
		data, err := os.ReadFile(target)
		if err != nil {
			writeAPIError(w, http.StatusInternalServerError, "error", "client module unreadable: "+err.Error())
			return
		}
		ct := mime.TypeByExtension(filepath.Ext(target))
		if ct == "" || target == m.Path {
			// The module entry is always JavaScript, whatever it is named.
			ct = "text/javascript; charset=utf-8"
		}
		w.Header().Set("Content-Type", ct)
		w.Header().Set("Cache-Control", "no-cache")
		_, _ = w.Write(data)
		return
	}
	http.NotFound(w, r)
}

// serveStatic serves the embedded SPA build with an index fallback.
func (s *Server) serveStatic(w http.ResponseWriter, r *http.Request) {
	if s.assets == nil {
		http.NotFound(w, r)
		return
	}
	p := strings.TrimPrefix(r.URL.Path, "/")
	if p == "" || p == "index.html" {
		b, err := fs.ReadFile(s.assets, "index.html")
		if err != nil {
			http.NotFound(w, r)
			return
		}
		w.Header().Set("Content-Type", "text/html; charset=utf-8")
		_, _ = w.Write(b)
		return
	}
	if f, err := s.assets.Open(p); err == nil {
		_ = f.Close()
		http.FileServerFS(s.assets).ServeHTTP(w, r)
		return
	}
	// SPA fallback.
	b, err := fs.ReadFile(s.assets, "index.html")
	if err != nil {
		http.NotFound(w, r)
		return
	}
	w.Header().Set("Content-Type", "text/html; charset=utf-8")
	_, _ = w.Write(b)
}

func (s *Server) serveAPI(w http.ResponseWriter, r *http.Request) {
	switch {
	case r.Method == http.MethodGet && r.URL.Path == "/api/ui/pages":
		data, ue := s.adapter.ListPages()
		s.writeResult(w, data, ue)
	case r.Method == http.MethodGet && r.URL.Path == "/api/ui/panels":
		data, ue := s.adapter.ListPanels()
		s.writeResult(w, data, ue)
	case r.Method == http.MethodGet && r.URL.Path == "/api/ui/client-modules":
		modules := make([]map[string]string, 0)
		for _, m := range s.adapter.ListClientModules() {
			modules = append(modules, map[string]string{
				"name": m.Name,
				// Directory-shaped entry URL: relative imports of vendored
				// libraries resolve inside the plugin directory.
				"url": "/client-modules/" + m.Name + "/" + filepath.Base(m.Path),
			})
		}
		s.writeResult(w, map[string]any{"modules": modules}, nil)
	case r.Method == http.MethodGet && r.URL.Path == "/api/plugins":
		if s.explorer == nil {
			writeAPIError(w, http.StatusServiceUnavailable, "unavailable", "plugin explorer is not active")
			return
		}
		data, ue := s.explorer.ListPlugins()
		s.writeResult(w, data, explorerErr(ue))
	case r.Method == http.MethodPost && r.URL.Path == "/api/plugins/control":
		if s.explorer == nil {
			writeAPIError(w, http.StatusServiceUnavailable, "unavailable", "plugin explorer is not active")
			return
		}
		var req explorer.ExplorerControlRequest
		if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		data, ue := s.explorer.ControlPluginContext(r.Context(), req)
		s.writeResult(w, data, explorerErr(ue))
	case r.Method == http.MethodPost && r.URL.Path == "/api/plugins/uninstall":
		if s.lifecycle == nil {
			writeAPIError(w, http.StatusServiceUnavailable, "unavailable", "plugin lifecycle not configured")
			return
		}
		var req explorer.ExplorerControlRequest
		if err := json.NewDecoder(r.Body).Decode(&req); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		if err := s.lifecycle.Uninstall(r.Context(), req.PluginID); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		writeData(w, http.StatusAccepted, nil)
	case r.Method == http.MethodPost && r.URL.Path == "/api/plugins/install":
		if s.lifecycle == nil {
			writeAPIError(w, http.StatusServiceUnavailable, "unavailable", "plugin lifecycle not configured")
			return
		}
		var req2 explorer.ExplorerControlRequest
		if err := json.NewDecoder(r.Body).Decode(&req2); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		if err := s.lifecycle.Install(r.Context(), req2.PluginID); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		writeData(w, http.StatusAccepted, nil)
	case r.Method == http.MethodGet && r.URL.Path == "/api/plugins/removed":
		if s.lifecycle == nil {
			writeData(w, http.StatusOK, []any{})
			return
		}
		removed, lerr := s.lifecycle.Removed(r.Context())
		if lerr != nil {
			writeAPIError(w, http.StatusInternalServerError, "error", lerr.Error())
			return
		}
		writeData(w, http.StatusOK, removed)
	case r.Method == http.MethodGet && strings.HasSuffix(r.URL.Path, "/config") && strings.HasPrefix(r.URL.Path, "/api/plugins/"):
		if s.lifecycle == nil {
			writeAPIError(w, http.StatusServiceUnavailable, "unavailable", "plugin lifecycle not configured")
			return
		}
		id := strings.TrimSuffix(strings.TrimPrefix(r.URL.Path, "/api/plugins/"), "/config")
		cfg, cerr := s.lifecycle.Config(r.Context(), id)
		if cerr != nil {
			writeAPIError(w, http.StatusNotFound, "not_found", cerr.Error())
			return
		}
		writeData(w, http.StatusOK, cfg)
	case r.Method == http.MethodPost && strings.HasSuffix(r.URL.Path, "/config") && strings.HasPrefix(r.URL.Path, "/api/plugins/"):
		if s.lifecycle == nil {
			writeAPIError(w, http.StatusServiceUnavailable, "unavailable", "plugin lifecycle not configured")
			return
		}
		id := strings.TrimSuffix(strings.TrimPrefix(r.URL.Path, "/api/plugins/"), "/config")
		var body struct {
			Config map[string]any `json:"config"`
		}
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		if err := s.lifecycle.SetConfig(r.Context(), id, body.Config); err != nil {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		writeData(w, http.StatusAccepted, nil)
	case r.Method == http.MethodGet && strings.HasPrefix(r.URL.Path, "/api/query/"):
		name := strings.TrimPrefix(r.URL.Path, "/api/query/")
		data, ue := s.adapter.Query(name, r.URL.Query())
		if ue != nil {
			status, code := uiErrorStatus(ue.Code)
			writeAPIError(w, status, code, ue.Message)
			return
		}
		writeData(w, http.StatusOK, data)
	case r.Method == http.MethodPost && strings.HasPrefix(r.URL.Path, "/api/command/"):
		name := strings.TrimPrefix(r.URL.Path, "/api/command/")
		var body json.RawMessage
		if err := json.NewDecoder(r.Body).Decode(&body); err != nil && err.Error() != "EOF" {
			writeAPIError(w, http.StatusBadRequest, "invalid_request", err.Error())
			return
		}
		if ue := s.adapter.Command(name, body); ue != nil {
			status, code := uiErrorStatus(ue.Code)
			writeAPIError(w, status, code, ue.Message)
			return
		}
		writeData(w, http.StatusAccepted, nil)
	case r.Method == http.MethodGet && r.URL.Path == "/api/meta":
		s.serveMeta(w, r)
	case r.Method == http.MethodGet && r.URL.Path == "/api/fleet":
		s.serveFleet(w, r)
	case r.Method == http.MethodGet && r.URL.Path == "/api/stream":
		s.serveStream(w, r)
	default:
		writeAPIError(w, http.StatusNotFound, "not_found", "unknown api route")
	}
}

func (s *Server) writeResult(w http.ResponseWriter, data any, ue *host.UIError) {
	if ue != nil {
		status, code := uiErrorStatus(ue.Code)
		writeAPIError(w, status, code, ue.Message)
		return
	}
	writeData(w, http.StatusOK, data)
}

// explorerErr converts the explorer failure shape (identical fields) into
// the shared UI error contract.
func explorerErr(ue *explorer.Error) *host.UIError {
	if ue == nil {
		return nil
	}
	return &host.UIError{Code: ue.Code, Message: ue.Message}
}

func uiErrorStatus(code string) (int, string) {
	switch code {
	case "not_found":
		return http.StatusNotFound, "not_found"
	case "invalid_request":
		return http.StatusBadRequest, "invalid_request"
	case "unavailable":
		return http.StatusServiceUnavailable, "unavailable"
	default:
		return http.StatusInternalServerError, "error"
	}
}

func writeData(w http.ResponseWriter, status int, data any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]any{"data": data})
}

func writeAPIError(w http.ResponseWriter, status int, code, message string) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(map[string]any{"code": code, "message": message})
}

// serveStream implements the Observation bridge for browsers: SSE events
// named "observation" carrying UIObservation. UI never polls.
func (s *Server) serveStream(w http.ResponseWriter, r *http.Request) {
	fl, ok := w.(http.Flusher)
	if !ok {
		writeAPIError(w, http.StatusInternalServerError, "error", "streaming unsupported")
		return
	}
	w.Header().Set("Content-Type", "text/event-stream")
	w.Header().Set("Cache-Control", "no-cache")
	w.Header().Set("Connection", "keep-alive")
	// Commit the headers immediately: EventSource waits for the response to
	// start, and without an initial flush the write buffer holds it until the
	// first observation happens to arrive. A leading SSE comment is ignored
	// by clients and doubles as the boundary "acquisition" signal.
	if _, err := w.Write([]byte(": connected\n\n")); err != nil {
		return
	}
	fl.Flush()
	ch := make(chan host.UIObservation, 8)
	s.mu.Lock()
	s.subs[ch] = struct{}{}
	s.mu.Unlock()
	defer func() {
		s.mu.Lock()
		delete(s.subs, ch)
		s.mu.Unlock()
	}()
	ctx := r.Context()
	if ctx == nil {
		ctx = context.Background()
	}
	for {
		select {
		case ev := <-ch:
			b, _ := json.Marshal(ev)
			_, _ = w.Write([]byte("event: observation\ndata: " + string(b) + "\n\n"))
			fl.Flush()
		case <-ctx.Done():
			return
		}
	}
}

var _ = errors.New // keep errors import for future typed handling
