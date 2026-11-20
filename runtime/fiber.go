package runtime

import (
	"context"
	"sync"

	"dynamic-runtime/runtime/internal/wait"
)

// Fiber is the Runtime-owned lifecycle object for one Component instance.
//
// A Fiber is a long-lived logical object; an activation is a single run cycle;
// a Context belongs to one activation. A Fiber MUST never hold a reusable
// Context across activations.
//
// The lifecycle state of a Fiber is mutated exclusively inside the
// orchestrator's serialized decision domain. Component code MUST NOT mutate
// Fiber state.
type Fiber struct {
	id        FiberID
	component Component
	rt        *Runtime

	// realm is the provider scope this fiber belongs to. Runtime.Load defaults
	// to the Runtime root realm; ctx.Child defaults to the parent's realm; an
	// explicit scope derives a child realm (Phase 3).
	realm *realm

	// rehome is the in-flight realm-move state (orchestrator-only; nil when
	// no rehome is pending). See rehome.go.
	rehome *rehomePending

	// revising guards Revise against concurrent invocations on the SAME fiber
	// (RT-06): two overlapping revisions would both dispose the old fiber and
	// both insert a replacement, producing two fibers at one ownership
	// position. CAS'd under mu by Fiber.Revise; cleared when the revision
	// returns (success, error, or ctx cancellation) unless the fiber reached
	// Gone during its own revision (it must never be revised again).
	revising bool

	// keyRealms is the fiber's per-key isolation table (paper Definition 24,
	// the realm table ρ): a key present here resolves and provides against the
	// named namespace instead of the fiber's scope realm. Entries are fixed at
	// insertion (Child/Load); a runtime reassignment is a revision (retire ->
	// reinsert), never an in-place resolve change.
	//
	// routeMu synchronizes this table across the two actors that touch it after
	// publication: the orchestrator (Rehome step 2 rewrites the whole table and
	// migrates records) and activation goroutines (Provide/Require resolve a
	// key's namespace, async Provide registers into it). It is a NARROW lock —
	// held only around the map lookup/assignment plus the realm map operation,
	// never across user install/inverse code or orchestrator waits. This is the
	// linearization point that keeps a Provide from registering into a namespace
	// Rehome has already migrated past (and fixes the map data race).
	routeMu   sync.RWMutex
	keyRealms map[CapabilityKey]*realm

	parent   *Fiber
	children map[FiberID]*Fiber

	// name caches Component.Name() at fiber creation (ENG-02): observability
	// and diagnostics read it from decision/read goroutines — user code runs
	// exactly once, on the mounting goroutine.
	name string

	// inject/provide cache the Component declarations at fiber-creation time so
	// the orchestrator never executes Component code on the decision goroutine.
	// ENG-03: the slices are COPIES — a component returning a shared, later
	// mutated slice would otherwise change a mounted fiber's declaration
	// behind the runtime's back.
	inject  []Dependency
	provide []Capability

	// mu guards the externally observable decision state for concurrent
	// readers (State, Err, waiters). Mutations are performed by the
	// orchestrator goroutine while holding mu, which makes every published
	// state a consistent snapshot.
	mu         sync.RWMutex
	state      FiberState
	intent     Intent
	err        error
	activation *activation

	signal *wait.Signal

	// orchestrator-owned withdrawal bookkeeping (see reconcile.go). These are
	// only touched by the orchestrator goroutine.
	withdrawing     bool
	unloadRequested bool
	waitGates       map[FiberID]struct{} // fibers whose end this fiber awaits

	// finalizePending defers the final state (Failed/Gone/Pending) until owned
	// children have reached Gone. Only set while a finished activation is
	// waiting for its children.
	finalizePending     bool
	pendingActivationID ActivationID // activation whose final state is deferred
	pendingApplyErr     error
	pendingUnwindErr    error
}

