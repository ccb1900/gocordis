// Package timer provides JS-style timers as a Runtime-managed capability.
//
// The service owns every consequence it creates: each timer entry is tracked,
// stopping a timer is reversible, and disposal stops all entries and JOINS the
// callback goroutines before the activation unwinds. No goroutine or callback
// survives the fiber. All user callbacks run outside the service lock.
//
// The package-level *Owned functions bind an entry to the CALLING fiber (the
// JS semantics of docs/r1.md P1-02): they install an effect whose inverse
// cancels the entry and joins its admitted callbacks, so nothing outlives the
// consumer. Callers never have to remember a separate Owned step.
package timer

import (
	"context"
	"errors"
	"iter"
	"sync"
	"sync/atomic"
	"time"

	"dynamic-runtime/runtime"
)

// Handle controls one timer entry.
//
// Stop cancels the timer and NEVER waits for callbacks. It is the ONLY method
// on the public handle, so it is safe to call from anywhere - including inside
// the timer's own callback, where a joining method would self-join and deadlock.
// Stop reports whether this call cancelled a still-live timer.
//
// Joining admitted callbacks is not part of this interface. It is an internal
// primitive reached only by effect inverses and Service.Close, never by user
// code (docs/r1.md P1-02 requires the interface itself to prevent self-wait).
type Handle interface {
	Stop() bool
}

// joiner is the narrow, unexported joining primitive. Keeping it off Handle is
// what makes self-wait structurally impossible through the capability API.
type joiner interface {
	stopAndWait() error
}

// Service is the timer capability consumed by components.
//
// Entries created by the plain methods belong to the Service and live until it
// is disposed. A consumer whose own fiber may be disposed first should use the
// package-level *Owned functions, which bind the entry to the calling fiber.
type Service interface {
	Timeout(fn func(), d time.Duration) Handle
	Interval(fn func(), d time.Duration) Handle
	Debounce(fn func(), d time.Duration) func()
	Throttle(fn func(), d time.Duration) func()
	ThrottleNoTrailing(fn func(), d time.Duration) func()
	DebounceHandle(fn func(), d time.Duration) (trigger func(), stop func() error)
	ThrottleHandle(fn func(), d time.Duration) (trigger func(), stop func() error)
	// DebounceWithHandle/ThrottleWithHandle are the interface-level answer to
	// the debounce/throttle self-wait problem (docs/r1.md P1-02): the returned
	// Handle exposes only the non-waiting Stop, so a callback may safely stop
	// its own entry; joining is reserved for internal effect inverses.
	DebounceWithHandle(fn func(), d time.Duration) (trigger func(), h Handle)
	ThrottleWithHandle(fn func(), d time.Duration) (trigger func(), h Handle)
}

// Owned installs a timer handle's stop function as an effect of the consumer's
// activation. When that activation unwinds, the handle is cancelled and its
// in-flight callbacks are joined before the consumer reaches Gone. An refused
// effect is compensated immediately so no timer outlives its consumer.
//
// Prefer the package-level TimeoutOwned/IntervalOwned/DebounceOwned/
// ThrottleOwned/ThrottleNoTrailingOwned functions: they perform this binding
// at creation time so callers do not have to remember it.
func Owned(c *runtime.Context, trigger func(), stop func() error) error {
	if c == nil {
		return errors.New("timer: nil context")
	}
	if stop == nil {
		return errors.New("timer: nil stop function")
	}
	err := c.Effect(func() (func() error, error) {
		return stop, nil
	})
	if err != nil {
		_ = stop()
	}
	return err
}

// requireService resolves the capability through the caller's declared Inject
// set. The consumer MUST declare runtime.Requires(ServiceKey) (declaration
// authority, AGENTS.md §1); if it does not, an inert handle/trigger is returned
// rather than letting an undeclared requirement leak a timer.
func requireService(c *runtime.Context) (*service, error) {
	if c == nil {
		return nil, errors.New("timer: nil context")
	}
	svc, err := runtime.Require(c, ServiceKey)
	if err != nil {
		return nil, err
	}
	s, ok := svc.(*service)
	if !ok {
		return nil, errors.New("timer: unexpected timer service implementation")
	}
	return s, nil
}

