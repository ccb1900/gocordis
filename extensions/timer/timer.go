// Package timer provides JS-style timers as a Runtime-managed capability.
//
// The service owns every consequence it creates: each timer entry is tracked,
// stopping a timer is reversible, and disposal stops all entries and JOINS the
// callback goroutines before the activation unwinds. No goroutine or callback
// survives the fiber. All user callbacks run outside the service lock.
package timer

import (
	"errors"
	"sync"
	"sync/atomic"
	"time"

	"dynamic-runtime/runtime"
)

// Handle controls one Timeout or Interval timer. Stop is idempotent, safe from
// any goroutine, and reports whether this call stopped a still-live timer.
type Handle interface {
	Stop() bool
}

// Service is the timer capability consumed by components.
//
// Debounce/Throttle entries are owned by the service until it is disposed.
// A consumer whose own fiber may be disposed first MUST use the *Handle forms
// with Owned so the timer's inverse unwinds with the consumer, not the service.
type Service interface {
	Timeout(fn func(), d time.Duration) Handle
	Interval(fn func(), d time.Duration) Handle
	Debounce(fn func(), d time.Duration) func()
	Throttle(fn func(), d time.Duration) func()
	ThrottleNoTrailing(fn func(), d time.Duration) func()
	DebounceHandle(fn func(), d time.Duration) (trigger func(), stop func() error)
	ThrottleHandle(fn func(), d time.Duration) (trigger func(), stop func() error)
}

// Owned installs a timer handle's stop function as an effect of the consumer's
// activation. When that activation unwinds, the handle is cancelled and its
// in-flight callbacks are joined before the consumer reaches Gone. An refused
// effect is compensated immediately so no timer outlives its consumer.
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

	// cancelled and wg are used by per-consumer debounce/throttle handles:
	// cancelled gates callback admission and wg lets stop join callbacks that
	// were already in flight when the consumer's effect unwound.
	cancelled bool
	wg        sync.WaitGroup
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
// entry cancelled no further callback can be admitted to its wg.
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
		e.cancel()
	}
	s.wg.Wait()
	return nil
}

type handle struct {
	svc     *service
	e       *timerEntry
	stopped atomic.Bool
}

func (h *handle) Stop() bool {
	if h == nil || !h.stopped.CompareAndSwap(false, true) {
		return false
	}
	return h.svc.stop(h.e)
}

func (s *service) Timeout(fn func(), d time.Duration) Handle {
	e := newEntry()
	if !s.register(e) {
		return &handle{svc: s, e: e}
	}
	tm := time.AfterFunc(d, func() {
		s.unregister(e) // one-shot: self-remove before firing
		s.fire(fn)
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
		for {
			select {
			case <-ticker.C:
				s.fire(fn)
			case <-e.stopCh:
				return
			}
		}
	}()
	return &handle{svc: s, e: e}
}

// DebounceHandle returns the debounced trigger and its inverse. Every pending
// timer is cancelled by the returned stop, and callbacks already admitted are
// joined before stop returns. Stop is idempotent and safe from any goroutine
// except a callback of this same entry: that callback is tracked by e.wg and
// would self-join, so callbacks must not stop their own entry.
func (s *service) DebounceHandle(fn func(), d time.Duration) (func(), func() error) {
	if d < 0 {
		d = 0
	}
	e := newEntry()
	if !s.register(e) {
		noop := func() {}
		return noop, func() error { return nil }
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
	var once sync.Once
	stop := func() error {
		once.Do(func() {
			mu.Lock()
			stopped = true
			if pending != nil {
				pending.Stop()
				pending = nil
			}
			mu.Unlock()
			e.cancel()
			s.unregister(e)
			e.wg.Wait()
		})
		return nil
	}
	return trigger, stop
}

func (s *service) Debounce(fn func(), d time.Duration) func() {
	trigger, _ := s.DebounceHandle(fn, d)
	return trigger
}

// throttleHandle implements both throttle modes; the no-trailing variant drops
// calls inside the window instead of scheduling a trailing fire.
func (s *service) throttleHandle(fn func(), d time.Duration, allowTrailing bool) (func(), func() error) {
	if d < 0 {
		d = 0
	}
	e := newEntry()
	if !s.register(e) {
		noop := func() {}
		return noop, func() error { return nil }
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
	var once sync.Once
	stop := func() error {
		once.Do(func() {
			mu.Lock()
			stopped = true
			if timer != nil {
				timer.Stop()
				timer = nil
			}
			trailing = false
			mu.Unlock()
			e.cancel()
			s.unregister(e)
			e.wg.Wait()
		})
		return nil
	}
	return trigger, stop
}

func (s *service) ThrottleHandle(fn func(), d time.Duration) (func(), func() error) {
	return s.throttleHandle(fn, d, true)
}

func (s *service) Throttle(fn func(), d time.Duration) func() {
	trigger, _ := s.ThrottleHandle(fn, d)
	return trigger
}

func (s *service) ThrottleNoTrailing(fn func(), d time.Duration) func() {
	trigger, _ := s.throttleHandle(fn, d, false)
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
