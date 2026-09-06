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
