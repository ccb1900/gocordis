package runtime

import (
	"context"
	"errors"
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
}

func newContext(rt *Runtime, fiberID FiberID, activationID ActivationID) *Context {
	base, cancel := context.WithCancel(context.Background())
	return &Context{
		rt:           rt,
		fiberID:      fiberID,
		activationID: activationID,
		cancel:       cancel,
		base:         base,
		state:        contextActive,
	}
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
		err = inverse()
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

	// Step 3: run install outside the lock.
	inverse, installErr := install()

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
func (c *Context) Child(component Component) (*Fiber, error) {
	if component == nil {
		return nil, ErrInvalidState
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
		reply:     reply,
	}) {
		return nil, ErrRuntimeClosed
	}
	resp := <-reply
	return resp.fiber, resp.err
}

func (c *Context) provideCap(key CapabilityKey, value any) error {
	id := ProviderIdentity{FiberID: c.fiberID, ActivationID: c.activationID}
	return c.Effect(func() (func() error, error) {
		if err := c.rt.providers.register(key, id, value); err != nil {
			return nil, err
		}
		return func() error {
			c.rt.providers.remove(key, id)
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
	rec, ok := c.rt.providers.lookup(key.Capability())
	if !ok || rec.value == nil {
		return zero, ErrDependencyMissing
	}
	v, ok := rec.value.(T)
	if !ok {
		return zero, errors.New("runtime: provider value type mismatch")
	}
	return v, nil
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
			if err := s.inverse(); err != nil {
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
