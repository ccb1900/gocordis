package runtime

import (
	"context"
	"errors"
	"fmt"
	"sort"
	"sync"
)

// ErrEventHandlerPanic wraps a panic raised inside an Event handler. The panic
// is contained at the Kernel boundary and reported as a handler error; the
// Emit broadcast continues to the remaining handlers (Emit semantics).
var ErrEventHandlerPanic = errors.New("event handler panic")

// ErrWaterfallNextTwice is returned by the second invocation of one Waterfall
// node's next() (P1.4 Next Contract: a node's next may advance the chain at
// most once; a repeated call is a Handler contract violation). The second call
// never re-runs the downstream chain, and the violation is surfaced in the
// dispatch result even if the handler ignores the returned error.
var ErrWaterfallNextTwice = errors.New("event waterfall: next called twice")

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
	handler func(context.Context, any) error
	// chain is non-nil only for chain-aware registrations (OnWaterfall, P1.4):
	// a handler that receives a real Next continuation. Waterfall dispatch
	// invokes it with a Next bound to the remaining snapshot; every other
	// dispatch mode invokes the handler projection instead (a terminal node
	// whose next() is trivially satisfied). Plain On registrations keep
	// chain == nil.
	chain func(context.Context, any, Next) error
}

func newEventRegistry() *eventRegistry {
	return &eventRegistry{byKey: make(map[eventKeyID][]*eventReg)}
}

// register appends one registration and assigns the next global registration
// sequence number. handler is the projection used by Emit/Serial/Parallel (and
// by Waterfall for plain On registrations); chain is the chain-aware variant
// used by Waterfall for OnWaterfall registrations (nil for plain On).
func (g *eventRegistry) register(owner ProviderIdentity, realm *realm, key eventKeyID, handler func(context.Context, any) error, chain func(context.Context, any, Next) error) *eventReg {
	g.mu.Lock()
	defer g.mu.Unlock()
	g.seq++
	reg := &eventReg{key: key, realm: realm, seq: g.seq, owner: owner, handler: handler, chain: chain}
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

// onEvent installs one Event registration as an Effect on ctx (shared by On
// and OnWaterfall). handler is the projection used by Emit/Serial/Parallel;
// chain is the chain-aware variant used by Waterfall (nil for plain On).
func onEvent(c *Context, key eventKeyID, handler func(context.Context, any) error, chain func(context.Context, any, Next) error) error {
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
			key,
			handler,
			chain,
		)
		return func() error {
			c.rt.eventReg.unregister(reg)
			return nil
		}, nil
	})
}

// On registers handler for key on ctx's current activation (Decision Record
// §20 contract 1).
//
// Registration IS an Effect: it goes through Context.effect with kind = Event,
// so the handler is installed under the activation's existing exactly-once,
// LIFO, install/unwind-race, and late-effect protections. Unwinding runs the
// inverse, which removes exactly this registration — there is no global
// handler registry and no handler can outlive its owner activation.
//
// On handlers receive (ctx, payload): they cannot continue a chain, which is
// the Emit/Serial/Parallel contract. Chain-aware handlers (P1.4) use
// OnWaterfall instead. Both live in the SAME registry, Event identity, Realm
// scope, snapshot, and ownership model — a plain handler reached by a
// Waterfall dispatch runs as a transparent node (it holds no chain authority,
// so the chain continues automatically after it returns nil).
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
	typed := func(ctx context.Context, payload any) error {
		return handler(ctx, payload.(T))
	}
	return onEvent(c, key.id(), typed, nil)
}

// OnWaterfall registers a chain-aware handler for key on ctx's current
// activation (P1.4 §3, §20).
//
// The handler receives a Next continuation and participates in Waterfall's
// ordered middleware chain: calling next() advances to the next snapshot
// handler and returns when the downstream chain completed; not calling next()
// short-circuits the chain. Registration ownership is identical to On — the
// handler is an Effect of its owner activation and can never outlive it.
//
// The handler is stored once and shared by every dispatch mode: Emit/Serial/
// Parallel invoke it as a terminal node (its next() is trivially satisfied and
// returns nil), and Waterfall invokes it with a Next bound to the remaining
// snapshot. Waterfall dispatch strategy is therefore the ONLY difference — the
// four modes share one registry, identity, scope, snapshot, and ownership
// (P1.4 §30).
func OnWaterfall[T any](c *Context, key EventKey[T], handler WaterfallHandler[T]) error {
	if c == nil {
		return errors.New("runtime: nil context")
	}
	if !key.valid() {
		return errors.New("runtime: zero-value EventKey (use NewEventKey)")
	}
	if handler == nil {
		return errors.New("runtime: nil waterfall handler")
	}
	chain := func(ctx context.Context, payload any, next Next) error {
		return handler(ctx, payload.(T), next)
	}
	// Projection for Emit/Serial/Parallel and for snapshots that mix plain and
	// chain-aware handlers: a terminal node whose next() is trivially
	// satisfied (there is no downstream in those dispatch frames).
	plain := func(ctx context.Context, payload any) error {
		return handler(ctx, payload.(T), func() error { return nil })
	}
	return onEvent(c, key.id(), plain, chain)
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
// ---------------------------------------------------------------------------
// Extension dispatch surface (ADR-0004)
// ---------------------------------------------------------------------------

// EventBinding is one registered Event handler as the kernel's registry read
// model exposes it: owner identity, deterministic global registration order,
// the guarded handler, and the waterfall chain (nil for a plain On
// registration). Bindings are a snapshot: dispatch modes are EXTENSION policy
// (ADR-0004), and the kernel guarantees only registration-as-reversible-effect
// plus this read model. Visibility (emitter realm path) is already resolved.
type EventBinding struct {
	Owner   ProviderIdentity
	Order   uint64
	Handler func(context.Context, any) error
	Chain   func(context.Context, any, Next) error
}

// EventBindings returns the handlers visible from this activation's context
// chain for the Event identified by id, in deterministic registration order.
// It is the single kernel touchpoint extension dispatch modes need.
func (c *Context) EventBindings(id EventKeyID) []EventBinding {
	if c == nil || c.rt == nil || c.rt.eventReg == nil {
		return nil
	}
	snap := c.rt.eventSnapshot(id, c.realm)
	out := make([]EventBinding, 0, len(snap))
	for _, reg := range snap {
		out = append(out, EventBinding{
			Owner:   reg.owner,
			Order:   reg.seq,
			Handler: reg.handler,
			Chain:   reg.chain,
		})
	}
	return out
}

// GuardEventHandler invokes one Event handler on the caller goroutine and
// converts a panic into an ErrEventHandlerPanic-wrapped error, so one
// panicking handler can never take down a dispatch. Extension dispatch modes
// apply it to every invocation (the kernel-side unified panic policy).
func GuardEventHandler(h func(context.Context, any) error, ctx context.Context, payload any) (err error) {
	defer func() {
		if r := recover(); r != nil {
			err = fmt.Errorf("%w: %v", ErrEventHandlerPanic, r)
		}
	}()
	return h(ctx, payload)
}
