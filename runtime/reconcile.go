package runtime

// reconcile computes the next action for a single Fiber from the current facts
// (intent, state, dependency state, runtime state). It never blocks: anything
// asynchronous is launched on a separate goroutine and reported back through a
// completion command.
//
// The decision shape is frozen by the specification's state-transition table:
//
//	Pending  + Unmounted                    -> Gone
//	Pending  + Mounted  + deps unsatisfied  -> Pending (wait)
//	Pending  + Mounted  + deps satisfied    -> Loading
//	Loading  + Unmounted/deps lost          -> cancel Apply, unwind after Apply
//	Active   + Unmounted/deps lost          -> begin withdrawal
//	Failed   + Unmounted                    -> Gone
//	Failed   + Mounted                      -> Failed (no automatic retry)
//	Gone     + Unmounted                    -> Gone
//	Gone     + Mounted  + deps unsatisfied  -> Pending
//	Gone     + Mounted  + deps satisfied    -> Loading
func (o *orchestrator) reconcile(f *Fiber) {
	if f == nil {
		return
	}

	switch f.state {
	case StatePending:
		o.reconcilePending(f)

	case StateLoading:
		// Apply is running. If the fiber no longer should activate, cancel
		// cooperatively now; the actual transition happens when cmdApplyDone
		// arrives (Unwind never overlaps Apply).
		if o.desiredIntent(f) == IntentUnmounted || !o.dependenciesStillValid(f, f.activation) {
			o.cancelLoadingActivation(f)
		}

	case StateActive:
		if o.desiredIntent(f) == IntentUnmounted || !o.dependenciesSatisfied(f) {
			o.beginWithdrawal(f)
		}

	case StateUnloading:
		// Unwind is running; cmdUnwindDone decides the next state.

	case StateFailed:
		if o.desiredIntent(f) == IntentUnmounted {
			o.finishWithoutActivation(f)
		}

	case StateGone:
		if o.closing || o.desiredIntent(f) == IntentUnmounted {
			// Terminal Gone: unlink from the owner.
			o.unlinkTerminalChild(f)
			return
		}
		if o.dependenciesSatisfied(f) {
			o.startActivation(f)
		} else {
			// Dependency loss lands in Pending, never Failed.
			o.transition(f, StatePending, nil)
		}
	}
}

func (o *orchestrator) reconcilePending(f *Fiber) {
	if o.desiredIntent(f) == IntentUnmounted {
		// No activation exists: Pending + Unmounted -> Gone.
		o.finishWithoutActivation(f)
		return
	}
	if o.dependenciesSatisfied(f) {
		o.startActivation(f)
	}
	// else: stay Pending until dependencies are satisfied.
}

// finishWithoutActivation moves an activation-less fiber (Pending or Failed)
// to terminal Gone and unlinks it from its owner.
func (o *orchestrator) finishWithoutActivation(f *Fiber) {
	o.transition(f, StateGone, nil)
	o.activationEnded(f)
	o.unlinkTerminalChild(f)
}

// cancelLoadingActivation cooperatively cancels an in-flight Apply. The fiber
// is marked so that when cmdApplyDone arrives the activation is unwound
// instead of becoming Active.
func (o *orchestrator) cancelLoadingActivation(f *Fiber) {
	f.unloadRequested = true
	if act := f.activation; act != nil && act.cancel != nil {
		act.cancel()
	}
}

// beginWithdrawal initiates withdrawal of a Fiber's current activation.
//
// Withdrawal is consumer-first: the capabilities this activation provides are
// marked retiring immediately (so no dependent can (re)bind to them), then
// every direct consumer is asked to withdraw. The provider itself only starts
// Unloading once all of those consumers have fully ended their activations
// (withdrawal gates). This guarantees the invariant that a provider is never
// withdrawn while a consumer still depends on it.
func (o *orchestrator) beginWithdrawal(f *Fiber) {
	if f.withdrawing {
		return
	}
	act := f.activation
	if act == nil {
		return
	}
	f.withdrawing = true
	f.unloadRequested = true

	if act.cancel != nil {
		act.cancel()
	}

	// 1. Retire every capability this activation provides (its own realm).
	id := ProviderIdentity{FiberID: f.id, ActivationID: act.id}
	for _, rec := range f.realm.recordsOwnedBy(id) {
		f.realm.markRetiringOwn(rec.key, id)
	}

	// 2. Collect direct consumers (unique) bound to THIS provider identity via
	// realm-resolved dependency edges.
	var consumers []*Fiber
	seen := make(map[*Fiber]struct{})
	for c := range o.graph[id] {
		if c == f {
			continue
		}
		if _, dup := seen[c]; dup {
			continue
		}
		seen[c] = struct{}{}
		consumers = append(consumers, c)
	}

	// 3. Gate this fiber's Unload on every live consumer ending.
	for _, c := range consumers {
		o.addWaitGate(f, c)
	}

	// 4. Ask each consumer to withdraw (recursively consumer-first).
	for _, c := range consumers {
		o.reconcile(c)
	}

	// 5. Dispose owned children (they gate this fiber's Unload).
	o.disposeChildren(f)

	// 6. If there are no dependents and no children, unload now.
	o.maybeStartUnload(f)
}

// maybeStartUnload begins the Unloading transition once the fiber's withdrawal
// gates (consumers/children) are all clear.
func (o *orchestrator) maybeStartUnload(f *Fiber) {
	if f.state != StateActive {
		return
	}
	if len(f.waitGates) > 0 {
		return
	}
	act := f.activation
	if act == nil {
		return
	}
	o.transition(f, StateUnloading, nil)
	o.rt.emitEvent(EventActivationUnloading, f.id, act.id, nil)
	slots := act.ctx.beginUnwind()
	go o.runUnwind(f, act, slots)
}
