package runtime

import (
	"errors"
	"fmt"
	"sort"
	"sync"
)

// ErrEventHandlerPanic wraps a panic raised inside an Event handler. The panic
// is contained at the Kernel boundary and reported as a handler error; the
// Emit broadcast continues to the remaining handlers (Emit semantics).
var ErrEventHandlerPanic = errors.New("event handler panic")

// eventRegistry is the Runtime-owned Kernel Event registry (P1.1).
//
// It is a plain data structure and dispatch infrastructure — NOT a lifecycle
// authority. It never creates Fibers, never changes Fiber/Activation state,
// never runs Dependency reconciliation, and never touches the orchestrator.
// Registration/unregistration are the install/inverse of ordinary Context
// Effects; dispatch is synchronous broadcast on the caller's goroutine.
//
// Linearization: every registration, unregistration, and dispatch snapshot is
// serialized on g.mu (registry changes and snapshot reads never execute
// handler code under the lock; handlers run strictly outside the lock).
type eventRegistry struct {
	mu sync.RWMutex

	// seq is the global monotonic registration order (Decision Record D5b:
	// the current deterministic implementation strategy, NOT a semantic
	// invariant). Every registration receives the next value; a dispatch
	// snapshot executes in ascending seq order, independent of map iteration.
	seq uint64

	// byKey maps an Event identity to its registrations. Each registration
	// records its registration realm (emitter-path matching) and owner
	// activation.
	byKey map[eventKeyID][]*eventReg
}

// eventReg is one handler registration.
type eventReg struct {
	key     eventKeyID
	realm   *realm // HR: the registration realm used for emitter-path matching
	seq     uint64 // global registration order
	owner   ProviderIdentity
	handler func(any) error
}

func newEventRegistry() *eventRegistry {
	return &eventRegistry{byKey: make(map[eventKeyID][]*eventReg)}
}

// register appends one registration and assigns the next global registration
// sequence number.
func (g *eventRegistry) register(owner ProviderIdentity, realm *realm, key eventKeyID, handler func(any) error) *eventReg {
	g.mu.Lock()
	defer g.mu.Unlock()
	g.seq++
	reg := &eventReg{key: key, realm: realm, seq: g.seq, owner: owner, handler: handler}
	g.byKey[key] = append(g.byKey[key], reg)
	return reg
}

// unregister removes exactly reg (idempotent). Called only from an Effect
// inverse; the Effect exactly-once machinery guarantees one physical removal
// per registration.
func (g *eventRegistry) unregister(reg *eventReg) {
	if reg == nil {
		return
	}
	g.mu.Lock()
	defer g.mu.Unlock()
	chain := g.byKey[reg.key]
	for i, x := range chain {
		if x == reg {
			if len(chain) == 1 {
				delete(g.byKey, reg.key)
			} else {
				g.byKey[reg.key] = append(chain[:i], chain[i+1:]...)
			}
			return
		}
	}
}

// count returns the number of live registrations (diagnostics / residue
// checks).
func (g *eventRegistry) count() int {
	g.mu.RLock()
	defer g.mu.RUnlock()
	n := 0
	for _, chain := range g.byKey {
		n += len(chain)
	}
	return n
}

// eventOwnerActive reports whether the registration owner is currently a
// valid handler owner: the owning Fiber exists, is StateActive, and its
// current activation matches the registration (Decision Record §17: owner
// valid = Active + activation match).
func (r *Runtime) eventOwnerActive(owner ProviderIdentity) bool {
	r.mu.RLock()
	f := r.fibers[owner.FiberID]
	r.mu.RUnlock()
	if f == nil {
		return false
	}
	f.mu.RLock()
	defer f.mu.RUnlock()
	return f.state == StateActive && f.activation != nil && f.activation.id == owner.ActivationID
}

// realmOnPath reports whether target lies on emitter's realm path
// ([emitter realm, ..., Root]).
func realmOnPath(target, emitter *realm) bool {
	for cur := emitter; cur != nil; cur = cur.parent {
		if cur == target {
			return true
		}
	}
	return false
}

