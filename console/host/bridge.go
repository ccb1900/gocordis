package host

import (
	"context"
	"encoding/json"
	"errors"
	"net/url"
	"sync"

	"dynamic-runtime/console/hub"
	appui "dynamic-runtime/console/registry"
)

// Host is the console transport adapter. Wails and HTTP both call it; it
// forwards to the hub (named queries/commands/observations) and the
// composition registry. It contains no business logic, holds no domain
// types, and never touches Storage/State/Executor/Runtime internals.
type Host struct {
	base     context.Context
	registry appui.Registry
	hub      *hub.Registry
}

// NewHost builds the console transport adapter from the composition registry
// and the hub owned by the same activation.
func NewHost(base context.Context, registry appui.Registry, h *hub.Registry) *Host {
	if base == nil {
		base = context.Background()
	}
	return &Host{base: base, registry: registry, hub: h}
}

func (h *Host) ctx() context.Context { return h.base }

// Query executes a named console query. The handler is provided by an
// application; params pass through untouched and the result is returned
// as-is (the transport encodes it as JSON "data").
func (h *Host) Query(name string, params url.Values) (any, *UIError) {
	if h.hub == nil {
		return nil, &UIError{Code: "unavailable", Message: "console hub not active"}
	}
	handler, ok := h.hub.Query(name)
	if !ok {
		return nil, &UIError{Code: "not_found", Message: "unknown query " + name}
	}
	res, herr := handler(h.ctx(), params)
	if herr != nil {
		return nil, &UIError{Code: herr.Code, Message: herr.Message}
	}
	return res, nil
}

// Command executes a named console command. Accepting is synchronous; the
// outcome reaches the console through the observation stream.
func (h *Host) Command(name string, body json.RawMessage) *UIError {
	if h.hub == nil {
		return &UIError{Code: "unavailable", Message: "console hub not active"}
	}
	handler, ok := h.hub.Command(name)
	if !ok {
		return &UIError{Code: "not_found", Message: "unknown command " + name}
	}
	if err := handler(h.ctx(), body); err != nil {
		if he, isHubErr := err.(*hub.Error); isHubErr {
			return &UIError{Code: he.Code, Message: he.Message}
		}
		return &UIError{Code: "error", Message: err.Error()}
	}
	return nil
}

// Composition Bridge -----------------------------------------------------------

// ListPages returns the current UI Composition as UI DTOs. It reads one shared
// Registry; Wails and HTTP expose the same method.
func (h *Host) ListPages() (UIPageList, *UIError) {
	if h.registry == nil {
		return UIPageList{}, &UIError{Code: "unavailable", Message: "UI composition unavailable"}
	}
	snap := h.registry.Snapshot()
	pages := make([]UIPage, 0, len(snap.Pages))
	for _, def := range snap.Pages {
		pages = append(pages, toUIPage(def))
	}
	return UIPageList{Pages: pages}, nil
}

// ListPanels returns the current UI Composition Panels as UI DTOs.
func (h *Host) ListPanels() (UIPanelList, *UIError) {
	if h.registry == nil {
		return UIPanelList{}, &UIError{Code: "unavailable", Message: "UI composition unavailable"}
	}
	snap := h.registry.Snapshot()
	panels := make([]UIPanel, 0, len(snap.Panels))
	for _, def := range snap.Panels {
		panels = append(panels, toUIPanel(def))
	}
	return UIPanelList{Panels: panels}, nil
}

// Observation bridge -----------------------------------------------------------

// observationBridge fans observations out to in-process listeners (the React
// host during tests, Wails bindings in production use the sink instead).
type observationBridge struct {
	mu      sync.Mutex
	subs    map[int]func(UIObservation)
	next    int
	history []UIObservation
}

func newObservationBridge() *observationBridge {
	return &observationBridge{subs: map[int]func(UIObservation){}}
}

func (b *observationBridge) notify(ev UIObservation) {
	b.mu.Lock()
	b.history = append(b.history, ev)
	handlers := make([]func(UIObservation), 0, len(b.subs))
	for _, h := range b.subs {
		handlers = append(handlers, h)
	}
	b.mu.Unlock()
	for _, h := range handlers {
		h(ev)
	}
}

func (b *observationBridge) on(handler func(UIObservation)) (func() error, error) {
	if handler == nil {
		return nil, errors.New("console: nil observation handler")
	}
	b.mu.Lock()
	id := b.next
	b.next++
	b.subs[id] = handler
	b.mu.Unlock()
	return func() error {
		b.mu.Lock()
		defer b.mu.Unlock()
		delete(b.subs, id)
		return nil
	}, nil
}

func (b *observationBridge) hasSubscribers() bool {
	b.mu.Lock()
	defer b.mu.Unlock()
	return len(b.subs) > 0
}

func (b *observationBridge) latest() []UIObservation {
	b.mu.Lock()
	defer b.mu.Unlock()
	out := make([]UIObservation, len(b.history))
	copy(out, b.history)
	return out
}

func (b *observationBridge) clear() {
	b.mu.Lock()
	defer b.mu.Unlock()
	b.subs = map[int]func(UIObservation){}
	b.history = nil
}
