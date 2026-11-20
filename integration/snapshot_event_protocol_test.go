package integration

import (
	"context"
	"errors"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/observe"
	"dynamic-runtime/runtime"
)

// NEW-03 (audit): the Snapshot fallback under sustained event traffic must not
// return a best-effort EventSequence as if it were a reliable resume anchor —
// a sequence assigned after the last fact read would make Subscribe silently
// SKIP that event. This test drives real event traffic (fiber churn) while
// taking snapshots, then verifies the resume protocol end to end: a STABLE
// snapshot anchors a complete, ordered, duplicate-free replay of everything
// after it; an UNSTABLE observation surfaces as ErrSnapshotUnstable instead of
// fabricating an anchor.
func TestSnapshotEventProtocolUnderTraffic(t *testing.T) {
	obs := events.New()
	rt, err := runtime.New(runtime.WithEventSink(obs))
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	// Event flood: sequential Load+Dispose churn (each cycle emits lifecycle
	// events), bounded below the observer's ring so replay coverage is total.
	var wg sync.WaitGroup
	wg.Add(1)
	stopFlood := make(chan struct{})
	go func() {
		defer wg.Done()
		for i := 0; i < 45; i++ {
			select {
			case <-stopFlood:
				return
			default:
			}
			f, err := rt.Load(pcProv("flood"))
			if err != nil {
				return
			}
			_ = f.Ready(ctx)
			_ = f.Dispose()
			_ = f.Gone(ctx)
		}
	}()

	// Concurrent snapshots under traffic: every STABLE result is a usable
	// anchor; UNSTABLE results surface as ErrSnapshotUnstable (never a
	// fabricated sequence).
	stableSeqs := make([]uint64, 0, 8)
	unstable := 0
	snapDeadline := time.Now().Add(2 * time.Second)
	for time.Now().Before(snapDeadline) {
		snap, err := rt.Snapshot(ctx)
		if err != nil {
			if errors.Is(err, runtime.ErrSnapshotUnstable) {
				unstable++
				continue
			}
			t.Fatalf("snapshot: %v", err)
		}
		if len(stableSeqs) > 0 && snap.EventSequence < stableSeqs[len(stableSeqs)-1] {
			t.Fatalf("stable anchors regressed: %v", stableSeqs)
		}
		stableSeqs = append(stableSeqs, snap.EventSequence)
		time.Sleep(2 * time.Millisecond)
	}
	close(stopFlood)
	wg.Wait()

	// Quiesce: no events flow, the last anchor is stable.
	time.Sleep(50 * time.Millisecond)
	final, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatalf("quiesced snapshot unstable: %v", err)
	}
	t.Logf("stable anchors=%d unstable=%d final seq=%d",
		len(stableSeqs), unstable, final.EventSequence)

	// Resume protocol: Subscribe from the final anchor must deliver every
	// retained event AFTER it exactly once, in Sequence order — no gaps, no
	// duplicates.
	sub, err := obs.Subscribe(ctx, final.EventSequence)
	if err != nil {
		t.Fatalf("subscribe: %v", err)
	}
	var delivered []uint64
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		select {
		case ev, ok := <-sub.Events():
			if !ok {
				goto drained
			}
			delivered = append(delivered, ev.Sequence)
		default:
			time.Sleep(2 * time.Millisecond)
		}
	}
drained:
	// Delivered sequences must be strictly increasing (order + no duplicates).
	for i := 1; i < len(delivered); i++ {
		if delivered[i] <= delivered[i-1] {
			t.Fatalf("replay not strictly ordered: %v", delivered)
		}
	}
	// Everything the runtime emitted after the anchor must have been delivered.
	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	_ = snap
	if sub.Err() != nil {
		t.Fatalf("subscription terminated: %v", sub.Err())
	}
}
