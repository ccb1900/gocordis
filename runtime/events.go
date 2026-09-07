package runtime

import (
	"sync"
	"time"
)

// Canonical Runtime event stream (UI-02 foundation; subscription arrives in
// UI-03).
//
// Events are NOT a logging system. Every event is produced at a canonical
// lifecycle decision/registration point and represents one Runtime state
// transition (fiber/activation lifecycle, provider publish/withdraw,
// dependency capture, effect lifecycle, failure). The Developer Console and
// future subscription layers derive their view from these events plus
// Snapshot — never from guessing over arbitrary logs.
//
// Sequence is monotonically increasing and is the ordering authority
// (Timestamps are informational). A Snapshot carries EventSequence so a
// reconnecting consumer can resume from the exact point the Snapshot was
// linearized.

// EventType is the canonical runtime event kind.
type EventType string

const (
	// EventFiberCreated fires when a Fiber is registered (Runtime.Load root or
	// ctx.Child spawn).
	EventFiberCreated EventType = "FiberCreated"
	// EventActivationLoading fires when an activation starts (Apply running).
	EventActivationLoading EventType = "ActivationLoading"
	// EventActivationActive fires when an activation becomes Active.
	EventActivationActive EventType = "ActivationActive"
	// EventActivationUnloading fires when an activation begins to unwind.
	EventActivationUnloading EventType = "ActivationUnloading"
	// EventActivationEnded fires when an activation has fully ended; Data is
	// ActivationEndedData{State, Err}.
	EventActivationEnded EventType = "ActivationEnded"
	// EventDependencyCaptured fires when an activation's dependency snapshot is
	// captured at Loading; Data is []DependencyData (one per required key).
	EventDependencyCaptured EventType = "DependencyCaptured"
	// EventProviderPublished fires when a provider record is registered.
	// Data is ProviderEventData{Key}.
	EventProviderPublished EventType = "ProviderPublished"
	// EventProviderWithdrawn fires when a provider record is removed without
	// having been previously retired (the record was never satisfiable, e.g.
	// failed-Apply cleanup). Data is ProviderEventData{Key}.
	EventProviderWithdrawn EventType = "ProviderWithdrawn"
	// EventEffectCommitted / EventEffectUndoing / EventEffectUndone track the
	// two-phase effect lifecycle. Data is EffectEventData{Key,Kind}.
	EventEffectCommitted EventType = "EffectCommitted"
	EventEffectUndoing   EventType = "EffectUndoing"
	EventEffectUndone    EventType = "EffectUndone"
	// EventFailure fires when an activation fails during Apply or Cleanup.
	// Data is FailureEventData{Phase, Err}.
	EventFailure EventType = "Failure"
)

// RuntimeEvent is one immutable, append-oriented timeline fact.
type RuntimeEvent struct {
	Sequence     uint64
	Timestamp    time.Time
	RuntimeID    RuntimeID
	Type         EventType
	FiberID      FiberID
	ActivationID ActivationID
	// Data is a value-typed payload (see event data types below); never raw
	// internal state.
	Data any
}

// ProviderEventData accompanies provider publish/withdraw events.
type ProviderEventData struct {
	Key string
}

// EffectEventData accompanies effect lifecycle events.
type EffectEventData struct {
	Kind EffectKind
	Key  string
}

// DependencyData is one captured dependency binding.
type DependencyData struct {
	Key                  string
	ProviderFiberID      FiberID
	ProviderActivationID ActivationID
}

// ActivationEndedData is the terminal outcome of an activation.
type ActivationEndedData struct {
	State FiberState
	Err   string
}

// FailureEventData identifies the failing phase and error.
type FailureEventData struct {
	Phase string // "Apply" | "Cleanup"
	Err   string
}

// eventRingSize bounds the retained event history (UI-02 foundation; UI-03
// subscription replays from this ring).
const eventRingSize = 1024

// eventLog is the Runtime-owned canonical event store. Sequence assignment is
// serialized here so events from the orchestrator goroutine and from
// Apply/Unwind goroutines share one monotonic order.
type eventLog struct {
	mu sync.Mutex

	nextSeq uint64
	start   uint64 // Sequence of ring[0]; 0 when empty
	ring    []RuntimeEvent
}

func newEventLog() *eventLog {
	return &eventLog{ring: make([]RuntimeEvent, 0, eventRingSize)}
}

func (l *eventLog) emit(ev RuntimeEvent) uint64 {
	l.mu.Lock()
	defer l.mu.Unlock()
	l.nextSeq++
	ev.Sequence = l.nextSeq
	if l.nextSeq == 1 {
		l.start = 1
	}
	if len(l.ring) == cap(l.ring) {
		// Drop the oldest event; the ring stays a contiguous suffix.
		copy(l.ring, l.ring[1:])
		l.ring[len(l.ring)-1] = ev
		l.start++
		return ev.Sequence
	}
	l.ring = append(l.ring, ev)
	return ev.Sequence
}

// current returns the sequence of the last emitted event (0 when none).
func (l *eventLog) current() uint64 {
	l.mu.Lock()
	defer l.mu.Unlock()
	return l.nextSeq
}

// all returns a copy of every retained event in sequence order (test/console
// diagnostic access).
func (l *eventLog) all() []RuntimeEvent {
	l.mu.Lock()
	defer l.mu.Unlock()
	out := make([]RuntimeEvent, len(l.ring))
	copy(out, l.ring)
	return out
}

// emitEvent appends a canonical event. Safe from the orchestrator goroutine
// and from Apply/Unwind goroutines.
func (r *Runtime) emitEvent(t EventType, f FiberID, a ActivationID, data any) {
	if r.events == nil {
		return
	}
	r.events.emit(RuntimeEvent{
		Timestamp:    time.Now(),
		RuntimeID:    r.runtimeID,
		Type:         t,
		FiberID:      f,
		ActivationID: a,
		Data:         data,
	})
}
