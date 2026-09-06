// Package config provides the Runtime Config Extension: reconciling a Desired
// Component tree toward the actual Runtime through the Kernel lifecycle API.
//
// Config Extension owns:
//   - desired / applied configuration state,
//   - validation,
//   - diff + minimal lifecycle operations,
//   - ownership of the Components it created.
//
// It does NOT own: file parsing, watch/HMR, Component loading, WASM, Event,
// Scheduler, or the Kernel Dependency/Provider semantics. Config ordering never
// implies dependency ordering; dependency resolution stays in the Kernel.
//
// Architecture:
//   - The Controller runs its operations on a single worker goroutine (like the
//     Kernel orchestrator), so Factory/Create code and Kernel waits never run
//     while holding a Controller state lock.
//   - Applied state records only Components that were successfully handed to
//     the Runtime (Load succeeded). It never claims a Component is Active.
//   - Validation of the whole Config happens before any Runtime mutation; an
//     invalid Config leaves the Runtime untouched.
package config

import (
	"context"
	"errors"
	"fmt"
	"reflect"
	"sync"

	"dynamic-runtime/runtime"
)

// Errors. Compare with errors.Is.
var (
	// ErrControllerClosed is returned by Reconcile after Close.
	ErrControllerClosed = errors.New("config controller closed")
	// ErrDuplicateComponentID is returned when a Config contains two
	// Components with the same ID.
	ErrDuplicateComponentID = errors.New("duplicate component id")
	// ErrUnknownComponentType is returned when a Config references a Type with
	// no registered Factory.
	ErrUnknownComponentType = errors.New("unknown component type")
	// ErrInvalidConfig is returned when a Config is structurally invalid
	// (empty/missing ID or Type, ...).
	ErrInvalidConfig = errors.New("invalid config")
	// ErrComponentCreatePanic is returned when a Factory.Create panics.
	ErrComponentCreatePanic = errors.New("component factory panic")
	// ErrFactoryExists is returned when registering a duplicate factory type.
	ErrFactoryExists = errors.New("factory already exists")
	// ErrFactoryNotFound is returned when looking up an unregistered type.
	ErrFactoryNotFound = errors.New("factory not found")
	// ErrInvalidFactory is returned when registering an empty type or nil
	// factory.
	ErrInvalidFactory = errors.New("invalid factory")
)

// ComponentConfig describes one desired Component.
type ComponentConfig struct {
	// ID is the stable logical identity of the Component within a Config. It
	// must be non-empty and unique.
	ID string
	// Type selects the Factory responsible for creating the Component. Type is
	// NOT a Kernel Capability Key.
	Type string
	// Config is the factory-specific configuration.
	Config map[string]any
}

// Config is a desired Component set.
type Config struct {
	Components []ComponentConfig
}

// Factory creates a Kernel Component from a ComponentConfig.
type Factory interface {
	Create(ComponentConfig) (runtime.Component, error)
}

// FactoryRegistry maps Type -> Factory. It is concurrency-safe and never runs
// user Factory code under its lock.
type FactoryRegistry interface {
	Register(typeName string, f Factory) error
	Lookup(typeName string) (Factory, bool)
}

// Runtime is the minimal Kernel surface the Controller needs. *runtime.Runtime
// satisfies it directly.
type Runtime interface {
	Load(component runtime.Component) (*runtime.Fiber, error)
}

// factoryRegistry is the default FactoryRegistry implementation.
type factoryRegistry struct {
	mu        sync.RWMutex
	factories map[string]Factory
}

// NewFactoryRegistry returns an empty FactoryRegistry.
func NewFactoryRegistry() FactoryRegistry {
	return &factoryRegistry{factories: make(map[string]Factory)}
}

