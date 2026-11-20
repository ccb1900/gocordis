package runtime

import (
	"context"
	"errors"
)

// Rehome (paper §5.2.1 short route): move a provider's provisions to a fresh
// namespace WITHOUT unloading the provider — "a realm moved without reloading
// its provider". The calculus carries a realm move as a revision composite
// (retire -> reinsert, see Revise + WithFreshIsolation); the loader may
// answer to the same endpoint with this shorter route: the provider fiber
// stays Active on the SAME activation, its provisions are withdrawn from the
// old namespace (consumers there withdraw first, consumer-first) and
// re-published in the fresh namespace. Dependents whose ρ still points at
// the old namespace lose their binding (Pending) — they follow only through
// their own revision, which is the paper-faithful reading of crossing a
// realm move.

// Errors. Compare with errors.Is.
var (
	ErrRehomeNotActive = errors.New("runtime: rehome requires an Active fiber")
	ErrRehomeBusy      = errors.New("runtime: rehome already in progress")
	ErrReviseBusy      = errors.New("runtime: a revision of this fiber is already in progress")
)

// RehomeOption configures Fiber.Rehome.
type RehomeOption func(*rehomeSpec)

type rehomeSpec struct {
	freshIsolation bool
}

// RehomeWithFreshIsolation reassigns the fiber's realm pairs to ONE fresh
// namespace: every declared key resolves and provides there after the rehome.
// This is v1's only rehome shape.
func RehomeWithFreshIsolation() RehomeOption {
	return func(o *rehomeSpec) { o.freshIsolation = true }
}

// rehomePending is the in-flight rehome state on a Fiber (orchestrator-only).
type rehomePending struct {
	fresh *realm
	// oldRealms is the namespace snapshot taken at the START of the rehome
	// (phase 1). Phase 2 MUST move provisions using THIS snapshot: deriving
	// old namespaces from fiberNamespaces(f) after replacing f.keyRealms
	// loses every per-key isolation realm whose key table just changed —
	// leaving retiring records stranded there (and Dispose unable to find
	// them). Paper Def 24/25: ρ is fixed at insertion; a realm move carries
	// the record, not just the routing table.
	oldRealms []*realm
	done      chan error
}

