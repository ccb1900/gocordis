package host_test

import (
	"context"
	"encoding/json"
	"net/url"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	appui "dynamic-runtime/extensions/console/registry"
	"dynamic-runtime/runtime"
)

// Console host conformance: the host component provides the composition
// registry + hub capabilities per activation, contribution plugins register
// pages/panels through Effect-owned cleanup, bridge queries route to
// application handlers — all without the host knowing any domain vocabulary.

type pagePlugin struct{ id, title string }

func (c *pagePlugin) Name() string { return "page-plugin:" + c.id }
func (c *pagePlugin) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(host.UIHostKey)}
}
func (c *pagePlugin) Provide() []runtime.Capability { return nil }
func (c *pagePlugin) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	reg, err := runtime.Require(ctx, host.UIHostKey)
	if err != nil {
		return nil, err
	}
	unregister, err := reg.RegisterPage(
		appui.ContributionOwner{PluginID: c.id, ComponentID: c.id, ActivationID: "test-activation"},
		appui.PageDefinition{ID: c.id, Title: c.title, Route: "/" + c.id, Renderer: c.id},
	)
	if err != nil {
		return nil, err
	}
	return func() error { return unregister() }, nil
}

type bridgePlugin struct {
	seen chan url.Values
}

func (c *bridgePlugin) Name() string { return "bridge-plugin" }
func (c *bridgePlugin) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(host.HubKey)}
}
func (c *bridgePlugin) Provide() []runtime.Capability { return nil }
func (c *bridgePlugin) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	h, err := runtime.Require(ctx, host.HubKey)
	if err != nil {
		return nil, err
	}
	if _, err := h.RegisterQuery("stats", "bridge-plugin", func(_ context.Context, params url.Values) (any, *hub.Error) {
		if c.seen != nil {
			select {
			case c.seen <- params:
			default:
			}
		}
		return map[string]any{"total": 3}, nil
	}); err != nil {
		return nil, err
	}
	unregisterCmd, err := h.RegisterCommand("reset", "bridge-plugin", func(_ context.Context, _ json.RawMessage) error {
		return nil
	})
	if err != nil {
		return nil, err
	}
	return runtime.Cleanup(unregisterCmd), nil
}

type hostConsumer struct {
	out chan *host.Host
}

func (c *hostConsumer) Name() string { return "host-consumer" }
func (c *hostConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(host.UIHostKey), runtime.Requires(host.HubKey)}
}
func (c *hostConsumer) Provide() []runtime.Capability { return nil }
func (c *hostConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	reg, err := runtime.Require(ctx, host.UIHostKey)
	if err != nil {
		return nil, err
	}
	h, err := runtime.Require(ctx, host.HubKey)
	if err != nil {
		return nil, err
	}
	c.out <- host.NewHost(ctx.Context(), reg, h)
	return nil, nil
}

func hostWaitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout %s -> %v (state %v err %v)", f.Name(), want, f.State(), f.Err())
}

func newHostStack(t *testing.T) (*runtime.Runtime, *host.UIComponent, *host.Host) {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	comp, err := host.NewConsole(config.ComponentConfig{ID: "console-host"})
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, f, runtime.StateActive)
	return rt, comp, comp.HostAdapter()
}

func mustRequireHost(t *testing.T, rt *runtime.Runtime) *host.Host {
	t.Helper()
	res := make(chan *host.Host, 1)
	f, err := rt.Load(&hostConsumer{out: res})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, f, runtime.StateActive)
	_ = f.Dispose()
	select {
	case h := <-res:
		return h
	case <-time.After(5 * time.Second):
		t.Fatal("timeout requiring host capability")
		return nil
	}
}

