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
	parent    *Fiber // nil for a root-loaded fiber
	component Component
	inject    []Dependency
	provide   []Capability
	realm     *realm
	keyRealms map[CapabilityKey]*realm
	reply     chan reviseInsertReply
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
	child.inject = c.inject
	child.provide = c.provide
	child.realm = c.realm
	if len(c.keyRealms) > 0 {
		child.keyRealms = make(map[CapabilityKey]*realm, len(c.keyRealms))
		for k, r := range c.keyRealms {
			child.keyRealms[k] = r
		}
	}
	if c.parent != nil {
		c.parent.mu.RLock()
		parentState := c.parent.state
		c.parent.mu.RUnlock()
		if parentState == StateGone {
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
