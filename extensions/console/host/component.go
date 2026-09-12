// Package host is the GOCORDIS console host. It owns one UI Composition
// Registry and one hub (named queries/commands/observations) per activation
// and exposes both as Runtime Capabilities. Business Pages/Panels are
// registered by independent UI Contribution plugins; data and commands are
// registered by application bridge components. This host never enumerates a
// business plugin, never owns a default page set, and knows no domain
// vocabulary.
package host

import (
	"context"
	"errors"
	"fmt"
	"path/filepath"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	"dynamic-runtime/extensions/console/configutil"
	"dynamic-runtime/extensions/console/hub"
	appui "dynamic-runtime/extensions/console/registry"
)

// UIHostKey is the UI Composition Registry capability exposed by the console
// host. Independent UI Contribution plugins require this key and register
// their own Pages/Panels through Effect-owned cleanup.
var UIHostKey = runtime.NewKey[appui.Registry]("ui.host")

// HubKey is the console hub capability: named queries, named commands, and
// the observation intake. Application bridge components require it and
// register their domain vocabulary during their own activation.
var HubKey = runtime.NewKey[*hub.Registry]("console.hub")

// CompositionChangedType is the Observation type used when composition has
// changed. It carries no full page/panel state; clients invalidate and
// re-query.
const CompositionChangedType = "composition.changed"

// ObservationSink is the production observation emitter boundary. The real
// Wails Host implements it with runtime.EventsEmit("observation", ...); the
// in-process bridge is the test adapter. The console never owns the sink.
type ObservationSink interface {
	NotifyObservation(ev UIObservation)
}

// UIComponent is the console host component. It requires nothing — the
// dependency direction points the other way: application bridges require its
// hub capability and register their vocabulary as Effects owned by their own
// activations.
type UIComponent struct {
	mu sync.Mutex

	registry      appui.Registry
	hub           *hub.Registry
	bridge        *observationBridge
	sink          ObservationSink
	hostAdapter   *Host
	baseCtx       context.Context
	invalidations int

	// identity + fleet (fleet self-description; see /api/meta and /api/fleet)
	hostID     string
	fleetPeers []string

	// plugin client modules: frontend renderer modules distributed with a
	// plugin and served same-origin under /client-modules/<name> (see
	// ClientModule). The console loads them at boot and hands them the
	// renderer registry — the frontend is homogeneous; a module poses no
	// risk beyond the plugin itself.
	clientModules []ClientModule
}

// ClientModule is one plugin client module: a same-origin served ES module
// whose default export registers renderers into the console.
type ClientModule struct {
	// Name is the URL slug (no separators — traversal-proof by shape).
	Name string
	// Path is the file location, resolved at serve time.
	Path string
}

func (c *UIComponent) Name() string                 { return "console:host" }
func (c *UIComponent) Inject() []runtime.Dependency { return nil }
func (c *UIComponent) Provide() []runtime.Capability {
	return []runtime.Capability{UIHostKey.Capability(), HubKey.Capability()}
}

