package hmr

import (
	"context"
	"errors"
	"fmt"
	"sync"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

const hmrOwner = "hmr"

// Controller is the concrete HMR implementation. It satisfies the HMR and
// TargetRegistry interfaces.
type Controller struct {
	rt     *runtime.Runtime
	loader ModuleLoader
	usage  loader.ModuleUsage

	stateMu sync.RWMutex
	closed  bool
	targets map[string]Target
	binding map[string]*Binding

	done chan struct{}
	wake chan struct{}

	sendMu  sync.Mutex
	stopped bool
	queue   []hmrCmd
}

// New creates a running HMR Controller bound to a Runtime, a ModuleLoader, and
// the Loader's ModuleUsage tracker.
func New(rt *runtime.Runtime, ml ModuleLoader, usage loader.ModuleUsage) *Controller {
	h := &Controller{
		rt:      rt,
		loader:  ml,
		usage:   usage,
		targets: make(map[string]Target),
		binding: make(map[string]*Binding),
		done:    make(chan struct{}),
		wake:    make(chan struct{}, 1),
	}
	go h.run()
	return h
}

// Register registers a Target mapping (no implementation is bound yet).
func (h *Controller) Register(target Target) error {
	return h.submitResp(&cmdRegister{target: target})
}

// Unregister removes a Target and its binding, releasing HMR-owned usage.
func (h *Controller) Unregister(id string) error {
	return h.submitResp(&cmdUnregister{id: id})
}

// Bind establishes the CURRENT implementation for a registered target. `mod`
// is the Loader Module the running fiber was built from. Bind acquires HMR
// usage on that module (released on replacement or close).
func (h *Controller) Bind(targetID string, mod loader.Module, fiber *runtime.Fiber) error {
	return h.submitResp(&cmdBind{targetID: targetID, mod: mod, fiber: fiber})
}

// Replace performs a warm replacement of the registered target's implementation
// with the implementation described by target.Artifact. It returns nil only
// when the new Fiber reached Active.
func (h *Controller) Replace(ctx context.Context, target Target) error {
	if ctx == nil {
		ctx = context.Background()
	}
	res := make(chan error, 1)
	if !h.submit(&cmdReplace{ctx: ctx, target: target, res: res}) {
		return ErrHMRClosed
	}
	select {
	case err := <-res:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// Close shuts the Controller down: it rejects new operations, waits for
// in-flight replacements, releases every HMR-owned Loader usage, and becomes
// Closed.
func (h *Controller) Close() error {
	return h.CloseContext(context.Background())
}

// CloseContext is Close with a bounded wait. On timeout it returns ctx.Err()
// while the Controller remains in its closing state.
func (h *Controller) CloseContext(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	select {
	case <-h.done:
		return nil
	default:
	}
	res := make(chan error, 1)
	if !h.submit(&cmdClose{res: res}) {
		select {
		case <-h.done:
			return nil
		default:
			return ErrHMRClosed
		}
	}
	select {
	case err := <-res:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// Snapshot returns a snapshot of registered targets.
func (h *Controller) Snapshot() []Target {
	h.stateMu.RLock()
	defer h.stateMu.RUnlock()
	out := make([]Target, 0, len(h.targets))
	for _, t := range h.targets {
		out = append(out, t)
	}
	return out
}

// Get returns the registered target for id.
func (h *Controller) Get(id string) (Target, bool) {
	h.stateMu.RLock()
	defer h.stateMu.RUnlock()
	t, ok := h.targets[id]
	return t, ok
}

// Has reports whether id is registered.
func (h *Controller) Has(id string) bool {
	h.stateMu.RLock()
	defer h.stateMu.RUnlock()
	_, ok := h.targets[id]
	return ok
}

// CurrentBinding returns the implementation currently bound to target id.
func (h *Controller) CurrentBinding(id string) (Binding, bool) {
	h.stateMu.RLock()
	defer h.stateMu.RUnlock()
	b, ok := h.binding[id]
	if !ok {
		return Binding{}, false
	}
	return *b, true
}

var (
	_ HMR            = (*Controller)(nil)
	_ TargetRegistry = (*Controller)(nil)
)

// ---------------------------------------------------------------------------
// Worker + commands
// ---------------------------------------------------------------------------

type hmrCmd interface{ run(h *Controller) }

type cmdRegister struct {
	target Target
	res    chan error
}
type cmdUnregister struct {
	id  string
	res chan error
}
type cmdBind struct {
	targetID string
	mod      loader.Module
	fiber    *runtime.Fiber
	res      chan error
}
type cmdReplace struct {
	ctx    context.Context
	target Target
	res    chan error
}
type cmdClose struct {
	res chan error
}

func (h *Controller) submit(cmd hmrCmd) bool {
	h.sendMu.Lock()
	if h.stopped {
		h.sendMu.Unlock()
		return false
	}
	h.queue = append(h.queue, cmd)
	h.sendMu.Unlock()
	select {
	case h.wake <- struct{}{}:
	default:
	}
	return true
}

func (h *Controller) submitResp(cmd hmrCmd) error {
	var res chan error
	switch v := cmd.(type) {
	case *cmdRegister:
		v.res = make(chan error, 1)
		res = v.res
	case *cmdUnregister:
		v.res = make(chan error, 1)
		res = v.res
	case *cmdBind:
		v.res = make(chan error, 1)
		res = v.res
	default:
		return ErrHMRUnavailable
	}
	if !h.submit(cmd) {
		return ErrHMRClosed
	}
	return <-res
}

func (h *Controller) run() {
	defer close(h.done)
	for {
		h.stateMu.RLock()
		closed := h.closed
		h.stateMu.RUnlock()
		if closed {
			h.finalize()
			return
		}
		h.sendMu.Lock()
		var cmd hmrCmd
		if len(h.queue) > 0 {
			cmd = h.queue[0]
			h.queue = h.queue[1:]
		}
		h.sendMu.Unlock()
		if cmd != nil {
			cmd.run(h)
			continue
		}
		<-h.wake
	}
}

func (h *Controller) finalize() {
	h.sendMu.Lock()
	h.stopped = true
	for len(h.queue) > 0 {
		cmd := h.queue[0]
		h.queue = h.queue[1:]
		h.sendMu.Unlock()
		cmd.run(h)
		h.sendMu.Lock()
	}
	h.sendMu.Unlock()
}

func (c *cmdRegister) run(h *Controller)   { c.res <- h.doRegister(c.target) }
func (c *cmdUnregister) run(h *Controller) { c.res <- h.doUnregister(c.id) }
func (c *cmdBind) run(h *Controller)       { c.res <- h.doBind(c.targetID, c.mod, c.fiber) }
func (c *cmdReplace) run(h *Controller)    { c.res <- h.doReplace(c.ctx, c.target) }

func (c *cmdClose) run(h *Controller) {
	h.stateMu.Lock()
	if !h.closed {
		h.closed = true
	}
	// Release every HMR-owned Loader usage and clear HMR state (Fibers are not
	// owned by HMR and are left to the Runtime/application).
	for id, b := range h.binding {
		_ = h.usage.Release(b.Module.ID, hmrOwner)
		delete(h.binding, id)
	}
	h.targets = make(map[string]Target)
	h.stateMu.Unlock()
	c.res <- nil
}

func validateTarget(t Target) error {
	if t.ID == "" {
		return errWrap(ErrInvalidTarget, "empty target id")
	}
	if t.ComponentID == "" {
		return errWrap(ErrInvalidTarget, "target %q has empty component id", t.ID)
	}
	// The Artifact describes the desired implementation; its backend and
	// logical type are validated by the Loader during Load (HMR never resolves
	// Type mapping itself).
	if t.Artifact.ID == "" || t.Artifact.Source == "" {
		return errWrap(ErrInvalidTarget, "target %q has invalid artifact", t.ID)
	}
	return nil
}

func (h *Controller) doRegister(target Target) error {
	if err := validateTarget(target); err != nil {
		return err
	}
	h.stateMu.Lock()
	defer h.stateMu.Unlock()
	if h.closed {
		return ErrHMRClosed
	}
	if _, exists := h.targets[target.ID]; exists {
		return ErrTargetExists
	}
	h.targets[target.ID] = target
	return nil
}

func (h *Controller) doUnregister(id string) error {
	h.stateMu.Lock()
	defer h.stateMu.Unlock()
	if h.closed {
		return ErrHMRClosed
	}
	if _, ok := h.targets[id]; !ok {
		return ErrTargetNotFound
	}
	delete(h.targets, id)
	if b, ok := h.binding[id]; ok {
		_ = h.usage.Release(b.Module.ID, hmrOwner)
		delete(h.binding, id)
	}
	return nil
}

func (h *Controller) doBind(targetID string, mod loader.Module, fiber *runtime.Fiber) error {
	h.stateMu.Lock()
	defer h.stateMu.Unlock()
	if h.closed {
		return ErrHMRClosed
	}
	if _, ok := h.targets[targetID]; !ok {
		return ErrTargetNotFound
	}
	if mod.ID == "" || mod.Type == "" || mod.Factory == nil || fiber == nil {
		return ErrInvalidTarget
	}
	if err := h.usage.Acquire(mod.ID, hmrOwner); err != nil && !errors.Is(err, loader.ErrModuleUseExists) {
		return err
	}
	h.binding[targetID] = &Binding{
		TargetID:    targetID,
		ComponentID: h.targets[targetID].ComponentID,
		Module:      mod.Identity(),
		ModuleType:  mod.Type,
		Fiber:       fiber,
	}
	return nil
}

// ---------------------------------------------------------------------------
// Replace algorithm (runs on the worker; user code and kernel waits never run
// under a state lock)
// ---------------------------------------------------------------------------

func (h *Controller) doReplace(ctx context.Context, target Target) error {
	h.stateMu.RLock()
	closed := h.closed
	reg, ok := h.targets[target.ID]
	bound, boundOK := h.binding[target.ID]
	h.stateMu.RUnlock()

	if closed {
		return ErrHMRClosed
	}
	if !ok {
		return ErrTargetNotFound
	}
	if !boundOK {
		return ErrNotBound
	}
	if err := validateTarget(target); err != nil {
		return err
	}
	if err := ctx.Err(); err != nil {
		return ctx.Err()
	}

	// 1. Load the new Module BEFORE touching the old implementation.
	newMod, err := h.loader.Load(ctx, target.Artifact)
	if err != nil {
		return err // old remains unchanged (H5)
	}

	// 2. Validate the new Module and check compatibility (H6 / H14-failure).
	if newMod.ID == "" || newMod.Type == "" || newMod.Factory == nil {
		_ = h.loader.Unload(context.Background(), newMod.ID)
		return errWrap(ErrReplacementFailed, "loader returned an invalid module")
	}
	if bound.ModuleType != "" && newMod.Type != bound.ModuleType {
		_ = h.loader.Unload(context.Background(), newMod.ID)
		return errWrap(ErrIncompatibleModule, "new module type %q != old type %q", newMod.Type, bound.ModuleType)
	}

	// 3. Acquire explicit usage on the new Module.
	if err := h.usage.Acquire(newMod.ID, hmrOwner); err != nil && !errors.Is(err, loader.ErrModuleUseExists) {
		_ = h.loader.Unload(context.Background(), newMod.ID)
		return err
	}
	releaseNew := func(fiber *runtime.Fiber) {
		if fiber != nil {
			_ = fiber.Dispose()
			_ = fiber.Gone(context.Background())
		}
		_ = h.usage.Release(newMod.ID, hmrOwner)
		_ = h.loader.Unload(context.Background(), newMod.ID)
	}

	// 4. Build the new Component from the new Module's Factory (H7).
	comp, err := createComponent(newMod, config.ComponentConfig{ID: reg.ComponentID, Type: newMod.Type})
	if err != nil {
		releaseNew(nil)
		return err
	}

	// 5. Load the new Fiber (H8).
	newFiber, err := h.rt.Load(comp)
	if err != nil {
		releaseNew(nil)
		return err
	}

	// 6. Wait for the new Fiber to reach Active (warm attempt).
	warmErr := newFiber.Ready(ctx)

	if warmErr == nil {
		// Context gate: cancellation before the destructive phase keeps Old
		// Active (H17). After this point the replacement commits.
		if err := ctx.Err(); err != nil {
			releaseNew(newFiber)
			return err
		}
		if err := h.commitReplacement(bound, newFiber, newMod); err != nil {
			releaseNew(newFiber)
			return err
		}
		return nil
	}

	// New Fiber did not become Active.
	duplicateProvider := newFiber.State() == runtime.StateFailed &&
		errors.Is(newFiber.Err(), runtime.ErrDuplicateProvider)

	if duplicateProvider {
		// The Kernel rejected exclusive-provider overlap: use the
		// Kernel-supported withdraw-then-load path (spec §68). This is the only
		// sanctioned route when warm overlap is impossible.
		if err := ctx.Err(); err != nil {
			releaseNew(newFiber)
			return err
		}
		if err := h.unloadOld(bound); err != nil {
			releaseNew(newFiber)
			return err
		}
		_ = newFiber.Dispose()
		_ = newFiber.Gone(context.Background())

		comp2, err2 := createComponent(newMod, config.ComponentConfig{ID: reg.ComponentID, Type: newMod.Type})
		if err2 != nil {
			releaseNew(nil)
			return errWrap(ErrReplacementFailed, "create after withdraw: %v", err2)
		}
		newFiber2, err2 := h.rt.Load(comp2)
		if err2 != nil {
			releaseNew(nil)
			return errWrap(ErrReplacementFailed, "load after withdraw: %v", err2)
		}
		// Destructive phase already started: wait with background.
		if err2 := newFiber2.Ready(context.Background()); err2 != nil {
			releaseNew(newFiber2)
			return errWrap(ErrReplacementFailed, "new fiber failed after old gone: %v", err2)
		}
		if err2 := h.bindNew(bound.TargetID, newFiber2, newMod); err2 != nil {
			releaseNew(newFiber2)
			return err2
		}
		return nil
	}

	// Any other failure (Pending on dependencies, Apply failure, ...): Old
	// remains Active and the new resources are cleaned up (H8/H14).
	releaseNew(newFiber)
	return errWrap(ErrReplacementFailed, "new fiber did not reach Active: %v", warmErr)
}

// commitReplacement performs the warm destructive phase: Old unload only AFTER
// New is Active, then Old Module usage release.
func (h *Controller) commitReplacement(bound *Binding, newFiber *runtime.Fiber, newMod *loader.Module) error {
	if err := bound.Fiber.Dispose(); err != nil {
		return err
	}
	if err := bound.Fiber.Gone(context.Background()); err != nil {
		return err
	}
	// Old Fiber Gone: only now release Old Module usage (H13).
	_ = h.usage.Release(bound.Module.ID, hmrOwner)
	_ = h.loader.Unload(context.Background(), bound.Module.ID)
	return h.bindNew(bound.TargetID, newFiber, newMod)
}

// unloadOld disposes the old fiber and waits for Gone (sequential fallback).
func (h *Controller) unloadOld(bound *Binding) error {
	if err := bound.Fiber.Dispose(); err != nil {
		return err
	}
	if err := bound.Fiber.Gone(context.Background()); err != nil {
		return err
	}
	_ = h.usage.Release(bound.Module.ID, hmrOwner)
	_ = h.loader.Unload(context.Background(), bound.Module.ID)
	return nil
}

func (h *Controller) bindNew(targetID string, fiber *runtime.Fiber, mod *loader.Module) error {
	h.stateMu.Lock()
	defer h.stateMu.Unlock()
	if h.closed {
		_ = h.usage.Release(mod.ID, hmrOwner)
		return ErrHMRClosed
	}
	reg, ok := h.targets[targetID]
	if !ok {
		_ = h.usage.Release(mod.ID, hmrOwner)
		return ErrTargetNotFound
	}
	h.binding[targetID] = &Binding{
		TargetID:    targetID,
		ComponentID: reg.ComponentID,
		Module:      mod.Identity(),
		ModuleType:  mod.Type,
		Fiber:       fiber,
	}
	return nil
}

// createComponent calls a Module Factory and converts a panic into an error.
func createComponent(mod *loader.Module, cc config.ComponentConfig) (comp runtime.Component, err error) {
	defer func() {
		if r := recover(); r != nil {
			comp = nil
			err = fmt.Errorf("%w: factory panic: %v", ErrReplacementFailed, r)
		}
	}()
	return mod.Factory.Create(cc)
}
