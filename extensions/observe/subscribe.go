package events

import (
	"context"
	"fmt"

	"dynamic-runtime/runtime"
)

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
// The replay is SPOOLED into the subscription's internal queue, so a
// reconnect gap is never lost to the delivery buffer (R12 P1-1 fix); the
// pump forwards it to Events() in order.
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
		stop: make(chan struct{}),
		wake: make(chan struct{}, 1),
	}

	// Registration and replay spooling happen under the observer lock -- the
	// same lock Emit holds -- so replay and live enqueue cannot interleave.
	o.mu.Lock()
	if o.closed != nil {
		closed := o.closed
		o.mu.Unlock()
		return nil, closed
	}
	if from > 0 && o.count > 0 && from < o.start {
		start := o.start
		o.mu.Unlock()
		return nil, fmt.Errorf("%w: requested from %d, retained from %d", ErrSequenceTooOld, from, start)
	}
	// Spool the replay into the subscription queue (ordered before live).
	for i := 0; i < o.count; i++ {
		idx := (o.head + i) % ringSize
		ev := o.ring[idx]
		if ev.Sequence > from {
			sub.queue = append(sub.queue, ev)
		}
	}
	o.subs[sub] = struct{}{}
	o.mu.Unlock()

	go sub.pump()
	return sub, nil
}
