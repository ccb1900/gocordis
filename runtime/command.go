package runtime

// command is a serialized lifecycle decision request. All lifecycle-changing
// operations (Load, Dispose, Replace, ApplyFinished, UnwindFinished,
// DependencyChanged, Close) are expressed as commands and linearized by the
// orchestrator's single command queue.
type command interface {
	apply(*orchestrator)
}

// cmdLoad registers a freshly created Fiber (intent is already Mounted).
type cmdLoad struct {
	fiber *Fiber
}

// cmdLoadIntent expresses Intent = Mounted for an existing Fiber.
type cmdLoadIntent struct {
	fiber *Fiber
}

// cmdDispose expresses Intent = Unmounted for an existing Fiber.
type cmdDispose struct {
	fiber *Fiber
}

// cmdApplyDone reports that an activation's Apply finished.
type cmdApplyDone struct {
	fiberID      FiberID
	activationID ActivationID
	cleanup      Cleanup
	err          error

	// diverted marks an iterator activation that observed a target-view turn
	// between iterations (inertial L-Divert): route to Unloading with the
	// accumulated inverses; never a Component failure.
	diverted bool
}

// cmdUnwindDone reports that an activation's unwind finished.
type cmdUnwindDone struct {
	fiberID      FiberID
	activationID ActivationID
	err          error
}

// cmdSpawnChild requests creation of an owned child Fiber. The declarations
// are cached by the caller so the orchestrator never runs Component code.
type cmdSpawnChild struct {
	ctx       *Context
	component Component
	inject    []Dependency
	provide   []Capability
	newScope  bool
	// scopeKeys lists the declared keys to re-home into the fresh namespace
	// when newScope is set (empty = all, the WithScope behavior).
	scopeKeys []CapabilityKey
	// keyRealms assigns per-key isolation realms for the new fiber (nil when
	// the child has no isolated key; WithScope-derived entry is pre-built).
	keyRealms map[CapabilityKey]*realm
	reply     chan spawnChildReply
}

type spawnChildReply struct {
	fiber *Fiber
	err   error
}

// cmdClose requests Runtime shutdown.
type cmdClose struct {
	ack chan error
}

// cmdReviseInsert reinserts a revised fiber at the same ownership position
// (paper §4.4 Configuration revision composite, reinsert step). It mirrors
// cmdSpawnChild's construction but is keyed on the parent FIBER rather than a
// parent Context: the old fiber's activation is gone by insert time, so no
// parent Context exists. Runs on the orchestrator; lifecycle authority stays
// with the Runtime.
type cmdReviseInsert struct {
	// old is the fiber being replaced; its final per-key isolation table is
	// read at insert time (RT-08 — see apply).
	old       *Fiber
	parent    *Fiber // nil for a root-loaded fiber
	component Component
	inject    []Dependency
	provide   []Capability
	realm     *realm
	// freshIsolation reassigns the revised fiber's realm pairs (paper §4.4
	// Configuration: a revision may carry "the new realm pairs"): the new
	// fiber gets a FRESH namespace and all its declared keys are re-homed to
	// it. Dependents bound to the old namespace see withdrawal and go Pending
	// (their own ρ still points there) — crossing namespaces requires their
	// own revision, which is the paper-faithful reading.
	freshIsolation bool
	reply          chan reviseInsertReply
}

type reviseInsertReply struct {
	fiber *Fiber
	err   error
}

func (c *cmdReviseInsert) apply(o *orchestrator) {
	reply := func(f *Fiber, err error) { c.reply <- reviseInsertReply{fiber: f, err: err} }
	if c.component == nil {
		reply(nil, ErrInvalidState)
		return
	}
	if o.closing {
		reply(nil, ErrRuntimeClosed)
		return
	}

	child := newFiber(o.rt, c.component)
	cacheDeclarations(child, c.inject, c.provide)
	child.realm = c.realm
	// RT-08: read the old fiber's FINAL per-key isolation table at insert time
	// (on the orchestrator, under routeMu). The table of a Gone fiber is stable
	// — Rehome requires Active — so a rehome that completed during the retire
	// window is inherited rather than lost (ρ fixed at the new fiber's
	// insertion, paper Definition 24).
	if c.old != nil && !c.freshIsolation {
		c.old.routeMu.RLock()
		if len(c.old.keyRealms) > 0 {
			child.keyRealms = make(map[CapabilityKey]*realm, len(c.old.keyRealms))
			for k, r := range c.old.keyRealms {
				child.keyRealms[k] = r
			}
		}
		c.old.routeMu.RUnlock()
	}
	if c.freshIsolation {
		chainParent := c.realm
		if c.parent != nil {
			chainParent = c.parent.realm
		}
		fresh := newRealm(chainParent)
		fresh.id = o.rt.newScopeID()
		child.realm = fresh
		child.keyRealms = make(map[CapabilityKey]*realm, len(child.inject)+len(child.provide))
		for _, d := range child.inject {
			child.keyRealms[d.Key] = fresh
		}
		for _, k := range child.provide {
			child.keyRealms[k] = fresh
		}
	}
	if c.parent != nil {
		c.parent.mu.RLock()
		parentState := c.parent.state
		parentFinalizing := c.parent.withdrawing
		c.parent.mu.RUnlock()
		// RT-07: the parent must not be finalizing. Inserting a child under a
		// Gone or withdrawing/unloading parent would orphan it (the owner's
		// disposeChildren has run or is about to run); the revision is refused
		// and the caller retries against a valid owner.
		if parentState == StateGone || parentState == StateUnloading || parentFinalizing {
			reply(nil, ErrInvalidState)
			return
		}
		child.parent = c.parent
	}

	o.rt.mu.Lock()
	child.id = FiberID(o.rt.nextFiberID.Add(1))
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

	if c.parent != nil {
		if c.parent.children == nil {
			c.parent.children = make(map[FiberID]*Fiber)
		}
		c.parent.children[child.id] = child
	}

	child.intent = IntentMounted
	o.reconcile(child)
	o.rt.emitEvent(EventFiberCreated, child.id, 0, nil)
	reply(child, nil)
}
