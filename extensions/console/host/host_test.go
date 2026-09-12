package host_test

import (
	"context"
	"net/url"
	"testing"
	"time"

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
	return h.RegisterQuery("stats", "bridge-plugin", func(_ context.Context, params url.Values) (any, *hub.Error) {
		if c.seen != nil {
			select {
			case c.seen <- params:
			default:
			}
		}
		return map[string]any{"total": 3}, nil
	})
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
