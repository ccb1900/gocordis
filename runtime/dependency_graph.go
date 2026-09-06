package runtime

// This file implements the realm-aware dependency graph, dependency
// resolution, and withdrawal-gate bookkeeping. All functions run on the
// orchestrator goroutine except where noted.
//
// Dependency edges are identity-resolved: an edge records the specific
// provider identity (realm owner fiber + activation) a consumer's activation
// resolved through its realm path. Withdrawal notifies only the fibers bound
// to that identity, so sibling realms with the same key never cross-notify.

// addGraphEdge records that f's current activation depends on the provider
// identity id (the provider it actually resolved).
func (o *orchestrator) addGraphEdge(id ProviderIdentity, f *Fiber) {
	set := o.graph[id]
	if set == nil {
		set = make(map[*Fiber]struct{})
		o.graph[id] = set
	}
	set[f] = struct{}{}
}

// removeGraphEdge removes f's dependency edge for provider id. Edges belong to
// an activation and are removed when that activation ends (no ghost edges).
func (o *orchestrator) removeGraphEdge(id ProviderIdentity, f *Fiber) {
	if set := o.graph[id]; set != nil {
		delete(set, f)
		if len(set) == 0 {
			delete(o.graph, id)
		}
	}
}

// resolveDependency reports whether key currently has a valid, satisfiable
// provider in the consumer realm r's path, and returns that provider identity.
// A provider is valid only while its owner Fiber is Active on the same
// activation that registered it and the record is not retiring.
func (o *orchestrator) resolveDependency(r *realm, key CapabilityKey) (ProviderIdentity, bool) {
	if r == nil {
		return ProviderIdentity{}, false
	}
	rec, ok := r.lookup(key)
	if !ok || rec.retiring {
		return ProviderIdentity{}, false
	}
	owner := o.lookupFiber(rec.identity.FiberID)
	if owner == nil {
		return ProviderIdentity{}, false
	}
	owner.mu.RLock()
	valid := owner.state == StateActive &&
		owner.activation != nil &&
		owner.activation.id == rec.identity.ActivationID
	owner.mu.RUnlock()
	if !valid {
		return ProviderIdentity{}, false
	}
	return rec.identity, true
}

// dependenciesSatisfied reports whether every declared dependency currently has
// a valid provider on f's realm path.
func (o *orchestrator) dependenciesSatisfied(f *Fiber) bool {
	for _, dep := range f.inject {
		if _, ok := o.resolveDependency(f.realm, dep.Key); !ok {
			return false
		}
	}
	return true
}

// captureDependencies snapshots the provider identity of every declared
// dependency as resolved through f's realm path. Called when the fiber enters
// Loading, before Apply runs.
func (o *orchestrator) captureDependencies(f *Fiber) []DependencySnapshot {
	var snaps []DependencySnapshot
	for _, dep := range f.inject {
		id, ok := o.resolveDependency(f.realm, dep.Key)
		if !ok {
			// reconcile() only starts Loading when all deps are satisfied, so
			// this is unreachable in a consistent runtime.
			continue
		}
		snaps = append(snaps, DependencySnapshot{Key: dep.Key, Provider: id})
	}
	return snaps
}

// dependenciesStillValid reports whether the activation's captured snapshot is
// still valid after Apply completed (identity + provider validity on the
// fiber's realm path).
func (o *orchestrator) dependenciesStillValid(f *Fiber, act *activation) bool {
	if act == nil {
		return false
	}
	for _, snap := range act.deps {
		id, ok := o.resolveDependency(f.realm, snap.Key)
		if !ok || id != snap.Provider {
			return false
		}
	}
	return true
}

// sweepWaiting re-evaluates Pending/Gone fibers, which may now be able to
// start because a provider became Active. Called after every activation that
// becomes Active.
func (o *orchestrator) sweepWaiting() {
	o.rt.mu.RLock()
	fibers := make([]*Fiber, 0, len(o.rt.fibers))
	for _, f := range o.rt.fibers {
		fibers = append(fibers, f)
	}
	o.rt.mu.RUnlock()

	for _, f := range fibers {
		if f.state == StatePending || f.state == StateGone {
			o.reconcile(f)
		}
	}
}

// addWaitGate makes f wait for member's activation to end before f may unload.
func (o *orchestrator) addWaitGate(f, member *Fiber) {
	member.mu.RLock()
	live := member.activation != nil && (member.state == StateLoading || member.state == StateActive || member.state == StateUnloading)
	member.mu.RUnlock()
	if !live {
		return
	}
	if f.waitGates == nil {
		f.waitGates = make(map[FiberID]struct{})
	}
	if _, dup := f.waitGates[member.id]; dup {
		return
	}
	f.waitGates[member.id] = struct{}{}

	waiters := o.gateWaiters[member.id]
	if waiters == nil {
		waiters = make(map[*Fiber]struct{})
		o.gateWaiters[member.id] = waiters
	}
	waiters[f] = struct{}{}
}

// notifyGateWaiters is called when f's activation has ended; any fiber that
// was waiting on f may now be able to unload.
func (o *orchestrator) notifyGateWaiters(f *Fiber) {
	waiters := o.gateWaiters[f.id]
	if len(waiters) == 0 {
		return
	}
	delete(o.gateWaiters, f.id)
	for w := range waiters {
		if w.waitGates != nil {
			delete(w.waitGates, f.id)
		}
		if len(w.waitGates) != 0 {
			continue
		}
		if w.finalizePending {
			o.finalizePendingFiber(w)
		} else if w.withdrawing && w.state == StateActive {
			o.maybeStartUnload(w)
		}
	}
}

// clearWaitGates drops any remaining gates f holds (defensive; normally gates
// are empty by the time an activation ends) and removes f from the reverse
// index.
func (o *orchestrator) clearWaitGates(f *Fiber) {
	if len(f.waitGates) == 0 {
		return
	}
	for memberID := range f.waitGates {
		if waiters := o.gateWaiters[memberID]; waiters != nil {
			delete(waiters, f)
			if len(waiters) == 0 {
				delete(o.gateWaiters, memberID)
			}
		}
	}
	f.waitGates = nil
}
