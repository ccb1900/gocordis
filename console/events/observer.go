// Package events implements the console-side canonical event stream:
// ring retention (1024), sequence-anchored resume, and subscription fan-out.
//
// It is the consumer layer of the runtime's paper-neutral observation hook:
// the application attaches an Observer as the runtime.EventSink
// (runtime.WithEventSink), and console components Subscribe for the live
// timeline. The kernel neither buffers observations nor knows about
// subscribers.
//
// Resume protocol (pairs with runtime Snapshot.EventSequence):
//
//	snap := rt.Snapshot(ctx)          // snap.EventSequence = S
//	sub  := obs.Subscribe(ctx, S)     // replays (S, ...] exactly once
//	for ev := range sub.Events() { … } // then streams live events
//
// Delivery guarantees within one subscription episode: every event at most
// once, in Sequence order, no gaps. A consumer that falls behind overflows:
// the subscription CLOSES with ErrSubscriptionOverflow (the runtime's emitter
// never blocks). Anchors older than the retained ring fail with
// ErrSequenceTooOld — re-Snapshot and re-anchor. Observer.Close terminates
// every subscription.
package events

import (
	"errors"
	"sync"

	"dynamic-runtime/runtime"
)

// Errors. Compare with errors.Is.
var (
	// ErrSequenceTooOld reports that the requested anchor precedes the
	// retained ring: the gap cannot be replayed. Re-Snapshot and re-anchor.
	ErrSequenceTooOld = errors.New("events: requested sequence predates the retained ring")
	// ErrSubscriptionOverflow reports that the subscriber fell behind and the
	// delivery buffer overflowed; the subscription is closed.
	ErrSubscriptionOverflow = errors.New("events: subscription overflowed")
	// ErrObserverClosed reports that the Observer was closed.
	ErrObserverClosed = errors.New("events: observer closed")
)

// ringSize bounds the retained event history.
const ringSize = 1024

// subscriptionBuffer bounds the per-subscriber delivery buffer.
const subscriptionBuffer = 256

// Observer retains the canonical event history and fans out to subscribers.
// It implements runtime.EventSink and is safe for concurrent use. Emit MUST
// NOT block (the runtime calls it inline on lifecycle goroutines).
type Observer struct {
	mu      sync.Mutex
	nextSeq uint64
	start   uint64 // Sequence of ring[0]; 0 when empty
	ring    []runtime.RuntimeEvent
	subs    map[*Subscription]struct{}
	closed  error
}

var _ runtime.EventSink = (*Observer)(nil)

// New creates an empty Observer.
func New() *Observer {
	return &Observer{ring: make([]runtime.RuntimeEvent, 0, ringSize), subs: make(map[*Subscription]struct{})}
}

// Emit implements runtime.EventSink. The event's Sequence is trusted when
// non-zero (the kernel pre-assigns it); a zero Sequence is assigned from the
// observer's own counter (for direct feeders).
func (o *Observer) Emit(ev runtime.RuntimeEvent) {
	o.mu.Lock()
	if o.closed != nil {
		o.mu.Unlock()
		return
	}
	if ev.Sequence == 0 {
		o.nextSeq++
		ev.Sequence = o.nextSeq
	}
	if ev.Sequence > o.nextSeq {
		o.nextSeq = ev.Sequence
	}
	if o.start == 0 {
		o.start = ev.Sequence
	}
	if len(o.ring) == ringSize {
		copy(o.ring, o.ring[1:])
		o.ring[ringSize-1] = ev
		if o.start != 0 {
			o.start++
		}
	} else {
		o.ring = append(o.ring, ev)
	}
	var overflowed []*Subscription
	for sub := range o.subs {
		if sub.deliver(ev) {
			overflowed = append(overflowed, sub)
		}
	}
	o.mu.Unlock()
	o.removeSubs(overflowed)
}

// removeSubs unregisters subscribers (call WITHOUT holding o.mu).
func (o *Observer) removeSubs(subs []*Subscription) {
	if len(subs) == 0 {
		return
	}
	o.mu.Lock()
	defer o.mu.Unlock()
	for _, sub := range subs {
		delete(o.subs, sub)
	}
}

// Close terminates every subscription with ErrObserverClosed and drops the
// ring. The Observer must not be emitted to afterwards.
func (o *Observer) Close() {
	o.mu.Lock()
	defer o.mu.Unlock()
	o.closed = ErrObserverClosed
	for sub := range o.subs {
		sub.finish(ErrObserverClosed)
	}
	o.subs = make(map[*Subscription]struct{})
	o.ring = nil
}
