package runtime

import "fmt"

// FiberID uniquely identifies a Fiber within a Runtime.
//
// ID 0 is reserved as invalid.
type FiberID uint64

func (id FiberID) String() string { return fmt.Sprintf("fiber:%d", uint64(id)) }

// ActivationID uniquely identifies one activation cycle of a Fiber.
//
// Every activation cycle MUST receive a fresh ActivationID; a new activation
// is a new provider generation even when it belongs to the same Fiber.
//
// ID 0 is reserved as invalid.
type ActivationID uint64

func (id ActivationID) String() string { return fmt.Sprintf("activation:%d", uint64(id)) }

// ScopeID uniquely identifies a provider scope (realm) within a Runtime.
//
// ID 0 is reserved for the Runtime root realm. Child realms (explicit scopes)
// receive monotonically increasing, stable IDs for the Runtime's lifetime;
// a scope never changes identity, so console projections can reference scopes
// reliably across snapshots and events.
type ScopeID uint64

func (id ScopeID) String() string { return fmt.Sprintf("scope:%d", uint64(id)) }

// RuntimeID uniquely identifies a Runtime instance.
//
// Developer-console observation (multi-runtime switching) requires events and
// snapshots to be attributable to exactly one Runtime. The ID is explicitly
// configurable via WithRuntimeID; when unset the Runtime assigns itself a
// process-unique default of the form "runtime:<n>".
type RuntimeID string
