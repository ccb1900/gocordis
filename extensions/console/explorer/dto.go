package explorer

import (
	"context"
	"sync/atomic"
)

// ExplorerPlugin is the transport DTO for one runtime plugin row. Go slices
// are copied; Runtime/Fiber/Registry types never cross this boundary.
type ExplorerPlugin struct {
	ID           string            `json:"id"`
	Name         string            `json:"name"`
	Type         string            `json:"type"`
	State        string            `json:"state"`
	Error        string            `json:"error,omitempty"`
	Components   []string          `json:"components"`
	Capabilities []string          `json:"capabilities"`
	Controllable bool              `json:"controllable"`
	Config       map[string]string `json:"config,omitempty"`
}

// ExplorerPluginList is the ListPlugins response envelope used by Wails and
// the HTTP host.
type ExplorerPluginList struct {
	Plugins []ExplorerPlugin `json:"plugins"`
}

// Error is the transport-neutral failure shape of explorer calls.
type Error struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

func unavailable(msg string) *Error { return &Error{Code: "unavailable", Message: msg} }

func invalidRequest(message string) *Error {
	return &Error{Code: "invalid_request", Message: message}
}

// ExplorerControlRequest is one Runtime Control request from the Console UI.
type ExplorerControlRequest struct {
	PluginID string `json:"pluginId"`
	Enable   bool   `json:"enable"`
}

// ExplorerControlResult carries the four-part Control outcome.
type ExplorerControlResult struct {
	PluginID string `json:"pluginId"`
	Accepted bool   `json:"accepted"`
	Rejected bool   `json:"rejected"`
	Failed   bool   `json:"failed"`
	State    string `json:"state"`
	Error    string `json:"error"`
}

// Host is the Plugin Explorer transport adapter. It is created during the
// Explorer activation and forwards inspection/control to the Application
// Service. React/Wails still never touch Runtime internals.
type ExplorerTransport struct {
	base    context.Context
	service *Service
	active  atomic.Bool
}

func NewTransport(base context.Context, service *Service) *ExplorerTransport {
	if base == nil {
		base = context.Background()
	}
	return &ExplorerTransport{base: base, service: service}
}

func (h *ExplorerTransport) activate()   { h.active.Store(true) }
func (h *ExplorerTransport) deactivate() { h.active.Store(false) }

func (h *ExplorerTransport) ListPlugins() (ExplorerPluginList, *Error) {
	if h == nil || !h.active.Load() || h.service == nil {
		return ExplorerPluginList{}, unavailable("plugin explorer is not active")
	}
	rows := h.service.Plugins()
	out := make([]ExplorerPlugin, 0, len(rows))
	for _, row := range rows {
		out = append(out, toExplorerPlugin(row))
	}
	return ExplorerPluginList{Plugins: out}, nil
}

func (h *ExplorerTransport) ControlPlugin(req ExplorerControlRequest) (ExplorerControlResult, *Error) {
	if h == nil || !h.active.Load() {
		return ExplorerControlResult{}, unavailable("plugin explorer is not active")
	}
	return h.ControlPluginContext(h.base, req)
}

// ControlPluginContext exposes the same Runtime Control with an explicit
// request context for bounded HTTP/testing calls.
func (h *ExplorerTransport) ControlPluginContext(ctx context.Context, req ExplorerControlRequest) (ExplorerControlResult, *Error) {
	if h == nil || !h.active.Load() || h.service == nil {
		return ExplorerControlResult{}, unavailable("plugin explorer is not active")
	}
	if req.PluginID == "" {
		return ExplorerControlResult{}, invalidRequest("pluginId is required")
	}
	res := h.service.Control(ctx, req.PluginID, req.Enable)
	return toControlResult(res), nil
}

func toExplorerPlugin(p Plugin) ExplorerPlugin {
	cfg := make(map[string]string, len(p.Config))
	for k, v := range p.Config {
		cfg[k] = v
	}
	return ExplorerPlugin{
		ID:           p.ID,
		Name:         p.Name,
		Type:         p.Type,
		State:        p.State,
		Error:        p.Error,
		Components:   cloneStrings(p.Components),
		Capabilities: cloneStrings(p.Capabilities),
		Controllable: p.Controllable,
		Config:       cfg,
	}
}

func cloneStrings(in []string) []string {
	out := make([]string, len(in))
	copy(out, in)
	return out
}

func toControlResult(r ControlResult) ExplorerControlResult {
	return ExplorerControlResult{
		PluginID: r.PluginID,
		Accepted: r.Accepted,
		Rejected: r.Rejected,
		Failed:   r.Failed,
		State:    r.State,
		Error:    r.Error,
	}
}
