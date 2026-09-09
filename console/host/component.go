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
	"sync"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	"dynamic-runtime/console/hub"
	appui "dynamic-runtime/console/registry"
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
	return nil, nil
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
	c.emitUIObservation(UIObservation(ev))
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

// NewConsole creates the console host component. It has no required config.
func NewConsole(cc config.ComponentConfig) (*UIComponent, error) {
	return &UIComponent{}, nil
}
