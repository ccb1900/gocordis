package explorer

import (
	"fmt"
	"sync"
	"sync/atomic"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	"dynamic-runtime/extensions/console/configutil"
	host "dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/registry"
)

// ExplorerComponent is the GOCORDIS Plugin Explorer component. It is not a
// special Root UI: it Requires the UI Host and registers its own Console Page
// through a Runtime Effect. Inspection/control are provided by the Application
// Service and exposed through its transport Host only while this activation is
// active.
type ExplorerComponent struct {
	id string

	mu      sync.Mutex
	service *Service
	adapter *ExplorerTransport

	page registry.PageDefinition
}

func NewPlugin(cc config.ComponentConfig, service *Service) (*ExplorerComponent, error) {
	if service == nil {
		return nil, fmt.Errorf("plugin-explorer %q requires the application explorer service", cc.ID)
	}
	page := registry.PageDefinition{
		ID:       configutil.OptionalString(cc, "page_id", "plugins"),
		Title:    configutil.OptionalString(cc, "title", "Plugins"),
		Route:    configutil.OptionalString(cc, "route", "/plugins"),
		Renderer: "plugin-explorer",
		Order:    configutil.OptionalInt(cc, "order", 0),
	}
	if page.ID == "" {
		page.ID = "plugins"
	}
	if page.Title == "" {
		page.Title = "Plugins"
	}
	if page.Route == "" {
		page.Route = "/plugins"
	}
	return &ExplorerComponent{id: cc.ID, service: service, page: page}, nil
}

func (c *ExplorerComponent) Name() string { return fmt.Sprintf("plugin-explorer:%s", c.id) }

func (c *ExplorerComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(host.UIHostKey)}
}

func (c *ExplorerComponent) Provide() []runtime.Capability { return nil }

func (c *ExplorerComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	reg, err := runtime.Require(ctx, host.UIHostKey)
	if err != nil {
		return nil, err
	}
	owner := registry.ContributionOwner{
		PluginID:     "plugin-explorer",
		ComponentID:  c.id,
		ActivationID: activationID(ctx),
	}
	adapter := NewTransport(ctx.Context(), c.service)
	adapter.activate()
	c.setAdapter(adapter)
	if err := ctx.Effect(func() (func() error, error) {
		unregister, err := reg.RegisterPage(owner, c.page)
		if err != nil {
			return nil, err
		}
		return func() error {
			adapter.deactivate()
			c.clearAdapter(adapter)
			return unregister()
		}, nil
	}); err != nil {
		adapter.deactivate()
		c.clearAdapter(adapter)
		return nil, err
	}
	return nil, nil
}

func (c *ExplorerComponent) setAdapter(h *ExplorerTransport) {
	c.mu.Lock()
	c.adapter = h
	c.mu.Unlock()
}

func (c *ExplorerComponent) clearAdapter(h *ExplorerTransport) {
	c.mu.Lock()
	if c.adapter == h {
		c.adapter = nil
	}
	c.mu.Unlock()
}

// HostAdapter returns the active transport Host, or nil when this Explorer
// activation is not active.
func (c *ExplorerComponent) HostAdapter() *ExplorerTransport {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.adapter
}

var activationSeq atomic.Uint64

func activationID(_ *runtime.Context) string {
	return fmt.Sprintf("activation:%d", activationSeq.Add(1))
}