func newFiber(rt *Runtime, component Component) *Fiber {
	return &Fiber{
		component: component,
		rt:        rt,
		realm:     rt.rootRealm,
		state:     StatePending,
		intent:    IntentMounted,
		signal:    wait.New(),
		// ENG-02: cache the component name at creation. Snapshot paths call it
		// from the orchestrator's read paths; Name() is user code and must run
		// once on the mounting goroutine, never on a decision/read path.
		name: component.Name(),
	}
}

// ID returns the Runtime-unique Fiber identity.
func (f *Fiber) ID() FiberID { return f.id }

// cacheDeclarations stores copies of the component's declared dependency and
// capability slices (ENG-03): the runtime must not observe later mutations of
// a slice the component returned from a shared backing array.
func cacheDeclarations(f *Fiber, inject []Dependency, provide []Capability) {
	if len(inject) > 0 {
		f.inject = append([]Dependency(nil), inject...)
	}
	if len(provide) > 0 {
		f.provide = append([]Capability(nil), provide...)
	}
}

// Name returns the Component name (diagnostics only; never an identity).
func (f *Fiber) Name() string { return f.name }

// Component returns the Component definition this Fiber instantiates.
func (f *Fiber) Component() Component { return f.component }

// State returns the current lifecycle state.
func (f *Fiber) State() FiberState {
	f.mu.RLock()
	defer f.mu.RUnlock()
	return f.state
}

// Err returns the lifecycle error attached to the current state, if any.
func (f *Fiber) Err() error {
	f.mu.RLock()
	defer f.mu.RUnlock()
	return f.err
}

// Load expresses Intent = Mounted.
//
// The intent is recorded synchronously (this is the operation's linearization
// point; concurrent Load/Dispose behave as last-writer-wins), while the actual
// state transition is decided asynchronously by the orchestrator. Load is
// idempotent.
//
// A Fiber in StateFailed does not automatically retry (per the frozen state
// table). To retry, Dispose then Load.
func (f *Fiber) Load() error {
	if f.rt.stateSnapshot() != RuntimeRunning {
		return ErrRuntimeClosed
	}
	f.mu.Lock()
	if f.intent == IntentMounted {
		f.mu.Unlock()
		return nil
	}
	f.intent = IntentMounted
	f.signal.Broadcast()
	f.mu.Unlock()

	if !f.rt.submit(&cmdLoadIntent{fiber: f}) {
		return ErrRuntimeClosed
	}
	return nil
}

// Dispose expresses Intent = Unmounted.
//
// The intent is recorded synchronously (this is the operation's linearization
// point), while the Fiber must still pass through Unloading before it becomes
// Gone. Dispose is idempotent: repeated calls are equivalent to one call and
// never double-unwind.
func (f *Fiber) Dispose() error {
	f.mu.Lock()
	if f.intent == IntentUnmounted {
		f.mu.Unlock()
		return nil
	}
	f.intent = IntentUnmounted
	f.signal.Broadcast()
	f.mu.Unlock()

	if !f.rt.submit(&cmdDispose{fiber: f}) {
		return ErrRuntimeClosed
	}
	return nil
}

// WaitInactive waits until the Fiber's current activation cycle has ended.
//
// Semantics:
//   - If the Fiber has no live activation when called (State Pending, Failed,
//     or Gone), it returns immediately.
//   - Otherwise it captures the activation that is current at call time and
//     waits until that activation has fully ended (its cleanup completed) —
//     even if the Fiber subsequently starts a new activation or rests in
//     StatePending.
//
// WaitInactive does NOT wait for the Fiber to reach StateGone and does not
// return the lifecycle error. Typical use: wait for the current run cycle to
// exit after a dependency loss:
//
//	provider.Dispose()
//	consumer.WaitInactive(ctx) // consumer may subsequently be StatePending
func (f *Fiber) WaitInactive(ctx context.Context) error {
	f.mu.RLock()
	act := f.activation
	f.mu.RUnlock()
	if act == nil {
		return nil
	}
	target := act.id

	for {
		f.mu.RLock()
		cur := f.activation
		ch := f.signal.Channel()
		f.mu.RUnlock()
		if cur == nil || cur.id != target {
			return nil
		}
		select {
		case <-ch:
		case <-ctx.Done():
			return ctx.Err()
		}
	}
}

