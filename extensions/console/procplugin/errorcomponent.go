package procplugin

import (
	"fmt"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/configutil"
	"dynamic-runtime/runtime"
)

// ErrorComponent is the loud placeholder a broken plugin manifest expands
// to (discover.errorRow). Its Apply raises immediately, so the plugin
// surfaces as a Failed fiber with a visible reason in the console explorer
// — a corrupted declaration is never interpreted as an uninstall (AGENTS
// 铁律 6: dependency/config truth lives in declarations).
//
// Per §4.4 raise semantics the Failed outcome blocks re-entry: the recovery
// path is fixing the declaration. Repairing manifest.toml changes the row
// (type/config), and the next reconcile replaces the error component with
// the plugin's real rows automatically.
type ErrorComponent struct {
	id     string
	reason string
}

// NewErrorComponent builds the plugin-error component from one discovered
// row: config reason carries what the manifest got wrong.
func NewErrorComponent(cc config.ComponentConfig) (*ErrorComponent, error) {
	reason := configutil.OptionalString(cc, "reason", "malformed plugin manifest")
	return &ErrorComponent{id: cc.ID, reason: reason}, nil
}

func (c *ErrorComponent) Name() string                 { return "console:plugin-error" }
func (c *ErrorComponent) Inject() []runtime.Dependency { return nil }
func (c *ErrorComponent) Provide() []runtime.Capability {
	return nil
}

func (c *ErrorComponent) Apply(*runtime.Context) (runtime.Cleanup, error) {
	return nil, fmt.Errorf("plugin %q has a broken declaration: %s", c.id, c.reason)
}

// Reason exposes the recorded manifest problem (diagnostics/tests).
func (c *ErrorComponent) Reason() string { return c.reason }
