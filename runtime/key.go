package runtime

import (
	"fmt"
	"reflect"
)

// CapabilityKey is the stable, comparable identity of a Capability.
//
// Identity is derived from (runtime type of T, name) and MUST NOT depend on
// pointer addresses, Component instances, Fiber IDs, or registration order.
//
// The fields are unexported on purpose: callers obtain CapabilityKey values
// from a typed Key[T], which prevents string-only collisions and keeps the
// runtime internal identity unforgeable.
type CapabilityKey struct {
	typeID reflect.Type
	name   string
}

// String renders the capability identity for diagnostics.
func (k CapabilityKey) String() string {
	t := "?"
	if k.typeID != nil {
		t = k.typeID.String()
	}
	return fmt.Sprintf("%s(%q)", t, k.name)
}

// Key[T] is the typed, user-facing capability key. T is the Go type of the
// provided value.
type Key[T any] struct {
	key CapabilityKey
}

// NewKey builds a typed capability key. Keys with the same T and name are the
// same Capability regardless of where they are constructed.
func NewKey[T any](name string) Key[T] {
	return Key[T]{key: CapabilityKey{
		typeID: reflect.TypeOf((*T)(nil)).Elem(),
		name:   name,
	}}
}

// Capability returns the untyped identity of this typed key.
func (k Key[T]) Capability() CapabilityKey { return k.key }
