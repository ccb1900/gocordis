package runtime_test

import (
	"encoding/json"
	"testing"

	consoRuntime "dynamic-runtime/extensions/console/runtime"
	coreruntime "dynamic-runtime/runtime"
)

func jsonMap(t *testing.T, v any) map[string]any {
	t.Helper()
	raw, err := json.Marshal(v)
	if err != nil {
		t.Fatal(err)
	}
	var out map[string]any
	if err := json.Unmarshal(raw, &out); err != nil {
		t.Fatal(err)
	}
	return out
}

func exactKeys(t *testing.T, m map[string]any, keys ...string) {
	t.Helper()
	for _, k := range keys {
		if _, ok := m[k]; !ok {
			t.Fatalf("JSON object missing %q: %+v", k, m)
		}
	}
	if len(m) != len(keys) {
		t.Fatalf("JSON object has %d keys %v, want exactly %v", len(m), keysOf(m), keys)
	}
}

func keysOf(m map[string]any) []string {
	out := make([]string, 0, len(m))
	for k := range m {
		out = append(out, k)
	}
	return out
}

func rowOf(t *testing.T, m map[string]any, field string) map[string]any {
	t.Helper()
	rows, ok := m[field].([]any)
	if !ok || len(rows) == 0 {
		t.Fatalf("field %q is not a non-empty array: %+v", field, m[field])
	}
	row, ok := rows[0].(map[string]any)
	if !ok {
		t.Fatalf("field %q first row = %T", field, rows[0])
	}
	return row
}

func TestSnapshotDTOJSONShape(t *testing.T) {
	snap := coreruntime.RuntimeSnapshot{
		RuntimeID:     "runtime:dto",
		State:         coreruntime.RuntimeRunning,
		EventSequence: 9,
		Fibers: []coreruntime.FiberSnapshot{{
			ID: 1, Name: "f", Component: "c", State: coreruntime.StateActive,
			Intent: coreruntime.IntentMounted, Err: "", ActivationID: 2,
			ScopeID: 0, ParentFiberID: 0, ChildFiberIDs: []coreruntime.FiberID{3},
		}},
		Scopes: []coreruntime.ScopeSnapshot{{
			ID: 0, ParentID: 0, ProviderKeys: []string{"k"}, FiberIDs: []coreruntime.FiberID{1},
		}},
		Providers: []coreruntime.ProviderView{{
			Key: "k", State: coreruntime.StateActive, Retiring: false,
			OwnerFiberID: 1, OwnerActivationID: 2, ScopeID: 0,
		}},
		Dependencies: []coreruntime.DependencyView{{
			Key: "k", ConsumerFiberID: 1, ConsumerActivationID: 2,
			ProviderFiberID: 1, ProviderActivationID: 2,
			Status: coreruntime.DependencySatisfied, Reason: "",
		}},
		Effects: []coreruntime.EffectView{{
			OwnerFiberID: 1, ActivationID: 2, Seq: 1,
			Kind: coreruntime.EffectKindProvider, Key: "k", State: "Committed",
		}},
	}
	dto := consoRuntime.SnapshotFrom(snap)
	doc := jsonMap(t, dto)
	exactKeys(t, doc, "sequence", "runtimeID", "state", "fibers", "scopes", "providers", "dependencies", "effects")
	if doc["sequence"] != float64(9) || doc["runtimeID"] != "runtime:dto" {
		t.Fatalf("snapshot envelope = %+v", doc)
	}
	exactKeys(t, rowOf(t, doc, "fibers"),
		"id", "name", "component", "state", "intent", "err", "activationID", "scopeID", "parentFiberIDs", "childFiberIDs")
	exactKeys(t, rowOf(t, doc, "scopes"), "id", "parentID", "providerKeys", "fiberIDs")
	exactKeys(t, rowOf(t, doc, "providers"), "key", "state", "retiring", "ownerFiberID", "ownerActivationID", "scopeID")
	exactKeys(t, rowOf(t, doc, "dependencies"),
		"key", "consumerFiberID", "consumerActivationID", "providerFiberID", "providerActivationID", "status", "reason")
	exactKeys(t, rowOf(t, doc, "effects"), "ownerFiberID", "activationID", "seq", "kind", "key", "state")
}

func TestDashboardDTOJSONShape(t *testing.T) {
	dto := consoRuntime.DashboardFrom(
		coreruntime.RuntimeSnapshot{State: coreruntime.RuntimeRunning, EventSequence: 4},
		consoRuntime.EventsDTO{Events: []consoRuntime.EventDTO{{Sequence: 4}}, Sequence: 4},
		[]consoRuntime.DiagnosticDTO{{Severity: "warn", Code: "X", Message: "m"}},
	)
	doc := jsonMap(t, dto)
	exactKeys(t, doc, "sequence", "state", "counts", "recentEvents", "diagnostics")
	exactKeys(t, doc["counts"].(map[string]any), "fibers", "active", "providers", "dependencies", "effects", "failed", "pending")
	exactKeys(t, rowOf(t, doc, "recentEvents"), "sequence", "timestamp", "runtimeID", "type", "fiberID", "activationID", "data")
	exactKeys(t, rowOf(t, doc, "diagnostics"), "severity", "code", "message")
}
