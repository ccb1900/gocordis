package runtime

import (
	"context"
	"errors"
	"fmt"
	"sync"
)

// contextState is the lifecycle state of one activation Context.
type contextState uint8

const (
	contextActive contextState = iota
	contextUnwinding
	contextUnwound
)

// effectSlotState is the per-effect lifecycle state (two-phase Effect slot).
type effectSlotState uint8

const (
	effectInstalling effectSlotState = iota
	effectCommitted
	effectUndoing
	effectUndone
)

// effectSlot is one reversible effect. Its state machine guarantees that an
// inverse is executed exactly once, even when installation races with unwind.
type effectSlot struct {
	seq uint64

	state effectSlotState

	inverse func() error
}

// Context is the scope of one Fiber activation.
//
// Every activation cycle gets a brand-new Context; contexts are NEVER reused
// across activations. The Context owns the reversible consequences of the
// activation: its effect stack, its provided capabilities (later phase), and
// its cancellation.
//
// The public effect/provider/child API is added in later phases; this file
// establishes the activation-scope machinery the orchestrator needs.
type Context struct {
	rt           *Runtime
	fiberID      FiberID
	activationID ActivationID

	cancel context.CancelFunc
	base   context.Context

	mu sync.Mutex

	state contextState

	effects   []*effectSlot
	nextSeq   uint64
	unwindErr error

	// declaredInject / declaredProvide are activation-local, immutable copies
	// of the owning Component's Inject/Provide declarations, captured when the
	// Context is created. Require/Provide outside these sets are rejected:
	// declarations are authoritative.
	declaredInject  map[CapabilityKey]struct{}
	declaredProvide map[CapabilityKey]struct{}

	// realm is the provider scope of this activation (the owning fiber's
	// realm). Provide writes here; Require resolves through this realm path.
	realm *realm
}

func newContext(rt *Runtime, fiberID FiberID, activationID ActivationID, inject []Dependency, provide []Capability, r *realm) *Context {
	base, cancel := context.WithCancel(context.Background())
	c := &Context{
		rt:              rt,
		fiberID:         fiberID,
		activationID:    activationID,
		cancel:          cancel,
		base:            base,
		state:           contextActive,
		realm:           r,
		declaredInject:  make(map[CapabilityKey]struct{}, len(inject)),
		declaredProvide: make(map[CapabilityKey]struct{}, len(provide)),
	}
	for _, d := range inject {
		c.declaredInject[d.Key] = struct{}{}
	}
	for _, cap := range provide {
		c.declaredProvide[cap] = struct{}{}
	}
	return c
}

// Context exposes the cancellable Go context for this activation.
func (c *Context) Context() context.Context { return c.base }

// Done is closed when the activation is canceled (cooperative cancellation).
func (c *Context) Done() <-chan struct{} { return c.base.Done() }

// Err reports the cancellation cause, if any.
func (c *Context) Err() error { return c.base.Err() }

// acceptingWork reports whether the context still accepts new Runtime-managed
// work (Effect / Provide / Child). A context stops accepting as soon as it is
// canceled (withdrawing/cancelled) or has begun unwinding.
func (c *Context) acceptingWork() bool {
	c.mu.Lock()
	defer c.mu.Unlock()
	return c.state == contextActive && c.base.Err() == nil
}

// addCommittedEffect appends an already-materialized inverse as the
// activation's last committed effect. Used by the orchestrator to convert a
// Component.Apply Cleanup return value into an effect.
func (c *Context) addCommittedEffect(inverse func() error) {
	c.mu.Lock()
	if c.state == contextActive {
		s := &effectSlot{seq: c.nextSeq, state: effectCommitted, inverse: inverse}
		c.nextSeq++
		c.effects = append(c.effects, s)
		c.mu.Unlock()
		return
	}
	c.mu.Unlock()

	// Defensive path: the activation is already unwinding/unwound. In the
	// sequential lifecycle this is unreachable (unwind never starts before
	// Apply finishes), but if it ever happens the inverse must still run
	// exactly once instead of leaking.
	var err error
	if inverse != nil {
		err = guardedInverse(inverse)
	}
	c.mu.Lock()
	c.state = contextUnwound
	c.unwindErr = errors.Join(c.unwindErr, err)
	c.mu.Unlock()
}

