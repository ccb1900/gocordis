package events

import (
	"context"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// EXT-01 P1 regression tests.

// TestSubscriptionCloseWhilePumpBlockedDoesNotPanic — EXT-01 bug 1: halt()
// closed s.ch while the pump could be parked in `select { case s.ch <- ev: ...
// case <-s.stop: }`. Go's select may pick the SEND branch even though stop is
// also closed, panicking on send-to-closed-channel. Each round has both cases
// ready at halt time, so the old code panics within a few rounds; the fix
// (only the pump closes the channel) never panics.
func TestSubscriptionCloseWhilePumpBlockedDoesNotPanic(t *testing.T) {
	for round := 0; round < 50; round++ {
		obs := New()
		sub, err := obs.Subscribe(context.Background(), 0)
		if err != nil {
			t.Fatal(err)
		}
		// The consumer never reads: after subscriptionBuffer events the pump
		// parks in the send branch, holding the race window open.
		for i := 0; i < subscriptionBuffer+64; i++ {
			obs.Emit(runtime.RuntimeEvent{Type: "Fill", Sequence: uint64(i + 1)})
		}
		// Give the pump time to park in the send before closing.
		deadline := time.Now().Add(2 * time.Second)
		for time.Now().Before(deadline) {
			sub.mu.Lock()
			backlog := len(sub.queue)
			sub.mu.Unlock()
			if backlog > subscriptionBuffer/2 {
				break
			}
			time.Sleep(time.Millisecond)
		}
		sub.Close()
		// Drain until closed (the pump must close the channel exactly once).
		for range sub.Events() {
		}
		if err := sub.Err(); err != nil {
			t.Fatalf("round %d: explicit Close must report nil Err, got %v", round, err)
		}
	}
}

// TestSubscriptionBacklogClosesWithOverflow — EXT-01 bug 2: the queue limit
// was only checked at pump POP time; a consumer that stops reading parks the
// pump in the send and the spool grows WITHOUT BOUND, contradicting the
// documented "past queueLimit the subscription CLOSES" contract. The limit
// must be enforced at enqueue time.
func TestSubscriptionBacklogClosesWithOverflow(t *testing.T) {
	obs := New()
	sub, err := obs.Subscribe(context.Background(), 0)
	if err != nil {
		t.Fatal(err)
	}
	// The consumer never reads: the delivery buffer (256) plus one in-flight
	// event absorb part of the flood, so the flood must exceed
	// queueLimit + subscriptionBuffer to overflow the SPOOL.
	for i := 0; i < queueLimit+subscriptionBuffer+100; i++ {
		obs.Emit(runtime.RuntimeEvent{Type: "Flood", Sequence: uint64(i + 1)})
	}
	// The subscription must terminate with the overflow cause and leave the
	// observer's subscriber set.
	deadline := time.Now().Add(5 * time.Second)
	for time.Now().Before(deadline) {
		if err := sub.Err(); err != nil {
			break
		}
		time.Sleep(time.Millisecond)
	}
	if err := sub.Err(); err == nil {
		t.Fatal("slow consumer did not terminate the subscription (unbounded spool)")
	}
	// Draining must reach a closed channel without hanging.
	for range sub.Events() {
	}
}
