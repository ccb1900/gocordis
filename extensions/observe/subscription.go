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
	chOnce   sync.Once

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

// terminate marks the terminal cause and closes the stream.
func (s *Subscription) terminate(cause error) {
	s.mu.Lock()
	if s.overflow == nil && !s.closed {
		s.overflow = cause
	}
	s.mu.Unlock()
	s.log.removeSubscriber(s)
	s.halt()
}

// halt signals the pump to stop and closes the delivery channel, each exactly
// once.
func (s *Subscription) halt() {
	s.stopOnce.Do(func() { close(s.stop) })
	s.chOnce.Do(func() { close(s.ch) })
}

// wakeSignal nudges the pump (non-blocking).
func (s *Subscription) wakeSignal() {
	select {
	case s.wake <- struct{}{}:
	default:
	}
}

// enqueue appends one event to the internal spool (never blocks the emitter).
// Returns false when the subscription is closed.
func (s *Subscription) enqueue(ev runtime.RuntimeEvent) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.closed {
		return false
	}
	s.queue = append(s.queue, ev)
	return true
}

// pump is the sole mover from the internal spool into Events(): FIFO order,
// Sequence order, terminating on Close / overflow / Observer close.
func (s *Subscription) pump() {
	for {
		s.mu.Lock()
		if s.closed {
			s.mu.Unlock()
			return
		}
		if len(s.queue) > queueLimit {
			s.mu.Unlock()
			s.terminate(ErrSubscriptionOverflow)
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
