package event_test

// Kernel-surface aliases for the migrated P1 dispatch conformance suites.
//
// These files were written against the kernel's INTERNAL test package when the
// dispatch modes lived in runtime (P1.1–P1.4). ADR-0004 moved dispatch to this
// extension; the suites moved with it and keep their original unqualified
// style via the aliases below. New tests should use the runtime./event.
// qualifiers directly.

import (
	"testing"

	"dynamic-runtime/runtime"
)

type (
	Runtime      = runtime.Runtime
	Component    = runtime.Component
	Context      = runtime.Context
	Fiber        = runtime.Fiber
	Cleanup      = runtime.Cleanup
	Capability   = runtime.Capability
	Dependency   = runtime.Dependency
	Next         = runtime.Next
	ActivationID = runtime.ActivationID
	FiberID      = runtime.FiberID
	FiberState   = runtime.FiberState
	EventBinding = runtime.EventBinding
)

const (
	StatePending   = runtime.StatePending
	StateLoading   = runtime.StateLoading
	StateActive    = runtime.StateActive
	StateUnloading = runtime.StateUnloading
	StateFailed    = runtime.StateFailed
	StateGone      = runtime.StateGone
	IntentMounted  = runtime.IntentMounted
)

var (
	ErrEventHandlerPanic   = runtime.ErrEventHandlerPanic
	ErrWaterfallNextTwiceL = runtime.ErrWaterfallNextTwice
)

func NewKey[T any](name string) runtime.Key[T] { return runtime.NewKey[T](name) }

func NewEventKey[T any](name string) runtime.EventKey[T] { return runtime.NewEventKey[T](name) }

func Requires[T any](k runtime.Key[T]) runtime.Dependency { return runtime.Requires(k) }

func Provide[T any](c *Context, k runtime.Key[T], v T) error { return runtime.Provide(c, k, v) }

func Require[T any](c *Context, k runtime.Key[T]) (T, error) { return runtime.Require(c, k) }

func On[T any](c *Context, k runtime.EventKey[T], h runtime.EventHandler[T]) error {
	return runtime.On(c, k, h)
}

func OnWaterfall[T any](c *Context, k runtime.EventKey[T], h runtime.WaterfallHandler[T]) error {
	return runtime.OnWaterfall(c, k, h)
}

func WithScope() runtime.ScopeOption { return runtime.WithScope() }

// p1CtxProbe publishes its own activation Context so tests can query the
// registry read model from a live context without kernel internals.
type p1CtxProbe struct {
	out  chan *Context
	name string
}

func (c *p1CtxProbe) Name() string          { return c.name }
func (c *p1CtxProbe) Inject() []Dependency  { return nil }
func (c *p1CtxProbe) Provide() []Capability { return nil }
func (c *p1CtxProbe) Apply(ctx *Context) (Cleanup, error) {
	c.out <- ctx
	return nil, nil
}

// p1ProbeCtx loads a context probe on the root realm and returns its context.
func p1ProbeCtx(t *testing.T, rt *Runtime, name string) *Context {
	t.Helper()
	ch := make(chan *Context, 1)
	f, err := rt.Load(&p1CtxProbe{out: ch, name: name})
	if err != nil {
		t.Fatalf("probe load: %v", err)
	}
	if err := f.Ready(p1Timeout(t)); err != nil {
		t.Fatalf("probe ready: %v", err)
	}
	return <-ch
}

// p1AssertBindings asserts the visible binding count for one key.
func p1AssertBindings(t *testing.T, c *Context, id runtime.EventKeyID, want int) {
	t.Helper()
	if got := len(c.EventBindings(id)); got != want {
		t.Fatalf("visible bindings = %d, want %d", got, want)
	}
}

// p1AssertNoBindings asserts zero visible bindings for every key: the public
// residue check (zero is realm-independent, so any probe context works).
func p1AssertNoBindings(t *testing.T, c *Context, ids ...runtime.EventKeyID) {
	t.Helper()
	for _, id := range ids {
		if n := len(c.EventBindings(id)); n != 0 {
			t.Fatalf("registry residue: %d bindings remain", n)
		}
	}
}
