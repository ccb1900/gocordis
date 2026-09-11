// Console DTO boundary: these shapes are the entire surface clients see.
// They are domain-free — application payloads travel through the hub's named
// queries and commands as opaque JSON. No Go internal type ever crosses.
package host

import (
	"encoding/json"

	"dynamic-runtime/extensions/console/registry"
)

// UIPage is one contributed console page. View carries the optional
// declarative view schema (opaque to the console; the client renderer
// interprets it).
type UIPage struct {
	ID          string          `json:"id"`
	Title       string          `json:"title"`
	Route       string          `json:"route"`
	Renderer    string          `json:"renderer"`
	Description string          `json:"description,omitempty"`
	View        json.RawMessage `json:"view,omitempty"`
	Views       json.RawMessage `json:"views,omitempty"`
	Actions     json.RawMessage `json:"actions,omitempty"`
}

// UIPanel is one contributed console panel. Pages lists the page IDs the
// panel appears on; empty means every page.
type UIPanel struct {
	ID       string          `json:"id"`
	Title    string          `json:"title"`
	Position string          `json:"position"`
	Renderer string          `json:"renderer"`
	Pages    []string        `json:"pages,omitempty"`
	Views    json.RawMessage `json:"views,omitempty"`
}

// UIObservation is the minimal invalidation message: it only says "something
// changed"; clients re-query. It never carries full state.
type UIObservation struct {
	Type      string `json:"type"`
	SourceID  string `json:"sourceId,omitempty"`
	Timestamp string `json:"timestamp"`
}

// UIError is the front-end error contract. Concrete Go error types are never
// exposed.
type UIError struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

type UIPageList struct {
	Pages []UIPage `json:"pages"`
}

type UIPanelList struct {
	Panels []UIPanel `json:"panels"`
}

func toUIPage(def registry.PageDefinition) UIPage {
	return UIPage{ID: def.ID, Title: def.Title, Route: def.Route, Renderer: def.Renderer, Description: def.Description, View: def.View, Views: def.Views, Actions: def.Actions}
}

func toUIPanel(def registry.PanelDefinition) UIPanel {
	return UIPanel{
		ID: def.ID, Title: def.Title, Position: string(def.Position),
		Renderer: def.Renderer, Pages: append([]string(nil), def.Pages...), Views: def.Views,
	}
}
