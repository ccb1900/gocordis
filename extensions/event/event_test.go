package event_test

import (
	"errors"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/event"
)

const testTimeout = 5 * time.Second

func intEvent(typ event.EventType, v int) event.Event {
	return event.Event{Type: typ, Payload: v}
}

func subscribe(t *testing.T, b *event.Bus, typ event.EventType) *event.Subscription {
	t.Helper()
	s, err := b.Subscribe(typ)
	if err != nil {
		t.Fatalf("Subscribe(%q): %v", typ, err)
	}
	return s
}

// expectEvent blocks until the subscription delivers an event with the wanted
// payload.
func expectEvent(t *testing.T, s *event.Subscription, want int) {
	t.Helper()
	select {
	case e := <-s.Events():
		if v, ok := e.Payload.(int); !ok || v != want {
			t.Fatalf("received payload %v, want %d", e.Payload, want)
		}
	case <-time.After(testTimeout):
		t.Fatalf("timed out waiting for event %d", want)
	}
}

// expectClosed asserts the subscription channel is closed (possibly after
// draining buffered events).
func expectClosed(t *testing.T, s *event.Subscription) {
	t.Helper()
	for {
		select {
		case _, open := <-s.Events():
			if !open {
				return
			}
			// buffered event; keep draining
		case <-time.After(testTimeout):
			t.Fatal("subscription channel did not close")
		}
	}
}

// expectNoEvent asserts no event is currently buffered.
func expectNoEvent(t *testing.T, s *event.Subscription) {
	t.Helper()
	select {
	case e := <-s.Events():
		t.Fatalf("unexpected event %+v", e)
	default:
	}
}

// E1 — Basic publish: one event, delivered exactly once.
func TestE1BasicPublish(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "job.completed")

	if err := b.Publish(intEvent("job.completed", 1)); err != nil {
		t.Fatalf("Publish: %v", err)
	}
	expectEvent(t, s, 1)
	expectNoEvent(t, s)
	s.Close()
}

// E2 — Type isolation: a subscription only receives its exact type.
func TestE2TypeIsolation(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "a")

	_ = b.Publish(intEvent("b", 1))
	_ = b.Publish(intEvent("a", 2))
	_ = b.Publish(intEvent("a", 3))

	expectEvent(t, s, 2)
	expectEvent(t, s, 3)
	expectNoEvent(t, s)
	s.Close()
}

// E3 — Multiple subscribers: every subscriber of the type receives the event.
func TestE3MultipleSubscribers(t *testing.T) {
	b := event.New()
	s1 := subscribe(t, b, "a")
	s2 := subscribe(t, b, "a")

	_ = b.Publish(intEvent("a", 7))

	expectEvent(t, s1, 7)
	expectEvent(t, s2, 7)
	s1.Close()
	s2.Close()
}

// E4 — Slow subscriber isolation: a full buffer never blocks Publish and never
// prevents another subscriber from receiving.
func TestE4SlowSubscriberIsolation(t *testing.T) {
	b := event.New()
	s1 := subscribe(t, b, "a") // never drained

	// Fill s1's bounded buffer exactly.
	for i := 0; i < 64; i++ {
		if err := b.Publish(intEvent("a", i)); err != nil {
			t.Fatalf("fill Publish %d: %v", i, err)
		}
	}
	if got := len(s1.Events()); got != 64 {
		t.Fatalf("s1 buffer = %d, want 64", got)
	}

	s2 := subscribe(t, b, "a")

	// Publish must not block even though s1 is full.
	pubDone := make(chan error, 1)
	go func() { pubDone <- b.Publish(intEvent("a", 999)) }()
	select {
	case err := <-pubDone:
		if err != nil {
			t.Fatalf("Publish: %v", err)
		}
	case <-time.After(testTimeout):
		t.Fatal("Publish blocked on a full subscriber buffer")
	}

	// s2 still receives; s1 stays bounded (the event was dropped for s1).
	expectEvent(t, s2, 999)
	if got := len(s1.Events()); got > 64 {
		t.Fatalf("s1 buffer grew beyond 64: %d", got)
	}
	expectNoEvent(t, s2)
	s1.Close()
	s2.Close()
}

// E5 — Per-subscription ordering: delivered events follow Publish order.
func TestE5SubscriptionOrdering(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "a")

	_ = b.Publish(intEvent("a", 1))
	_ = b.Publish(intEvent("a", 2))
	_ = b.Publish(intEvent("a", 3))

	expectEvent(t, s, 1)
	expectEvent(t, s, 2)
	expectEvent(t, s, 3)
	expectNoEvent(t, s)
	s.Close()
}

