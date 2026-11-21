// Package runtime exposes read-only Runtime Inspector and Framework Dashboard
// data to the console through the existing hub and UI composition registry.
// It never mutates Fibers and never recomputes dependency facts: every row
// comes from one Runtime.Snapshot or the bounded canonical event ring.
package runtime

import (
	"context"
	"errors"
	"fmt"
	"net/url"
	"sync/atomic"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/configutil"
	host "dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	"dynamic-runtime/extensions/console/registry"
	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/extensions/pluginmeta"
	coreruntime "dynamic-runtime/runtime"
)

const (
	hubOwner = "console:runtime"

	defaultInspectorTitle = "Runtime Inspector"
	defaultDashboardTitle = "Framework Dashboard"
)

var activationSeq atomic.Uint64

func activationLabel() string { return fmt.Sprintf("activation:%d", activationSeq.Add(1)) }

// Option configures optional diagnostics for the runtime console contributor.
type Option func(*Component)

// WithDevtoolsDiagnostics includes devtools.Service findings in the dashboard.
// The dependency is declared only when this option is used.
func WithDevtoolsDiagnostics() Option {
	return func(c *Component) { c.withDiagnostics = true }
}

// Component is an Effect-owned console contributor. Queries and pages are
// registered during Apply and removed by the inverse on unwind.
type Component struct {
	id              string
	rt              *coreruntime.Runtime
	ring            *Ring
	inspector       registry.PageDefinition
	dashboard       registry.PageDefinition
	withDiagnostics bool
	diagnostics     devtools.Service
}

// New builds the contributor from a declared component config, a live Runtime
// and the event ring that was attached with runtime.WithEventSink.
func New(cc config.ComponentConfig, rt *coreruntime.Runtime, ring *Ring, opts ...Option) (*Component, error) {
	if rt == nil {
		return nil, fmt.Errorf("runtime console %q requires a Runtime", cc.ID)
	}
	if ring == nil {
		return nil, fmt.Errorf("runtime console %q requires an event ring", cc.ID)
	}
	c := &Component{id: cc.ID, rt: rt, ring: ring}
	for _, opt := range opts {
		if opt != nil {
			opt(c)
		}
	}
	c.inspector = registry.PageDefinition{
		ID:       configutil.OptionalString(cc, "inspector_page_id", InspectorPageID),
		Title:    configutil.OptionalString(cc, "inspector_title", defaultInspectorTitle),
		Route:    configutil.OptionalString(cc, "inspector_route", InspectorRoute),
		Renderer: InspectorRenderer,
		Icon:     configutil.OptionalString(cc, "inspector_icon", "search"),
		Order:    configutil.OptionalInt(cc, "inspector_order", 20),
	}
	c.dashboard = registry.PageDefinition{
		ID:       configutil.OptionalString(cc, "dashboard_page_id", DashboardPageID),
		Title:    configutil.OptionalString(cc, "dashboard_title", defaultDashboardTitle),
		Route:    configutil.OptionalString(cc, "dashboard_route", DashboardRoute),
		Renderer: DashboardRenderer,
		Icon:     configutil.OptionalString(cc, "dashboard_icon", "dashboard"),
		Order:    configutil.OptionalInt(cc, "dashboard_order", 10),
	}
	if c.inspector.ID == "" {
		c.inspector.ID = InspectorPageID
	}
	if c.inspector.Route == "" {
		c.inspector.Route = InspectorRoute
	}
	if c.dashboard.ID == "" {
		c.dashboard.ID = DashboardPageID
	}
	if c.dashboard.Route == "" {
		c.dashboard.Route = DashboardRoute
	}
	return c, nil
}

func (c *Component) Name() string { return fmt.Sprintf("runtime-console:%s", c.id) }

func (c *Component) Inject() []coreruntime.Dependency {
	deps := []coreruntime.Dependency{
		coreruntime.Requires(host.HubKey),
		coreruntime.Requires(host.UIHostKey),
	}
	if c.withDiagnostics {
		deps = append(deps, coreruntime.Requires(devtools.ServiceKey))
	}
	return deps
}

func (c *Component) Provide() []coreruntime.Capability { return nil }

func hubError(err error) *hub.Error {
	return &hub.Error{Code: "runtime_snapshot_unavailable", Message: err.Error()}
}

func (c *Component) snapshotQuery(ctx context.Context, _ url.Values) (any, *hub.Error) {
	snap, err := c.rt.Snapshot(ctx)
	if err != nil {
		return nil, hubError(err)
	}
	return SnapshotFrom(snap), nil
}

func (c *Component) eventsQuery(_ context.Context, _ url.Values) (any, *hub.Error) {
	return c.ring.DTO(), nil
}

func (c *Component) dashboardQuery(ctx context.Context, _ url.Values) (any, *hub.Error) {
	snap, err := c.rt.Snapshot(ctx)
	if err != nil {
		return nil, hubError(err)
	}
	var diagnostics []DiagnosticDTO
	if c.withDiagnostics {
		// The capability is resolved once at Apply; diagnostics are asked to
		// describe the framework itself, not a specific external manifest.
		if c.diagnostics != nil {
			diagnostics = DiagnosticsFrom(c.diagnostics.Diagnose(pluginmeta.Manifest{}))
		}
	}
	return DashboardFrom(snap, c.ring.DTO(), diagnostics), nil
}

func (c *Component) Apply(ctx *coreruntime.Context) (runtimeCleanup coreruntime.Cleanup, applyErr error) {
	h, err := coreruntime.Require(ctx, host.HubKey)
	if err != nil {
		return nil, err
	}
	ui, err := coreruntime.Require(ctx, host.UIHostKey)
	if err != nil {
		return nil, err
	}
	if c.withDiagnostics {
		svc, err := coreruntime.Require(ctx, devtools.ServiceKey)
		if err != nil {
			return nil, err
		}
		c.diagnostics = svc
	}

	owner := registry.ContributionOwner{
		PluginID:     "runtime-console",
		ComponentID:  c.id,
		ActivationID: activationLabel(),
	}
	// Query undo is owner-scoped in the hub; include the activation label so a
	// stale undo can never remove a newer activation's registration.
	queryOwner := hubOwner + ":" + owner.ActivationID
	queries := []struct {
		name    string
		handler hub.QueryHandler
	}{
		{SnapshotQuery, c.snapshotQuery},
		{EventsQuery, c.eventsQuery},
		{DashboardQuery, c.dashboardQuery},
	}
	if err := ctx.Effect(func() (func() error, error) {
		var undos []func() error
		undoAll := func() error {
			var errs []error
			for i := len(undos) - 1; i >= 0; i-- {
				if err := undos[i](); err != nil {
					errs = append(errs, err)
				}
			}
			return errors.Join(errs...)
		}
		for _, q := range queries {
			undo, err := h.RegisterQuery(q.name, queryOwner, q.handler)
			if err != nil {
				_ = undoAll()
				return nil, err
			}
			undos = append(undos, undo)
		}
		for _, page := range []registry.PageDefinition{c.dashboard, c.inspector} {
			undo, err := ui.RegisterPage(owner, page)
			if err != nil {
				_ = undoAll()
				return nil, err
			}
			undos = append(undos, undo)
		}
		return undoAll, nil
	}); err != nil {
		return nil, err
	}
	return nil, nil
}
