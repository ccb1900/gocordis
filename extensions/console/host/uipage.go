package host

import (
	"encoding/json"
	"fmt"
	"strings"
	"sync/atomic"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/configutil"
	appui "dynamic-runtime/extensions/console/registry"
	"dynamic-runtime/runtime"
)

// UIPageComponent is the composition-governed form of a plugin page
// declaration: one ui-page component declares one console page. It is the
// factory for the ui-page rows DiscoverPlugins expands from a plugin
// manifest's [[pages]] entries — with it registered in the application's
// factory registry, a declared page appears in the composition and vanishes
// with the plugin's switch, exactly like every other contribution. The
// declaration is data: the renderer name is resolved by the console frontend,
// never executable here.
//
// Config:
//
//	page_id     — page id. Default: the component id with its
//	              "<plugin>:page:" prefix removed (the DiscoverPlugins shape).
//	title       — menu title. Required.
//	route       — route, must start with "/". Required.
//	description — optional helper text.
//	renderer    — renderer identity resolved by the console frontend. Required
//	              unless the page declares views (the generic stack path).
//	icon        — optional menu icon hint.
//	order       — composition order (default 100).
//	views       — optional declarative view block stack (transported verbatim).
//	actions     — optional page-level hub command buttons (transported verbatim).
//	follows     — optional plugin id whose backend row (proc-plugin) governs
//	              this page: disabling the backend withdraws the page through
//	              the Runtime's dependency rules (see ClientModuleComponent).
type UIPageComponent struct {
	id      string
	page    appui.PageDefinition
	follows string
}

var uiPageActivationSeq atomic.Int64

// NewUIPageComponent creates the ui-page component.
func NewUIPageComponent(cc config.ComponentConfig) (*UIPageComponent, error) {
	pageID := configutil.OptionalString(cc, "page_id", "")
	if pageID == "" {
		pageID = cc.ID
		if i := strings.LastIndex(cc.ID, ":page:"); i >= 0 {
			pageID = cc.ID[i+len(":page:"):]
		}
	}
	title, err := configutil.RequiredString(cc, "title")
	if err != nil {
		return nil, fmt.Errorf("ui-page %q: %w", cc.ID, err)
	}
	route, err := configutil.RequiredString(cc, "route")
	if err != nil {
		return nil, fmt.Errorf("ui-page %q: %w", cc.ID, err)
	}
	if !strings.HasPrefix(route, "/") {
		return nil, fmt.Errorf("ui-page %q: route must start with /, got %q", cc.ID, route)
	}
	renderer := configutil.OptionalString(cc, "renderer", "")
	views, err := optionalRaw(cc, "views")
	if err != nil {
		return nil, fmt.Errorf("ui-page %q: %w", cc.ID, err)
	}
	actions, err := optionalRaw(cc, "actions")
	if err != nil {
		return nil, fmt.Errorf("ui-page %q: %w", cc.ID, err)
	}
	if renderer == "" && len(views) == 0 {
		return nil, fmt.Errorf("ui-page %q: needs a renderer or a views stack", cc.ID)
	}
	follows := configutil.OptionalString(cc, "follows", "")
	if follows != "" {
		if err := ValidateFollows(follows); err != nil {
			return nil, fmt.Errorf("ui-page %q: %w", cc.ID, err)
		}
	}
	return &UIPageComponent{
		id: cc.ID,
		page: appui.PageDefinition{
			ID:          pageID,
			Title:       title,
			Route:       route,
			Description: configutil.OptionalString(cc, "description", ""),
			Renderer:    renderer,
			Icon:        configutil.OptionalString(cc, "icon", ""),
			Order:       configutil.OptionalInt(cc, "order", 100),
			Views:       views,
			Actions:     actions,
		},
		follows: follows,
	}, nil
}

// optionalRaw transports a declared JSON-shaped config value (views stack,
// page actions) verbatim: the console host carries declarations, the
// frontend interprets them.
func optionalRaw(cc config.ComponentConfig, key string) (json.RawMessage, error) {
	v, ok := cc.Config[key]
	if !ok || v == nil {
		return nil, nil
	}
	b, err := json.Marshal(v)
	if err != nil {
		return nil, fmt.Errorf("config %q: %w", key, err)
	}
	return b, nil
}

// Page returns the declared page definition (introspection view; tests and
// applications read it to verify what their declaration produced).
func (c *UIPageComponent) Page() appui.PageDefinition { return c.page }

func (c *UIPageComponent) Name() string { return "console:ui-page" }
func (c *UIPageComponent) Inject() []runtime.Dependency {
	deps := []runtime.Dependency{runtime.Requires(UIHostKey)}
	if c.follows != "" {
		deps = append(deps, runtime.Requires(PluginAliveKey(c.follows)))
	}
	return deps
}
func (c *UIPageComponent) Provide() []runtime.Capability { return nil }

func (c *UIPageComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	reg, err := runtime.Require(ctx, UIHostKey)
	if err != nil {
		return nil, err
	}
	owner := appui.ContributionOwner{
		PluginID:     "ui-page",
		ComponentID:  c.id,
		ActivationID: fmt.Sprintf("activation:%d", uiPageActivationSeq.Add(1)),
	}
	if err := ctx.Effect(func() (func() error, error) {
		return reg.RegisterPage(owner, c.page)
	}); err != nil {
		return nil, err
	}
	return nil, nil
}
