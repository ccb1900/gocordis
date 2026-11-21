package runtime_test

import (
	"sync"
	"testing"
	"time"

	consoRuntime "dynamic-runtime/extensions/console/runtime"
	coreruntime "dynamic-runtime/runtime"
)

func syntheticEvent(seq uint64) coreruntime.RuntimeEvent {
	return coreruntime.RuntimeEvent{
		Sequence:     seq,
		Timestamp:    time.Unix(int64(seq), 0).UTC(),
		RuntimeID:    "runtime:ring-test",
		Type:         coreruntime.EventActivationActive,
		FiberID:      coreruntime.FiberID(seq),
		ActivationID: coreruntime.ActivationID(seq),
		Data:         coreruntime.ActivationEndedData{State: coreruntime.StateActive},
	}
}

func TestRingReceivesEventsAndIsBounded(t *testing.T) {
	ring := consoRuntime.NewRing(3)
	for i := uint64(1); i <= 10; i++ {
		ring.Emit(syntheticEvent(i))
	}
	events := ring.Events()
	if len(events) != 3 {
		t.Fatalf("ring kept %d events, want 3: %+v", len(events), events)
	}
	for i, ev := range events {
		want := uint64(8 + i) // oldest dropped: 8,9,10 survive
		if ev.Sequence != want {
			t.Fatalf("event %d sequence = %d, want %d", i, ev.Sequence, want)
		}
	}
	if got := ring.Sequence(); got != 10 {
		t.Fatalf("ring sequence = %d, want 10", got)
	}
}

func TestRingDTOShape(t *testing.T) {
	ring := consoRuntime.NewRing(2)
	ring.Emit(syntheticEvent(1))
	ring.Emit(syntheticEvent(2))
	dto := ring.DTO()
	if dto.Sequence != 2 {
		t.Fatalf("events sequence = %d, want 2", dto.Sequence)
	}
	if len(dto.Events) != 2 {
		t.Fatalf("events = %d, want 2", len(dto.Events))
	}
	e := dto.Events[0]
	if e.Sequence != 1 || e.RuntimeID != "runtime:ring-test" || e.Type != string(coreruntime.EventActivationActive) {
		t.Fatalf("event dto = %+v", e)
	}
	if e.FiberID != 1 || e.ActivationID != 1 || e.Timestamp == "" || e.Data == nil {
		t.Fatalf("event dto fields incomplete: %+v", e)
	}
}

func TestRingConcurrentEmitIsRaceFree(t *testing.T) {
	ring := consoRuntime.NewRing(64)
	var wg sync.WaitGroup
	for w := 0; w < 8; w++ {
		wg.Add(1)
		go func(w int) {
			defer wg.Done()
			for i := uint64(1); i <= 50; i++ {
				ring.Emit(syntheticEvent(uint64(w)*1000 + i))
			}
		}(w)
	}
	wg.Wait()
	if len(ring.Events()) != 64 {
		t.Fatalf("ring capacity not respected under concurrency: %d", len(ring.Events()))
	}
}
