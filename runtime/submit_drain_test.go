package runtime

import (
	"context"
	"testing"
	"time"
)

// RT-01 regression: a command that submit() accepted must ALWAYS be applied.
// The old shutdown path closed stop and returned without draining the buffer,
// so a caller that saw submit()==true could wait forever on a reply that never
// comes (Snapshot/Revise/Rehome all submit reply-carrying commands).
//
// Choreography (fully deterministic, no sleeps):
//  1. a blocking command occupies the orchestrator goroutine — it provably
//     cannot receive, so the buffer state below is stable;
//  2. cmdClose is enqueued synchronously, then the buffer is filled exactly to
//     capacity with barrier commands (all fast-path admits);
//  3. one more barrier submission MUST park in the slow path (buffer full,
//     orchestrator stuck) while holding the admission lock;
//  4. the blocker is released: cmdClose runs, the runtime has zero fibers so
//     the shutdown drain fires — it must apply cmdClose AND every queued
//     barrier, and its receiving loop is what unblocks the parked submitter;
//  5. after the halt gate, submit deterministically rejects.
type submitBlockCmd struct {
	entered chan struct{}
	release chan struct{}
}

func (c *submitBlockCmd) apply(o *orchestrator) {
	close(c.entered)
	<-c.release
}

type submitBarrierCmd struct{ done chan struct{} }

func (c *submitBarrierCmd) apply(o *orchestrator) { close(c.done) }

func TestSubmitAcceptedCommandsAreAlwaysApplied(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	const bufCap = 256 // orchestrator command buffer capacity

	blocker := &submitBlockCmd{entered: make(chan struct{}), release: make(chan struct{})}
	if !rt.submit(blocker) {
		t.Fatal("blocker submit rejected on a live runtime")
	}
	<-blocker.entered // orchestrator goroutine is now inside the blocker

	// cmdClose lands first, synchronously, so the shutdown decision is queued
	// deterministically before the buffer is filled.
	if !rt.submit(&cmdClose{}) {
		t.Fatal("cmdClose submit rejected on a live runtime")
	}

	// Fill the buffer to capacity while the orchestrator is provably stuck.
	barriers := make([]chan struct{}, 0, bufCap)
	for i := 0; i < bufCap-1; i++ {
		done := make(chan struct{})
		barriers = append(barriers, done)
		if ok := rt.submit(&submitBarrierCmd{done: done}); !ok {
			t.Fatalf("barrier %d rejected on a live runtime with a free buffer", i)
		}
	}

	// One more barrier cannot fit: its submission parks in the slow path
	// holding the admission lock. Whether it is ultimately admitted (during the
	// drain) or rejected (after the halt) is a correct race — but if admitted,
	// it MUST be applied.
	lateDone := make(chan struct{})
	lateAdmitted := make(chan bool, 1)
	go func() {
		lateAdmitted <- rt.submit(&submitBarrierCmd{done: lateDone})
	}()

	close(blocker.release) // orchestrator resumes: blocker -> cmdClose -> drain

	select {
	case <-rt.orch.done:
	case <-ctx.Done():
		t.Fatal("shutdown never completed — the drain stalled or a command was dropped")
	}
	for i, done := range barriers {
		select {
		case <-done:
		case <-ctx.Done():
			t.Fatalf("accepted barrier command %d was never applied (RT-01)", i)
		}
	}
	select {
	case admitted := <-lateAdmitted:
		if admitted {
			select {
			case <-lateDone:
			case <-ctx.Done():
				t.Fatal("late barrier reported admitted but was never applied")
			}
		}
	case <-ctx.Done():
		t.Fatal("late submission never returned")
	}

	if rt.stateSnapshot() != RuntimeClosed {
		t.Fatalf("runtime state after drain = %v, want RuntimeClosed", rt.stateSnapshot())
	}

	// After the halt gate, admission is deterministically rejected.
	if rt.submit(&submitBarrierCmd{done: make(chan struct{})}) {
		t.Fatal("submit accepted a command after shutdown halt")
	}
}
