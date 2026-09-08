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

// EffectKind classifies what an effect slot represents. The kind is
// observability metadata recorded at registration time so a Developer Console
// can interpret an effect (Provider registration, Component Cleanup, or a
// generic reversible Effect) without re-deriving semantics.
type EffectKind uint8

const (
	// EffectKindCustom is a generic reversible Effect installed by Component
	// code through Context.Effect.
	EffectKindCustom EffectKind = iota
	// EffectKindProvider is a provider registration made through Provide.
	EffectKindProvider
	// EffectKindCleanup is the inverse of the Component.Apply Cleanup return.
	EffectKindCleanup
	// EffectKindEvent is a Kernel Event handler registration made through
	// Context.On (P1.1). Metadata only: the slot carries no Capability key.
	EffectKindEvent
	// EffectKindIntercept is a read-time interception installment (value
	// interceptor or context-carried metadata ν, paper Definition 27).
	EffectKindIntercept
)

func (k EffectKind) String() string {
	switch k {
	case EffectKindProvider:
		return "Provider"
	case EffectKindCleanup:
		return "Cleanup"
	case EffectKindCustom:
		return "Custom"
	case EffectKindEvent:
		return "Event"
	case EffectKindIntercept:
		return "Intercept"
	default:
		return fmt.Sprintf("EffectKind(%d)", uint8(k))
	}
}

// effectSlot is one reversible effect. Its state machine guarantees that an
// inverse is executed exactly once, even when installation races with unwind.
type effectSlot struct {
	seq uint64

	state effectSlotState

	// kind + key are registration-time observability metadata (UI-02): kind is
	// always set; key is set for Provider effects and empty otherwise.
	kind EffectKind
	key  CapabilityKey

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

	// fiber is the owning fiber (its keyRealms table participates in per-key
	// isolation resolution, paper Definition 24). Orchestrator-published and
	// read-only for the context's lifetime.
	fiber *Fiber

	// diverted records that an iterator activation observed a target-view
	// turn between iterations (paper L-Divert). Written only by the Apply
	// goroutine between its own probe and its completion; read by the same.
	diverted bool

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
	declaredInject map[CapabilityKey]struct{}
	// declaredMeta carries the component-declared metadata d(k) for declared
	// MetaKey dependencies (paper Definition 26), if any.
	declaredMeta    map[CapabilityKey]any
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
		declaredMeta:    make(map[CapabilityKey]any),
		declaredProvide: make(map[CapabilityKey]struct{}, len(provide)),
	}
	for _, d := range inject {
		c.declaredInject[d.Key] = struct{}{}
		if d.Meta != nil {
			c.declaredMeta[d.Key] = d.Meta
		}
	}
	for _, cap := range provide {
		c.declaredProvide[cap] = struct{}{}
	}
	return c
}

// Cancel cancels the activation's cooperative-cancellation context (Done /
// Err observers). It is a host-side cooperative signal, not a lifecycle
// decision: the Fiber's state is still owned by the orchestrator. Cancellation
// of an emitter's context is how an activation stops participating in
// extension-level dispatch mid-flight.
func (c *Context) Cancel() {
	if c.cancel != nil {
		c.cancel()
	}
}

// markDiverted records the divert observed by this activation's iterator.
func (c *Context) markDiverted() {
	c.mu.Lock()
	c.diverted = true
	c.mu.Unlock()
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
		s := &effectSlot{seq: c.nextSeq, state: effectCommitted, kind: EffectKindCleanup, inverse: inverse}
		c.nextSeq++
		c.effects = append(c.effects, s)
		c.mu.Unlock()
		c.emitEffectEvent(EventEffectCommitted, s)
		return
	}
	c.mu.Unlock()

	// Defensive path: the activation is already unwinding/unwound. In the
	// sequential lifecycle this is unreachable (unwind never starts before
	// Apply finishes), but if it ever happens the inverse must still run
	// exactly once instead of leaking.
	s := &effectSlot{seq: c.nextSeq, state: effectUndoing, kind: EffectKindCleanup, inverse: inverse}
	c.mu.Lock()
	c.state = contextUnwound
	c.nextSeq++
	c.mu.Unlock()
	c.emitEffectEvent(EventEffectUndoing, s)
	var err error
	if inverse != nil {
		err = guardedInverse(inverse)
	}
	c.mu.Lock()
	s.state = effectUndone
	c.unwindErr = errors.Join(c.unwindErr, err)
	c.mu.Unlock()
	c.emitEffectEvent(EventEffectUndone, s)
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
	return c.effect(EffectKindCustom, CapabilityKey{}, install)
}