// installOwnedEffect registers h's joining inverse. runtime.Context.Effect
// runs the inverse immediately if the context is already unwinding, so a timer
// created in the final moments of an activation cannot leak.
func installOwnedEffect(c *runtime.Context, j joiner) {
	err := c.Effect(func() (func() error, error) {
		return j.stopAndWait, nil
	})
	if err != nil {
		_ = j.stopAndWait()
	}
}

// TimeoutOwned creates a one-shot timer owned by the calling fiber. The
// entry's joining inverse is already installed on the fiber.
func TimeoutOwned(c *runtime.Context, fn func(), d time.Duration) Handle {
	s, err := requireService(c)
	if err != nil {
		return noopHandle{}
	}
	h := s.Timeout(fn, d).(*handle)
	installOwnedEffect(c, h)
	return h
}

// IntervalOwned creates a repeating timer owned by the calling fiber.
func IntervalOwned(c *runtime.Context, fn func(), d time.Duration) Handle {
	s, err := requireService(c)
	if err != nil {
		return noopHandle{}
	}
	h := s.Interval(fn, d).(*handle)
	installOwnedEffect(c, h)
	return h
}

// DebounceOwned creates a debounced trigger owned by the calling fiber. The
// inverse cancels pending fires and joins callbacks admitted before disposal.
func DebounceOwned(c *runtime.Context, fn func(), d time.Duration) func() {
	s, err := requireService(c)
	if err != nil {
		return func() {}
	}
	trigger, h := s.debounceWithHandle(fn, d)
	installOwnedEffect(c, h)
	return trigger
}

// ThrottleOwned creates a leading/trailing throttle owned by the calling fiber.
func ThrottleOwned(c *runtime.Context, fn func(), d time.Duration) func() {
	return throttleOwned(c, fn, d, true)
}

// ThrottleNoTrailingOwned creates a leading-only throttle owned by the calling
// fiber; calls inside the window are dropped.
func ThrottleNoTrailingOwned(c *runtime.Context, fn func(), d time.Duration) func() {
	return throttleOwned(c, fn, d, false)
}

func throttleOwned(c *runtime.Context, fn func(), d time.Duration, trailing bool) func() {
	s, err := requireService(c)
	if err != nil {
		return func() {}
	}
	trigger, h := s.throttleWithHandle(fn, d, trailing)
	installOwnedEffect(c, h)
	return trigger
}

// ---- async forms (JS @cordisjs/timer timeout(delay)/interval(delay)/Scheduled<F>) ----
//
// These helpers are the Go-native counterpart of the JS APIs that take no
// user callback. They still go through the service's joiner-backed handle: the
// entry is installed as an effect of the calling fiber, so a consumer reaching
// Gone waits for admitted callbacks and no callback can fire afterwards.

// TimeoutWait is the Go form of `await ctx.timeout(delay)`. It resolves when
// the timer fires and returns the owner's cancellation error if the consumer is
// disposed/withdrawn first. The signaling callback is fully tracked: nothing
// is left running when the caller returns.
func TimeoutWait(c *runtime.Context, d time.Duration) error {
	s, err := requireService(c)
	if err != nil {
		return err
	}
	fired := make(chan struct{})
	h := s.Timeout(func() { close(fired) }, d).(*handle)
	installOwnedEffect(c, h)
	select {
	case <-fired:
		return nil
	case <-c.Done():
		// A fire that already committed before disposal resolves the wait:
		// the "promise" won the race, so do not report a spurious error.
		select {
		case <-fired:
			return nil
		default:
		}
		return c.Err()
	}
}

