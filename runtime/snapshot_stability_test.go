package runtime

import (
	"context"
	"errors"
	"testing"
	"time"
)

// NEW-03: under SUSTAINED event traffic, a snapshot must fail loudly with
// ErrSnapshotUnstable instead of returning a best-effort EventSequence that
// anchors consumers past events they never observed (silent replay loss).
func TestSnapshotUnstableUnderSustainedEventFlood(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	stop := make(chan struct{})
	go func() {
		for {
			select {
			case <-stop:
				return
			default:
			}
			// In-package flood: emitEvent is the canonical producer the
			// snapshot watermark must stay consistent with.
			rt.emitEvent(EventActivationActive, 0, 0, nil)
		}
	}()

	// The snapshot loop runs the whole flood window: with continuous
	// emission, every retry window sees sequence movement, so at least one
	// ErrSnapshotUnstable must surface.
	var sawUnstable, sawStable bool
	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		_, err := rt.Snapshot(ctx)
		switch {
		case err == nil:
			sawStable = true
		case errors.Is(err, ErrSnapshotUnstable):
			sawUnstable = true
		default:
			stop <- struct{}{}
			t.Fatalf("unexpected snapshot error: %v", err)
		}
		if sawUnstable && sawStable {
			break
		}
		time.Sleep(time.Millisecond)
	}
	close(stop)

	if !sawUnstable {
		t.Fatal("sustained flood never produced ErrSnapshotUnstable — the fallback fabricates anchors")
	}
	// A stable snapshot may interleave when the flood goroutine is descheduled;
	// both outcomes are honest. After the flood stops, snapshots must be stable.
	time.Sleep(20 * time.Millisecond)
	if _, err := rt.Snapshot(ctx); err != nil {
		t.Fatalf("snapshot after flood: %v", err)
	}
}
