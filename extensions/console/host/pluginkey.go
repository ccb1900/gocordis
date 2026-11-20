package host

import (
	"fmt"
	"strings"

	"dynamic-runtime/runtime"
)

// PluginAliveKey names the per-plugin liveness capability provided by a
// plugin's backend component (proc-plugin). UI contribution rows that
// declare `follows = <plugin id>` require this key, so disabling/removing
// the backend withdraws the UI through the Runtime's own dependency rules
// (§5.2.1: the switch flips a declaration, dependency loss retracts to
// Pending, restoration re-activates) — the plugin's rows form one business
// unit WITHOUT a second lifecycle authority (AGENTS 铁律 7).
//
// Key identity is (type, name) — construction site is irrelevant
// (runtime/domain_contract_test pins this), so provider and consumer agree
// from the plugin id alone. A frontend-only plugin has no backend and MUST
// NOT declare follows: its UI would wait Pending forever.
func PluginAliveKey(pluginID string) runtime.Key[string] {
	return runtime.NewKey[string]("console:plugin-alive/" + pluginID)
}

// ValidateFollows checks a declared follows plugin id for key-name safety
// (the id is embedded into the capability name).
func ValidateFollows(follows string) error {
	if follows == "" || strings.ContainsAny(follows, "/\\") || strings.Contains(follows, "..") {
		return fmt.Errorf("invalid follows plugin id %q", follows)
	}
	return nil
}