// Effect records a Runtime-managed reversible mutation.
//
// install runs outside the context lock. If install returns an error, no
// effect is committed and the error is returned. If install succeeds, the
// returned inverse is committed and will run exactly once when the activation
// unwinds (in strict LIFO order across all committed effects).
//
// If the Context is already unwinding/unwound when install completes, the
// effect is NOT committed as a persistent effect: the returned inverse is
// executed immediately, exactly once, preventing late-effect leakage.
func (c *Context) Effect(install func() (func() error, error)) error {
	if install == nil {
		return errors.New("runtime: nil effect install")
	}

	// Step 1-2: under the lock, verify the context is active and not canceled
	// (no new work once withdrawing/cancelled), then reserve an Installing slot.
	c.mu.Lock()
	if c.state != contextActive || c.base.Err() != nil {
		c.mu.Unlock()
		return ErrContextClosed
	}
	s := &effectSlot{seq: c.nextSeq, state: effectInstalling}
	c.nextSeq++
	c.effects = append(c.effects, s)
	c.mu.Unlock()

	// Step 3: run install outside the lock; a panic is contained and reported
	// as ErrEffectInstallPanic (the reserved Installing slot is then removed).
	var inverse func() error
	var installErr error
	inverse, installErr = guardedInstall(install)

	// Step 4: re-acquire the lock and commit or self-undo.
	c.mu.Lock()
	if installErr != nil {
		c.removeSlot(s)
		c.mu.Unlock()
		return installErr
	}
	if c.state == contextActive {
		s.inverse = inverse
		s.state = effectCommitted
		c.mu.Unlock()
		return nil
	}

	// The context is already unwinding/unwound: do not commit as a persistent
	// effect; execute the returned inverse immediately (exactly once).
	s.inverse = inverse
	s.state = effectUndoing
	c.mu.Unlock()

	var invErr error
	if inverse != nil {
		invErr = inverse()
	}

	c.mu.Lock()
	s.state = effectUndone
	c.unwindErr = errors.Join(c.unwindErr, invErr)
	c.mu.Unlock()
	return nil
}

// Child creates a new Fiber owned by this activation's Fiber.
//
// Ownership means the parent's lifecycle contains the child: when the parent
// activation withdraws, owned children are disposed and must reach Gone before
// the parent finalizes. Ownership is distinct from dependency.
// ScopeOption configures Context.Child scope derivation.
type ScopeOption func(*scopeOptions)

type scopeOptions struct {
	newRealm bool
}

// WithScope makes Child create an explicit child realm (parent = this fiber's
// realm) instead of inheriting this fiber's realm. Only explicit scoping
// introduces sibling isolation; child realms may shadow ancestor bindings.
func WithScope() ScopeOption {
	return func(o *scopeOptions) { o.newRealm = true }
}

func (c *Context) Child(component Component, opts ...ScopeOption) (*Fiber, error) {
	if component == nil {
		return nil, ErrInvalidState
	}
	o := scopeOptions{}
	for _, opt := range opts {
		if opt != nil {
			opt(&o)
		}
	}
	// Cache declarations on the caller goroutine so the orchestrator never
	// executes Component code while making lifecycle decisions.
	inject := component.Inject()
	provide := component.Provide()

	reply := make(chan spawnChildReply, 1)
	if !c.rt.submit(&cmdSpawnChild{
		ctx:       c,
		component: component,
		inject:    inject,
		provide:   provide,
		newScope:  o.newRealm,
		reply:     reply,
	}) {
		return nil, ErrRuntimeClosed
	}
	resp := <-reply
	return resp.fiber, resp.err
}

func (c *Context) provideCap(key CapabilityKey, value any) error {
	if _, ok := c.declaredProvide[key]; !ok {
		return fmt.Errorf("%w: %s is not declared in this activation's Provide set", ErrUndeclaredProvide, key)
	}
	id := ProviderIdentity{FiberID: c.fiberID, ActivationID: c.activationID}
	return c.Effect(func() (func() error, error) {
		if err := c.realm.registerOwn(key, id, value); err != nil {
			return nil, err
		}
		return func() error {
			c.realm.removeOwn(key, id)
			return nil
		}, nil
	})
}

// Provide registers c's activation as the exclusive provider of key.
//
// Provider registration is equivalent to a Runtime-managed reversible Effect:
// when the activation unwinds, the provider is unregistered again. A second
// provider for the same capability fails with ErrDuplicateProvider and never
// disturbs the existing provider.
func Provide[T any](c *Context, key Key[T], value T) error {
	return c.provideCap(key.Capability(), value)
}

