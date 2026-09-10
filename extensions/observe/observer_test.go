package events_test

import (
	"context"
	"errors"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/observe"
	"dynamic-runtime/runtime"
)

// UI-03 subscription conformance (S-01..S-07 — THIS REPO's suite numbering
// for the subscription suite, following the C/L/H/S/E/V convention; the paper
// has no subscription concept).
//
// Classification: PAPER-NEUTRAL platform infrastructure (console observation
// layer; the kernel only assigns sequences and hands events to the sink).
//
// Conformance: ring retention, sequence-anchored resume, overflow protocol,
// observer close — over the extensions/observe Observer.

func newObs(t *testing.T) *events.Observer {
	t.Helper()
	return events.New()
}

// emit assigns the next sequence and feeds one event (the kernel sink path
// pre-assigns sequences; direct feeders use this helper).
func emit(o *events.Observer, seq *uint64, id int) {
	*seq++
	o.Emit(runtime.RuntimeEvent{
		Sequence:  *seq,
		Timestamp: time.Now(),
		RuntimeID: "rt-test",
		Type:      runtime.EventFiberCreated,
		FiberID:   runtime.FiberID(id),
	})
}

func waitEvent(t *testing.T, sub *events.Subscription, minSeq uint64) runtime.RuntimeEvent {
	t.Helper()
	deadline := time.After(8 * time.Second)
	for {
		select {
		case ev, ok := <-sub.Events():
			if !ok {
				t.Fatalf("subscription closed while waiting (err=%v)", sub.Err())
			}
			if ev.Sequence >= minSeq {
				return ev
			}
		case <-deadline:
			t.Fatalf("timeout waiting for seq >= %d", minSeq)
		}
	}
}

// S-01 — live-only subscription (from = 0).
func TestSubscribeLiveOnly(t *testing.T) {
	o := newObs(t)
	var seq uint64
	emit(o, &seq, 1) // history before subscribe

	sub, err := o.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	defer sub.Close()

	emit(o, &seq, 2)
	ev := waitEvent(t, sub, seq)
	if ev.FiberID != 2 {
		t.Fatalf("live event = %+v", ev)
	}
	select {
	case e := <-sub.Events():
		t.Fatalf("unexpected extra event %+v", e)
	case <-time.After(100 * time.Millisecond):
	}
}

// S-02 — resume protocol: Subscribe(from = snapshot sequence) replays the
// exact gap then streams live, no gaps, in order.
func TestSubscribeResumeFromSequence(t *testing.T) {
	o := newObs(t)
	var seq uint64
	for i := 0; i < 5; i++ {
		emit(o, &seq, i+1)
	}
	snapSeq := seq

	emit(o, &seq, 9) // gap events emitted before subscribing
	emit(o, &seq, 10)

	sub, err := o.Subscribe(context.Background(), snapSeq)
	if err != nil {
		t.Fatal(err)
	}
	defer sub.Close()

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
	emit(o, &seq, 11)
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
// ErrSequenceTooOld.
func TestSubscribeSequenceTooOld(t *testing.T) {
	o := newObs(t)
	var seq uint64
	for i := 0; i < 1100; i++ {
		emit(o, &seq, i+1)
	}
	if _, err := o.Subscribe(context.Background(), 1); !errors.Is(err, events.ErrSequenceTooOld) {
		t.Fatalf("err = %v, want ErrSequenceTooOld", err)
	}
	if _, err := o.Subscribe(context.Background(), seq); err != nil {
		t.Fatalf("fresh anchor rejected: %v", err)
	}
}

// S-04 — slow consumer: buffer overflow closes the subscription with
// ErrSubscriptionOverflow and the EMITTER never blocks.
func TestSubscribeSlowConsumerOverflow(t *testing.T) {
	o := newObs(t)
	sub, err := o.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	start := time.Now()
	var seq uint64
	for i := 0; i < 350; i++ {
		emit(o, &seq, i+1)
	}
	if elapsed := time.Since(start); elapsed > 2*time.Second {
		t.Fatalf("emitter blocked on slow subscriber: %v", elapsed)
	}
	if !errors.Is(sub.Err(), events.ErrSubscriptionOverflow) {
		t.Fatalf("Err() = %v, want ErrSubscriptionOverflow", sub.Err())
	}
	n := 0
	for range sub.Events() {
		n++
		if n > 400 {
			t.Fatal("overflowed channel never closed")
		}
	}
	emit(o, &seq, 9999) // must not touch the overflowed subscription
}

// S-05 — Observer.Close terminates every subscription with
// ErrObserverClosed.
func TestSubscribeObserverCloseTerminates(t *testing.T) {
	o := newObs(t)
	sub1, err := o.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	sub2, err := o.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	o.Close()
	for i, sub := range []*events.Subscription{sub1, sub2} {
		for range sub.Events() {
		}
		if !errors.Is(sub.Err(), events.ErrObserverClosed) {
			t.Fatalf("sub %d Err = %v, want ErrObserverClosed", i+1, sub.Err())
		}
	}
	if _, err := o.Subscribe(context.Background(), 0); !errors.Is(err, events.ErrObserverClosed) {
		t.Fatalf("subscribe after close = %v, want ErrObserverClosed", err)
	}
}

// S-06 — explicit Close: channel closes, Err is nil, idempotent.
func TestSubscribeExplicitClose(t *testing.T) {
	o := newObs(t)
	sub, err := o.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	sub.Close()
	sub.Close()
	if err := sub.Err(); err != nil {
		t.Fatalf("Err after explicit close = %v, want nil", err)
	}
	if _, ok := <-sub.Events(); ok {
		t.Fatal("channel must be closed after Close")
	}
	var seq uint64
	emit(o, &seq, 1) // must not panic on closed sub
}

// S-07 — concurrent subscribers and feeders under -race: subscribers that
// keep draining see every event exactly once, in Sequence order.
func TestSubscribeConcurrent(t *testing.T) {
	o := newObs(t)
	const feeders, perFeeder = 4, 50
	total := feeders * perFeeder

	subs := make([]*events.Subscription, 3)
	for i := range subs {
		sub, err := o.Subscribe(context.Background(), 0)
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

	// Feeders serialize sequence-assignment WITH emission (the same atomicity
	// the kernel's emitEvent provides), so the observer receives events in
	// Sequence order.
	var wg sync.WaitGroup
	var seq uint64
	var seqMu sync.Mutex
	for f := 0; f < feeders; f++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < perFeeder; i++ {
				seqMu.Lock()
				seq++
				o.Emit(runtime.RuntimeEvent{
					Sequence:  seq,
					Timestamp: time.Now(),
					RuntimeID: "rt-test",
					Type:      runtime.EventFiberCreated,
					FiberID:   runtime.FiberID(seq),
				})
				seqMu.Unlock()
			}
		}()
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
			t.Fatalf("timeout: counts = %v, want %d each",
				[]int32{counts[0].Load(), counts[1].Load(), counts[2].Load()}, total)
		case <-time.After(20 * time.Millisecond):
		}
	}
	close(stop)
	pumpWG.Wait()
}