// effect is the metadata-aware implementation behind Effect.
func (c *Context) effect(kind EffectKind, key CapabilityKey, install func() (func() error, error)) error {
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
	s := &effectSlot{seq: c.nextSeq, state: effectInstalling, kind: kind, key: key}
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
		c.emitEffectEvent(EventEffectCommitted, s)
		return nil
	}

	// The context is already unwinding/unwound: do not commit as a persistent
	// effect; execute the returned inverse immediately (exactly once).
	s.inverse = inverse
	s.state = effectUndoing
	c.mu.Unlock()
	c.emitEffectEvent(EventEffectUndoing, s)

	var invErr error
	if inverse != nil {
		invErr = inverse()
	}

	c.mu.Lock()
	s.state = effectUndone
	c.unwindErr = errors.Join(c.unwindErr, invErr)
	c.mu.Unlock()
	c.emitEffectEvent(EventEffectUndone, s)
	return nil
}

// emitEffectEvent publishes one effect-lifecycle event for slot s. Called
// without holding the context lock.
func (c *Context) emitEffectEvent(t EventType, s *effectSlot) {
	var key string
	if s.kind == EffectKindProvider {
		key = s.key.String()
	}
	c.rt.emitEvent(t, c.fiberID, c.activationID, EffectEventData{Kind: s.kind, Key: key})
}

// Child creates a new Fiber owned by this activation's Fiber.
//
// Ownership means the parent's lifecycle contains the child: when the parent
// activation withdraws, owned children are disposed and must reach Gone before
// the parent finalizes. Ownership is distinct from dependency.
// ScopeOption configures Context.Child scope derivation.
type ScopeOption func(*scopeOptions)

type scopeOptions struct {
	newRealm  bool
	scopeKeys []CapabilityKey
	keyRealms map[CapabilityKey]*realm
}

// WithScope makes Child create an explicit child realm (its own provider
// namespace) instead of inheriting this fiber's realm. Keys the child declares
// resolve and provide against that namespace — sibling scopes are invisible to
// each other and resolution never falls back to ancestors (ADR-0001, paper
// Definition 24: isolation is a per-key realm table fixed at insertion). The
// child realm still chains to its parent realm for context inheritance
// (interception metadata, extension event scoping) — never for resolution.
func WithScope() ScopeOption {
	return func(o *scopeOptions) { o.newRealm = true }
}

// Isolate derives a fresh namespace for ONLY the listed declared keys of the
// child (paper Definition 24/25: per-key realm assignment ρ, fixed at
// insertion). The child's other declared keys keep resolving and providing in
// the parent's namespace, so a component can isolate one key while sharing the
// rest of its context — per-key isolation with sharing. Isolate implies a
// fresh namespace like WithScope; combining it with WithScope re-homes the
// union (explicitly listed keys plus nothing else — WithScope alone is full).
func Isolate(keys ...Capability) ScopeOption {
	return func(o *scopeOptions) {
		o.newRealm = true
		o.scopeKeys = append(o.scopeKeys, keys...)
	}
}