// Ticks is the Go form of `ctx.interval(delay)` as an async iterable. Ranging
// over the sequence consumes ticks; breaking out (or the owner's disposal)
// stops the interval and joins the dispatcher and any admitted callback before
// the range statement completes.
func Ticks(c *runtime.Context, d time.Duration) iter.Seq[time.Time] {
	return func(yield func(time.Time) bool) {
		s, err := requireService(c)
		if err != nil {
			return
		}
		ticks := make(chan time.Time, 1)
		h := s.Interval(func() {
			// Never block a tracked callback: a slow consumer drops ticks
			// instead of delaying disposal's join.
			select {
			case ticks <- time.Now():
			default:
			}
		}, d).(*handle)
		installOwnedEffect(c, h)
		defer h.stopAndWait() // break/return: stop and join before range ends
		for {
			select {
			case t := <-ticks:
				if !yield(t) {
					return
				}
			case <-c.Done():
				return
			}
		}
	}
}

// TimeoutContext is the context-shaped form of `ctx.timeout(delay)`: the
// returned context is cancelled when the timer fires, when the owner fires, or
// when the caller invokes the CancelFunc. Its inverse (cancel + join) is an
// effect of c, so no part of it outlives the consumer.
func TimeoutContext(c *runtime.Context, d time.Duration) (context.Context, context.CancelFunc) {
	base := context.Background()
	if c != nil {
		base = c.Context()
	}
	ctx, cancelTimer := context.WithCancel(base)
	s, err := requireService(c)
	if err != nil {
		cancelTimer()
		return ctx, func() {}
	}
	h := s.Timeout(func() { cancelTimer() }, d).(*handle)
	inverse := func() error {
		cancelTimer()
		return h.stopAndWait()
	}
	if err := c.Effect(func() (func() error, error) { return inverse, nil }); err != nil {
		inverse() // refused effect: compensate now, nothing may outlive c
	}
	var once sync.Once
	cancel := func() {
		once.Do(func() {
			cancelTimer()
			_ = h.stopAndWait()
		})
	}
	return ctx, cancel
}

// TimeoutArgs is the Scheduled<F> form whose callback receives arg (the JS
// `callback(...args)` forwarding convention). Like TimeoutOwned, it is bound to
// c's activation at creation time.
func TimeoutArgs[A any](c *runtime.Context, fn func(A), d time.Duration, arg A) Handle {
	if fn == nil {
		return TimeoutOwned(c, nil, d)
	}
	return TimeoutOwned(c, func() { fn(arg) }, d)
}

// IntervalArgs forwards arg to a repeating callback, bound to c's activation.
func IntervalArgs[A any](c *runtime.Context, fn func(A), d time.Duration, arg A) Handle {
	if fn == nil {
		return IntervalOwned(c, nil, d)
	}
	return IntervalOwned(c, func() { fn(arg) }, d)
}

// TimeoutArgsOwned/IntervalArgsOwned are explicit aliases for the argument
// forwarding forms; TimeoutArgs/IntervalArgs already bind c automatically.
func TimeoutArgsOwned[A any](c *runtime.Context, fn func(A), d time.Duration, arg A) Handle {
	return TimeoutArgs(c, fn, d, arg)
}

func IntervalArgsOwned[A any](c *runtime.Context, fn func(A), d time.Duration, arg A) Handle {
	return IntervalArgs(c, fn, d, arg)
}

// noopHandle is returned when the capability could not be resolved; it keeps
// the API total without leaking an untracked timer.
type noopHandle struct{}

func (noopHandle) Stop() bool { return false }

// ServiceKey is the capability identity of the timer service.
var ServiceKey = runtime.NewKey[Service]("extension.timer.service")

