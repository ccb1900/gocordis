package events

import (
	"context"
	"fmt"
	"sync"

	"dynamic-runtime/runtime"
)

// Subscription is one live event stream owned by its consumer. Events() is
// closed exactly once when the subscription ends; Err reports the terminal
// cause (nil for an explicit Close).
type Subscription struct {
	ch        chan runtime.RuntimeEvent
	log       *Observer
	done      chan struct{}
	closeOnce sync.Once

	mu       sync.Mutex
	overflow error
	closed   bool
}

// Events returns the ordered event channel, closed when the subscription ends.
func (s *Subscription) Events() <-chan runtime.RuntimeEvent { return s.ch }

// Err reports the terminal cause after Events() is closed: nil for an
// explicit Close, ErrSubscriptionOverflow after a slow-consumer overflow,
// ErrObserverClosed after the Observer closed. Before the channel is closed
// it returns nil.
func (s *Subscription) Err() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.overflow
}

// Close unsubscribes and closes Events(). Idempotent; Err becomes nil.
func (s *Subscription) Close() {
	s.mu.Lock()
	s.closed = true
	s.overflow = nil
	s.mu.Unlock()
	s.log.removeSubscriber(s)
	s.end()
}

// end closes the channel exactly once.
func (s *Subscription) end() {
	s.closeOnce.Do(func() { close(s.ch) })
}

// finish marks a terminal cause and ends. The CALLER unregisters the
// subscription from the log (this runs under the log lock in Observer.Close).
func (s *Subscription) finish(cause error) {
	s.mu.Lock()
	if s.overflow == nil && !s.closed {
		s.overflow = cause
	}
	s.mu.Unlock()
	s.end()
}

// deliver enqueues one event without ever blocking the emitter. It reports
// whether the subscriber overflowed (the CALLER unregisters it — deliver may
// run while the observer lock is held and must not take it).
func (s *Subscription) deliver(ev runtime.RuntimeEvent) bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	if s.overflow != nil || s.closed {
		return false
	}
	select {
	case s.ch <- ev:
		return false
	default:
		s.overflow = ErrSubscriptionOverflow
		s.end()
		return true
	}
}

// Subscribe returns a live stream of canonical runtime events.
//
// from anchors the stream: events with Sequence > from are replayed from the
// retained ring (in order, before any live event), then live events stream.
// from = 0 streams live events only. The intended pairing with the runtime
// Snapshot:
//
//	snap := rt.Snapshot(ctx)          // snap.EventSequence = S
//	sub  := obs.Subscribe(ctx, S)     // no-gap resume from S
//
// If from predates the retained ring, Subscribe fails with
// ErrSequenceTooOld: re-Snapshot and anchor at the fresh EventSequence.
// Subscribe after Observer.Close fails with ErrObserverClosed.
func (o *Observer) Subscribe(ctx context.Context, from uint64) (*Subscription, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}

	sub := &Subscription{
		ch:   make(chan runtime.RuntimeEvent, subscriptionBuffer),
		log:  o,
		done: make(chan struct{}),
	}

	// Registration and replay-slice construction happen under the observer
	// lock — the same lock Emit holds — so replay and live delivery cannot
	// interleave, duplicate, or drop.
	o.mu.Lock()
	if o.closed != nil {
		closed := o.closed
		o.mu.Unlock()
		return nil, closed
	}
	if from > 0 && len(o.ring) > 0 && from < o.start {
		start := o.start
		o.mu.Unlock()
		return nil, fmt.Errorf("%w: requested from %d, retained from %d", ErrSequenceTooOld, from, start)
	}
	replay := make([]runtime.RuntimeEvent, 0, len(o.ring))
	for _, ev := range o.ring {
		if ev.Sequence > from {
			replay = append(replay, ev)
		}
	}
	o.subs[sub] = struct{}{}
	o.mu.Unlock()

	// Replay through the delivery path so ordering and overflow policy are
	// identical to live events. Replay alone can overflow when the ring
	// exceeds the subscriber buffer — the episode closes and the consumer
	// re-Snapshots.
	for _, ev := range replay {
		if sub.deliver(ev) {
			o.removeSubscriber(sub)
			return nil, sub.Err()
		}
	}
	return sub, nil
}

// removeSubscriber unregisters sub (idempotent; call WITHOUT holding o.mu).
func (o *Observer) removeSubscriber(sub *Subscription) {
	o.mu.Lock()
	defer o.mu.Unlock()
	delete(o.subs, sub)
}