// scopeRealms carries a per-key isolation table built by IsolateIn.
func (o *scopeOptions) isolateIn(fiberRealm *realm, parentTable map[CapabilityKey]*realm, declared []Dependency, provide []Capability) {
	if o.keyRealms == nil {
		o.keyRealms = make(map[CapabilityKey]*realm)
	}
	for k, r := range parentTable {
		o.keyRealms[k] = r
	}
	for _, d := range declared {
		if _, ok := o.keyRealms[d.Key]; !ok {
			o.keyRealms[d.Key] = fiberRealm
		}
	}
	for _, k := range provide {
		if _, ok := o.keyRealms[k]; !ok {
			o.keyRealms[k] = fiberRealm
		}
	}
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

	// Per-key isolation table (paper Definition 24): inherit overrides already
	// present on this context's fiber, then default every declared key to this
	// fiber's effective namespaces. WithScope re-homes all keys to the fresh
	// child realm (built on the orchestrator at spawn).
	if len(c.fiber.keyRealms) > 0 || o.newRealm {
		o.isolateIn(c.realm, c.fiber.keyRealms, inject, provide)
	}

	reply := make(chan spawnChildReply, 1)
	if !c.rt.submit(&cmdSpawnChild{
		ctx:       c,
		component: component,
		inject:    inject,
		provide:   provide,
		newScope:  o.newRealm,
		keyRealms: o.keyRealms,
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
	// The provision lives in the key's EFFECTIVE namespace (paper Definition
	// 24: the fiber provides (k, ρ(k))) — not blindly in its scope realm.
	eff := effectiveRealm(c.fiber, key)
	err := c.effect(EffectKindProvider, key, func() (func() error, error) {
		if err := eff.registerOwn(key, id, value); err != nil {
			return nil, err
		}
		return func() error {
			removed, wasRetiring := eff.removeOwn(key, id)
			if removed && !wasRetiring {
				// The record was never satisfiable (e.g. failed-Apply cleanup);
				// a retiring record already announced its withdrawal to its
				// dependents at the retirement decision.
				c.rt.emitEvent(EventProviderWithdrawn, c.fiberID, c.activationID, ProviderEventData{Key: key.String()})
			}
			return nil
		}, nil
	})
	if err != nil {
		return err
	}
	c.rt.emitEvent(EventProviderPublished, c.fiberID, c.activationID, ProviderEventData{Key: key.String()})
	return nil
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

// ProvideMeta registers c's activation as the exclusive provider of key with
// a metadata-INTERPRETING provider (paper Definition 26: σ(k): ℳₖ → 𝒱ₖ). Every
// RequireMeta of key evaluates the provider on merged metadata
// d(k) ⊕ₖ ι(k): the component-declared metadata merged with the
// context-carried metadata, right-biased (context wins).
func ProvideMeta[T, M any](c *Context, key MetaKey[T, M], provider func(M) T) error {
	return c.provideCap(key.Capability(), provider)
}

// RequireMeta resolves key's provider and evaluates it on the merged metadata
// d(k) ⊕ₖ ι(k) (paper Definition 27 get): the DECLARED metadata from this
// activation's Inject set merged with the CONTEXT-CARRIED metadata installed
// by interceptors (ancestor realms first, install order; rightmost wins).
// Interception affects only how the binding is USED — it never changes what
// the key resolves to and cannot gate activation (paper §6.3).
func RequireMeta[T, M any](c *Context, key MetaKey[T, M]) (T, error) {
	var zero T
	cap := key.Capability()
	if _, ok := c.declaredInject[cap]; !ok {
		return zero, fmt.Errorf("%w: %s is not declared in this activation's Inject set", ErrUndeclaredRequire, cap)
	}
	rec, ok := effectiveRealm(c.fiber, cap).lookupOwn(cap)
	if !ok || rec.value == nil {
		return zero, ErrDependencyMissing
	}
	fn, ok := rec.value.(func(M) T)
	if !ok {
		return zero, errors.New("runtime: provider value type mismatch")
	}
	// Declared metadata d(k).
	var mu M
	if dep, ok := c.declaredMeta[cap]; ok {
		dm, ok := dep.(M)
		if !ok {
			return zero, errors.New("runtime: declared dependency metadata type mismatch")
		}
		mu = key.merge(mu, dm)
	}
	// Context-carried metadata ι(k): ancestor installments first, install
	// order; each ν folds onto the inherited value (rightmost wins).
	if iota, found := c.realm.metaForKey(cap); found {
		im, ok := iota.(M)
		if !ok {
			return zero, errors.New("runtime: context metadata type mismatch")
		}
		mu = key.merge(mu, im)
	}
	return fn(mu), nil
}

// InterceptMeta installs context-carried metadata ν for key (paper Definition
// 27 intercept): the derived context merges ν onto the inherited metadata at
// key (ι(k) ⊕ₖ ν, right-biased — the context constrains how components use the
// coeffect without modifying the provider or the dependency value).
//
// It is a generic FREE function. Installation is reversible: it goes through
// ctx.Effect, so unwinding restores the inherited metadata. Like the value
// interceptor chain, metadata is not part of the dependency graph: installing
// it never triggers a reload and never changes what the key resolves to. A
// panic inside a combine function is contained and returned as an error.
func InterceptMeta[T, M any](c *Context, key MetaKey[T, M], nu M) error {
	if c == nil {
		return errors.New("runtime: nil context")
	}
	cap := key.Capability()
	combineAny := func(a, b any) (out any) {
		av, aok := a.(M)
		bv, bok := b.(M)
		if a != nil && !aok {
			return errors.New("runtime: context metadata type mismatch")
		}
		if b != nil && !bok {
			return errors.New("runtime: context metadata type mismatch")
		}
		defer func() {
			if r := recover(); r != nil {
				out = fmt.Errorf("runtime: metadata combine panicked: %v", r)
			}
		}()
		return key.merge(av, bv)
	}
	entry := &metaEntry{key: cap, nu: nu, combine: combineAny}
	if err := c.effect(EffectKindIntercept, cap, func() (func() error, error) {
		c.realm.addMeta(entry)
		return func() error {
			c.realm.removeMeta(entry)
			return nil
		}, nil
	}); err != nil {
		return err
	}
	return nil
}

// Require resolves the value provided for key by the activation's declared
// dependency. It returns ErrDependencyMissing when no provider is registered.
func Require[T any](c *Context, key Key[T]) (T, error) {
	var zero T
	if _, ok := c.declaredInject[key.Capability()]; !ok {
		return zero, fmt.Errorf("%w: %s is not declared in this activation's Inject set", ErrUndeclaredRequire, key.Capability())
	}
	rec, ok := effectiveRealm(c.fiber, key.Capability()).lookupOwn(key.Capability())
	if !ok || rec.value == nil {
		return zero, ErrDependencyMissing
	}
	// Apply the read-time interception chain (ancestor -> this realm). The
	// chain inherits along the scope-realm path (ADR-0002 predecessor: this is
	// the context-carried metadata of paper Definition 26; P3 replaces the
	// value-transform chain with the metadata monoid).
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
		c.emitEffectEvent(EventEffectUndoing, s)

		if s.inverse != nil {
			if err := guardedInverse(s.inverse); err != nil {
				errs = append(errs, err)
			}
		}

		c.mu.Lock()
		s.state = effectUndone
		c.mu.Unlock()
		c.emitEffectEvent(EventEffectUndone, s)
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