// eventSnapshot returns the owner-valid registrations matching key that are
// visible from emitterRealm, in deterministic global registration order. The
// snapshot is captured under the registry lock: registrations and
// unregistrations during handler execution never change the current dispatch
// batch (they are visible from the next Emit).
func (r *Runtime) eventSnapshot(key eventKeyID, emitterRealm *realm) []*eventReg {
	g := r.eventReg
	if g == nil || emitterRealm == nil {
		return nil
	}
	g.mu.RLock()
	defer g.mu.RUnlock()
	var out []*eventReg
	for _, reg := range g.byKey[key] {
		if !realmOnPath(reg.realm, emitterRealm) {
			continue
		}
		if !r.eventOwnerActive(reg.owner) {
			continue
		}
		out = append(out, reg)
	}
	sort.Slice(out, func(i, j int) bool { return out[i].seq < out[j].seq })
	return out
}

// On registers handler for key on ctx's current activation (Decision Record
// §20 contract 1).
//
// Registration IS an Effect: it goes through Context.effect with kind = Event,
// so the handler is installed under the activation's existing exactly-once,
// LIFO, install/unwind-race, and late-effect protections. Unwinding runs the
// inverse, which removes exactly this registration — there is no global
// handler registry and no handler can outlive its owner activation.
func On[T any](c *Context, key EventKey[T], handler EventHandler[T]) error {
	if c == nil {
		return errors.New("runtime: nil context")
	}
	if !key.valid() {
		return errors.New("runtime: zero-value EventKey (use NewEventKey)")
	}
	if handler == nil {
		return errors.New("runtime: nil event handler")
	}
	typed := func(payload any) error { return handler(payload.(T)) }
	return c.effect(EffectKindEvent, CapabilityKey{}, func() (func() error, error) {
		if c.rt == nil || c.rt.eventReg == nil {
			return nil, errors.New("runtime: event registry unavailable")
		}
		if c.realm == nil {
			return nil, errors.New("runtime: activation has no realm")
		}
		reg := c.rt.eventReg.register(
			ProviderIdentity{FiberID: c.fiberID, ActivationID: c.activationID},
			c.realm,
			key.id(),
			typed,
		)
		return func() error {
			c.rt.eventReg.unregister(reg)
			return nil
		}, nil
	})
}

// Emit synchronously broadcasts payload to every handler of key visible from
// ctx's realm path (ancestor registrations + current-scope registrations;
// additive, no shadowing, sibling-isolated) in deterministic registration
// order (Decision Record §5 D4, §17).
//
// Semantics:
//   - Emit is a synchronous broadcast — NOT await-all, NOT Parallel, NOT
//     channel+goroutine delivery. When Emit returns, every handler it invoked
//     has already completed.
//   - A dispatch snapshot is captured before any handler runs; registrations
//     and unregistrations during handler execution do not change the current
//     batch and only affect the next Emit.
//   - Every matching handler is invoked on the caller goroutine; one
//     handler's error does not stop later handlers. Handler errors (and
//     contained handler panics) are aggregated with errors.Join.
//   - Emitter cancellation is cooperative: it is checked before each handler.
//     A canceled emitter stops further dispatch and Emit returns ctx.Err()
//     joined with the errors already collected. A running handler is never
//     force-terminated.
func Emit[T any](c *Context, key EventKey[T], payload T) error {
	if c == nil {
		return errors.New("runtime: nil context")
	}
	if !key.valid() {
		return errors.New("runtime: zero-value EventKey (use NewEventKey)")
	}
	if c.rt == nil || c.rt.eventReg == nil {
		return errors.New("runtime: event registry unavailable")
	}
	if c.base != nil && c.base.Err() != nil {
		return c.base.Err()
	}
	snap := c.rt.eventSnapshot(key.id(), c.realm)
	var errs []error
	for _, reg := range snap {
		if c.base != nil && c.base.Err() != nil {
			return errors.Join(append(errs, c.base.Err())...)
		}
		if err := guardedEventHandler(reg.handler, payload); err != nil {
			errs = append(errs, err)
		}
	}
	return errors.Join(errs...)
}

// guardedEventHandler invokes one Event handler on the caller goroutine and
// converts a panic into an ErrEventHandlerPanic-wrapped handler error, so one
// panicking handler can never stop the remaining handlers of an Emit
// broadcast.
func guardedEventHandler(h func(any) error, payload any) (err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("%w: %v", ErrEventHandlerPanic, r)
		}
	}()
	return h(payload)
}
