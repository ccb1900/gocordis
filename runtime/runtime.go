package runtime

import (
	"context"
	"sync"
	"sync/atomic"
)

// Runtime is the Dynamic Composable Runtime Kernel entry point.
//
// All lifecycle decisions are serialized by an internal orchestrator; public
// methods are commands that linearize against one another.
type Runtime struct {
	mu sync.RWMutex

	state RuntimeState

	fibers map[FiberID]*Fiber

	providers *providerRegistry

	orch *orchestrator

	nextFiberID      atomic.Uint64
	nextActivationID atomic.Uint64
}

// Option configures a Runtime. v0.1 defines no options; the type exists to
// keep the constructor stable for extensions and test hooks.
type Option func(*options)

type options struct{}

// New creates and starts a Runtime.
func New(opts ...Option) (*Runtime, error) {
	r := &Runtime{
		state:     RuntimeRunning,
		fibers:    make(map[FiberID]*Fiber),
		providers: newProviderRegistry(),
	}
	for _, o := range opts {
		if o != nil {
			o(&options{})
		}
	}
	r.orch = newOrchestrator(r)
	go r.orch.run()
	return r, nil
}

// Load instantiates a root Fiber for component with Intent = Mounted and
// returns immediately. Load does not wait for the Fiber to become Active; use
// Fiber.Ready to wait.
//
// Load fails once the Runtime is Closing or Closed.
func (r *Runtime) Load(component Component) (*Fiber, error) {
	if component == nil {
		return nil, ErrInvalidState
	}
	f := newFiber(r, component)
	// Cache declarative metadata on the caller's goroutine so the orchestrator
	// never runs Component code while making lifecycle decisions.
	f.inject = component.Inject()
	f.provide = component.Provide()

	r.mu.Lock()
	if r.state != RuntimeRunning {
		r.mu.Unlock()
		return nil, ErrRuntimeClosed
	}
	f.id = FiberID(r.nextFiberID.Add(1))
	r.fibers[f.id] = f
	r.mu.Unlock()

	if !r.submit(&cmdLoad{fiber: f}) {
		r.mu.Lock()
		delete(r.fibers, f.id)
		r.mu.Unlock()
		return nil, ErrRuntimeClosed
	}
	return f, nil
}

// Close shuts the Runtime down.
//
// Close transitions the Runtime Running -> Closing -> Closed:
//   - rejects new Loads (and cancels in-flight Applies),
//   - unloads every reachable Fiber (dependency-aware, ownership-aware),
//   - waits until every Fiber reaches a terminal state and the orchestrator
//     has drained,
//   - only then marks the Runtime Closed and returns nil.
//
// Close is cooperative: if a Component blocks forever in Apply/Cleanup, Close
// cannot force termination. In that case Close returns ctx.Err() once ctx is
// done; the Runtime remains Closing (NOT Closed) and finishes the shutdown on
// its own once the blocked Component eventually returns.
func (r *Runtime) Close(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}

	r.mu.Lock()
	switch r.state {
	case RuntimeClosed:
		r.mu.Unlock()
		return nil
	case RuntimeClosing:
		// Another Close is already draining; wait for it.
		r.mu.Unlock()
	case RuntimeRunning:
		r.state = RuntimeClosing
		r.mu.Unlock()
		if !r.submit(&cmdClose{}) {
			// The orchestrator already stopped (runtime became Closed between
			// the state check and the submit).
		}
	}

	select {
	case <-r.orch.done:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

// submit enqueues a command for the orchestrator. It returns false when the
// orchestrator has already stopped, in which case the command is dropped.
//
// The command channel is intentionally never closed; shutdown is signaled via
// the stop channel so late asynchronous completions can be safely dropped
// instead of panicking on a send to a closed channel.
func (r *Runtime) submit(cmd command) bool {
	o := r.orch

	// Fast path: while holding sendMu, stop cannot be closed concurrently, so
	// a successful non-blocking send is authoritative.
	o.sendMu.Lock()
	select {
	case <-o.stop:
		o.sendMu.Unlock()
		return false
	default:
	}
	select {
	case o.commands <- cmd:
		o.sendMu.Unlock()
		return true
	default:
		o.sendMu.Unlock()
	}

	// The command buffer is full: block until either a slot frees (orchestrator
	// still draining) or the orchestrator stops. stop is closed under sendMu on
	// shutdown, so this select cannot deadlock.
	select {
	case o.commands <- cmd:
		return true
	case <-o.stop:
		return false
	}
}

// stateSnapshot returns the public runtime state.
func (r *Runtime) stateSnapshot() RuntimeState {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return r.state
}