// timerEntry is the cancellable state shared by a timer and its inverses.
type timerEntry struct {
	mu      sync.Mutex
	stopCh  chan struct{}
	once    sync.Once
	stopped bool
	timer   *time.Timer
	ticker  *time.Ticker

	// cancelled gates callback admission and wg lets stop join callbacks that
	// were already in flight when the timer's inverse unwound. loopWg tracks
	// the interval dispatcher goroutine so joining also covers it.
	cancelled bool
	wg        sync.WaitGroup
	loopWg    sync.WaitGroup
}

func newEntry() *timerEntry {
	return &timerEntry{stopCh: make(chan struct{})}
}

// cancel closes stopCh exactly once and stops whichever underlying timer has
// been installed so far. A timer installed after cancel is stopped by setTimer.
func (e *timerEntry) cancel() {
	e.once.Do(func() {
		e.mu.Lock()
		e.stopped = true
		e.cancelled = true
		close(e.stopCh)
		t, tk := e.timer, e.ticker
		e.mu.Unlock()
		if t != nil {
			t.Stop()
		}
		if tk != nil {
			tk.Stop()
		}
	})
}

// cancelAndWait blocks new admissions, then joins the dispatcher (if any) and
// every callback admitted before cancellation. It is the unexported primitive
// behind handle.stopAndWait (effect inverses) and Service.Close.
func (e *timerEntry) cancelAndWait() {
	e.cancel()
	e.loopWg.Wait()
	e.wg.Wait()
}

func (e *timerEntry) setTimer(t *time.Timer) {
	e.mu.Lock()
	e.timer = t
	stopped := e.stopped
	e.mu.Unlock()
	if stopped {
		t.Stop()
	}
}

func (e *timerEntry) setTicker(tk *time.Ticker) {
	e.mu.Lock()
	e.ticker = tk
	stopped := e.stopped
	e.mu.Unlock()
	if stopped {
		tk.Stop()
	}
}

// service implements Service. mu protects closed, the entry set, and gates
// every wg.Add so that no Add can race with the Close wait.
type service struct {
	mu      sync.Mutex
	closed  bool
	entries map[*timerEntry]struct{}
	wg      sync.WaitGroup
}

func newService() *service {
	return &service{entries: make(map[*timerEntry]struct{})}
}

func (s *service) register(e *timerEntry) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return false
	}
	s.entries[e] = struct{}{}
	return true
}

// registerLoop registers an interval and reserves its loop goroutine in the
// same critical section as the closed check, so Close cannot begin waiting in
// between registration and the goroutine's Add.
func (s *service) registerLoop(e *timerEntry) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return false
	}
	s.entries[e] = struct{}{}
	s.wg.Add(1)
	e.loopWg.Add(1)
	return true
}

func (s *service) unregister(e *timerEntry) bool {
	s.mu.Lock()
	_, ok := s.entries[e]
	if ok {
		delete(s.entries, e)
	}
	s.mu.Unlock()
	return ok
}

// stop cancels without waiting. It is the implementation behind Handle.Stop.
func (s *service) stop(e *timerEntry) bool {
	if !s.unregister(e) {
		return false
	}
	e.cancel()
	return true
}

// fire runs a callback in a tracked goroutine. The closed check and wg.Add are
// atomic with respect to Close, so no callback can start after disposal and no
// Add can happen after Close's Wait. fn itself never runs under a service lock.
func (s *service) fire(fn func()) {
	s.fireFor(nil, fn)
}

// fireFor additionally rejects callbacks of an individually stopped handle.
// The entry's cancelled flag and wg.Add share e.mu, so once stop marks the
// entry cancelled no further callback can be admitted to its wg. Every timer
// (including Timeout and Interval) fires through here so that stopping a
// handle also prevents a late, already-scheduled tick from starting a callback.
func (s *service) fireFor(e *timerEntry, fn func()) {
	if fn == nil {
		return
	}
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return
	}
	if e != nil {
		e.mu.Lock()
		if e.cancelled {
			e.mu.Unlock()
			s.mu.Unlock()
			return
		}
		e.wg.Add(1)
		e.mu.Unlock()
	}
	s.wg.Add(1)
	s.mu.Unlock()
	go func() {
		defer s.wg.Done()
		if e != nil {
			defer e.wg.Done()
		}
		fn()
	}()
}

