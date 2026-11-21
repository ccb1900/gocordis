// Package runtime is the console backend for the Runtime Inspector and the
// Framework Dashboard. It reads the Runtime only through Runtime.Snapshot and
// receives canonical lifecycle events through a bounded in-memory EventSink.
// No kernel pointer, channel or mutex crosses the hub DTO boundary.
package runtime

import (
	"sync"

	coreruntime "dynamic-runtime/runtime"
)

// DefaultRingCapacity is used when a non-positive capacity is requested.
const DefaultRingCapacity = 256

// Ring is a bounded, concurrency-safe EventSink. The kernel calls Emit inline
// on orchestrator and activation goroutines, so Emit never blocks and never
// allocates: when the ring is full the oldest event is overwritten. Retention,
// replay and fan-out are consumer concerns; the kernel neither buffers nor
// knows about subscribers.
type Ring struct {
	mu    sync.Mutex
	buf   []coreruntime.RuntimeEvent
	head  int // index of the oldest event
	count int
	last  uint64 // highest sequence observed (0 when none)
}

// NewRing returns an empty ring holding at most capacity events.
func NewRing(capacity int) *Ring {
	if capacity <= 0 {
		capacity = DefaultRingCapacity
	}
	return &Ring{buf: make([]coreruntime.RuntimeEvent, capacity)}
}

// Emit implements runtime.EventSink. It is non-blocking and sequence-monotonic.
func (r *Ring) Emit(ev coreruntime.RuntimeEvent) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if len(r.buf) == 0 {
		return
	}
	if r.count < len(r.buf) {
		r.buf[(r.head+r.count)%len(r.buf)] = ev
		r.count++
	} else {
		r.buf[r.head] = ev
		r.head = (r.head + 1) % len(r.buf)
	}
	if ev.Sequence > r.last {
		r.last = ev.Sequence
	}
}

// Events returns a copy of the retained events, oldest first.
func (r *Ring) Events() []coreruntime.RuntimeEvent {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]coreruntime.RuntimeEvent, 0, r.count)
	for i := 0; i < r.count; i++ {
		out = append(out, r.buf[(r.head+i)%len(r.buf)])
	}
	return out
}

// Sequence returns the highest event sequence observed, or 0 when empty.
func (r *Ring) Sequence() uint64 {
	r.mu.Lock()
	defer r.mu.Unlock()
	return r.last
}

// DTO returns one coherent snapshot of the ring contents and its watermark.
func (r *Ring) DTO() EventsDTO {
	r.mu.Lock()
	defer r.mu.Unlock()
	events := make([]EventDTO, 0, r.count)
	for i := 0; i < r.count; i++ {
		events = append(events, eventDTO(r.buf[(r.head+i)%len(r.buf)]))
	}
	return EventsDTO{Events: events, Sequence: r.last}
}

var _ coreruntime.EventSink = (*Ring)(nil)