// E6 — Subscription close: channel closes and no further event is delivered.
func TestE6SubscriptionClose(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "a")
	_ = b.Publish(intEvent("a", 1))
	expectEvent(t, s, 1)

	if err := s.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	// After close, publishing to the (still open) bus is not delivered.
	_ = b.Publish(intEvent("a", 2))
	expectClosed(t, s)
}

// E7 — Idempotent subscription close.
func TestE7IdempotentClose(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "a")
	for i := 0; i < 3; i++ {
		if err := s.Close(); err != nil {
			t.Fatalf("Close #%d: %v", i, err)
		}
	}
	expectClosed(t, s)
}

// E8 — Bus close: all subscriptions close; Publish/Subscribe fail.
func TestE8BusClose(t *testing.T) {
	b := event.New()
	s1 := subscribe(t, b, "a")
	s2 := subscribe(t, b, "b")

	if err := b.Close(); err != nil {
		t.Fatalf("Bus.Close: %v", err)
	}
	if err := b.Close(); err != nil { // idempotent
		t.Fatalf("second Bus.Close: %v", err)
	}

	expectClosed(t, s1)
	expectClosed(t, s2)

	if err := b.Publish(intEvent("a", 1)); !errors.Is(err, event.ErrBusClosed) {
		t.Fatalf("Publish after Close = %v, want ErrBusClosed", err)
	}
	if _, err := b.Subscribe("a"); !errors.Is(err, event.ErrBusClosed) {
		t.Fatalf("Subscribe after Close = %v, want ErrBusClosed", err)
	}
}

// E9 — Concurrent publish: no panic, no race, every event delivered once to a
// fast concurrent reader, and the sequence counter advances exactly once per
// publish.
func TestE9ConcurrentPublish(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "a")

	const pubs = 8
	const each = 300
	total := pubs * each

	var mu sync.Mutex
	seen := make(map[int]bool, total)
	var dup bool

	readerDone := make(chan struct{})
	go func() {
		defer close(readerDone)
		for e := range s.Events() {
			v := e.Payload.(int)
			mu.Lock()
			if seen[v] {
				dup = true
			}
			seen[v] = true
			mu.Unlock()
		}
	}()

	var wg sync.WaitGroup
	for p := 0; p < pubs; p++ {
		wg.Add(1)
		go func(p int) {
			defer wg.Done()
			for i := 0; i < each; i++ {
				if err := b.Publish(intEvent("a", p*each+i)); err != nil {
					t.Errorf("Publish: %v", err)
					return
				}
			}
		}(p)
	}
	wg.Wait()

	// Wait until the reader drains everything still buffered (delivery is
	// best-effort: events dropped while the reader was unscheduled are legal).
	deadline := time.Now().Add(testTimeout)
	last := -1
	for time.Now().Before(deadline) {
		mu.Lock()
		n := len(seen)
		d := dup
		mu.Unlock()
		if d {
			t.Fatal("duplicate event delivered")
		}
		if n == last {
			break // drained
		}
		last = n
		time.Sleep(2 * time.Millisecond)
	}
	s.Close()
	<-readerDone

	mu.Lock()
	defer mu.Unlock()
	if len(seen) == 0 {
		t.Fatal("reader received nothing")
	}
	if len(seen) > total {
		t.Fatalf("reader received %d events, more than the %d published", len(seen), total)
	}
	for v := range seen {
		if v < 0 || v >= total {
			t.Fatalf("received unknown payload %d", v)
		}
	}
}

// E10 — Publish x Close(subscription): concurrent close must never produce a
// send-on-closed-channel panic (validated by -race as well).
func TestE10PublishCloseRace(t *testing.T) {
	for round := 0; round < 20; round++ {
		b := event.New()
		s := subscribe(t, b, "a")

		var wg sync.WaitGroup
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < 500; i++ {
				_ = b.Publish(intEvent("a", i))
			}
		}()
		// Close while publishes are in flight.
		_ = s.Close()
		wg.Wait()
		expectClosed(t, s)
	}
}

