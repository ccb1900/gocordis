package events

import (
	"sync"

	"dynamic-runtime/runtime"
)

// Subscription is one live event stream owned by its consumer. Events() is
// closed exactly once when the subscription ends; Err reports the terminal
// cause (nil for an explicit Close).
//
// Delivery model: the Observer (and the resume replay) APPEND to an internal
// ordered queue; a per-subscription pump goroutine is the sole mover from the
// queue into Events(). Consequences:
//   - the replay phase never overflows (a reconnect gap <= ring is spooled in
//     full -- no permanent loss, fixing R12 P1-1);
//   - ordering is preserved (queue FIFO == Sequence order);
//   - a consumer that stops reading accumulates queue backlog; past
//     queueLimit the subscription CLOSES with ErrSubscriptionOverflow (the
//     Observer's emitter never blocks) -- re-Snapshot and re-subscribe.

// queueLimit bounds the internal spool backlog (16x the delivery buffer).
const queueLimit = 4096

type Subscription struct {
	ch       chan runtime.RuntimeEvent
	log      *Observer
	stop     chan struct{}
	wake     chan struct{}
	stopOnce sync.Once

	mu       sync.Mutex
	queue    []runtime.RuntimeEvent
	overflow error
	closed   bool
}

// Events returns the ordered event channel, closed when the subscription ends.
func (s *Subscription) Events() <-chan runtime.RuntimeEvent { return s.ch }

// Err reports the terminal cause after Events() is closed: nil for an
// explicit Close, ErrSubscriptionOverflow after a slow-consumer overflow,
// ErrObserverClosed after the Observer closed.
func (s *Subscription) Err() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.overflow
}

// Close unsubscribes and closes Events(). Idempotent; Err becomes nil.
func (s *Subscription) Close() {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return
	}
	s.closed = true
	s.mu.Unlock()
	s.log.removeSubscriber(s)
	s.halt()
}

// terminate marks the terminal cause and closes the stream. The CALLER owns
// unregistration: Observer.Close replaces the map wholesale (sub.finish);
// the pump (which never holds the observer lock) calls removeSubscriber
// itself after terminate.
func (s *Subscription) terminate(cause error) {
	s.mu.Lock()
	if s.overflow == nil && !s.closed {
		s.overflow = cause
	}
	s.mu.Unlock()
	s.halt()
}

// finish marks the terminal cause and ends the stream WITHOUT unregistering
// (used by Observer.Close, which replaces the subscriber map wholesale).
func (s *Subscription) finish(cause error) {
	s.mu.Lock()
	if s.overflow == nil && !s.closed {
		s.overflow = cause
	}
	s.mu.Unlock()
	s.halt()
}

// halt signals the pump to stop. It must NOT close the delivery channel
// (EXT-01): the pump may be parked in `select { case s.ch <- ev: ... case
// <-s.stop: }` and Go's select may pick the send branch even though stop is
// closed — a send into a just-closed channel panics. The pump is the SOLE
// closer of s.ch (via defer in pump), which makes send-to-closed structurally
// impossible: the channel is only closed after the pump has stopped sending.
func (s *Subscription) halt() {
	s.stopOnce.Do(func() { close(s.stop) })
}

// wakeSignal nudges the pump (non-blocking).
func (s *Subscription) wakeSignal() {
	select {
	case s.wake <- struct{}{}:
	default:
	}
}

// enqueue appends one event to the internal spool (never blocks the emitter).
// Returns false when the subscription is closed or has overflowed. The
// backlog limit is enforced HERE (EXT-01): the old pop-time-only check was
// unreachable while the pump sat parked in the send, so a stopped consumer
// made the spool grow without bound. On overflow the subscription terminates
// with ErrSubscriptionOverflow (the pump — never holding the observer lock —
// performs the unregistration when it wakes).
func (s *Subscription) enqueue(ev runtime.RuntimeEvent) bool {
	s.mu.Lock()
	if s.closed || s.overflow != nil {
		s.mu.Unlock()
		return false
	}
	if len(s.queue) >= queueLimit {
		s.overflow = ErrSubscriptionOverflow
		s.mu.Unlock()
		s.halt() // non-blocking: wakes the pump, which closes Events()
		return false
	}
	s.queue = append(s.queue, ev)
	s.mu.Unlock()
	return true
}

// pump is the sole mover from the internal spool into Events(): FIFO order,
// Sequence order, terminating on Close / overflow / Observer close.
func (s *Subscription) pump() {
	// EXT-01: the pump owns BOTH terminal duties. It is the sole closer of the
	// delivery channel (deferred, covering every exit path), and it performs
	// the unregistration itself (idempotent; Close() may have done it already).
	// No send can observe a closed channel: the channel closes only after the
	// pump has stopped sending.
	defer close(s.ch)
	defer func() { s.log.removeSubscriber(s) }()
	for {
		s.mu.Lock()
		if s.closed || s.overflow != nil {
			s.mu.Unlock()
			return
		}
		if len(s.queue) == 0 {
			s.mu.Unlock()
			select {
			case <-s.stop:
				return
			case <-s.wake:
			}
			continue
		}
		// Defense in depth: the enqueue-side cap keeps the backlog within the
		// limit, so this branch is unreachable in normal operation.
		if len(s.queue) > queueLimit {
			s.mu.Unlock()
			s.terminate(ErrSubscriptionOverflow)
			return
		}
		ev := s.queue[0]
		s.queue = s.queue[1:]
		s.mu.Unlock()

		select {
		case s.ch <- ev:
		case <-s.stop:
			return
		}
	}
}