// H-01 — the host provides registry + hub capabilities; a page plugin's
// contribution is visible through the transport while its activation is
// live, and disappears with it (Effect-owned cleanup).
func TestHostCompositionLifecycle(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT2(t))

	hostFiber, err := rt.Load(&host.UIComponent{})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, hostFiber, runtime.StateActive)

	pp, err := rt.Load(&pagePlugin{id: "fleet-x", title: "Fleet X"})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, pp, runtime.StateActive)

	hf := mustRequireHost(t, rt)
	pages, ue := hf.ListPages()
	if ue != nil {
		t.Fatalf("ListPages error: %+v", ue)
	}
	if len(pages.Pages) != 1 || pages.Pages[0].ID != "fleet-x" {
		t.Fatalf("pages = %+v, want [fleet-x]", pages.Pages)
	}

	if err := pp.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pp.Gone(ctxT2(t)); err != nil {
		t.Fatal(err)
	}
	if pages, _ = hf.ListPages(); len(pages.Pages) != 0 {
		t.Fatalf("pages after withdraw = %+v, want empty", pages.Pages)
	}
}

// H-02 — bridge queries route to application handlers with params forwarded.
func TestHostBridgeQuery(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT2(t))

	hostFiber, err := rt.Load(&host.UIComponent{})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, hostFiber, runtime.StateActive)

	paramsSeen := make(chan url.Values, 1)
	bpF, err := rt.Load(&bridgePlugin{seen: paramsSeen})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, bpF, runtime.StateActive)

	hf := mustRequireHost(t, rt)
	res, ue := hf.Query("stats", url.Values{"limit": []string{"5"}})
	if ue != nil {
		t.Fatalf("query error: %+v", ue)
	}
	if m, ok := res.(map[string]any); !ok || m["total"] != 3 {
		t.Fatalf("query result = %v", res)
	}
	select {
	case p := <-paramsSeen:
		if p.Get("limit") != "5" {
			t.Fatalf("params = %v", p)
		}
	default:
		t.Fatal("params not forwarded to the handler")
	}

	if _, ue = hf.Query("ghost", nil); ue == nil {
		t.Fatal("unknown query must return an error")
	}
}

func ctxT2(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// ---------------------------------------------------------------------------
// Transport / observation conformance (H-03..H-06)
// ---------------------------------------------------------------------------

// H-03 — observation bridge: listeners receive hub-routed observations;
// unsubscribe stops delivery; history is retained for diagnostics.
func TestHostObservationBridge(t *testing.T) {
	rt, comp, _ := newHostStack(t)

	var mu sync.Mutex
	var got []host.UIObservation
	unsub, err := comp.OnObservation(func(ev host.UIObservation) {
		mu.Lock()
		got = append(got, ev)
		mu.Unlock()
	})
	if err != nil {
		t.Fatal(err)
	}

	// A page contribution triggers composition.changed through the hub intake.
	pp, err := rt.Load(&pagePlugin{id: "obs-page", title: "Obs"})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, pp, runtime.StateActive)
	time.Sleep(50 * time.Millisecond)

	mu.Lock()
	if len(got) == 0 {
		mu.Unlock()
		t.Fatal("composition.changed not delivered before unsubscribe")
	}
	mu.Unlock()

	// Unsubscribe, then discard pre-unsub deliveries: from here the detached
	// listener must receive NOTHING. The 50ms grace window turns a broken
	// unsubscribe into a deterministic failure.
	unsub()
	mu.Lock()
	got = nil
	mu.Unlock()

	// A FRESH listener subscribed after the unsubscribe must still receive
	// events (proves events are flowing).
	var freshGot []host.UIObservation
	freshUnsub, err := comp.OnObservation(func(ev host.UIObservation) {
		mu.Lock()
		freshGot = append(freshGot, ev)
		mu.Unlock()
	})
	if err != nil {
		t.Fatal(err)
	}
	defer freshUnsub()

	pp2, err := rt.Load(&pagePlugin{id: "obs-page-2", title: "Obs2"})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, pp2, runtime.StateActive)
	time.Sleep(50 * time.Millisecond)

	mu.Lock()
	defer mu.Unlock()
	if len(freshGot) == 0 {
		t.Fatal("fresh listener received nothing (events not flowing)")
	}
	if len(got) != 0 {
		t.Fatalf("unsubscribed listener still received %+v (bridge history: %+v)", got, comp.Observations())
	}
}

