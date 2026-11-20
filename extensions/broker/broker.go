// Package broker implements the paper's §6.2 service-multiplexing pattern:
// a central service (the broker) is the entrypoint injected to both backing
// providers and consumers, so multiple providers coexist and the broker
// dispatches each call among them.
//
// Compared to exclusive binding (one provider per capability key), the broker
// absorbs replacement perturbation: a backing provider registers through a
// REVERSIBLE EFFECT, so unloading it reverts the registration and drops it
// from the routing set automatically (paper: "each provider registers with the
// broker through a revertible effect"), while the broker's own capability
// binding stays put — consumers see no dependency change and no reload is
// triggered.
//
// The broker is an extension: it holds no lifecycle authority and never
// touches fiber state; it is itself just a component that provides one
// capability and keeps a routing set.
package broker

import (
	"errors"
	"sync"
	"sync/atomic"

	"dynamic-runtime/runtime"
)

// ErrNoProvider is returned by Call when the routing set is empty.
var ErrNoProvider = errors.New("broker: no backing provider registered")

// Policy selects one backing provider among the routing set.
type Policy[T any] func(providers []T) T

// RoundRobin cycles through the routing set in registration order, starting at
// the oldest registered provider (index 0 on the first call).
func RoundRobin[T any]() func([]T) T {
	var counter atomic.Uint64
	return func(providers []T) T {
		return providers[(counter.Add(1)-1)%uint64(len(providers))]
	}
}

// First always selects the first (oldest registration).
func First[T any]() func([]T) T {
	return func(providers []T) T { return providers[0] }
}

// Broker is the typed service entrypoint. Create one per service interface
// and provide it through Provide.
type Broker[T any] struct {
	mu     sync.RWMutex
	routes []route[T]
	policy func([]T) T
}

type route[T any] struct {
	name string
	impl T
}

// New builds a broker with the given selection policy (nil = RoundRobin).
func New[T any](policy func([]T) T) *Broker[T] {
	if policy == nil {
		policy = RoundRobin[T]()
	}
	return &Broker[T]{policy: policy}
}

// Call dispatches one invocation to the selected backing provider.
func (b *Broker[T]) Call(fn func(T) error) error {
	b.mu.RLock()
	if len(b.routes) == 0 {
		b.mu.RUnlock()
		return ErrNoProvider
	}
	impls := make([]T, len(b.routes))
	names := make([]string, len(b.routes))
	for i, r := range b.routes {
		impls[i] = r.impl
		names[i] = r.name
	}
	b.mu.RUnlock()
	selected := b.policy(impls)
	return fn(selected)
}

// Names returns the current routing set in registration order.
func (b *Broker[T]) Names() []string {
	b.mu.RLock()
	defer b.mu.RUnlock()
	out := make([]string, len(b.routes))
	for i, r := range b.routes {
		out[i] = r.name
	}
	return out
}

// Register adds one backing provider as ONE ATOMIC EFFECT: the route is
// registered inside the Effect install function, so the registration and its
// inverse are committed together by the effect system. If the effect is
// refused (the activation context is already cancelling/unwinding), nothing is
// registered — a provider can never leak into the broker beyond its fiber's
// lifecycle. When the provider's activation unwinds, the committed inverse
// drops it from the routing set automatically.
func Register[T any](ctx *runtime.Context, b *Broker[T], name string, impl T) error {
	// Duplicate pre-check before engaging the effect machinery: a duplicate is
	// rejected as a plain error, with no registration side effects.
	b.mu.RLock()
	for _, r := range b.routes {
		if r.name == name {
			b.mu.RUnlock()
			return errors.New("broker: duplicate provider name " + name)
		}
	}
	b.mu.RUnlock()
	return ctx.Effect(func() (func() error, error) {
		b.mu.Lock()
		// Re-check under the write lock: a concurrent Register may have won the
		// race between the pre-check and this install. A failing install
		// refuses the whole effect, so nothing is registered.
		for _, r := range b.routes {
			if r.name == name {
				b.mu.Unlock()
				return nil, errors.New("broker: duplicate provider name " + name)
			}
		}
		b.routes = append(b.routes, route[T]{name: name, impl: impl})
		b.mu.Unlock()
		return func() error {
			b.mu.Lock()
			defer b.mu.Unlock()
			for i, r := range b.routes {
				if r.name == name {
					b.routes = append(b.routes[:i], b.routes[i+1:]...)
					return nil
				}
			}
			return nil
		}, nil
	})
}
