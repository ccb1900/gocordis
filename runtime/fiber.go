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

	// keyRealms is the fiber's per-key isolation table (paper Definition 24,
	// the realm table ρ): a key present here resolves and provides against the
	// named namespace instead of the fiber's scope realm. Entries are fixed at
	// insertion (Child/Load); runtime reassignment of a key's realm is a
	// revision (retire -> reinsert), never an in-place resolve change.
	// Written only on the orchestrator goroutine before publication; read-only
	// afterwards, so no lock.
	keyRealms map[CapabilityKey]*realm

	parent   *Fiber
	children map[FiberID]*Fiber

	// inject/provide cache the Component declarations at fiber-creation time so
	// the orchestrator never executes Component code on the decision goroutine.
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
	}
}

// ID returns the Runtime-unique Fiber identity.
func (f *Fiber) ID() FiberID { return f.id }

// Name returns the Component name (diagnostics only; never an identity).
func (f *Fiber) Name() string { return f.component.Name() }

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
			if f.intent == IntentUnmounted || f.rt.stateSnapshot() != RuntimeRunning {
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
