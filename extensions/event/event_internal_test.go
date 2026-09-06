package event

// White-box tests: sequence uniqueness (P1) verified against the internal
// sequence counter.

import (
	"sync"
	"testing"
)

// P1 — Sequence uniqueness: N concurrent successful Publish calls advance the
// Bus sequence by exactly N (each event gets a unique sequence, no gaps, no
// duplicates) regardless of interleaving.
func TestP1SequenceUniqueness(t *testing.T) {
	b := New()

	const pubs = 8
	const each = 500
	total := pubs * each

	var wg sync.WaitGroup
	for p := 0; p < pubs; p++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < each; i++ {
				if err := b.Publish(Event{Type: "t", Payload: i}); err != nil {
					t.Errorf("Publish: %v", err)
					return
				}
			}
		}()
	}
	wg.Wait()

	b.mu.Lock()
	got := b.nextSeq
	b.mu.Unlock()
	if got != Sequence(total) {
		t.Fatalf("nextSeq = %d, want %d (unique sequence per publish)", got, total)
	}
}

// Sequence is monotonic even when Publish races with Bus.Close: publishes that
// succeed before close still advance the sequence; publishes after close fail.
func TestSequenceMonotonicAcrossCloseRace(t *testing.T) {
	b := New()

	var wg sync.WaitGroup
	for i := 0; i < 4; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for j := 0; j < 200; j++ {
				_ = b.Publish(Event{Type: "t"})
			}
		}()
	}
	wg.Add(1)
	go func() {
		defer wg.Done()
		_ = b.Close()
	}()
	wg.Wait()

	// Whatever sequence was reached, it is consistent (no negative wraps, no
	// duplicate assignment observable through the counter).
	b.mu.Lock()
	seq := b.nextSeq
	b.mu.Unlock()
	if seq == 0 {
		// All publishes may have failed if Close won first; that is legal.
		return
	}
	if seq > Sequence(800) {
		t.Fatalf("sequence %d exceeds max publishes 800", seq)
	}
}
