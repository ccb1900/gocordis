package host

import (
	"fmt"
	"path/filepath"
	"strings"
	"sync/atomic"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	"dynamic-runtime/extensions/console/configutil"
	appui "dynamic-runtime/extensions/console/registry"
)

// ClientModuleComponent is the composition-governed form of a plugin
// frontend module: one ui-client component declares one client module. As a
// component it behaves like everything else in the declarative layer — it
// appears in the desired configuration and the plugin explorer, honors the
// enabled switch (paper §5.2.1), and its contribution is an Effect owned by
// its activation: uninstall/disable removes the module from the manifest and
// the console stops serving/loading it. A module file existing on disk never
// loads by itself.
//
// Config:
//
//	module — the module name (URL slug). Default: the component id.
//	path   — entry file. Default: the convention plugins/<module>/ui.js
//	         (DefaultPluginsDir).
type ClientModuleComponent struct {
	id     string
	module appui.ClientModule
}

var clientModuleActivationSeq atomic.Int64

// NewClientModuleComponent creates the ui-client component.
func NewClientModuleComponent(cc config.ComponentConfig) (*ClientModuleComponent, error) {
	name := configutil.OptionalString(cc, "module", cc.ID)
	if name == "" || strings.ContainsAny(name, "/\\") || strings.Contains(name, "..") {
		return nil, fmt.Errorf("ui-client %q: invalid module name", cc.ID)
	}
	path := configutil.OptionalString(cc, "path", filepath.Join(DefaultPluginsDir, name, "ui.js"))
	return &ClientModuleComponent{
		id:     cc.ID,
		module: appui.ClientModule{Name: name, Path: path},
	}, nil
}

func (c *ClientModuleComponent) Name() string { return "console:client-module" }
func (c *ClientModuleComponent) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(UIHostKey)}
}
func (c *ClientModuleComponent) Provide() []runtime.Capability { return nil }

func (c *ClientModuleComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	reg, err := runtime.Require(ctx, UIHostKey)
	if err != nil {
		return nil, err
	}
	owner := appui.ContributionOwner{
		PluginID:     "ui-client",
		ComponentID:  c.id,
		ActivationID: fmt.Sprintf("activation:%d", clientModuleActivationSeq.Add(1)),
	}
	if err := ctx.Effect(func() (func() error, error) {
		return reg.RegisterClientModule(owner, c.module)
	}); err != nil {
		return nil, err
	}
	return nil, nil
}
