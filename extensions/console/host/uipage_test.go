package host_test

// UI-page component conformance: the factory behind DiscoverPlugins' ui-page
// rows — a declared page is a composition contribution (Effect-owned),
// validated at construction, owned by its activation, and withdrawn on
// dispose. Paper anchor §5.2.1: the declaration persists, the fiber (and its
// contribution) comes and goes with the enabled switch.
import (
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/runtime"
)

func uiPageConfig(id string, cfg map[string]any) config.ComponentConfig {
	return config.ComponentConfig{ID: id, Type: "ui-page", Config: cfg}
}

func TestUIPageComponentDefaultsAndValidation(t *testing.T) {
	// Default page_id strips the "<plugin>:page:" prefix DiscoverPlugins uses.
	c, err := host.NewUIPageComponent(uiPageConfig("alarm:page:alarms", map[string]any{
		"title": "告警", "route": "/alarms", "renderer": "alarm-console",
	}))
	if err != nil {
		t.Fatal(err)
	}
	page := c.Page()
	if page.ID != "alarms" || page.Title != "告警" || page.Route != "/alarms" || page.Renderer != "alarm-console" {
		t.Fatalf("page = %+v", page)
	}
	if page.Order != 100 {
		t.Fatalf("default order = %d, want 100", page.Order)
	}

	// Explicit keys override the defaults; a views stack makes the renderer
	// optional (the generic stack path).
	cv, err := host.NewUIPageComponent(uiPageConfig("custom", map[string]any{
		"page_id": "alarms2", "title": "告警 2", "route": "/alarms2", "description": "d",
		"icon": "bell", "order": 3,
		"views": []any{map[string]any{"kind": "table", "query": "alarms"}},
	}))
	if err != nil {
		t.Fatal(err)
	}
	p2 := cv.Page()
	if p2.ID != "alarms2" || p2.Renderer != "" || p2.Order != 3 || len(p2.Views) == 0 {
		t.Fatalf("page = %+v views=%s", p2, p2.Views)
	}

	// Invalid declarations fail loudly at construction — nothing reaches the
	// runtime half-built.
	for name, cfg := range map[string]map[string]any{
		"missing title":            {"route": "/x", "renderer": "r"},
		"missing route":            {"title": "t", "renderer": "r"},
		"relative route":           {"title": "t", "route": "alarms", "renderer": "r"},
		"no renderer and no views": {"title": "t", "route": "/x"},
	} {
		if _, err := host.NewUIPageComponent(uiPageConfig("bad", cfg)); err == nil {
			t.Fatalf("%s: must be rejected", name)
		}
	}

	// follows: accepted when well-formed (key name safety), rejected with
	// separators; a declared follows adds the liveness dependency.
	c2, err := host.NewUIPageComponent(uiPageConfig("alarm:page:p", map[string]any{
		"title": "t", "route": "/t", "renderer": "r", "follows": "alarm",
	}))
	if err != nil {
		t.Fatal(err)
	}
	if len(c2.Inject()) != 2 {
		t.Fatalf("follows page must declare two dependencies, got %+v", c2.Inject())
	}
	if _, err := host.NewUIPageComponent(uiPageConfig("bad", map[string]any{
		"title": "t", "route": "/t", "renderer": "r", "follows": "a/b",
	})); err == nil {
		t.Fatal("separated follows must be rejected")
	}
}

func TestUIPageComponentLifecycle(t *testing.T) {
	rt, _, hf := newHostStack(t)
	_ = rt

	comp, err := host.NewUIPageComponent(uiPageConfig("alarm:page:alarms", map[string]any{
		"title": "告警", "route": "/alarms", "renderer": "alarm-console", "order": 7,
	}))
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, f, runtime.StateActive)

	pages, ue := hf.ListPages()
	if ue != nil || len(pages.Pages) != 1 || pages.Pages[0].ID != "alarms" || pages.Pages[0].Route != "/alarms" {
		t.Fatalf("pages = %+v err=%+v, want [alarms]", pages, ue)
	}

	// Disable → the contribution withdraws with the activation.
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctxT2(t)); err != nil {
		t.Fatal(err)
	}
	if pages, _ = hf.ListPages(); len(pages.Pages) != 0 {
		t.Fatalf("pages after withdraw = %+v, want empty", pages.Pages)
	}

	// Re-enable (declaration unchanged, new activation) → the page returns.
	f2, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	hostWaitState(t, f2, runtime.StateActive)
	if pages, _ = hf.ListPages(); len(pages.Pages) != 1 || pages.Pages[0].ID != "alarms" {
		t.Fatalf("pages after re-enable = %+v, want [alarms]", pages.Pages)
	}
}
