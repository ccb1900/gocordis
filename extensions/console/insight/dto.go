// Package insight exposes a read-only, declaration-vs-runtime graph of a
// running composition through the existing Console hub. It owns no lifecycle,
// resolves no dependencies and mutates no Fiber: the declared side is projected
// from the Config Controller, the runtime side solely from Runtime.Snapshot.
package insight

import "encoding/json"

// Graph is the "insight/graph" response. EventSequence is the Runtime's
// canonical event sequence at the snapshot's linearization point — the version
// anchor a console uses to decide whether a cached graph is stale.
type Graph struct {
	EventSequence uint64        `json:"eventSequence"`
	Declared      DeclaredGraph `json:"declared"`
	Runtime       RuntimeGraph  `json:"runtime"`
}

// DeclaredGraph is the declaration-side view: known component declarations and
// the potential key-based edges between them. Edges are declaration matches,
// NOT runtime bindings (those live in RuntimeGraph.Edges).
type DeclaredGraph struct {
	Nodes []DeclaredNode `json:"nodes"`
	Edges []DeclaredEdge `json:"edges"`
}

// DeclaredNode is one Config Controller declaration. Requires/Provides hold the
// canonical capability key strings declared by the component.
type DeclaredNode struct {
	ID       string   `json:"id"`
	Name     string   `json:"name"`
	Type     string   `json:"type"`
	Requires []string `json:"requires"`
	Provides []string `json:"provides"`
}

// DeclaredEdge connects a requiring declaration to a providing declaration on
// a shared capability key.
type DeclaredEdge struct {
	From string `json:"from"`
	To   string `json:"to"`
	Key  string `json:"key"`
}

// RuntimeGraph is the fact-side view projected from Runtime.Snapshot.
type RuntimeGraph struct {
	Nodes []RuntimeNode `json:"nodes"`
	Edges []RuntimeEdge `json:"edges"`
}

// RuntimeNode is one live Fiber in the snapshot. FiberID/ActivationID/ScopeID
// are the kernel identities; State is the Fiber lifecycle state.
type RuntimeNode struct {
	FiberID      uint64   `json:"fiberId"`
	Name         string   `json:"name"`
	State        string   `json:"state"`
	ActivationID uint64   `json:"activationId"`
	ScopeID      uint64   `json:"scopeId"`
	Provides     []string `json:"provides"`
	Requires     []string `json:"requires"`
}

// RuntimeEdge is one dependency row from the snapshot. Status is the snapshot's
// DependencyStatus ("Satisfied"/"Waiting"/"Withdrawn") and carries the binding
// provider fiber; ProviderFiberID is 0 while no provider is bound.
type RuntimeEdge struct {
	ConsumerFiberID uint64 `json:"consumerFiberId"`
	ProviderFiberID uint64 `json:"providerFiberId"`
	Key             string `json:"key"`
	Status          string `json:"status"`
	Reason          string `json:"reason"`
}

// SchemaList is the "config/schema" response. Schema is the generated JSON
// Schema document (not the Go struct), so a console can drive forms directly.
type SchemaList struct {
	Components []SchemaEntry `json:"components"`
}

// SchemaEntry pairs a component Type with its declared JSON Schema.
type SchemaEntry struct {
	Type   string          `json:"type"`
	Schema json.RawMessage `json:"schema"`
}