func (s *service) isClosed() bool {
	s.mu.Lock()
	closed := s.closed
	s.mu.Unlock()
	return closed
}

// Close is the effect inverse: it makes all future fires impossible, cancels
// every outstanding timer, and joins all callback goroutines. Once Close
// returns, the service is fully quiescent.
func (s *service) Close() error {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return nil
	}
	s.closed = true
	entries := make([]*timerEntry, 0, len(s.entries))
	for e := range s.entries {
		entries = append(entries, e)
	}
	s.entries = nil
	s.mu.Unlock()

	for _, e := range entries {
		e.cancelAndWait()
	}
	// Entries that self-removed after firing are not in the set above; their
	// tracked goroutines are still covered by the service-level wait.
	s.wg.Wait()
	return nil
}

// handle implements the public Handle with a single, non-waiting Stop. The
// joining inverse stopAndWait is deliberately unexported. onStop, when set,
// removes the debounce/throttle trigger's pending timer so no new pending fire
// can be scheduled.
type handle struct {
	svc     *service
	e       *timerEntry
	stopped atomic.Bool
	onStop  func()
}

func (h *handle) runOnStop() {
	if h.onStop != nil {
		h.onStop()
	}
}

// Stop cancels without waiting, so it is safe from inside this timer's own
// callback.
func (h *handle) Stop() bool {
	if h == nil || !h.stopped.CompareAndSwap(false, true) {
		return false
	}
	h.runOnStop()
	return h.svc.stop(h.e)
}

// stopAndWait cancels and joins admitted callbacks. It is reachable only from
// effect inverses and Close through the unexported joiner interface; user code
// (including callbacks) can only reach the non-waiting Stop.
func (h *handle) stopAndWait() error {
	if h == nil {
		return nil
	}
	if h.stopped.CompareAndSwap(false, true) {
		h.runOnStop()
	}
	// A one-shot timeout self-removes when it fires, so do not rely on
	// registration state: cancel directly to close the admission gate.
	h.svc.unregister(h.e)
	h.e.cancelAndWait()
	return nil
}

func (s *service) Timeout(fn func(), d time.Duration) Handle {
	e := newEntry()
	if !s.register(e) {
		return &handle{svc: s, e: e}
	}
	tm := time.AfterFunc(d, func() {
		s.unregister(e) // one-shot: self-remove before firing
		s.fireFor(e, fn)
	})
	e.setTimer(tm)
	return &handle{svc: s, e: e}
}

func (s *service) Interval(fn func(), d time.Duration) Handle {
	if d <= 0 {
		d = time.Nanosecond
	}
	e := newEntry()
	if !s.registerLoop(e) {
		return &handle{svc: s, e: e}
	}
	ticker := time.NewTicker(d)
	e.setTicker(ticker)
	go func() {
		defer s.wg.Done()
		defer e.loopWg.Done()
		for {
			select {
			case <-ticker.C:
				s.fireFor(e, fn)
			case <-e.stopCh:
				return
			}
		}
	}()
	return &handle{svc: s, e: e}
}

// debounceWithHandle returns the debounced trigger and its full handle. Every
// pending timer is cancelled by the handle and callbacks already admitted are
// joined by the unexported inverse. The public Stop alone cancels without
// joining, which makes it safe for a callback to stop its own entry.
func (s *service) debounceWithHandle(fn func(), d time.Duration) (func(), *handle) {
	if d < 0 {
		d = 0
	}
	e := newEntry()
	h := &handle{svc: s, e: e}
	if !s.register(e) {
		return func() {}, h
	}
	var mu sync.Mutex
	var pending *time.Timer
	var stopped bool
	trigger := func() {
		if s.isClosed() {
			return
		}
		mu.Lock()
		defer mu.Unlock()
		if stopped {
			return
		}
		// A pending call is cancelled by stopping its timer: the burst's last
		// call wins, and the callback fires only after a quiet period.
		if pending != nil {
			pending.Stop()
		}
		pending = time.AfterFunc(d, func() { s.fireFor(e, fn) })
		e.setTimer(pending)
	}
	h.onStop = func() {
		mu.Lock()
		stopped = true
		if pending != nil {
			pending.Stop()
			pending = nil
		}
		mu.Unlock()
	}
	return trigger, h
}

