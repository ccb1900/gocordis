package market

import (
	"context"
	"encoding/json"
	"errors"
	"fmt"
	"net/url"
	"sync/atomic"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/configutil"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	"dynamic-runtime/extensions/console/registry"
	"dynamic-runtime/runtime"
)

// ServiceKey is the typed capability applications can Require to drive the
// marketplace directly (mutations remain declaration-level).
var ServiceKey = runtime.NewKey[Service]("market.service")

// Console contribution identities.
const (
	CatalogQuery     = "market/catalog" // read-only hub query
	InstallCommand   = "market/install"
	UpgradeCommand   = "market/upgrade"
	UninstallCommand = "market/uninstall"
	PageID           = "market"
	PageRoute        = "/market"
	PageRenderer     = "market-catalog"

	marketHubOwner = "console:market"
)

// Component is the read-only-hub / declaration-mutation contributor. It
// requires the console hub and UI registry, registers its query, commands and
// page as Effects of its own activation, and exposes the typed Service.
type Component struct {
	id      string
	service Service
	page    registry.PageDefinition
}

// NewComponent reads the Component config (catalog_dirs, runtime_version,
// api_version, trust) and builds a Service bound to ctrl. The service keeps
// this component's own declaration via Base, and preserves every other applied
// declaration from the Controller on each reconcile — so unrelated app rows
// survive Install/Upgrade/Uninstall. Applications with declarations that have
// not yet been applied should use NewContributor with a Service built by
// NewService with a Base covering those rows.
func NewComponent(cc config.ComponentConfig, ctrl *config.Controller) (*Component, error) {
	self := cc
	self.Enabled = nil // enabled declaration; the entry identity survives
	svc, err := NewService(Options{
		Controller:     ctrl,
		CatalogDirs:    configutil.OptionalStringSlice(cc, "catalog_dirs", nil),
		RuntimeVersion: configutil.OptionalString(cc, "runtime_version", ""),
		APIVersion:     configutil.OptionalString(cc, "api_version", ""),
		Trust:          configutil.OptionalString(cc, "trust", "local"),
		OwnID:          cc.ID,
		Base: func(context.Context) ([]config.ComponentConfig, error) {
			return []config.ComponentConfig{self}, nil
		},
	})
	if err != nil {
		return nil, err
	}
	return NewContributor(cc, svc)
}

// NewContributor binds an already-built Service to the console contribution.
func NewContributor(cc config.ComponentConfig, svc Service) (*Component, error) {
	if svc == nil {
		return nil, fmt.Errorf("market %q requires a Service", cc.ID)
	}
	return &Component{
		id:      cc.ID,
		service: svc,
		page: registry.PageDefinition{
			ID: PageID, Title: "Market", Route: PageRoute, Renderer: PageRenderer,
		},
	}, nil
}

func (c *Component) Name() string { return "market:" + c.id }

func (c *Component) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(host.HubKey),
		runtime.Requires(host.UIHostKey),
	}
}

func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	h, err := runtime.Require(ctx, host.HubKey)
	if err != nil {
		return nil, err
	}
	reg, err := runtime.Require(ctx, host.UIHostKey)
	if err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, ServiceKey, c.service); err != nil {
		return nil, err
	}
	owner := registry.ContributionOwner{
		PluginID:     "market",
		ComponentID:  c.id,
		ActivationID: activationLabel(),
	}
	if err := ctx.Effect(func() (func() error, error) {
		var undos []func() error
		un, err := h.RegisterQuery(CatalogQuery, marketHubOwner, c.catalogQuery)
		if err != nil {
			undoAll(undos)
			return nil, err
		}
		undos = append(undos, un)
		for _, cmd := range []struct {
			name string
			call func(context.Context, string) error
		}{
			{InstallCommand, c.service.Install},
			{UpgradeCommand, c.service.Upgrade},
			{UninstallCommand, c.service.Uninstall},
		} {
			handler := c.commandHandler(h, cmd.call)
			unCmd, err := h.RegisterCommand(cmd.name, marketHubOwner, handler)
			if err != nil {
				undoAll(undos)
				return nil, err
			}
			undos = append(undos, unCmd)
		}
		unPage, err := reg.RegisterPage(owner, c.page)
		if err != nil {
			undoAll(undos)
			return nil, err
		}
		undos = append(undos, unPage)
		return func() error {
			undoAll(undos) // LIFO; hub/registry unregisters are idempotent
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}

func (c *Component) catalogQuery(ctx context.Context, _ url.Values) (any, *hub.Error) {
	cat, err := c.service.Catalog(ctx)
	if err != nil && !errors.Is(err, ErrInvalidCatalog) {
		// Scan/cancellation failure: no trustworthy partial state to show.
		return nil, &hub.Error{Code: "unavailable", Message: err.Error()}
	}
	// Invalid files and duplicate IDs are non-fatal: the partial catalog is
	// still served, with reasons in the additive DTO "issues" field, so one
	// bad manifest cannot blank the whole Market page (review 3).
	return cat, nil
}

type commandBody struct {
	ID string `json:"id"`
}

func (c *Component) commandHandler(h *hub.Registry, call func(context.Context, string) error) hub.CommandHandler {
	return func(ctx context.Context, body json.RawMessage) error {
		var b commandBody
		if len(body) > 0 && string(body) != "null" {
			if err := json.Unmarshal(body, &b); err != nil {
				return &hub.Error{Code: "invalid_request", Message: err.Error()}
			}
		}
		if b.ID == "" {
			return &hub.Error{Code: "invalid_request", Message: "id is required"}
		}
		if err := call(ctx, b.ID); err != nil {
			return &hub.Error{Code: "market_error", Message: err.Error()}
		}
		h.Publish(hub.Observation{
			Type: "market.changed", SourceID: b.ID,
			Timestamp: time.Now().UTC().Format(time.RFC3339),
		})
		return nil
	}
}

func undoAll(undos []func() error) {
	for i := len(undos) - 1; i >= 0; i-- {
		_ = undos[i]()
	}
}

var activationSeq atomic.Uint64

func activationLabel() string { return fmt.Sprintf("market:%d", activationSeq.Add(1)) }
