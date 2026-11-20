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
	mu sync.RWMutex

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
}

func (c *UIComponent) Name() string                 { return "console:host" }
func (c *UIComponent) Inject() []runtime.Dependency { return nil }
func (c *UIComponent) Provide() []runtime.Capability {
	return []runtime.Capability{UIHostKey.Capability(), HubKey.Capability()}
}

func (c *UIComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	h := hub.New()
	// The registry belongs to this console activation. Contributions arrive
	// later from independent plugins; no page/panel is hard-coded here. The
	// onChange closure captures the bridge VARIABLE (assigned below) so the
	// registration callbacks use the activation-local references.
	var bridge *observationBridge
	registry := appui.NewRegistry(func() { c.emitCompositionChangedWith(bridge) })
	bridge = newObservationBridge()
	baseCtx := ctx.Context()
	hostAdapter := NewHost(baseCtx, registry, h)
	if err := runtime.Provide(ctx, UIHostKey, registry); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, HubKey, h); err != nil {
		return nil, err
	}
	// Observations published into the hub fan out to the bridge and sink.
	// The subscription is created INSIDE the Effect install (P2-4): a refused
	// Effect (context already closing) must leave nothing behind — the old
	// order spawned the hub delivery goroutine before admission, leaking it
	// permanently when admission failed.
	if err := ctx.Effect(func() (func() error, error) {
		unsub, err := h.OnObservation(16, c.onObservation)
		if err != nil {
			return nil, err
		}
		return func() error {
			unsub()
			bridge.clear()
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	// Fleet self-contribution: with peers configured, the console contributes
	// its own Fleet page — contribution-driven like every other page, no
	// hard-coded navigation anywhere. Registered inside the Effect install for
	// the same admission-atomicity reason (a refused Effect must not leave a
	// page in the registry nor fire a composition-changed event for it).
	if len(c.fleetPeers) > 0 {
		owner := appui.ContributionOwner{
			PluginID:     "console",
			ComponentID:  "console:host",
			ActivationID: activationLabel(),
		}
		page := appui.PageDefinition{
			ID: "fleet", Title: "Fleet", Route: "/fleet", Renderer: "fleet", Order: 90,
		}
		if err := ctx.Effect(func() (func() error, error) {
			unregister, err := registry.RegisterPage(owner, page)
			if err != nil {
				return nil, err
			}
			return unregister, nil
		}); err != nil {
			return nil, err
		}
	}
	// Publish the wiring under the component mutex (P2-3): every accessor and
	// callback reads these fields under c.mu; registration callbacks above run
	// against the LOCAL references, so no c.mu critical section in Apply
	// overlaps with a synchronous registry onChange (which would deadlock on
	// the non-reentrant mutex).
	c.mu.Lock()
	c.hub, c.registry, c.bridge, c.baseCtx, c.hostAdapter = h, registry, bridge, baseCtx, hostAdapter
	c.mu.Unlock()
	return nil, nil
}

var activationCounter atomic.Int64

// activationLabel is a process-unique generation label for self-contributions
// (no Kernel identity is invented; ownership stays with this activation).
func activationLabel() string {
	return fmt.Sprintf("console:%d", activationCounter.Add(1))
}

func (c *UIComponent) emitUIObservation(ev UIObservation) {
	c.mu.RLock()
	sink, bridge := c.sink, c.bridge
	c.mu.RUnlock()
	if sink != nil {
		sink.NotifyObservation(ev)
	} else if bridge != nil {
		// test adapter only; production path uses SetObservationSink.
		bridge.notify(ev)
	}
}

// emitCompositionChangedWith is the registry onChange callback. It captures
// the activation-local bridge (created in Apply) so a synchronous
// registration callback never needs c.mu while Apply may be mid-wiring; the
// production sink is still read dynamically under c.mu.
func (c *UIComponent) emitCompositionChangedWith(bridge *observationBridge) {
	c.mu.RLock()
	sink := c.sink
	c.mu.RUnlock()
	if sink == nil && (bridge == nil || !bridge.hasSubscribers()) {
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
func (c *UIComponent) Registry() appui.Registry {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.registry
}

// HostAdapter returns the transport adapter (hub forwarding + composition
// DTOs). A Wails App binds its methods; the web server wraps it.
func (c *UIComponent) HostAdapter() *Host {
	c.mu.RLock()
	defer c.mu.RUnlock()
	return c.hostAdapter
}

// OnObservation registers an observation listener. It returns an unsubscribe
// function; lifecycle is Effect-owned on the Component.
func (c *UIComponent) OnObservation(handler func(UIObservation)) (func() error, error) {
	c.mu.RLock()
	bridge := c.bridge
	c.mu.RUnlock()
	if bridge == nil {
		return nil, errors.New("console: observation bridge not active")
	}
	return bridge.on(handler)
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
	c.mu.RLock()
	bridge := c.bridge
	c.mu.RUnlock()
	if bridge == nil {
		return nil
	}
	return bridge.latest()
}

// Pages returns the registered UI pages from the isolated Composition Snapshot.
func (c *UIComponent) Pages() []PageDefinition {
	c.mu.RLock()
	registry := c.registry
	c.mu.RUnlock()
	if registry == nil {
		return nil
	}
	return registry.Snapshot().Pages
}

// Panels returns the registered UI panels from the isolated Composition Snapshot.
func (c *UIComponent) Panels() []PanelDefinition {
	c.mu.RLock()
	registry := c.registry
	c.mu.RUnlock()
	if registry == nil {
		return nil
	}
	return registry.Snapshot().Panels
}

// NewConsole creates the console host component.
//
// Recognized config keys:
//
//	host_id        — stable identity reported by /api/meta (default: hostname)
//	fleet_peers    — base URLs of peer consoles; when non-empty the host
//	                 self-registers a Fleet page (renderer "fleet") whose view
//	                 aggregates the peers' /api/meta
//
// Plugin frontend modules are NOT configured here: they are ui-client
// components (see ClientModuleComponent) — composition-governed, visible in
// the plugin explorer, removable, honoring the enabled switch.
func NewConsole(cc config.ComponentConfig) (*UIComponent, error) {
	c := &UIComponent{
		hostID:     configutil.OptionalString(cc, "host_id", ""),
		fleetPeers: configutil.OptionalStringSlice(cc, "fleet_peers", nil),
	}
	return c, nil
}

// HostID returns the configured identity ("" = caller falls back to hostname).
func (c *UIComponent) HostID() string { return c.hostID }

// FleetPeers returns the configured peer console base URLs.
func (c *UIComponent) FleetPeers() []string { return append([]string(nil), c.fleetPeers...) }