// Rehome moves the fiber's provisions to a fresh namespace without reloading
// the fiber: provisions are withdrawn from the old namespace consumer-first
// (the relied guard orders dependents first), the per-key isolation table is
// re-homed to the fresh namespace, and the provisions are re-published there.
// The fiber stays Active on the same activation — that is the short-route
// property this primitive exists for.
//
// The call blocks until the rehome completes (consumers of the old namespace
// have detached) or ctx expires. Rehome must not race with Dispose; a
// withdrawal started concurrently completes after the rehome.
func (f *Fiber) Rehome(ctx context.Context, opts ...RehomeOption) error {
	if ctx == nil {
		ctx = context.Background()
	}
	var spec rehomeSpec
	for _, opt := range opts {
		if opt != nil {
			opt(&spec)
		}
	}
	if !spec.freshIsolation {
		return errors.New("runtime: rehome requires RehomeWithFreshIsolation (v1)")
	}
	if ctx.Err() != nil {
		return ctx.Err()
	}
	if r := f.rt; r == nil || r.stateSnapshot() != RuntimeRunning {
		return ErrRuntimeClosed
	}

	f.mu.RLock()
	active := f.state == StateActive && f.activation != nil
	f.mu.RUnlock()
	if !active {
		return ErrRehomeNotActive
	}

	done := make(chan error, 1)
	if !f.rt.submit(&cmdRehomeBegin{fiber: f, done: done}) {
		return ErrRuntimeClosed
	}
	select {
	case err := <-done:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// cmdRehomeBegin runs phase 1 on the orchestrator: retire the provisions in
// the old namespaces, gate the fiber on its consumers, ask the consumers to
// withdraw. Phase 2 (re-home + re-publish) runs when the last consumer
// detaches (see notifyGateWaiters).
type cmdRehomeBegin struct {
	fiber *Fiber
	done  chan error
}

func (c *cmdRehomeBegin) apply(o *orchestrator) {
	f := c.fiber
	f.mu.RLock()
	act := f.activation
	st := f.state
	f.mu.RUnlock()

	if o.closing || st != StateActive || act == nil {
		c.done <- ErrRehomeNotActive
		return
	}
	if o.rt.mode == runtimeDeterministic {
		c.done <- errors.New("runtime: rehome is not supported in deterministic mode")
		return
	}
	if f.rehome != nil {
		c.done <- ErrRehomeBusy
		return
	}
	id := ProviderIdentity{FiberID: f.id, ActivationID: act.id}

	// Capture the old namespaces, then publish the rehome marker and retire
	// every record already in them — BOTH under routeMu. Publishing the marker
	// inside the same critical section as the retiring sweep closes the window
	// in which a late Provide could register a non-retiring record into a
	// namespace the sweep had already passed (paper §5.2.1: a record in a
	// namespace being vacated must never satisfy a new dependent).
	oldRealms := fiberNamespaces(f)
	fresh := newRealm(f.realm)
	fresh.id = o.rt.newScopeID()
	f.routeMu.Lock()
	f.rehome = &rehomePending{fresh: fresh, oldRealms: oldRealms, done: c.done}
	for _, rr := range oldRealms {
		for _, rec := range rr.recordsOwnedBy(id) {
			rr.markRetiringOwn(rec.key, id)
		}
	}
	f.routeMu.Unlock()

	// Phase 1b — collect the consumers bound to THIS provider identity and
	// gate the rehome on their full detachment (consumer-first, identical to
	// the withdrawal cascade).
	var consumers []*Fiber
	seen := make(map[*Fiber]struct{})
	for c2 := range o.graph[id] {
		if c2 == f {
			continue
		}
		if _, dup := seen[c2]; dup {
			continue
		}
		seen[c2] = struct{}{}
		consumers = append(consumers, c2)
	}
	for _, c2 := range consumers {
		o.addWaitGate(f, c2)
	}

	for _, c2 := range consumers {
		o.reconcile(c2)
	}
	if len(consumers) == 0 {
		o.executeRehomeStep2(f)
	}
}

// executeRehomeStep2 runs phase 2: re-home the per-key table to the fresh
// namespace, move the provision records (same identity, retiring cleared),
// and sweep waiting fibers. Runs on the orchestrator.
func (o *orchestrator) executeRehomeStep2(f *Fiber) {
	plan := f.rehome
	if plan == nil {
		return
	}
	f.mu.RLock()
	act := f.activation
	f.mu.RUnlock()
	if act == nil {
		f.routeMu.Lock()
		f.rehome = nil
		f.routeMu.Unlock()
		plan.done <- ErrRehomeNotActive
		return
	}
	id := ProviderIdentity{FiberID: f.id, ActivationID: act.id}

	// Re-home routing UNDER routeMu. RT-03/RT-04: the fresh table MERGES with
	// the current table instead of replacing it, and only keys this fiber
	// PROVIDES are re-homed. The short route moves the fiber's PROVISIONS —
	// not its dependencies and not other fibers' routes:
	//   - an inject-only key keeps resolving where its provider actually lives,
	//     so the fiber stays Active on the same activation (RT-03; re-homing it
	//     would strand the binding in the vacated namespace and force a
	//     withdrawal, contradicting the short-route promise);
	//   - inherited entries for keys this fiber does not declare are preserved
	//     for future children (RT-04; paper Def 24/25: ρ is fixed at insertion,
	//     and a realm move carries the provisions, not the whole table);
	//   - a key that is both injected and provided follows its provision into
	//     the fresh namespace, keeping the per-key realm identity consistent.
	var moved []CapabilityKey
	// The migration scans the phase-1 snapshot (NOT fiberNamespaces(f): the
	// table was just re-merged, so the current namespaces may no longer include
	// the per-key isolation realms that hold the provisions). Events are
	// collected and emitted after unlocking — a sink must never run while the
	// route lock is held.
	f.routeMu.Lock()
	freshTable := make(map[CapabilityKey]*realm, len(f.keyRealms)+len(f.provide))
	for k, r := range f.keyRealms {
		freshTable[k] = r
	}
	for _, k := range f.provide {
		freshTable[k] = plan.fresh
	}
	f.keyRealms = freshTable
	for _, rr := range plan.oldRealms {
		if rr == plan.fresh {
			continue
		}
		for _, rec := range rr.recordsOwnedBy(id) {
			if _, _ = rr.removeOwn(rec.key, id); true {
				if err := plan.fresh.registerOwn(rec.key, id, rec.value); err != nil {
					// The fresh namespace is fresh: a duplicate is impossible
					// unless the record was already moved; ignore defensively.
					continue
				}
				moved = append(moved, rec.key)
			}
		}
	}
	f.rehome = nil
	f.routeMu.Unlock()

	for _, k := range moved {
		o.rt.emitEvent(EventProviderWithdrawn, f.id, act.id, ProviderEventData{Key: k.String()})
		o.rt.emitEvent(EventProviderPublished, f.id, act.id, ProviderEventData{Key: k.String()})
	}
	close(plan.done)

	// Consumers mounted inside the fresh namespace (children created after
	// the rehome inherit the new table) can now bind.
	o.sweepWaiting()

	// Re-decide THIS fiber now that the rehome guard is gone. A Dispose (or
	// Runtime.Close) that arrived while f.rehome != nil saw beginWithdrawal
	// return early; without this reconcile the recorded Unmounted intent
	// would be stranded with the fiber still Active and Gone never reached.
	// reconcile is idempotent: a normal rehome with no intent change is a
	// no-op, and an already-withdrawing fiber is not re-entered.
	o.reconcile(f)

	// RT-02: a Dispose that PRECEDED the rehome had already set withdrawing=true
	// (gates pending). notifyGateWaiters dispatches to the rehome branch first,
	// and reconcile's beginWithdrawal defers to the withdrawing flag — so the
	// pending unload would never resume and the fiber would stay Active
	// forever. The rehome guard is gone and the gates are clear, so resume the
	// unload here (maybeStartUnload re-checks state, rehome and gates itself).
	f.mu.RLock()
	pendingUnload := f.withdrawing && f.state == StateActive
	f.mu.RUnlock()
	if pendingUnload {
		o.maybeStartUnload(f)
	}
}