func (c *UIComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.hub = hub.New()
	// The registry belongs to this console activation. Contributions arrive
	// later from independent plugins; no page/panel is hard-coded here.
	c.registry = appui.NewRegistry(c.emitCompositionChanged)
	c.bridge = newObservationBridge()
	c.baseCtx = ctx.Context()
	c.hostAdapter = NewHost(c.baseCtx, c.registry, c.hub)
	if err := runtime.Provide(ctx, UIHostKey, c.registry); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, HubKey, c.hub); err != nil {
		return nil, err
	}
	// Observations published into the hub fan out to the bridge and sink.
	unsub, err := c.hub.OnObservation(16, c.onObservation)
	if err != nil {
		return nil, err
	}
	if err := ctx.Effect(func() (func() error, error) {
		return func() error {
			unsub()
			if c.bridge != nil {
				c.bridge.clear()
			}
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	// Fleet self-contribution: with peers configured, the console contributes
	// its own Fleet page — contribution-driven like every other page, no
	// hard-coded navigation anywhere.
	if len(c.fleetPeers) > 0 {
		owner := appui.ContributionOwner{
			PluginID:     "console",
			ComponentID:  "console:host",
			ActivationID: activationLabel(),
		}
		page := appui.PageDefinition{
			ID: "fleet", Title: "Fleet", Route: "/fleet", Renderer: "fleet", Order: 90,
		}
		unregister, err := c.registry.RegisterPage(owner, page)
		if err != nil {
			return nil, err
		}
		if err := ctx.Effect(func() (func() error, error) { return unregister, nil }); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

var activationCounter atomic.Int64

// activationLabel is a process-unique generation label for self-contributions
// (no Kernel identity is invented; ownership stays with this activation).
func activationLabel() string {
	return fmt.Sprintf("console:%d", activationCounter.Add(1))
}

func (c *UIComponent) emitUIObservation(ev UIObservation) {
	if c.sink != nil {
		c.sink.NotifyObservation(ev)
	} else if c.bridge != nil {
		// test adapter only; production path uses SetObservationSink.
		c.bridge.notify(ev)
	}
}

func (c *UIComponent) emitCompositionChanged() {
	// Startup composition is fetched directly by clients. Only emit an
	// observation when a production sink or a live listener can consume it;
	// the event never carries state.
	if c.sink == nil && c.bridge != nil && !c.bridge.hasSubscribers() {
		return
	}
	c.emitUIObservation(UIObservation{
		Type:      CompositionChangedType,
		Timestamp: time.Now().UTC().Format(time.RFC3339),
	})
}

func (c *UIComponent) onObservation(ev hub.Observation) {
	c.mu.Lock()
	c.invalidations++
	c.mu.Unlock()
	c.emitUIObservation(UIObservation{
		Type:      ev.Type,
		SourceID:  ev.SourceID,
		Timestamp: ev.Timestamp,
		Message:   ev.Message,
	})
}

// Invalidations returns how many Observation-driven refreshes ran.
func (c *UIComponent) Invalidations() int {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.invalidations
}

// Registry returns the UI Host composition registry.
func (c *UIComponent) Registry() appui.Registry { return c.registry }

// HostAdapter returns the transport adapter (hub forwarding + composition
// DTOs). A Wails App binds its methods; the web server wraps it.
func (c *UIComponent) HostAdapter() *Host { return c.hostAdapter }

// OnObservation registers an observation listener. It returns an unsubscribe
// function; lifecycle is Effect-owned on the Component.
func (c *UIComponent) OnObservation(handler func(UIObservation)) (func() error, error) {
	if c.bridge == nil {
		return nil, errors.New("console: observation bridge not active")
	}
	return c.bridge.on(handler)
}

// SetObservationSink switches observation delivery to the production sink
// (real Wails EventsEmit). The in-process bridge remains a test adapter.
func (c *UIComponent) SetObservationSink(sink ObservationSink) {
	c.mu.Lock()
	c.sink = sink
	c.mu.Unlock()
}

// Observations returns the observation events seen since activation (feed
// history, newest last).
// PublishObservation injects an application-side observation into the hub:
// boundary events (composition apply failures, recovery notes) that do not
// originate from the query provider's invalidation stream.
func (c *UIComponent) PublishObservation(typ, sourceID, message string) {
	c.mu.Lock()
	hubRegistry := c.hub
	c.mu.Unlock()
	if hubRegistry == nil {
		return
	}
	hubRegistry.Publish(hub.Observation{
		Type:      typ,
		SourceID:  sourceID,
		Timestamp: time.Now().UTC().Format(time.RFC3339),
		Message:   message,
	})
}

func (c *UIComponent) Observations() []UIObservation {
	if c.bridge == nil {
		return nil
	}
	return c.bridge.latest()
}

// Pages returns the registered UI pages from the isolated Composition Snapshot.
func (c *UIComponent) Pages() []PageDefinition { return c.registry.Snapshot().Pages }

// Panels returns the registered UI panels from the isolated Composition Snapshot.
func (c *UIComponent) Panels() []PanelDefinition { return c.registry.Snapshot().Panels }

// NewConsole creates the console host component.
//
// Recognized config keys:
//
//	host_id        — stable identity reported by /api/meta (default: hostname)
//	fleet_peers    — base URLs of peer consoles; when non-empty the host
//	                 self-registers a Fleet page (renderer "fleet") whose view
//	                 aggregates the peers' /api/meta
//	client_modules — [[components.config.client_modules]] rows {name, path}:
//	                 plugin frontend modules served same-origin under
//	                 /client-modules/<name> and registered by the console at
//	                 boot. name defaults to the file's basename without
//	                 extension and must not contain separators.
func NewConsole(cc config.ComponentConfig) (*UIComponent, error) {
	c := &UIComponent{
		hostID:     configutil.OptionalString(cc, "host_id", ""),
		fleetPeers: configutil.OptionalStringSlice(cc, "fleet_peers", nil),
	}
	modules, err := parseClientModules(cc.Config["client_modules"])
	if err != nil {
		return nil, err
	}
	c.clientModules = modules
	return c, nil
}

// ClientModules returns the configured plugin client modules.
func (c *UIComponent) ClientModules() []ClientModule {
	return append([]ClientModule(nil), c.clientModules...)
}

// parseClientModules validates the client_modules config rows. Names are
// URL slugs: one default from the file basename, and never a separator —
// the served URL is derived from the name alone, so traversal by shape is
// impossible.
func parseClientModules(raw any) ([]ClientModule, error) {
	if raw == nil {
		return nil, nil
	}
	rows, ok := raw.([]any)
	if !ok {
		return nil, fmt.Errorf("client_modules must be an array of tables")
	}
	out := make([]ClientModule, 0, len(rows))
	seen := map[string]bool{}
	for i, r := range rows {
		m, ok := r.(map[string]any)
		if !ok {
			return nil, fmt.Errorf("client_modules #%d must be a table", i)
		}
	path, _ := m["path"].(string)
	if path == "" {
		return nil, fmt.Errorf("client_modules #%d: path is required", i)
	}
	name, _ := m["name"].(string)
	if name == "" {
			base := filepath.Base(filepath.ToSlash(path))
			if ext := filepath.Ext(base); ext != "" {
				base = strings.TrimSuffix(base, ext)
			}
			name = base
		}
		if name == "" || strings.ContainsAny(name, "/\\") || strings.Contains(name, "..") {
			return nil, fmt.Errorf("client_modules #%d: invalid module name %q", i, name)
		}
		if seen[name] {
			return nil, fmt.Errorf("client_modules: duplicate module name %q", name)
		}
		seen[name] = true
		out = append(out, ClientModule{Name: name, Path: path})
	}
	return out, nil
}

// HostID returns the configured identity ("" = caller falls back to hostname).
func (c *UIComponent) HostID() string { return c.hostID }

// FleetPeers returns the configured peer console base URLs.
func (c *UIComponent) FleetPeers() []string { return append([]string(nil), c.fleetPeers...) }
