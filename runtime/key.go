package runtime

import (
	"fmt"
	"reflect"
	"sync"
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
// same Capability regardless of where they are constructed. An empty name is
// rejected: the zero CapabilityKey{} would otherwise give every type's zero
// Key[T] the same identity (RT-13), silently colliding provides and requires.
func NewKey[T any](name string) Key[T] {
	if name == "" {
		panic("runtime: NewKey requires a non-empty name")
	}
	return Key[T]{key: CapabilityKey{
		typeID: reflect.TypeOf((*T)(nil)).Elem(),
		name:   name,
	}}
}

// valid reports whether the key was constructed by NewKey/NewMetaKey rather
// than defaulted to the zero value. Runtime entry points refuse zero keys
// defensively: their identity would collide across every type parameter.
func (k CapabilityKey) valid() bool {
	return k.name != "" && k.typeID != nil
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

// metaKeyMonoid records the monoid definition a MetaKey identity was first
// constructed with, so a second construction of the same (T, M, name) cannot
// silently diverge (RT-12): identical Capability identity with different
// zero/combine would make the fold depend on which construction site the
// caller happened to import.
type metaKeyMonoid struct {
	zero       any
	combinePtr uintptr
}

var (
	metaKeyMonoidsMu sync.Mutex
	metaKeyMonoids   = map[CapabilityKey]metaKeyMonoid{}
)

// NewMetaKey builds a metadata-interpreting capability key. Keys with the same
// (M, T) and name are the same Capability regardless of where constructed.
// Reconstructing the same identity with a DIFFERENT monoid definition is a
// programming error and panics (best-effort detection: combine functions are
// compared by code pointer, so identical top-level functions match).
func NewMetaKey[T, M any](name string, zero M, combine func(M, M) M) MetaKey[T, M] {
	if name == "" {
		panic("runtime: NewMetaKey requires a non-empty name")
	}
	if combine == nil {
		panic("runtime: NewMetaKey requires a combine function")
	}
	key := CapabilityKey{
		typeID: reflect.TypeOf((*func(M) T)(nil)).Elem(),
		name:   name,
	}
	def := metaKeyMonoid{zero: zero, combinePtr: reflect.ValueOf(combine).Pointer()}
	metaKeyMonoidsMu.Lock()
	if first, ok := metaKeyMonoids[key]; ok {
		same := first.combinePtr == def.combinePtr && reflect.DeepEqual(first.zero, def.zero)
		metaKeyMonoidsMu.Unlock()
		if !same {
			panic(fmt.Sprintf("runtime: MetaKey %q redefined with a different monoid (zero or combine diverges from the first construction)", name))
		}
	} else {
		metaKeyMonoids[key] = def
		metaKeyMonoidsMu.Unlock()
	}
	return MetaKey[T, M]{
		key:     key,
		zero:    zero,
		combine: combine,
	}
}

// Capability returns the untyped identity of this typed key.
func (k MetaKey[T, M]) Capability() CapabilityKey { return k.key }

// merge folds μ onto the inherited metadata (⊕ₖ, right-biased).
func (k MetaKey[T, M]) merge(a, b M) M { return k.combine(a, b) }
