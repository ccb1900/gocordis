package runtime

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"
)

// UI-03 subscription conformance (S-01..S-07 — THIS REPO's suite numbering
// for the subscription suite, following the C/L/H/S/E/V convention; the paper
// has no subscription concept).
//
// Classification: PAPER-NEUTRAL platform infrastructure (UI-03 observation
// ladder, pre-dating the paper-first pivot). It adds a read model over the
// canonical event log and touches none of the calculus semantics — theorems
// 64/68/70/73/80 surfaces are unchanged.
//
// Conformance: event stream subscription over the canonical event log.
//
//	Resume protocol: Snapshot(EventSequence S) -> Subscribe(S) replays (S, ...]
//	exactly once, then streams live events, no gaps, in Sequence order.
//	Anchors older than the retained ring fail with ErrSequenceTooOld.
//	A slow consumer overflows -> subscription closes with
//	ErrSubscriptionOverflow (emitter never blocks). Runtime close terminates
//	every subscription with ErrRuntimeClosed.

func u03RT(t *testing.T) *Runtime {
	t.Helper()
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

func u03WaitEvent(t *testing.T, sub *EventSubscription, want EventType) RuntimeEvent {
	t.Helper()
	deadline := time.After(8 * time.Second)
	for {
		select {
		case ev, ok := <-sub.Events():
			if !ok {
				t.Fatalf("subscription closed while waiting for %v (err=%v)", want, sub.Err())
			}
			if ev.Type == want {
				return ev
			}
		case <-deadline:
			t.Fatalf("timeout waiting for %v", want)
		}
	}
}

// S-01 — live-only subscription (from = 0): events emitted after Subscribe
// arrive in order; earlier history is not replayed.
func TestSubscribeLiveOnly(t *testing.T) {
	rt := u03RT(t)
	rt.emitEvent(EventFiberCreated, 1, 0, nil) // history before subscribe

	sub, err := rt.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	defer sub.Close()

	rt.emitEvent(EventProviderPublished, 2, 0, ProviderEventData{Key: "k"})
	ev := u03WaitEvent(t, sub, EventProviderPublished)
	if ev.Sequence == 0 || ev.FiberID != 2 {
		t.Fatalf("live event = %+v", ev)
	}
	// The pre-subscribe event must NOT have been replayed.
	select {
	case e := <-sub.Events():
		t.Fatalf("unexpected extra event %+v", e)
	case <-time.After(100 * time.Millisecond):
	}
}

// S-02 — resume protocol: Subscribe(from = snapshot sequence) replays the
// exact gap then streams live, no gaps, in order.
func TestSubscribeResumeFromSequence(t *testing.T) {
	rt := u03RT(t)
	for i := 0; i < 5; i++ {
		rt.emitEvent(EventFiberCreated, FiberID(i+1), 0, nil)
	}
	snapSeq := rt.events.current() // the "Snapshot" anchor

	// Gap events emitted BEFORE subscribing (the reconnect window).
	rt.emitEvent(EventFailure, 9, 0, FailureEventData{Phase: "Apply"})
	rt.emitEvent(EventFailure, 10, 0, FailureEventData{Phase: "Apply"})

	sub, err := rt.Subscribe(context.Background(), snapSeq)
	if err != nil {
		t.Fatal(err)
	}
	defer sub.Close()

	// Replay: the two gap events, in order.
	for want := snapSeq + 1; want <= snapSeq+2; want++ {
		select {
		case ev := <-sub.Events():
			if ev.Sequence != want {
				t.Fatalf("replay sequence = %d, want %d", ev.Sequence, want)
			}
		case <-time.After(8 * time.Second):
			t.Fatalf("timeout waiting for replay %d", want)
		}
	}
	// Live: an event emitted after subscription arrives next, in order.
	rt.emitEvent(EventFailure, 11, 0, FailureEventData{Phase: "Apply"})
	select {
	case ev := <-sub.Events():
		if ev.Sequence != snapSeq+3 {
			t.Fatalf("live sequence = %d, want %d (no gap after replay)", ev.Sequence, snapSeq+3)
		}
	case <-time.After(8 * time.Second):
		t.Fatal("timeout waiting for live event")
	}
}

// S-03 — an anchor older than the retained ring fails with
// ErrSequenceTooOld (the resume protocol requires a fresh Snapshot).
func TestSubscribeSequenceTooOld(t *testing.T) {
	rt := u03RT(t)
	for i := 0; i < eventRingSize+50; i++ {
		rt.emitEvent(EventFiberCreated, FiberID(i+1), 0, nil)
	}
	_, err := rt.Subscribe(context.Background(), 1)
	if !errors.Is(err, ErrSequenceTooOld) {
		t.Fatalf("err = %v, want ErrSequenceTooOld", err)
	}
	// A fresh anchor (current sequence) still works.
	if _, err := rt.Subscribe(context.Background(), rt.events.current()); err != nil {
		t.Fatalf("fresh anchor rejected: %v", err)
	}
}

// S-04 — slow consumer: when the buffer overflows the subscription closes
// with ErrSubscriptionOverflow and the EMITTER never blocks (emit latency
// stays bounded).
func TestSubscribeSlowConsumerOverflow(t *testing.T) {
	rt := u03RT(t)
	sub, err := rt.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	start := time.Now()
	for i := 0; i < subscriptionBuffer+50; i++ {
		rt.emitEvent(EventFiberCreated, FiberID(i+1), 0, nil)
	}
	elapsed := time.Since(start)
	if elapsed > 2*time.Second {
		t.Fatalf("emitter blocked on slow subscriber: %v", elapsed)
	}
	if !errors.Is(sub.Err(), ErrSubscriptionOverflow) {
		t.Fatalf("Err() = %v, want ErrSubscriptionOverflow", sub.Err())
	}
	// The channel still holds buffered events; draining must end in a closed
	// channel, and later emits must not touch the overflowed subscription.
	n := 0
	for range sub.Events() {
		n++
		if n > subscriptionBuffer+100 {
			t.Fatal("overflowed channel never closed")
		}
	}
	rt.emitEvent(EventFiberCreated, 9999, 0, nil)
}

// S-05 — Runtime.Close terminates every subscription with ErrRuntimeClosed.
func TestSubscribeRuntimeCloseTerminates(t *testing.T) {
	rt := u03RT(t)
	sub1, err := rt.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	sub2, err := rt.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := rt.Close(ctx); err != nil {
		t.Fatal(err)
	}
	for i, sub := range []*EventSubscription{sub1, sub2} {
		for range sub.Events() {
		}
		if !errors.Is(sub.Err(), ErrRuntimeClosed) {
			t.Fatalf("sub %d Err = %v, want ErrRuntimeClosed", i+1, sub.Err())
		}
	}
	// Subscribe after close fails.
	if _, err := rt.Subscribe(context.Background(), 0); !errors.Is(err, ErrRuntimeClosed) {
		t.Fatalf("subscribe after close = %v, want ErrRuntimeClosed", err)
	}
}

// S-06 — explicit Close: channel closes, Err is nil, idempotent, and the
// subscription stops receiving.
func TestSubscribeExplicitClose(t *testing.T) {
	rt := u03RT(t)
	sub, err := rt.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	sub.Close()
	sub.Close() // idempotent
	if err := sub.Err(); err != nil {
		t.Fatalf("Err after explicit close = %v, want nil", err)
	}
	if _, ok := <-sub.Events(); ok {
		t.Fatal("channel must be closed after Close")
	}
	rt.emitEvent(EventFiberCreated, 1, 0, nil) // must not panic on closed sub
}

// S-07 — concurrent subscribers and emitters under -race: subscribers that
// keep draining see every event exactly once, in Sequence order.
func TestSubscribeConcurrent(t *testing.T) {
	rt := u03RT(t)
	const emitters, perEmitter = 4, 50
	total := emitters * perEmitter

	// Subscribe BEFORE emission; three independent streams.
	subs := make([]*EventSubscription, 3)
	for i := range subs {
		sub, err := rt.Subscribe(context.Background(), 0)
		if err != nil {
			t.Fatal(err)
		}
		defer sub.Close()
		subs[i] = sub
	}

	counts := make([]atomic.Int32, len(subs))
	lasts := make([]uint64, len(subs))
	var pumpWG sync.WaitGroup
	stop := make(chan struct{})
	for i, sub := range subs {
		i, sub := i, sub
		pumpWG.Add(1)
		go func() {
			defer pumpWG.Done()
			for {
				select {
				case ev, ok := <-sub.Events():
					if !ok {
						return
					}
					if ev.Sequence <= lasts[i] {
						t.Errorf("sub %d out of order: %d after %d", i, ev.Sequence, lasts[i])
						return
					}
					lasts[i] = ev.Sequence
					counts[i].Add(1)
					if counts[i].Load() >= int32(total) {
						return
					}
				case <-stop:
					return
				}
			}
		}()
	}

	var wg sync.WaitGroup
	for e := 0; e < emitters; e++ {
		wg.Add(1)
		go func(e int) {
			defer wg.Done()
			for i := 0; i < perEmitter; i++ {
				rt.emitEvent(EventFiberCreated, FiberID(e*1000+i+1), 0, nil)
			}
		}(e)
	}
	wg.Wait()

	deadline := time.After(8 * time.Second)
	for {
		allDone := true
		for i := range counts {
			if counts[i].Load() < int32(total) {
				allDone = false
			}
		}
		if allDone {
			break
		}
		select {
		case <-deadline:
			t.Fatalf("timeout: counts = %v, want %d each", []int32{counts[0].Load(), counts[1].Load(), counts[2].Load()}, total)
		case <-time.After(20 * time.Millisecond):
		}
	}
	close(stop)
	pumpWG.Wait()
}
