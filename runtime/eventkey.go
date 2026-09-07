package runtime

import (
	"context"
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
// A handler is a synchronous function of the typed payload that receives the
// dispatch context (P1.2 extension of the P1.1 contract): Emit passes the
// emitter activation context; Serial passes the caller-supplied dispatch
// context. The context is a cooperative cancellation signal only — it carries
// NO Runtime / Orchestrator / Registry internal authority, and a handler can
// never obtain lifecycle authority or registry write access through it
// (Decision Record D6: richer handler-facing Context is a P2 decision).
//
// A handler returns an error to report failure without interrupting the
// dispatch (handler errors are aggregated with errors.Join by every dispatch
// mode).
type EventHandler[T any] func(context.Context, T) error

// Next is the continuation of one Waterfall chain node (P1.4).
//
// Calling next() advances the chain to the next handler in the dispatch
// snapshot and returns only after the downstream chain has completed (Next
// Contract: next is synchronous). The returned error is the downstream chain
// result — a downstream handler error propagates back through next() so the
// current handler can observe, transform, or handle it.
//
// A node's next may advance the chain at most once: a second invocation is a
// Handler contract violation and returns ErrWaterfallNextTwice without
// re-running the downstream chain. next() must be called synchronously from
// the handler goroutine (go next() is not a legal Waterfall middleware
// pattern).
type Next func() error

// WaterfallHandler[T] is the handler contract for a Waterfall chain node
// (P1.4). It receives the dispatch context, the typed payload, and the chain
// continuation:
//
//	before := do()
//	if err := next(); err != nil { ... }
//	after := do()
//
// Not calling next() short-circuits the chain (the node handled the event);
// returning an error without calling next() fails the dispatch. The payload is
// passed through the chain unchanged: state flows between handlers only
// through the caller-supplied payload/state the handlers share, never through
// a second channel invented by the Kernel.
type WaterfallHandler[T any] func(context.Context, T, Next) error