// Ready waits until the Fiber is Active, Failed, or Gone.
//
//	Active -> nil
//	Failed -> the lifecycle error
//	Gone   -> the lifecycle error if any, otherwise ErrFiberGone
func (f *Fiber) Ready(ctx context.Context) error {
	for {
		f.mu.RLock()
		st := f.state
		err := f.err
		// intent must be snapshot INSIDE the lock: Load/Dispose write it under
		// the same mutex. Reading it after RUnlock races with a concurrent
		// Load/Dispose (联合审查 P0-01).
		intent := f.intent
		ch := f.signal.Channel()
		f.mu.RUnlock()

		switch st {
		case StateActive:
			return nil
		case StateFailed:
			if err != nil {
				return err
			}
			return ErrInvalidState
		case StateGone:
			// Gone is terminal only when the fiber does not intend to mount
			// again (or the runtime is shutting down). A Mounted fiber that is
			// transiently Gone is about to remount or land in Pending.
			if intent == IntentUnmounted || f.rt.stateSnapshot() != RuntimeRunning {
				if err != nil {
					return err
				}
				return ErrFiberGone
			}
		}

		select {
		case <-ch:
		case <-ctx.Done():
			return ctx.Err()
		}
	}
}

// Gone waits until the Fiber reaches StateGone and returns the cleanup error,
// if any. A Fiber that reaches Gone with a cleanup error still counts as Gone.
func (f *Fiber) Gone(ctx context.Context) error {
	for {
		f.mu.RLock()
		st := f.state
		err := f.err
		ch := f.signal.Channel()
		f.mu.RUnlock()

		if st == StateGone {
			return err
		}

		select {
		case <-ch:
		case <-ctx.Done():
			return ctx.Err()
		}
	}
}

// ReviseOption configures one revision composite.
type ReviseOption func(*reviseOptions)

type reviseOptions struct {
	freshIsolation bool
	resolvedRealms bool
}

// WithResolvedRealms makes the revision ADOPT the post-move namespace of
// whatever active provider currently satisfies each declared dependency
// (paper §5.2.1: after a provider's realm move, old-namespace dependents
// "follow only through their own revision" — §4.4 allows a revision to carry
// the new realm pairs). For every Inject key that currently resolves to a
// live, non-retiring provider record, the reinserted fiber's ρ(k) is fixed to
// that provider's EFFECTIVE realm for k — re-insertion-time resolution made
// explicit. Unresolvable keys keep their prior routing. This is a declared
// realm reassignment at the revision boundary, not an implicit ancestor walk:
// ordinary Require keeps resolving exactly one namespace (Def 24). Without
// it, a dependent's revision reinserts into the OLD namespace and can never
// rebind to a rehomed provider (verified at composition level).
func WithResolvedRealms() ReviseOption {
	return func(o *reviseOptions) { o.resolvedRealms = true }
}

// WithFreshIsolation reassigns the revised fiber's realm pairs (paper §4.4
// Configuration): the reinserted fiber receives a FRESH namespace and every
// declared key of the new definition resolves and provides there. Dependents
// whose own ρ still points at the old namespace see a withdrawal (Pending) —
// they follow only through their own revision, which is the paper-faithful
// reading of crossing a realm reassignment. When combined with
// WithResolvedRealms, WithFreshIsolation wins for the declared keys (a fresh
// namespace cannot simultaneously adopt foreign realms).
func WithFreshIsolation() ReviseOption {
	return func(o *reviseOptions) { o.freshIsolation = true }
}

