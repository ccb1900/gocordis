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
	done  chan error
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
	if f.rehome != nil {
		c.done <- ErrRehomeBusy
		return
	}
	id := ProviderIdentity{FiberID: f.id, ActivationID: act.id}

	// Phase 1a — retire the provisions in every namespace they live in.
	oldRealms := fiberNamespaces(f)
	for _, rr := range oldRealms {
		for _, rec := range rr.recordsOwnedBy(id) {
			rr.markRetiringOwn(rec.key, id)
		}
	}

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

	// Prepare the plan; phase 2 fires from the gate-clear continuation.
	fresh := newRealm(f.realm)
	fresh.id = o.rt.newScopeID()
	f.rehome = &rehomePending{fresh: fresh, done: c.done}

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
		f.rehome = nil
		plan.done <- ErrRehomeNotActive
		return
	}
	id := ProviderIdentity{FiberID: f.id, ActivationID: act.id}

	// Re-home the declared keys (inject + provide) to the fresh namespace.
	declared := make(map[CapabilityKey]struct{}, len(f.inject)+len(f.provide))
	for _, d := range f.inject {
		declared[d.Key] = struct{}{}
	}
	for _, k := range f.provide {
		declared[k] = struct{}{}
	}
	freshTable := make(map[CapabilityKey]*realm, len(declared))
	for k := range declared {
		freshTable[k] = plan.fresh
	}
	f.keyRealms = freshTable

	// Move the provision records out of the old namespaces.
	for _, rr := range fiberNamespaces(f) {
		if rr == plan.fresh {
			continue
		}
		for _, rec := range rr.recordsOwnedBy(id) {
			if _, wasRetiring := rr.removeOwn(rec.key, id); wasRetiring || true {
				if err := plan.fresh.registerOwn(rec.key, id, rec.value); err != nil {
					// The fresh namespace is fresh: a duplicate is impossible
					// unless the record was already moved; ignore defensively.
					continue
				}
			}
		}
	}
	f.rehome = nil
	close(plan.done)

	// Consumers mounted inside the fresh namespace (children created after
	// the rehome inherit the new table) can now bind.
	o.sweepWaiting()
}