// Require resolves the value provided for key by the activation's declared
// dependency. It returns ErrDependencyMissing when no provider is registered.
func Require[T any](c *Context, key Key[T]) (T, error) {
	var zero T
	if _, ok := c.declaredInject[key.Capability()]; !ok {
		return zero, fmt.Errorf("%w: %s is not declared in this activation's Inject set", ErrUndeclaredRequire, key.Capability())
	}
	rec, ok := c.realm.lookup(key.Capability())
	if !ok || rec.value == nil {
		return zero, ErrDependencyMissing
	}
	// Apply the read-time interception chain (ancestor -> this realm).
	val := rec.value
	for _, e := range c.realm.interceptsForKey(key.Capability()) {
		if e == nil || e.apply == nil {
			continue
		}
		nv, err := e.apply(val)
		if err != nil {
			return zero, err
		}
		val = nv
	}
	v, ok := val.(T)
	if !ok {
		return zero, errors.New("runtime: provider value type mismatch")
	}
	return v, nil
}

// Intercept installs a read-time interceptor for key on this Context's realm.
//
// It is a generic FREE function (Go methods cannot be generic). Every Require
// of key that resolves through this realm path first applies the chain from
// ancestor scopes to this scope (install order within a scope). Installation is
// reversible: it goes through ctx.Effect, so unwinding removes the interceptor
// in reverse install order. Interceptors may only transform the returned value
// or reject a read; they never mutate the provider registry, provider identity,
// or bypass declaration/realm checks. A panic inside an interceptor is
// contained and returned as an error.
func Intercept[T any](c *Context, key Key[T], fn func(value T, key Key[T]) (T, error)) error {
	if c == nil {
		return errors.New("runtime: nil context")
	}
	if fn == nil {
		return errors.New("runtime: nil interceptor")
	}
	entry := &interceptEntry{key: key.Capability()}
	entry.apply = func(value any) (out any, err error) {
		v, ok := value.(T)
		if !ok {
			return nil, errors.New("runtime: interceptor value type mismatch")
		}
		func() {
			defer func() {
				if r := recover(); r != nil {
					err = fmt.Errorf("interceptor panic for %s: %v", key.Capability(), r)
					out = nil
				}
			}()
			out, err = fn(v, key)
		}()
		return out, err
	}
	return c.Effect(func() (func() error, error) {
		if c.realm == nil {
			return nil, errors.New("runtime: activation has no realm")
		}
		c.realm.addIntercept(entry)
		return func() error {
			c.realm.removeIntercept(entry)
			return nil
		}, nil
	})
}

// removeSlot drops an uncommitted Installing slot (install failed).
func (c *Context) removeSlot(target *effectSlot) {
	for i, s := range c.effects {
		if s == target {
			c.effects = append(c.effects[:i], c.effects[i+1:]...)
			return
		}
	}
}

// beginUnwind marks the context as unwinding, cancels it, and snapshots the
// committed effects (in registration order). The caller executes the returned
// snapshot in reverse order outside the context lock.
func (c *Context) beginUnwind() []*effectSlot {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.state != contextActive {
		return nil
	}
	c.state = contextUnwinding
	c.cancel()
	var out []*effectSlot
	for _, s := range c.effects {
		if s.state == effectCommitted {
			out = append(out, s)
		}
	}
	return out
}

// runInverses executes the committed-effect snapshot in strict LIFO order,
// continuing past errors, and aggregates every error. Must not be called while
// holding the context lock.
func (c *Context) runInverses(slots []*effectSlot) error {
	var errs []error
	for i := len(slots) - 1; i >= 0; i-- {
		s := slots[i]
		c.mu.Lock()
		if s.state != effectCommitted {
			// Already handled by a late-install path; exactly-once.
			c.mu.Unlock()
			continue
		}
		s.state = effectUndoing
		c.mu.Unlock()

		if s.inverse != nil {
			if err := guardedInverse(s.inverse); err != nil {
				errs = append(errs, err)
			}
		}

		c.mu.Lock()
		s.state = effectUndone
		c.mu.Unlock()
	}
	c.mu.Lock()
	c.state = contextUnwound
	c.unwindErr = errors.Join(c.unwindErr, errors.Join(errs...))
	err := c.unwindErr
	c.mu.Unlock()
	return err
}

// guardedInstall runs an Effect install and converts a panic into an
// ErrEffectInstallPanic-wrapped error, so user code can never crash the
// activation boundary from inside an install function.
func guardedInstall(install func() (func() error, error)) (inverse func() error, err error) {
	defer func() {
		if r := recover(); r != nil {
			inverse = nil
			err = fmt.Errorf("%w: %v", ErrEffectInstallPanic, r)
		}
	}()
	return install()
}

// guardedInverse runs one effect inverse (or Component Cleanup) and converts a
// panic into an ErrInversePanic-wrapped error. Unwinding always continues past
// a panicking inverse so no remaining effect is skipped.
func guardedInverse(inverse func() error) (err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("%w: %v", ErrInversePanic, r)
		}
	}()
	return inverse()
}
