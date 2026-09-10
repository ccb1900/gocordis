package runtime

import (
	"context"
	"errors"
	"fmt"
	"sync"
)

// UI-03 — Runtime event stream subscription.
//
// The console-facing contract (paper §5.3: the console is a second, live view
// of the same runtime):
//
//	snap, _ := rt.Snapshot(ctx)        // snap.EventSequence = S
//	sub, _  := rt.Subscribe(ctx, S)    // replays (S, ...] exactly once
//	for ev := range sub.Events() { … } // then streams live events
//
// Delivery guarantees WITHIN one subscription episode: every event at most
// once, in Sequence order, no gaps. If a consumer falls behind and the buffer
// overflows, the subscription CLOSES with ErrSubscriptionOverflow — the
// consumer re-Snapshots and re-subscribes (the resume protocol above); the
// next episode's replay re-reads the ring.
//
// The emitter is never blocked: fan-out is best-effort under the event-log
// lock, so a slow console cannot stall the orchestrator (cooperative-host
// boundary). Subscriptions also close when the Runtime closes
// (ErrRuntimeClosed) or via Close().

// Errors. Compare with errors.Is.
var (
	// ErrSequenceTooOld reports that the requested anchor precedes the
	// retained ring: the gap cannot be replayed. Re-Snapshot and subscribe
	// from the fresh EventSequence.
	ErrSequenceTooOld = errors.New("runtime: requested event sequence predates the retained ring")
	// ErrSubscriptionOverflow reports that the subscriber fell behind and the
	// delivery buffer overflowed; the subscription is closed. Re-Snapshot and
	// re-subscribe.
	ErrSubscriptionOverflow = errors.New("runtime: event subscription overflowed")
)

// subscriptionBuffer bounds the per-subscriber delivery buffer.
const subscriptionBuffer = 256

// EventSubscription is one live event stream owned by its consumer. Events()
// is closed exactly once when the subscription ends; Err reports the terminal
// cause (nil for an explicit Close).
type EventSubscription struct {
	ch        chan RuntimeEvent
	log       *eventLog
	done      chan struct{}
	closeOnce sync.Once

	mu       sync.Mutex
	overflow error
	closed   bool
}

// Events returns the ordered event channel, closed when the subscription ends.
func (s *EventSubscription) Events() <-chan RuntimeEvent { return s.ch }

// Err reports the terminal cause after Events() is closed: nil for an
// explicit Close, ErrSubscriptionOverflow after a slow-consumer overflow,
// ErrRuntimeClosed after the Runtime closed. Before the channel is closed it
// returns nil.
func (s *EventSubscription) Err() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.overflow
}

// Close unsubscribes and closes Events(). Idempotent; Err becomes nil.
func (s *EventSubscription) Close() {
	s.mu.Lock()
	s.closed = true
	s.overflow = nil
	s.mu.Unlock()
	s.log.removeSubscriber(s)
	s.end()
}

// end closes the channel exactly once.
func (s *EventSubscription) end() {
	s.closeOnce.Do(func() { close(s.ch) })
}

// finish marks a terminal cause (overflow / runtime closed) and ends. The
// CALLER unregisters the subscription from the log (this runs under the log
// lock in closeAll).
func (s *EventSubscription) finish(cause error) {
	s.mu.Lock()
	if s.overflow == nil && !s.closed {
		s.overflow = cause
	}
	s.mu.Unlock()
	s.end()
}

// deliver enqueues one event without ever blocking the emitter. It reports
// whether the subscriber overflowed (the CALLER unregisters it — deliver may
// run while the event-log lock is held and must not take it). On overflow the
// subscriber finishes with ErrSubscriptionOverflow.
func (s *EventSubscription) deliver(ev RuntimeEvent) bool {
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

// Subscribe returns a live stream of canonical Runtime events.
//
// from anchors the stream: events with Sequence > from are replayed from the
// retained ring (in order, before any live event), then live events stream.
// from = 0 streams live events only. The intended pairing with Snapshot:
//
//	snap := rt.Snapshot()          // EventSequence = S
//	sub  := rt.Subscribe(ctx, S)   // no-gap resume from S
//
// If from predates the retained ring (older events were dropped under ring
// pressure), Subscribe fails with ErrSequenceTooOld: re-Snapshot and anchor at
// the fresh EventSequence. Subscribe after Runtime.Close fails with
// ErrRuntimeClosed.
func (r *Runtime) Subscribe(ctx context.Context, from uint64) (*EventSubscription, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	if r.stateSnapshot() != RuntimeRunning {
		return nil, ErrRuntimeClosed
	}

	sub := &EventSubscription{
		ch:   make(chan RuntimeEvent, subscriptionBuffer),
		log:  r.events,
		done: make(chan struct{}),
	}

	// Registration and replay-slice construction happen under the event-log
	// lock — the same lock emit holds — so replay and live delivery cannot
	// interleave, duplicate, or drop.
	r.events.mu.Lock()
	if from > 0 && len(r.events.ring) > 0 && from < r.events.start {
		start := r.events.start
		r.events.mu.Unlock()
		return nil, fmt.Errorf("%w: requested from %d, retained from %d", ErrSequenceTooOld, from, start)
	}
	replay := make([]RuntimeEvent, 0, len(r.events.ring))
	for _, ev := range r.events.ring {
		if ev.Sequence > from {
			replay = append(replay, ev)
		}
	}
	r.events.subs[sub] = struct{}{}
	r.events.mu.Unlock()

	// Replay through the delivery path so ordering and overflow policy are
	// identical to live events. Replay alone cannot overflow unless the ring
	// (1024) exceeds the subscriber buffer (256) — in that case the episode
	// closes and the consumer re-Snapshots.
	for _, ev := range replay {
		if sub.deliver(ev) {
			r.events.removeSubscriber(sub)
			return nil, sub.Err()
		}
	}
	return sub, nil
}
