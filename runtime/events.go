package runtime

import (
	"sync"
	"time"
)

// Canonical Runtime event vocabulary + sequence authority (paper-neutral
// observation surface, UI-02).
//
// Events are NOT a logging system. Every event is produced at a canonical
// lifecycle decision/registration point and represents one Runtime state
// transition (fiber/activation lifecycle, provider publish/withdraw,
// dependency capture, effect lifecycle, failure). Sequence is monotonically
// increasing and is the ordering authority (Timestamps are informational).
//
// The kernel's ENTIRE observation responsibility is: assign the monotonic
// Sequence and hand each event to the optional EventSink. Ring retention,
// replay, and subscription fan-out live in the consumer layer
// (console/events) — the kernel neither buffers observations nor knows about
// subscribers (paper-anchored surface: events appear in no calculus rule).

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
	// two-phase effect lifecycle. Data is EffectEventData{Kind, Key}.
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

// EventSink receives every canonical event in sequence order. Implementations
// MUST NOT block (the orchestrator and activation goroutines call it inline)
// and MUST NOT mutate the Runtime. Ring retention and subscriber management
// belong to the implementation (see console/events).
type EventSink interface {
	Emit(ev RuntimeEvent)
}

// WithEventSink attaches an observation sink (paper-neutral platform hook).
// nil disables event delivery — the sequence counter still advances so
// Snapshot.EventSequence stays a valid resume anchor for whatever stream the
// application feeds from its own source.
func WithEventSink(sink EventSink) Option {
	return func(o *options) {
		if sink != nil {
			o.eventSink = sink
		}
	}
}

// eventSeq is the monotonic sequence authority. The mutex makes
// sequence-assignment and sink delivery ONE atomic step, so sinks observe
// events in Sequence order even with concurrent emitters.
type eventSeq struct {
	mu sync.Mutex
	n  uint64
}

// current returns the sequence of the last emitted event (0 when none).
func (s *eventSeq) current() uint64 {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.n
}

// emitEvent appends a canonical event: assign the sequence and hand to the
// optional sink as one atomic step (sinks observe Sequence order). Safe from
// the orchestrator goroutine and from Apply/Unwind goroutines; the sink
// contract requires it not to block.
func (r *Runtime) emitEvent(t EventType, f FiberID, a ActivationID, data any) {
	r.evSeq.mu.Lock()
	defer r.evSeq.mu.Unlock()
	r.evSeq.n++
	ev := RuntimeEvent{
		Sequence:     r.evSeq.n,
		Timestamp:    time.Now(),
		RuntimeID:    r.runtimeID,
		Type:         t,
		FiberID:      f,
		ActivationID: a,
		Data:         data,
	}
	if r.eventSink != nil {
		r.eventSink.Emit(ev)
	}
}
