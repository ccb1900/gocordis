package explorer

import (
	"encoding/json"
	"testing"
)

// Wire-format conformance for the explorer DTOs: client/src/types.ts
// hand-mirrors these shapes, so the JSON keys are a contract. This test pins
// them (R12 F-2/P3-6): any field rename here breaks the console client and
// MUST be reflected in types.ts in the same commit.

func jsonKeys(t *testing.T, v any) map[string]bool {
	t.Helper()
	b, err := json.Marshal(v)
	if err != nil {
		t.Fatal(err)
	}
	var m map[string]any
	if err := json.Unmarshal(b, &m); err != nil {
		t.Fatal(err)
	}
	keys := make(map[string]bool, len(m))
	for k := range m {
		keys[k] = true
	}
	return keys
}

func assertKeys(t *testing.T, got map[string]bool, want ...string) {
	t.Helper()
	for _, w := range want {
		if !got[w] {
			t.Errorf("missing key %q in %v", w, got)
		}
	}
}

func TestDTOWireShapes(t *testing.T) {
	assertKeys(t, jsonKeys(t, Plugin{
		ID: "p", Name: "n", Type: "t", State: "Active",
		Components: []string{"c"}, Capabilities: []string{"k"},
		Controllable: true,
	}), "ID", "Name", "Type", "State", "Error", "Components", "Capabilities", "Controllable", "Config")

	assertKeys(t, jsonKeys(t, ExplorerControlResult{
		PluginID: "p", Accepted: true, State: "Active",
	}), "pluginId", "accepted", "state")

	assertKeys(t, jsonKeys(t, ExplorerControlRequest{
		PluginID: "p", Enable: true,
	}), "pluginId", "enable")
}