// H-04 — production sink handover: with SetObservationSink, observations go
// to the sink and the in-process bridge listeners are bypassed.
func TestHostSinkHandover(t *testing.T) {
	rt, comp, _ := newHostStack(t)

	sink := &collectSink{}
	comp.SetObservationSink(sink)

	bridgeGot := 0
	unsub, err := comp.OnObservation(func(host.UIObservation) { bridgeGot++ })
	if err != nil {
		t.Fatal(err)
	}
	defer unsub()

	pp, err := rt.Load(&pagePlugin{id: "sink-page", title: "Sink"})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, pp, runtime.StateActive)
	time.Sleep(50 * time.Millisecond)

	if n := len(sink.all()); n == 0 {
		t.Fatal("production sink received nothing")
	}
	if bridgeGot != 0 {
		t.Fatalf("bridge listener received %d events under sink mode, want 0", bridgeGot)
	}
}

// H-05 — fleet self-contribution: with peers configured, the console
// contributes its own Fleet page (contribution-driven, no hard-coded nav).
func TestHostFleetSelfContribution(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(ctxT2(t))

	comp, err := host.NewConsole(config.ComponentConfig{
		ID: "console-host",
		Config: map[string]any{
			"host_id":     "node-a",
			"fleet_peers": []any{"http://peer-1", "http://peer-2"},
		},
	})
	if err != nil {
		t.Fatal(err)
	}
	if comp.HostID() != "node-a" {
		t.Fatalf("HostID = %q", comp.HostID())
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, f, runtime.StateActive)

	pages, ue := comp.HostAdapter().ListPages()
	if ue != nil {
		t.Fatal(ue)
	}
	found := false
	for _, p := range pages.Pages {
		if p.ID == "fleet" {
			found = true
		}
	}
	if !found {
		t.Fatalf("fleet self-contribution missing: %+v", pages.Pages)
	}
}

// H-06 — query/command routing through the transport adapter: unknown names
// are not_found; known names reach the application handler; Names() lists
// them.
func TestHostQueryCommandRouting(t *testing.T) {
	rt, _, hf := newHostStack(t)

	bpF, err := rt.Load(&bridgePlugin{})
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, bpF, runtime.StateActive)

	if _, ue := hf.Query("nope", nil); ue == nil || ue.Code != "not_found" {
		t.Fatalf("unknown query ue = %+v, want not_found", ue)
	}
	if ue := hf.Command("nope", nil); ue == nil || ue.Code != "not_found" {
		t.Fatalf("unknown command ue = %+v, want not_found", ue)
	}

	res, ue := hf.Query("stats", url.Values{})
	if ue != nil {
		t.Fatalf("query: %+v", ue)
	}
	if m, ok := res.(map[string]any); !ok || m["total"] != 3 {
		t.Fatalf("query result = %v", res)
	}
	if ue := hf.Command("reset", []byte(`{}`)); ue != nil {
		t.Fatalf("command: %+v", ue)
	}

	queries, commands := hf.Names()
	qOk, cOk := false, false
	for _, q := range queries {
		if q == "stats" {
			qOk = true
		}
	}
	for _, c := range commands {
		if c == "reset" {
			cOk = true
		}
	}
	if !qOk || !cOk {
		t.Fatalf("Names() = %v / %v, want stats in both", queries, commands)
	}
}

type collectSink struct {
	mu  sync.Mutex
	evs []host.UIObservation
}

func (c *collectSink) NotifyObservation(ev host.UIObservation) {
	c.mu.Lock()
	defer c.mu.Unlock()
	c.evs = append(c.evs, ev)
}

func (c *collectSink) all() []host.UIObservation {
	c.mu.Lock()
	defer c.mu.Unlock()
	return append([]host.UIObservation(nil), c.evs...)
}
