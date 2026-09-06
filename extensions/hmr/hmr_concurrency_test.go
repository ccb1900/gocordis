package hmr_test

import (
	"errors"
	"fmt"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// P1/P3/P4 — repeated successful replacements: each new fiber is Active and
// fresh, old is Gone, and Loader usage is conserved (old released, new held).
func TestP1P3P4RepeatedReplacementConservation(t *testing.T) {
	e := newEnv(t)
	const versions = 5
	for i := 0; i < versions; i++ {
		src := fmt.Sprintf("builtin://cam-v%d", i)
		e.regFactory(t, src, fmt.Sprintf("v%d", i), &markerFactory{tag: fmt.Sprintf("v%d", i)})
	}

	// Install v0.
	oldMod := e.loadModule(t, artifact("cam-v0", "camera", "builtin://cam-v0", "0"))
	oldFiber, _ := e.install(t, "cam", "camera", oldMod, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))

	for i := 1; i < versions; i++ {
		a := artifact(fmt.Sprintf("cam-v%d", i), "camera", fmt.Sprintf("builtin://cam-v%d", i), fmt.Sprintf("%d", i))
		if err := e.ctrl.Replace(ctxT(t), target("cam", a)); err != nil {
			t.Fatalf("replace v%d: %v", i, err)
		}
		b, _ := e.ctrl.CurrentBinding("cam")
		if b.Fiber.State() != runtime.StateActive {
			t.Fatalf("new fiber v%d not Active", i)
		}
		if oldFiber.State() != runtime.StateGone {
			t.Fatalf("old fiber not Gone after replace v%d", i)
		}
		oldFiber = b.Fiber
		// Usage conservation: latest held; previous released & unloaded.
		if !e.ld.Usage().InUse(b.Module.ID) {
			t.Fatalf("module %s usage not held", b.Module.ID)
		}
		for j := 0; j < i; j++ {
			oldID := fmt.Sprintf("cam-v%d", j)
			if e.ld.Usage().InUse(oldID) {
				t.Fatalf("old module %s usage leaked", oldID)
			}
			if e.ld.Has(oldID) {
				t.Fatalf("old module %s still loaded", oldID)
			}
		}
	}
}

// P5 — target isolation under concurrency: a failing target never disturbs
// another target running concurrently.
func TestP5ConcurrentTargetIsolation(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://a-v1", "a1", &markerFactory{tag: "a1"})
	e.regFactory(t, "builtin://a-bad", "bad", &markerFactory{tag: "bad", applyFn: func(*runtime.Context) error { return errors.New("bad") }})
	e.regFactory(t, "builtin://b-v1", "b1", &markerFactory{tag: "b1"})
	e.regFactory(t, "builtin://b-v2", "b2", &markerFactory{tag: "b2"})

	modA := e.loadModule(t, artifact("a-v1", "camera", "builtin://a-v1", "1"))
	fiberA, _ := e.install(t, "A", "camera", modA, artifact("a-bad", "camera", "builtin://a-bad", "9"))
	modB := e.loadModule(t, artifact("b-v1", "camera", "builtin://b-v1", "1"))
	fiberB, _ := e.install(t, "B", "camera", modB, artifact("b-v2", "camera", "builtin://b-v2", "2"))

	var wg sync.WaitGroup
	var errA, errB error
	wg.Add(2)
	go func() {
		defer wg.Done()
		errA = e.ctrl.Replace(ctxT(t), target("A", artifact("a-bad", "camera", "builtin://a-bad", "9")))
	}()
	go func() {
		defer wg.Done()
		errB = e.ctrl.Replace(ctxT(t), target("B", artifact("b-v2", "camera", "builtin://b-v2", "2")))
	}()
	wg.Wait()

	if errA == nil {
		t.Fatal("A replacement should fail")
	}
	if errB != nil {
		t.Fatalf("B replacement failed: %v", errB)
	}
	if fiberA.State() != runtime.StateActive {
		t.Fatal("A old fiber not Active after its failure")
	}
	_ = fiberB
}

// P7 — Close race: no panic, no hang, no usage leak; after Close Replace is
// rejected.
func TestP7CloseRace(t *testing.T) {
	e := newEnv(t)
	e.regFactory(t, "builtin://cam-v1", "v1", &markerFactory{tag: "v1"})
	e.regFactory(t, "builtin://cam-v2", "v2", &markerFactory{tag: "v2"})
	oldMod := e.loadModule(t, artifact("cam-v1", "camera", "builtin://cam-v1", "1"))
	_, _ = e.install(t, "cam", "camera", oldMod, artifact("cam-v2", "camera", "builtin://cam-v2", "2"))

	var wg sync.WaitGroup
	var okReplace, closedReplace int32
	for g := 0; g < 4; g++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2")))
				if err == nil {
					atomic.AddInt32(&okReplace, 1)
					continue
				}
				if errors.Is(err, hmr.ErrHMRClosed) {
					atomic.AddInt32(&closedReplace, 1)
					return
				}
				if errors.Is(err, loader.ErrModuleExists) {
					// A concurrent replace already consumed v2; keep retrying a
					// valid serialized attempt until Close.
					continue
				}
				t.Errorf("unexpected replace error: %v", err)
				return
			}
		}()
	}
	time.Sleep(10 * time.Millisecond)
	if err := e.ctrl.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	wg.Wait()
	if okReplace == 0 {
		t.Fatal("no replacement succeeded before Close")
	}
	if closedReplace == 0 {
		t.Fatal("no replacement was rejected after Close")
	}
	if e.ld.Usage().InUse("cam-v1") {
		t.Fatal("usage leaked after Close race")
	}
	// Replace after close is deterministically rejected.
	if err := e.ctrl.Replace(ctxT(t), target("cam", artifact("cam-v2", "camera", "builtin://cam-v2", "2"))); !errors.Is(err, hmr.ErrHMRClosed) {
		t.Fatalf("Replace after Close = %v, want ErrHMRClosed", err)
	}
}