func (s *service) DebounceHandle(fn func(), d time.Duration) (func(), func() error) {
	trigger, h := s.debounceWithHandle(fn, d)
	return trigger, h.stopAndWait
}

func (s *service) DebounceWithHandle(fn func(), d time.Duration) (func(), Handle) {
	trigger, h := s.debounceWithHandle(fn, d)
	return trigger, h
}

func (s *service) Debounce(fn func(), d time.Duration) func() {
	trigger, _ := s.debounceWithHandle(fn, d)
	return trigger
}

// throttleWithHandle implements both throttle modes; the no-trailing variant
// drops calls inside the window instead of scheduling a trailing fire.
func (s *service) throttleWithHandle(fn func(), d time.Duration, allowTrailing bool) (func(), *handle) {
	if d < 0 {
		d = 0
	}
	e := newEntry()
	h := &handle{svc: s, e: e}
	if !s.register(e) {
		return func() {}, h
	}
	var mu sync.Mutex
	var window time.Time
	var timer *time.Timer
	var trailing bool
	var stopped bool
	trigger := func() {
		if s.isClosed() {
			return
		}
		mu.Lock()
		if stopped {
			mu.Unlock()
			return
		}
		now := time.Now()
		if window.IsZero() || now.Sub(window) >= d {
			window = now
			mu.Unlock()
			s.fireFor(e, fn) // leading edge
			return
		}
		if !allowTrailing {
			mu.Unlock()
			return
		}
		trailing = true
		if timer != nil {
			mu.Unlock()
			return
		}
		timer = time.AfterFunc(d-now.Sub(window), func() {
			mu.Lock()
			timer = nil
			fire := trailing
			trailing = false
			if fire {
				window = time.Now()
			}
			mu.Unlock()
			if fire {
				s.fireFor(e, fn) // trailing edge
			}
		})
		t := timer
		e.setTimer(t)
		mu.Unlock()
	}
	h.onStop = func() {
		mu.Lock()
		stopped = true
		if timer != nil {
			timer.Stop()
			timer = nil
		}
		trailing = false
		mu.Unlock()
	}
	return trigger, h
}

func (s *service) ThrottleHandle(fn func(), d time.Duration) (func(), func() error) {
	trigger, h := s.throttleWithHandle(fn, d, true)
	return trigger, h.stopAndWait
}

func (s *service) ThrottleWithHandle(fn func(), d time.Duration) (func(), Handle) {
	trigger, h := s.throttleWithHandle(fn, d, true)
	return trigger, h
}

func (s *service) Throttle(fn func(), d time.Duration) func() {
	trigger, _ := s.throttleWithHandle(fn, d, true)
	return trigger
}

func (s *service) ThrottleNoTrailing(fn func(), d time.Duration) func() {
	trigger, _ := s.throttleWithHandle(fn, d, false)
	return trigger
}

// Component provides the timer service and owns its lifecycle through a
// reversible effect. It holds no lifecycle authority of its own.
type Component struct{}

func (c *Component) Name() string                 { return "timer" }
func (c *Component) Inject() []runtime.Dependency { return nil }
func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}
func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	s := newService()
	if err := ctx.Effect(func() (func() error, error) {
		return s.Close, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, ServiceKey, Service(s)); err != nil {
		return nil, err
	}
	return nil, nil
}
