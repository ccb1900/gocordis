package insight

import (
	"context"
	"fmt"
	"net/url"
	"sync/atomic"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	"dynamic-runtime/extensions/console/configutil"
	host "dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	"dynamic-runtime/extensions/console/registry"
)

const (
	// GraphQuery is the hub query returning the declared/runtime graph pair.
	GraphQuery = "insight/graph"
	// SchemasQuery is the hub query returning registered component JSON Schemas.
	SchemasQuery = "config/schema"

	hubOwner      = "console:insight"
	defaultPageID = "insight"
)

// Component is the read-only insight contributor. It requires the Console hub
// and UI registry and registers both queries plus its page as Effects of its
// own activation; disposing the activation removes all three (no residue).
type Component struct {
	id      string
	service *Service
	page    registry.PageDefinition
}

// NewComponent builds the insight contributor. The Service must be bound to
// the application's Config Controller and Runtime; the component never builds
// its own dependency system.
func NewComponent(cc config.ComponentConfig, service *Service) (*Component, error) {
	if service == nil {
		return nil, fmt.Errorf("insight %q requires an insight Service", cc.ID)
	}
	page := registry.PageDefinition{
		ID:       configutil.OptionalString(cc, "page_id", defaultPageID),
		Title:    configutil.OptionalString(cc, "title", "Insight"),
		Route:    configutil.OptionalString(cc, "route", "/insight"),
		Renderer: "insight-graph",
		Icon:     configutil.OptionalString(cc, "icon", ""),
		Order:    configutil.OptionalInt(cc, "order", 0),
	}
	if page.ID == "" {
		page.ID = defaultPageID
	}
	if page.Title == "" {
		page.Title = "Insight"
	}
	if page.Route == "" {
		page.Route = "/insight"
	}
	return &Component{id: cc.ID, service: service, page: page}, nil
}

func (c *Component) Name() string { return fmt.Sprintf("insight:%s", c.id) }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(host.HubKey),
		runtime.Requires(host.UIHostKey),
	}
}

func (c *Component) Provide() []runtime.Capability { return nil }

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	h, err := runtime.Require(ctx, host.HubKey)
	if err != nil {
		return nil, err
	}
	reg, err := runtime.Require(ctx, host.UIHostKey)
	if err != nil {
		return nil, err
	}
	owner := registry.ContributionOwner{
		PluginID:     "insight",
		ComponentID:  c.id,
		ActivationID: activationLabel(),
	}
	if err := ctx.Effect(func() (func() error, error) {
		var undos []func() error
		queries := []struct {
			name    string
			handler hub.QueryHandler
		}{
			{GraphQuery, c.graphQuery},
			{SchemasQuery, c.schemasQuery},
		}
		for _, q := range queries {
			un, err := h.RegisterQuery(q.name, hubOwner, q.handler)
			if err != nil {
				undoAll(undos)
				return nil, err
			}
			undos = append(undos, un)
		}
		unPage, err := reg.RegisterPage(owner, c.page)
		if err != nil {
			undoAll(undos)
			return nil, err
		}
		undos = append(undos, unPage)
		return func() error {
			undoAll(undos)
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

func (c *Component) graphQuery(ctx context.Context, _ url.Values) (any, *hub.Error) {
	g, err := c.service.Graph(ctx)
	if err != nil {
		return nil, &hub.Error{Code: "unavailable", Message: err.Error()}
	}
	return g, nil
}

func (c *Component) schemasQuery(_ context.Context, _ url.Values) (any, *hub.Error) {
	list, err := c.service.Schemas()
	if err != nil {
		return nil, &hub.Error{Code: "unavailable", Message: err.Error()}
	}
	return list, nil
}

func undoAll(undos []func() error) {
	for i := len(undos) - 1; i >= 0; i-- {
		_ = undos[i]()
	}
}

var activationSeq atomic.Uint64

func activationLabel() string { return fmt.Sprintf("activation:%d", activationSeq.Add(1)) }
