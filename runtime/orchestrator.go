package runtime

import (
	"errors"
	"fmt"
	"sync"
)

// orchestrator is the Runtime's single lifecycle decision authority.
//
// It owns all Fiber state transitions and dependency/provider/ownership
// decisions. Component Apply/Cleanup code is NEVER executed on this goroutine:
// the orchestrator only decides and launches asynchronous work, then processes
// completion commands. Per-fiber exclusivity (at most one Apply OR one Unwind
// per Fiber at a time) is enforced here.
type orchestrator struct {
	rt *Runtime

	commands chan command
	stop     chan struct{}
	done     chan struct{}

	// sendMu serializes submit() against orchestrator shutdown: stop is closed
	// while holding sendMu, so once the orchestrator has stopped, submit()
	// deterministically returns false (it can never win a race against a
	// buffered command channel).
	sendMu sync.Mutex

	// closing is the orchestrator-local view of Runtime shutdown. Once true,
	// every Fiber is treated as wanting Intent = Unmounted and no new
	// activation may start.
	closing bool

	// graph records, per provider identity, the fibers whose current activation
	// resolved a dependency to that specific provider (realm-aware: edges are
	// identity-resolved, never key-only).
	graph map[ProviderIdentity]map[*Fiber]struct{}

	// gateWaiters is the reverse of Fiber.waitGates: fiber id -> set of
	// provider fibers waiting for that fiber's activation to end.
	gateWaiters map[FiberID]map[*Fiber]struct{}
}

func newOrchestrator(rt *Runtime) *orchestrator {
	return &orchestrator{
		rt:          rt,
		commands:    make(chan command, 256),
		stop:        make(chan struct{}),
		done:        make(chan struct{}),
		graph:       make(map[ProviderIdentity]map[*Fiber]struct{}),
		gateWaiters: make(map[FiberID]map[*Fiber]struct{}),
	}
}

func (o *orchestrator) run() {
	// done is closed after the loop exits; stop is closed (under sendMu)
	// before that, so late submissions are deterministically dropped first.
	defer close(o.done)
	for {
		select {
		case cmd := <-o.commands:
			cmd.apply(o)
			if o.closing && o.allFibersTerminal() {
				o.markClosed()
				o.sendMu.Lock()
				close(o.stop)
				o.sendMu.Unlock()
				return
			}
		case <-o.stop:
			return
		}
	}
}

// cmdClose begins Runtime shutdown on the orchestrator.
func (c *cmdClose) apply(o *orchestrator) {
	if o.closing {
		return
	}
	o.closing = true
	o.disposeAll()
}

func (o *orchestrator) lookupFiber(id FiberID) *Fiber {
	o.rt.mu.RLock()
	defer o.rt.mu.RUnlock()
	return o.rt.fibers[id]
}

// ---------------------------------------------------------------------------
// Command handlers
// ---------------------------------------------------------------------------

func (c *cmdLoad) apply(o *orchestrator) {
	o.reconcile(c.fiber)
}

func (c *cmdLoadIntent) apply(o *orchestrator) {
	// Intent was recorded synchronously by Fiber.Load; the orchestrator only
	// makes the state decision.
	o.reconcile(c.fiber)
}

func (c *cmdDispose) apply(o *orchestrator) {
	// Intent was recorded synchronously by Fiber.Dispose.
	o.reconcile(c.fiber)
}

// cmdApplyDone handles an Apply completion. Stale completions (activation ID
// mismatch) are ignored: an old activation's async completion must never
// mutate the current activation.
func (c *cmdApplyDone) apply(o *orchestrator) {
	f := o.lookupFiber(c.fiberID)
	if f == nil {
		return
	}

	f.mu.RLock()
	act := f.activation
	st := f.state
	f.mu.RUnlock()

	if act == nil || act.id != c.activationID {
		return // stale completion: ignore.
	}
	if st != StateLoading {
		// Duplicate/out-of-order completion for the current activation is an
		// internal inconsistency; never corrupt state over it.
		return
	}

	// A Component.Apply Cleanup return value becomes the activation's last
	// effect (unwound first).
	if c.cleanup != nil {
		act.ctx.addCommittedEffect(c.cleanup)
	}

	if c.err != nil {
		act.applyErr = c.err
		o.unwindAfterApply(f, act)
		return
	}

	if o.closing || o.desiredIntent(f) == IntentUnmounted || f.unloadRequested {
		o.unwindAfterApply(f, act)
		return
	}

	if !o.dependenciesStillValid(f, act) {
		// Dependencies changed while Apply was running: the activation cannot
		// become Active. This is dependency loss, not a Component failure.
		o.unwindAfterApply(f, act)
		return
	}

	o.transition(f, StateActive, nil)
	o.activationBecameActive(f, act)
}

