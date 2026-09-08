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

// MetaKey[T, M] is a typed capability key whose providers interpret metadata
// (paper Definition 26): the provider table maps the key to a provider
// FUNCTION ℳₖ → 𝒱ₖ, and each key carries a monoid (ℳₖ, ⊕ₖ, εₖ) — combine is
// ⊕ₖ (right-biased: combine(a, b) lets b override a on conflicting fields),
// zero is εₖ.
//
// Capability identity is derived from (func(M) T, name), so a MetaKey and a
// plain Key never collide, and two MetaKeys with different metadata types are
// different capabilities.
type MetaKey[T, M any] struct {
	key     CapabilityKey
	zero    M
	combine func(M, M) M
}

// NewMetaKey builds a metadata-interpreting capability key. Keys with the same
// (M, T) and name are the same Capability regardless of where constructed.
func NewMetaKey[T, M any](name string, zero M, combine func(M, M) M) MetaKey[T, M] {
	if combine == nil {
		panic("runtime: NewMetaKey requires a combine function")
	}
	return MetaKey[T, M]{
		key: CapabilityKey{
			typeID: reflect.TypeOf((*func(M) T)(nil)).Elem(),
			name:   name,
		},
		zero:    zero,
		combine: combine,
	}
}

// Capability returns the untyped identity of this typed key.
func (k MetaKey[T, M]) Capability() CapabilityKey { return k.key }

// merge folds μ onto the inherited metadata (⊕ₖ, right-biased).
func (k MetaKey[T, M]) merge(a, b M) M { return k.combine(a, b) }