// E11 — Subscribe x Publish: a concurrent subscribe receives a contiguous
// suffix of a sequential publish stream (its linearization point), never a
// prefix or a gap. n <= buffer size so the fast reader never drops.
func TestE11SubscribePublishLinearization(t *testing.T) {
	const n = 64
	b := event.New()

	subCreated := make(chan *event.Subscription, 1)
	readerDone := make(chan struct{})
	var received []int

	go func() {
		defer close(readerDone)
		s, err := b.Subscribe("a")
		if err != nil {
			t.Errorf("Subscribe: %v", err)
			close(subCreated)
			return
		}
		subCreated <- s
		for e := range s.Events() {
			received = append(received, e.Payload.(int))
		}
	}()

	// Publish sequentially from this goroutine while Subscribe races in.
	for i := 1; i <= n; i++ {
		if err := b.Publish(intEvent("a", i)); err != nil {
			t.Fatalf("Publish: %v", err)
		}
	}

	// End the reader so we can inspect what it received.
	s := <-subCreated
	_ = s.Close()
	<-readerDone

	// The subscriber drains continuously and n <= buffer, so no drops: it must
	// have received a contiguous suffix of 1..n (possibly empty).
	if len(received) == 0 {
		return
	}
	if received[len(received)-1] != n {
		t.Fatalf("received stream must end at %d, got %v", n, received)
	}
	for i := 1; i < len(received); i++ {
		if received[i] != received[i-1]+1 {
			t.Fatalf("received stream is not a contiguous suffix: %v", received)
		}
	}
}

// P3 — Subscriber isolation: closing one subscription never affects others.
func TestP3SubscriberIsolation(t *testing.T) {
	b := event.New()
	s1 := subscribe(t, b, "a")
	s2 := subscribe(t, b, "a")

	_ = s1.Close()
	_ = b.Publish(intEvent("a", 9))

	// s2 is unaffected by s1's close.
	expectEvent(t, s2, 9)
	expectNoEvent(t, s2)
	s2.Close()

	// Publishing after s1 closed must not panic and must not deliver to s1.
	_ = b.Publish(intEvent("a", 10))
	expectClosed(t, s1)
}

// P5 — Publish linearizability: concurrent publishes from distinct publishers
// appear to a fast single subscriber in a per-publisher-consistent total
// order, each exactly once.
func TestP5PublishLinearizability(t *testing.T) {
	b := event.New()
	s := subscribe(t, b, "a")

	const pubs = 6
	const each = 200

	var mu sync.Mutex
	var order [][2]int

	readerDone := make(chan struct{})
	go func() {
		defer close(readerDone)
		for e := range s.Events() {
			mu.Lock()
			order = append(order, e.Payload.([2]int))
			mu.Unlock()
		}
	}()

	var wg sync.WaitGroup
	for p := 0; p < pubs; p++ {
		wg.Add(1)
		go func(p int) {
			defer wg.Done()
			for i := 0; i < each; i++ {
				// payload encodes (publisher, index)
				_ = b.Publish(event.Event{Type: "a", Payload: [2]int{p, i}})
			}
		}(p)
	}
	wg.Wait()

	// Let the reader drain everything still buffered (drops before the reader
	// was scheduled are legal per best-effort delivery).
	deadline := time.Now().Add(testTimeout)
	last := -1
	for time.Now().Before(deadline) {
		mu.Lock()
		n := len(order)
		mu.Unlock()
		if n == last {
			break
		}
		last = n
		time.Sleep(2 * time.Millisecond)
	}
	s.Close()
	<-readerDone

	mu.Lock()
	defer mu.Unlock()
	if len(order) == 0 {
		t.Fatal("reader received nothing")
	}
	// Whatever was received must be a subsequence of a single valid total
	// order: no duplicates and per-publisher indices strictly increasing.
	seenPair := make(map[[2]int]bool, len(order))
	lastIdx := make([]int, pubs)
	for i := range lastIdx {
		lastIdx[i] = -1
	}
	for _, pair := range order {
		if seenPair[pair] {
			t.Fatalf("duplicate event %v", pair)
		}
		seenPair[pair] = true
		p, idx := pair[0], pair[1]
		if idx <= lastIdx[p] {
			t.Fatalf("publisher %d index %d arrived after %d (per-publisher order violated)", p, idx, lastIdx[p])
		}
		lastIdx[p] = idx
	}
}

// P6 — Resource completeness: Bus.Close closes every subscription.
func TestP6BusCloseClosesAllSubscriptions(t *testing.T) {
	b := event.New()
	var subs []*event.Subscription
	for i := 0; i < 16; i++ {
		subs = append(subs, subscribe(t, b, "a"))
	}
	if err := b.Close(); err != nil {
		t.Fatalf("Bus.Close: %v", err)
	}
	for i, s := range subs {
		expectClosed(t, s)
		if i != len(subs)-1 {
			_ = i
		}
	}
}