// Register registers a Factory for typeName. Duplicate registration fails with
// ErrFactoryExists and never replaces the original.
func (r *factoryRegistry) Register(typeName string, f Factory) error {
	if typeName == "" || f == nil {
		return ErrInvalidFactory
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, exists := r.factories[typeName]; exists {
		return ErrFactoryExists
	}
	r.factories[typeName] = f
	return nil
}

// Lookup returns the Factory for typeName.
func (r *factoryRegistry) Lookup(typeName string) (Factory, bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	f, ok := r.factories[typeName]
	return f, ok
}

// OwnedComponent is a read-only snapshot of one Controller-owned Component.
type OwnedComponent struct {
	ID     string
	Type   string
	Config map[string]any
	Fiber  *runtime.Fiber
}

// componentSpec is the canonicalized, immutable-in-practice internal form of a
// ComponentConfig.
type componentSpec struct {
	id     string
	typ    string
	config map[string]any
}

func (s componentSpec) equal(o componentSpec) bool {
	return s.id == o.id && s.typ == o.typ && valuesEqual(s.config, o.config)
}

type ownedEntry struct {
	spec  componentSpec
	fiber *runtime.Fiber
}

// Controller reconciles a Runtime toward desired Configs and owns the
// Components it creates.
type Controller struct {
	rt        Runtime
	factories FactoryRegistry

	stateMu sync.RWMutex
	closed  bool
	applied map[string]*ownedEntry

	cleaning        map[*runtime.Fiber]struct{}
	cleanupStarted  bool
	cleaned         chan struct{}
	drainerLaunched bool

	done chan struct{}

	// sendMu guards the command queue and the stopped flag. append() never
	// blocks; shutdown sets stopped under sendMu and then drains the queue, so
	// a command can never be enqueued and then left unprocessed (which would
	// hang its caller).
	sendMu  sync.Mutex
	stopped bool
	queue   []controllerCmd
	wake    chan struct{}
}

// NewController creates a running Controller bound to the given Runtime facade
// and FactoryRegistry.
func NewController(rt Runtime, factories FactoryRegistry) *Controller {
	c := &Controller{
		rt:        rt,
		factories: factories,
		applied:   make(map[string]*ownedEntry),
		cleaning:  make(map[*runtime.Fiber]struct{}),
		cleaned:   make(chan struct{}),
		done:      make(chan struct{}),
		wake:      make(chan struct{}, 1),
	}
	go c.run()
	return c
}

// Reconcile drives the Runtime toward `desired`. See package docs and the
// specification for semantics (validation before mutation, minimal diff,
// accurate partial-failure Applied state, no automatic retry).
func (c *Controller) Reconcile(ctx context.Context, desired Config) error {
	if ctx == nil {
		ctx = context.Background()
	}
	res := make(chan error, 1)
	if !c.submit(&cmdReconcile{ctx: ctx, desired: desired, res: res}) {
		return ErrControllerClosed
	}
	select {
	case err := <-res:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// Owned returns a snapshot of currently applied (Controller-owned) Components.
// Read-only; iteration order is unspecified.
func (c *Controller) Owned() []OwnedComponent {
	c.stateMu.RLock()
	defer c.stateMu.RUnlock()
	out := make([]OwnedComponent, 0, len(c.applied))
	for _, e := range c.applied {
		out = append(out, OwnedComponent{
			ID:     e.spec.id,
			Type:   e.spec.typ,
			Config: copyConfig(e.spec.config),
			Fiber:  e.fiber,
		})
	}
	return out
}

// Closed reports whether Close has been called.
func (c *Controller) Closed() bool {
	c.stateMu.RLock()
	defer c.stateMu.RUnlock()
	return c.closed
}

// Close shuts the Controller down: it unloads every owned Component and waits
// for the cleanup to finish. Close is idempotent. Stubborn Components (whose
// cleanup never returns) make Close block; use CloseContext for a bounded wait.
func (c *Controller) Close() error {
	return c.CloseContext(context.Background())
}

// CloseContext shuts the Controller down with a bounded wait. After
// CloseContext has been called (even on timeout) Reconcile returns
// ErrControllerClosed and no new Runtime mutation starts; the cleanup completes
// on its own once the remaining Components return, and a later CloseContext
// finishes the wait.
func (c *Controller) CloseContext(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	select {
	case <-c.done:
		return nil // already fully closed and drained
	default:
	}
	res := make(chan error, 1)
	if !c.submit(&cmdClose{ctx: ctx, res: res}) {
		select {
		case <-c.done:
			return nil
		default:
			return ErrControllerClosed
		}
	}
	select {
	case err := <-res:
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// submit enqueues a command for the worker. Returns false after the worker has
// stopped. append() never blocks and shutdown drains the queue after setting
// stopped, so a successful submit is always processed (or rejected) by the
// worker.
func (c *Controller) submit(cmd controllerCmd) bool {
	c.sendMu.Lock()
	if c.stopped {
		c.sendMu.Unlock()
		return false
	}
	c.queue = append(c.queue, cmd)
	c.sendMu.Unlock()

	select {
	case c.wake <- struct{}{}:
	default:
	}
	return true
}

type controllerCmd interface {
	run(*Controller)
}

type cmdReconcile struct {
	ctx     context.Context
	desired Config
	res     chan error
}

func (cmd *cmdReconcile) run(c *Controller) {
	c.stateMu.RLock()
	closed := c.closed
	c.stateMu.RUnlock()
	if closed {
		cmd.res <- ErrControllerClosed
		return
	}
	if err := cmd.ctx.Err(); err != nil {
		cmd.res <- err
		return
	}
	cmd.res <- c.doReconcile(cmd.ctx, cmd.desired)
}

type cmdClose struct {
	ctx context.Context
	res chan error
}

func (cmd *cmdClose) run(c *Controller) {
	c.stateMu.Lock()
	c.closed = true
	if !c.cleanupStarted {
		c.cleanupStarted = true
		for id, e := range c.applied {
			c.cleaning[e.fiber] = struct{}{}
			delete(c.applied, id)
		}
	}
	pending := len(c.cleaning) > 0
	if pending && !c.drainerLaunched {
		c.drainerLaunched = true
		fibers := make([]*runtime.Fiber, 0, len(c.cleaning))
		for f := range c.cleaning {
			fibers = append(fibers, f)
		}
		go c.drain(fibers)
	}
	c.stateMu.Unlock()

	// Dispose every fiber that is not yet gone (idempotent), then wait for the
	// drainer (or the caller context).
	c.stateMu.RLock()
	cleaning := make([]*runtime.Fiber, 0, len(c.cleaning))
	for f := range c.cleaning {
		cleaning = append(cleaning, f)
	}
	c.stateMu.RUnlock()
	for _, f := range cleaning {
		_ = f.Dispose()
	}

	if len(cleaning) == 0 {
		// Nothing owned/pending: fully closed already.
		cmd.res <- nil
		return
	}
	select {
	case <-c.cleaned:
		cmd.res <- nil
	case <-cmd.ctx.Done():
		cmd.res <- cmd.ctx.Err()
	}
}

// drain waits (in the background) for every owned fiber to reach Gone.
func (c *Controller) drain(fibers []*runtime.Fiber) {
	for _, f := range fibers {
		_ = f.Gone(context.Background())
	}
	c.stateMu.Lock()
	for _, f := range fibers {
		delete(c.cleaning, f)
	}
	close(c.cleaned)
	c.stateMu.Unlock()
}

// run is the Controller worker goroutine. All Reconcile/Close operations run
// here, serially; user Factory code and Kernel waits never run under a state
// lock.
func (c *Controller) run() {
	defer close(c.done)
	for {
		c.stateMu.RLock()
		closed := c.closed
		pending := len(c.cleaning)
		c.stateMu.RUnlock()
		if closed && pending == 0 {
			c.finalize()
			return
		}

		c.sendMu.Lock()
		var cmd controllerCmd
		if len(c.queue) > 0 {
			cmd = c.queue[0]
			c.queue = c.queue[1:]
		}
		c.sendMu.Unlock()

		if cmd != nil {
			cmd.run(c) // never under sendMu
			continue
		}
		select {
		case <-c.wake:
		case <-c.cleaned:
		}
	}
}

// finalize stops accepting new commands and drains the remaining queue (each
// command observes the closed controller and replies without mutating the
// Runtime). Runs are executed outside sendMu; no new commands can be appended
// because stopped is already set.
func (c *Controller) finalize() {
	c.sendMu.Lock()
	c.stopped = true
	for len(c.queue) > 0 {
		cmd := c.queue[0]
		c.queue = c.queue[1:]
		c.sendMu.Unlock()
		cmd.run(c)
		c.sendMu.Lock()
	}
	c.sendMu.Unlock()
}

// ---------------------------------------------------------------------------
// Reconcile implementation (runs on the worker goroutine)
// ---------------------------------------------------------------------------

func (c *Controller) doReconcile(ctx context.Context, desired Config) error {
	specs, err := validateAndCanonicalize(desired, c.factories)
	if err != nil {
		return err
	}
	desiredIndex := make(map[string]componentSpec, len(specs))
	for _, s := range specs {
		desiredIndex[s.id] = s
	}

	c.stateMu.RLock()
	appliedSnapshot := make(map[string]*ownedEntry, len(c.applied))
	for id, e := range c.applied {
		appliedSnapshot[id] = e
	}
	c.stateMu.RUnlock()

	var removed []string
	var replacedOld []*ownedEntry
	var added []componentSpec
	for id, spec := range desiredIndex {
		cur, ok := appliedSnapshot[id]
		if !ok {
			added = append(added, spec)
			continue
		}
		if cur.spec.equal(spec) {
			continue // idempotent no-op
		}
		replacedOld = append(replacedOld, cur)
		added = append(added, spec)
	}
	for id := range appliedSnapshot {
		if _, keep := desiredIndex[id]; !keep {
			removed = append(removed, id)
		}
	}

	var errs []error

	// 1. Removals and replacements: unload the old Component first (Kernel
	//    withdraw-then-load semantics), removing from Applied only once Gone.
	for _, id := range removed {
		if err := c.removeOne(ctx, id); err != nil {
			errs = append(errs, err)
		}
	}
	for _, old := range replacedOld {
		if err := c.removeOne(ctx, old.spec.id); err != nil {
			errs = append(errs, err)
			continue // keep old applied; do not overlay the new one
		}
	}

	// 2. Additions (and replacements' new side).
	for _, spec := range added {
		if err := c.addOne(ctx, spec); err != nil {
			errs = append(errs, err)
		}
	}

	return errors.Join(errs...)
}

func (c *Controller) removeOne(ctx context.Context, id string) error {
	c.stateMu.RLock()
	e := c.applied[id]
	c.stateMu.RUnlock()
	if e == nil {
		return nil
	}
	if err := e.fiber.Dispose(); err != nil {
		return err
	}
	if err := e.fiber.Gone(ctx); err != nil {
		// Not confirmed gone: keep it in Applied (no partial-state lie).
		return err
	}
	c.stateMu.Lock()
	if cur := c.applied[id]; cur == e {
		delete(c.applied, id)
	}
	c.stateMu.Unlock()
	return nil
}

func (c *Controller) addOne(ctx context.Context, spec componentSpec) error {
	factory, ok := c.factories.Lookup(spec.typ)
	if !ok {
		return fmt.Errorf("%w: %q", ErrUnknownComponentType, spec.typ)
	}

	comp, err := createSafely(factory, componentConfigFromSpec(spec))
	if err != nil {
		return err
	}
	if comp == nil {
		return fmt.Errorf("%w: factory %q returned a nil component", ErrInvalidConfig, spec.typ)
	}

	fiber, err := c.rt.Load(comp)
	if err != nil {
		// Load failed: the Component was not handed to the Runtime; nothing to
		// clean up at the fiber level.
		return err
	}

	c.stateMu.Lock()
	if c.closed {
		c.stateMu.Unlock()
		// Controller closed concurrently with this add: release the fiber.
		_ = fiber.Dispose()
		_ = fiber.Gone(context.Background())
		return ErrControllerClosed
	}
	c.applied[spec.id] = &ownedEntry{spec: spec, fiber: fiber}
	c.stateMu.Unlock()
	return nil
}

// createSafely calls Factory.Create and converts a panic into
// ErrComponentCreatePanic.
func createSafely(factory Factory, cfg ComponentConfig) (comp runtime.Component, err error) {
	defer func() {
		if r := recover(); r != nil {
			comp = nil
			err = fmt.Errorf("%w: %v", ErrComponentCreatePanic, r)
		}
	}()
	return factory.Create(cfg)
}

// ---------------------------------------------------------------------------
// Validation and canonicalization
// ---------------------------------------------------------------------------

func validateAndCanonicalize(desired Config, factories FactoryRegistry) ([]componentSpec, error) {
	seen := make(map[string]struct{}, len(desired.Components))
	out := make([]componentSpec, 0, len(desired.Components))
	for _, cc := range desired.Components {
		if cc.ID == "" {
			return nil, fmt.Errorf("%w: empty component id", ErrInvalidConfig)
		}
		if cc.Type == "" {
			return nil, fmt.Errorf("%w: component %q has empty type", ErrInvalidConfig, cc.ID)
		}
		if _, dup := seen[cc.ID]; dup {
			return nil, fmt.Errorf("%w: %q", ErrDuplicateComponentID, cc.ID)
		}
		seen[cc.ID] = struct{}{}

		if _, ok := factories.Lookup(cc.Type); !ok {
			return nil, fmt.Errorf("%w: %q", ErrUnknownComponentType, cc.Type)
		}
		out = append(out, componentSpec{
			id:     cc.ID,
			typ:    cc.Type,
			config: copyConfig(cc.Config),
		})
	}
	return out, nil
}

func componentConfigFromSpec(s componentSpec) ComponentConfig {
	return ComponentConfig{ID: s.id, Type: s.typ, Config: copyConfig(s.config)}
}

// copyConfig returns a defensive deep copy in canonical form (string-keyed maps
// become map[string]any, slices become []any).
func copyConfig(in map[string]any) map[string]any {
	if in == nil {
		return map[string]any{}
	}
	out := make(map[string]any, len(in))
	for k, v := range in {
		out[k] = deepCopyValue(v)
	}
	return out
}

func deepCopyValue(v any) any {
	rv := reflect.ValueOf(v)
	switch rv.Kind() {
	case reflect.Map:
		if rv.Type().Key().Kind() == reflect.String {
			out := make(map[string]any, rv.Len())
			iter := rv.MapRange()
			for iter.Next() {
				out[iter.Key().String()] = deepCopyValue(iter.Value().Interface())
			}
			return out
		}
	case reflect.Slice:
		out := make([]any, rv.Len())
		for i := 0; i < rv.Len(); i++ {
			out[i] = deepCopyValue(rv.Index(i).Interface())
		}
		return out
	}
	return v // scalars; exotic references are copied by reference
}

// valuesEqual compares two canonicalized config values deterministically.
func valuesEqual(a, b any) bool {
	switch av := a.(type) {
	case map[string]any:
		bv, ok := b.(map[string]any)
		if !ok || len(av) != len(bv) {
			return false
		}
		for k, v := range av {
			bvv, ok := bv[k]
			if !ok || !valuesEqual(v, bvv) {
				return false
			}
		}
		return true
	case []any:
		bv, ok := b.([]any)
		if !ok || len(av) != len(bv) {
			return false
		}
		for i := range av {
			if !valuesEqual(av[i], bv[i]) {
				return false
			}
		}
		return true
	default:
		return a == b
	}
}