// unwindAfterApply begins unwinding an activation whose Apply already
// finished (per-fiber exclusivity: Unwind never overlaps Apply).
func (o *orchestrator) unwindAfterApply(f *Fiber, act *activation) {
	o.transition(f, StateUnloading, nil)
	slots := act.ctx.beginUnwind()
	go o.runUnwind(f, act, slots)
}

// cmdUnwindDone handles an Unwind completion. Stale completions are ignored.
func (c *cmdUnwindDone) apply(o *orchestrator) {
	f := o.lookupFiber(c.fiberID)
	if f == nil {
		return
	}

	f.mu.RLock()
	act := f.activation
	st := f.state
	f.mu.RUnlock()

	if act == nil || act.id != c.activationID {
		return // stale completion: ignore.
	}
	if st != StateUnloading {
		return // defensive: duplicate completion.
	}

	o.finishActivation(f, act, c.err)
}

// ---------------------------------------------------------------------------
// Activation lifecycle
// ---------------------------------------------------------------------------

// startActivation moves a Fiber from Pending/Gone into Loading: it allocates a
// fresh ActivationID, creates a fresh Context, publishes Loading, and launches
// Apply asynchronously. Order matters: the activation is registered and the
// state is published before Apply can possibly report back.
func (o *orchestrator) startActivation(f *Fiber) {
	if o.closing {
		return
	}
	actID := ActivationID(o.rt.nextActivationID.Add(1))
	ctx := newContext(o.rt, f.id, actID, f.inject, f.provide, f.realm)
	act := &activation{
		id:     actID,
		fiber:  f,
		ctx:    ctx,
		cancel: ctx.cancel,
	}

	// Capture the dependency snapshot and register dependency edges before the
	// state is published as Loading.
	act.deps = o.captureDependencies(f)
	for _, d := range act.deps {
		o.addGraphEdge(d.Provider, f)
	}

	f.mu.Lock()
	f.activation = act
	f.state = StateLoading
	f.err = nil
	f.signal.Broadcast()
	f.mu.Unlock()

	go o.runApply(f, act)
}

func (o *orchestrator) runApply(f *Fiber, act *activation) {
	cleanup, err := callComponentApply(f.component, act.ctx)
	o.rt.admitCommand(&cmdApplyDone{
		fiberID:      f.id,
		activationID: act.id,
		cleanup:      cleanup,
		err:          err,
	})
}

// callComponentApply invokes Component.Apply and contains a panic at the Kernel
// boundary: it is converted to an ErrComponentApplyPanic-wrapped lifecycle
// error, so a panicking Component can never crash the process. Committed
// effects installed before the panic are unwound by the normal failure path.
func callComponentApply(comp Component, ctx *Context) (cleanup Cleanup, err error) {
	defer func() {
		if r := recover(); r != nil {
			cleanup = nil
			err = fmt.Errorf("%w: %v", ErrComponentApplyPanic, r)
		}
	}()
	return comp.Apply(ctx)
}

// runUnwind executes the activation's committed effects in LIFO order on a
// dedicated goroutine and reports the aggregated result.
func (o *orchestrator) runUnwind(f *Fiber, act *activation, slots []*effectSlot) {
	err := act.ctx.runInverses(slots)
	o.rt.admitCommand(&cmdUnwindDone{
		fiberID:      f.id,
		activationID: act.id,
		err:          err,
	})
}

// finishActivation finalizes an activation whose unwind finished.
func (o *orchestrator) finishActivation(f *Fiber, act *activation, unwindErr error) {
	o.activationEnded(f)

	f.mu.Lock()
	f.activation = nil
	f.signal.Broadcast()
	f.mu.Unlock()
	o.clearWaitGates(f)
	f.withdrawing = false
	f.unloadRequested = false

	// Owned resources (children) must reach Gone before their owner finalizes.
	if o.disposeChildren(f) {
		// Defer the final state until every live child is Gone.
		f.finalizePending = true
		f.pendingApplyErr = act.applyErr
		f.pendingUnwindErr = unwindErr
		return
	}

	o.finalizeActivation(f, act.applyErr, unwindErr)
}

