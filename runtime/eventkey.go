package runtime

import (
	"fmt"
	"reflect"
)

// EventKey[T] is the typed, user-facing identity of a Kernel Event.
//
// T is the static payload type of the Event: identity is derived from
// (runtime type of T, name) and MUST NOT depend on pointer addresses,
// Component instances, Fiber IDs, or registration order. Two EventKey values
// with the same T and name are the same Event regardless of where they are
// constructed. A bare string can never be the whole identity, and payload
// type is never recovered from a string at runtime.
//
// The fields are unexported on purpose: callers obtain EventKey values from
// the typed constructor NewEventKey, which keeps the Kernel identity
// unforgeable and comparable (EventKey values are valid map keys).
type EventKey[T any] struct {
	key eventKeyID
}

// eventKeyID is the internal comparable identity of one Event.
type eventKeyID struct {
	typeID reflect.Type
	name   string
}

// NewEventKey builds a typed Event key. Keys with the same payload type T and
// name are the same Event regardless of where they are constructed.
func NewEventKey[T any](name string) EventKey[T] {
	return EventKey[T]{key: eventKeyID{
		typeID: reflect.TypeOf((*T)(nil)).Elem(),
		name:   name,
	}}
}

// String renders the Event identity for diagnostics.
func (k EventKey[T]) String() string {
	t := "?"
	if k.key.typeID != nil {
		t = k.key.typeID.String()
	}
	return fmt.Sprintf("%s(%q)", t, k.key.name)
}

// id returns the internal registry identity of this Event.
func (k EventKey[T]) id() eventKeyID { return k.key }

// valid reports whether the key was constructed through NewEventKey (a
// zero-value EventKey has no payload type and is unusable).
func (k EventKey[T]) valid() bool { return k.key.typeID != nil }

// EventHandler[T] is the Kernel-facing handler contract for one Event.
//
// A handler is a synchronous function of the typed payload; it returns an
// error to report failure without interrupting the Emit broadcast (handler
// errors are aggregated). The handler-facing Context / listener signature is
// a P2 decision (Decision Record D6): P1.1 handlers never hold Runtime,
// Orchestrator, or Registry internal authority.
type EventHandler[T any] func(T) error
