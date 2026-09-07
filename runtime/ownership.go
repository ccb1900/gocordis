package runtime

// Ownership links a Fiber to the child Fibers it owns.
//
// Ownership is distinct from dependency: "I need you" (dependency) is not
// "my lifecycle contains you" (ownership). Children are created explicitly
// through Context.Child and are withdrawn before their owner finalizes.

// cmdSpawnChild creates a child Fiber owned by the activation that owns ctx.
func (c *cmdSpawnChild) apply(o *orchestrator) {
	reply := func(f *Fiber, err error) {
		c.reply <- spawnChildReply{fiber: f, err: err}
	}
	if c.ctx == nil {
		reply(nil, ErrInvalidState)
		return
	}

	parent := o.lookupFiber(c.ctx.fiberID)
	if parent == nil {
		reply(nil, ErrInvalidState)
		return
	}

	// The context must belong to the fiber's current activation and must still
	// be active (parent not already withdrawing/closed).
	parent.mu.RLock()
	act := parent.activation
	actOK := act != nil && act.id == c.ctx.activationID
	parentState := parent.state
	parent.mu.RUnlock()

	if o.closing {
		reply(nil, ErrRuntimeClosed)
		return
	}
	if !actOK {
		reply(nil, ErrContextClosed)
		return
	}
	if parentState != StateLoading && parentState != StateActive {
		reply(nil, ErrContextClosed)
		return
	}
	if !c.ctx.acceptingWork() {
		reply(nil, ErrContextClosed)
		return
	}

	child := newFiber(o.rt, c.component)
	child.inject = c.inject
	child.provide = c.provide
	child.parent = parent
	// Default ctx.Child inherits the parent's scope/realm. An explicit scope
	// (WithScope) derives a child realm whose parent is this realm, enabling
	// shadowing and sibling isolation.
	if c.newScope {
		child.realm = newRealm(parent.realm)
		child.realm.id = o.rt.newScopeID()
	} else {
		child.realm = parent.realm
	}

	o.rt.mu.Lock()
	child.id = FiberID(o.rt.nextFiberID.Add(1))
	// Reject a declared dependency cycle at the Child boundary before the new
	// child is published.
	var existing []*Fiber
	for _, x := range o.rt.fibers {
		existing = append(existing, x)
	}
	if err := findDeclaredCycle(child, existing); err != nil {
		o.rt.mu.Unlock()
		reply(nil, err)
		return
	}
	o.rt.fibers[child.id] = child
	o.rt.mu.Unlock()

	if parent.children == nil {
		parent.children = make(map[FiberID]*Fiber)
	}
	parent.children[child.id] = child

	o.reconcile(child)
	o.rt.emitEvent(EventFiberCreated, child.id, 0, nil)
	reply(child, nil)
}

// disposeChildren disposes every owned child that is not already terminally
// Gone, and gates the parent's finalization on live children reaching Gone.
//
// Returns true when at least one live child was gated (the caller must defer
// finalizing until those children end).
func (o *orchestrator) disposeChildren(f *Fiber) bool {
	if len(f.children) == 0 {
		return false
	}
	ids := make([]FiberID, 0, len(f.children))
	for id := range f.children {
		ids = append(ids, id)
	}

	gated := false
	for _, id := range ids {
		ch := f.children[id]
		if ch == nil {
			delete(f.children, id)
			continue
		}
		ch.mu.RLock()
		st := ch.state
		in := ch.intent
		live := ch.activation != nil && (st == StateLoading || st == StateActive || st == StateUnloading)
		ch.mu.RUnlock()

		if st == StateGone && in == IntentUnmounted {
			delete(f.children, id) // prune terminal child
			continue
		}
		if live {
			// Gate the parent on this child's activation ending (Gone), then
			// ask the child to withdraw. Order matters: the gate must exist
			// before the child can possibly end.
			o.addWaitGate(f, ch)
			gated = true
		}
		o.setIntent(ch, IntentUnmounted)
		o.reconcile(ch)
	}
	return gated
}

// unlinkTerminalChild removes a child that has become terminally Gone from its
// parent's children map (called after finalizing a child as Gone+Unmounted).
func (o *orchestrator) unlinkTerminalChild(child *Fiber) {
	if child.parent == nil {
		return
	}
	if m := child.parent.children; m != nil {
		delete(m, child.id)
	}
	child.parent = nil
}