// finalizeActivation publishes the terminal state of a finished activation
// (Failed on apply failure with Mounted intent; otherwise Gone followed by a
// reconcile toward Pending/Loading).
func (o *orchestrator) finalizeActivation(f *Fiber, applyErr, unwindErr error) {
	desired := o.desiredIntent(f)
	failed := applyErr != nil && desired == IntentMounted

	var err error
	if failed {
		err = errors.Join(applyErr, unwindErr)
	} else {
		err = unwindErr
	}

	if failed {
		o.transition(f, StateFailed, err)
		return
	}

	o.transition(f, StateGone, err)
	o.reconcile(f)
}

// finalizePendingFiber completes a deferred finalization once owned children
// are Gone.
func (o *orchestrator) finalizePendingFiber(f *Fiber) {
	applyErr := f.pendingApplyErr
	unwindErr := f.pendingUnwindErr
	f.finalizePending = false
	f.pendingApplyErr = nil
	f.pendingUnwindErr = nil
	o.finalizeActivation(f, applyErr, unwindErr)
}

// activationBecameActive is called after a Fiber becomes Active. Fibers that
// were waiting (Pending/Gone) on providers this activation may now satisfy are
// re-evaluated.
func (o *orchestrator) activationBecameActive(f *Fiber, act *activation) {
	o.sweepWaiting()
}

// activationEnded is called whenever a Fiber's current activation fully ended.
// It removes the activation's dependency edges and notifies fibers whose
// withdrawal gates included this fiber.
func (o *orchestrator) activationEnded(f *Fiber) {
	if act := f.activation; act != nil {
		for _, d := range act.deps {
			o.removeGraphEdge(d.Provider, f)
		}
	}
	o.notifyGateWaiters(f)
}

// ---------------------------------------------------------------------------
// Intent / state publishing
// ---------------------------------------------------------------------------

// disposeAll unmounts every registered Fiber (Runtime Close).
func (o *orchestrator) disposeAll() {
	o.rt.mu.RLock()
	fibers := make([]*Fiber, 0, len(o.rt.fibers))
	for _, f := range o.rt.fibers {
		fibers = append(fibers, f)
	}
	o.rt.mu.RUnlock()

	for _, f := range fibers {
		o.setIntent(f, IntentUnmounted)
		o.reconcile(f)
	}
}

// allFibersTerminal reports whether every registered Fiber has reached Gone.
func (o *orchestrator) allFibersTerminal() bool {
	o.rt.mu.RLock()
	defer o.rt.mu.RUnlock()
	for _, f := range o.rt.fibers {
		f.mu.RLock()
		st := f.state
		f.mu.RUnlock()
		if st != StateGone {
			return false
		}
	}
	return true
}

// markClosed publishes the RuntimeClosed state once the orchestrator drains.
func (o *orchestrator) markClosed() {
	o.rt.mu.Lock()
	o.rt.state = RuntimeClosed
	o.rt.mu.Unlock()
}

// setIntent records the desired intent. Intent mutation is idempotent.
func (o *orchestrator) setIntent(f *Fiber, intent Intent) {
	f.mu.Lock()
	if f.intent != intent {
		f.intent = intent
		f.signal.Broadcast()
	}
	f.mu.Unlock()
}

// transition publishes a new Fiber state plus its lifecycle error under the
// fiber lock, waking waiters. Only the orchestrator calls this.
func (o *orchestrator) transition(f *Fiber, st FiberState, err error) {
	f.mu.Lock()
	f.state = st
	f.err = err
	f.signal.Broadcast()
	f.mu.Unlock()
}

// desiredIntent returns the effective intent. While the Runtime is closing,
// every Fiber is treated as wanting to unmount.
func (o *orchestrator) desiredIntent(f *Fiber) Intent {
	if o.closing {
		return IntentUnmounted
	}
	o.rt.mu.RLock()
	closing := o.rt.state != RuntimeRunning
	o.rt.mu.RUnlock()
	if closing {
		return IntentUnmounted
	}
	f.mu.RLock()
	defer f.mu.RUnlock()
	return f.intent
}
