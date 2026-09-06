// Package event provides the Runtime Event Extension: a general publish /
// subscribe facility for "something happened" notifications.
//
// Boundary (spec §2):
//
//	Kernel Dependency : "I need this capability to exist"
//	Registry          : "I need a dynamic member set"
//	Event             : "something happened, notify me"
//
// An Event Subscription is NOT a Runtime Dependency and must never be used to
// simulate one (e.g. provider disappearance must be handled by the Kernel).
//
// Event is an Extension, not part of the Kernel:
//   - this package does not import the runtime package;
//   - a Bus never creates Fibers, Activations, or Contexts;
//   - there is no second Kernel lifecycle (a Subscription is only a resource
//     with an internal Active/Closed state);
//   - delivery is channel-based; the Bus never executes subscriber code.
//
// v0.1 semantics (all documented):
//   - future events only (no replay), exact EventType match (no wildcard),
//     no predicate filters, no persistence, no global singleton, no polling;
//   - per-subscription FIFO order matching Publish sequence; no cross-
//     subscription ordering guarantee;
//   - bounded per-subscription buffer (default 64); a slow subscriber's events
//     are dropped (per-subscription loss) and never block Publish;
//   - Subscription.Close and Bus.Close are idempotent and are synchronized so
//     a closed channel can never receive a concurrent send (no recover()).
package event

import (
	"errors"
	"sync"
	"time"
)

// Errors. Compare with errors.Is.
var (
	// ErrBusClosed is returned by Publish/Subscribe after Bus.Close.
	ErrBusClosed = errors.New("event bus closed")
	// ErrSubscriptionClosed is the reserved error for operations targeting a
	// closed Subscription. Close itself is idempotent and returns nil.
	ErrSubscriptionClosed = errors.New("subscription closed")
)

// EventType is the exact-match event type identity. It is plain data, NOT a
// Kernel Capability Key.
type EventType string

// Sequence is the Bus-internal unique publish order number. It is used to
// define per-subscription ordering only; it is not exposed to subscribers.
type Sequence uint64

// Event is a fact that has happened. The Bus does not copy, validate, or own
// the Payload: its concurrency safety and lifetime are the responsibility of
// the publisher and the subscriber.
type Event struct {
	Type      EventType
	Payload   any
	Timestamp time.Time
}

// defaultBuffer is the bounded per-subscription delivery buffer.
const defaultBuffer = 64

// Bus is a general publish/subscribe event bus instance.
//
// All public operations (Publish, Subscribe, Close) have a single
// linearization point on the Bus mutex.
type Bus struct {
	mu sync.Mutex

	closed  bool
	nextSeq Sequence

	subscribers map[EventType]map[*Subscription]struct{}

	closeOnce sync.Once
}

// New creates an empty, running Bus.
func New() *Bus {
	return &Bus{
		subscribers: make(map[EventType]map[*Subscription]struct{}),
	}
}

// Publish assigns the next unique sequence and delivers the event to every
// currently subscribed Subscription of event.Type.
//
// Delivery is performed inside the Bus critical section with non-blocking
// channel sends:
//   - per-subscription delivery order equals Publish sequence order, even
//     under concurrent publishers (§14);
//   - a slow subscriber can never block Publish (its events are dropped when
//     its bounded buffer is full) (§11/§12);
//   - no subscriber user code executes under the lock (§39).
//
// Publish returns ErrBusClosed after Bus.Close. Success means the event
// entered the Bus's visible sequence - never that any subscriber has processed
// it (§9).
func (b *Bus) Publish(event Event) error {
	b.mu.Lock()
	defer b.mu.Unlock()

	if b.closed {
		return ErrBusClosed
	}

	b.nextSeq++

	for s := range b.subscribers[event.Type] {
		select {
		case s.events <- event:
		default:
			// per-subscription loss: this subscriber's buffer is full
		}
	}
	return nil
}

// Subscribe registers a new Subscription for exact EventType matches.
//
// The subscription's linearization point is the Bus-mutex critical section:
// Publish calls that linearize after Subscribe may deliver to it; Publish
// calls that linearize before Subscribe never do. Subscribe returns
// ErrBusClosed after Bus.Close.
func (b *Bus) Subscribe(eventType EventType) (*Subscription, error) {
	b.mu.Lock()
	defer b.mu.Unlock()

	if b.closed {
		return nil, ErrBusClosed
	}

	s := &Subscription{
		bus:       b,
		eventType: eventType,
		events:    make(chan Event, defaultBuffer),
	}
	m := b.subscribers[eventType]
	if m == nil {
		m = make(map[*Subscription]struct{})
		b.subscribers[eventType] = m
	}
	m[s] = struct{}{}
	return s, nil
}

// Close shuts the Bus down. It is idempotent.
//
// After Close:
//   - Publish and Subscribe return ErrBusClosed;
//   - every Subscription's Events channel is closed (buffered events may still
//     be drained first; v0.1 does not guarantee draining);
//   - Close does not wait for subscribers to consume events.
func (b *Bus) Close() error {
	b.closeOnce.Do(func() {
		b.mu.Lock()
		b.closed = true
		var subs []*Subscription
		for _, m := range b.subscribers {
			for s := range m {
				subs = append(subs, s)
			}
		}
		b.subscribers = make(map[EventType]map[*Subscription]struct{})
		b.mu.Unlock()

		for _, s := range subs {
			s.closeOnce.Do(func() { close(s.events) })
		}
	})
	return nil
}

// Subscription receives events of one EventType for one subscriber.
//
// A Subscription is a resource with only an internal Active/Closed state - it
// is NOT a Kernel Fiber lifecycle. It must be released with Close; v0.1 does
// not auto-GC forgotten subscriptions.
type Subscription struct {
	bus       *Bus
	eventType EventType
	events    chan Event

	closeOnce sync.Once
}

// Events returns the delivery channel. After Close it is closed (buffered
// events remain readable until drained).
func (s *Subscription) Events() <-chan Event { return s.events }

// Close releases the subscription. It is idempotent and returns nil.
//
// Synchronization guarantee (§47): the subscription is removed from the Bus
// subscriber map under the Bus mutex BEFORE its channel is closed, and all
// sends happen only to subscriptions still present in the map under the Bus
// mutex. Therefore a closed channel can never receive a concurrent send - no
// recover() is needed.
func (s *Subscription) Close() error {
	b := s.bus

	b.mu.Lock()
	if !b.closed {
		if m := b.subscribers[s.eventType]; m != nil {
			delete(m, s)
			if len(m) == 0 {
				delete(b.subscribers, s.eventType)
			}
		}
	}
	b.mu.Unlock()

	s.closeOnce.Do(func() { close(s.events) })
	return nil
}