// Revise replaces the fiber's component definition in place, following the
// paper's revision composite (§4.4 Configuration): retire (target view to ⊥),
// let the lifecycle rules deactivate it (the relied-upon guard orders the
// withdrawal consumer-first), remove the entry (owned children reach Gone
// first, children before parent), then reinsert a fresh fiber at the same
// ownership position — same parent, same provider namespace and per-key
// isolation table — with the new component definition. Dependents follow
// unprompted: once the reinserted fiber provides its keys again, the
// target-view comparison reactivates them.
//
// By Theorem 80 the system quiesces where a load of the revised configuration
// from scratch would have left it; Revise is the strict composite that HMR's
// warm routes may only shortcut while answering to the same endpoint.
//
// Revise blocks the caller until the old fiber's entry is removed and the
// reinserted fiber is published; Ready the returned fiber to await its
// activation. A revision of a Failed fiber is a sanctioned retry (a revision
// reinserts without an outcome).
//
// Cancellation semantics (ENG-06): once the retire steps have begun, the
// revision runs to completion server-side — the retire/reinsert composite is
// either finished or explicitly refused at a gate (parent finalizing, cycle,
// invalid key); it is never abandoned between retire and reinsert. An expired
// ctx makes THIS call return ctx.Err() after the insert command was admitted;
// the replacement fiber still appears (wait Ready on it via a fresh handle or
// Snapshot). Calling Revise again on the same fiber while a revision is in
// flight returns ErrReviseBusy, and a Gone fiber can never be revised again.
//
// testReviseHook is a test-only seam (never set in production) fired after the
// revision snapshots the ownership position and before the retire steps, so
// tests can interleave a Rehome into the revision window.
var testReviseHook func()

// Contract: Revise must not be called from the revising fiber's own activation
// (it waits for that activation to end — a self-revision would deadlock). The
// revision composite is a loader/orchestrator-level operation, not a component
// step.
func (f *Fiber) Revise(ctx context.Context, component Component, opts ...ReviseOption) (*Fiber, error) {
	if ctx == nil || component == nil {
		return nil, ErrInvalidState
	}
	if f.rt.stateSnapshot() != RuntimeRunning {
		return nil, ErrRuntimeClosed
	}
	var o reviseOptions
	for _, opt := range opts {
		if opt != nil {
			opt(&o)
		}
	}
	// RT-06: at most one revision in flight per fiber, and never revise a Gone
	// fiber (reinserting would resurrect the position). The guard is taken
	// BEFORE any retire step: two overlapping revisions would both dispose the
	// old fiber and both insert a replacement at the same ownership position.
	f.mu.Lock()
	if f.state == StateGone {
		f.mu.Unlock()
		return nil, ErrInvalidState
	}
	if f.revising {
		f.mu.Unlock()
		return nil, ErrReviseBusy
	}
	f.revising = true
	f.mu.Unlock()
	defer func() {
		f.mu.Lock()
		if f.state != StateGone {
			f.revising = false
		}
		f.mu.Unlock()
	}()

	// Ownership position (parent/realm are immutable after creation).
	f.mu.RLock()
	parent := f.parent
	scope := f.realm
	f.mu.RUnlock()
	if testReviseHook != nil {
		testReviseHook()
	}

	// Retire + deactivate + remove (children first). Dispose is idempotent;
	// Gone waits for the entry's removal with the deferred-finalization gate.
	if err := f.Dispose(); err != nil {
		return nil, err
	}
	if err := f.Gone(ctx); err != nil {
		return nil, err
	}

	inject := component.Inject()
	provide := component.Provide()
	reply := make(chan reviseInsertReply, 1)
	// RT-08: the per-key isolation table is read at INSERT time on the
	// orchestrator from the old fiber (its table is final once Gone — no
	// rehome can touch a non-Active fiber), so a rehome that completed during
	// the retire window is inherited, not lost.
	if !f.rt.submit(&cmdReviseInsert{
		old:            f,
		parent:         parent,
		component:      component,
		inject:         inject,
		provide:        provide,
		realm:          scope,
		freshIsolation: o.freshIsolation,
		resolvedRealms: o.resolvedRealms,
		reply:          reply,
	}) {
		return nil, ErrRuntimeClosed
	}
	res := <-reply
	if res.err != nil {
		return nil, res.err
	}
	return res.fiber, nil
}
