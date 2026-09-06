package loader_test

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/loader"
)

func bgCtx() context.Context { return context.Background() }

// P1 — Concurrent Load of different Module IDs: all succeed.
func TestP1ConcurrentLoadDifferentIDs(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://f", staticBuiltin("f"))

	const n = 16
	var wg sync.WaitGroup
	errs := make([]error, n)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			id := fmt.Sprintf("m%d", i)
			_, errs[i] = l.Load(bgCtx(), art(id, "t", "builtin://f", ""))
		}(i)
	}
	wg.Wait()
	for i, err := range errs {
		if err != nil {
			t.Fatalf("load m%d failed: %v", i, err)
		}
	}
	if len(l.Snapshot()) != n {
		t.Fatalf("registry has %d modules, want %d", len(l.Snapshot()), n)
	}
}

// P2 — Concurrent Load of the same ID: exactly one succeeds.
func TestP2ConcurrentSameID(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://f", staticBuiltin("f"))

	const n = 16
	var wg sync.WaitGroup
	var okCount int32
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			if _, err := l.Load(bgCtx(), art("same", "t", "builtin://f", "")); err == nil {
				atomic.AddInt32(&okCount, 1)
			} else if !errors.Is(err, loader.ErrModuleExists) {
				t.Errorf("unexpected error: %v", err)
			}
		}()
	}
	wg.Wait()
	if okCount != 1 {
		t.Fatalf("successful loads = %d, want exactly 1", okCount)
	}
	if !l.Has("same") {
		t.Fatal("module not registered")
	}
}

// P3 — Concurrent Load/Unload of the same ID: registry stays consistent with
// some legal serial order (no corruption, no race).
func TestP3LoadUnloadRace(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://f", staticBuiltin("f"))

	const iters = 200
	var wg sync.WaitGroup
	for g := 0; g < 4; g++ {
		wg.Add(1)
		go func(g int) {
			defer wg.Done()
			for i := 0; i < iters; i++ {
				id := fmt.Sprintf("id-%d", g%2) // two shared ids
				if g%2 == 0 {
					_, _ = l.Load(bgCtx(), art(id, "t", "builtin://f", ""))
				} else {
					_ = l.Unload(bgCtx(), id)
				}
			}
		}(g)
	}
	wg.Wait()

	// Registry must be internally consistent: Get agrees with Has, and a final
	// deterministic Unload of every present module succeeds.
	for _, o := range l.Snapshot() {
		if _, ok := l.Get(o.ID); !ok {
			t.Fatalf("Get(%s) missing while present in snapshot", o.ID)
		}
		if err := l.Unload(bgCtx(), o.ID); err != nil && !errors.Is(err, loader.ErrModuleNotFound) {
			t.Fatalf("final Unload(%s) = %v", o.ID, err)
		}
	}
}

// P4 — Usage race: balanced Acquire/Release conserves ownership.
func TestP4UsageRaceConservation(t *testing.T) {
	u := loader.NewModuleUsage()
	if err := u.Acquire("M", "seed"); err != nil {
		t.Fatal(err)
	}
	_ = u.Release("M", "seed")

	const owners = 8
	const rounds = 200
	var wg sync.WaitGroup
	var acqFail, relFail int32
	for o := 0; o < owners; o++ {
		wg.Add(1)
		go func(o int) {
			defer wg.Done()
			owner := fmt.Sprintf("o%d", o)
			for i := 0; i < rounds; i++ {
				if err := u.Acquire("M", owner); err != nil {
					atomic.AddInt32(&acqFail, 1)
					return
				}
				if err := u.Release("M", owner); err != nil {
					atomic.AddInt32(&relFail, 1)
					return
				}
			}
		}(o)
	}
	wg.Wait()
	if acqFail+relFail != 0 {
		t.Fatalf("acquire/release failures: %d/%d", acqFail, relFail)
	}
	if u.InUse("M") {
		t.Fatal("ownership leaked: module still in use after balanced acquire/release")
	}
}

// P5 — Load x Close race: no panic, no deadlock, no corruption. Loader
// goroutines keep loading until they observe ErrLoaderClosed, so loads
// necessarily race with Close.
func TestP5CloseRace(t *testing.T) {
	l := loader.NewBuiltinLoader()
	regBuiltin(t, l, "builtin://f", staticBuiltin("f"))

	var wg sync.WaitGroup
	var okLoads, closedLoads atomic.Int32
	var nextID atomic.Int32
	for g := 0; g < 6; g++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				id := fmt.Sprintf("c%d", nextID.Add(1))
				ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
				_, err := l.Load(ctx, art(id, "t", "builtin://f", ""))
				cancel()
				if err == nil {
					okLoads.Add(1)
					continue
				}
				if errors.Is(err, loader.ErrLoaderClosed) {
					closedLoads.Add(1)
					return
				}
				if errors.Is(err, context.DeadlineExceeded) {
					t.Error("Load hung during Close")
					return
				}
				t.Errorf("unexpected Load error: %v", err)
				return
			}
		}()
	}
	time.Sleep(5 * time.Millisecond) // let loads start
	if err := l.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	wg.Wait()
	if okLoads.Load() == 0 {
		t.Fatal("no load succeeded before Close")
	}
	if closedLoads.Load() == 0 {
		t.Fatal("no load was rejected after Close")
	}
	if len(l.Snapshot()) != 0 {
		t.Fatalf("registry not empty after Close: %+v", l.Snapshot())
	}
}

// P6 — Snapshot race with concurrent mutation: no data race; snapshots remain
// consistent views.
func TestP6SnapshotRace(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://f", staticBuiltin("f"))

	var wg sync.WaitGroup
	stop := make(chan struct{})
	// Mutator.
	wg.Add(1)
	go func() {
		defer wg.Done()
		i := 0
		for {
			select {
			case <-stop:
				return
			default:
			}
			id := fmt.Sprintf("s%d", i%8)
			_ = l.Unload(bgCtx(), id)
			_, _ = l.Load(bgCtx(), art(id, "t", "builtin://f", ""))
			i++
		}
	}()
	// Snapshot readers.
	for r := 0; r < 4; r++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for {
				select {
				case <-stop:
					return
				default:
				}
				snap := l.Snapshot()
				seen := map[string]bool{}
				for _, m := range snap {
					if seen[m.ID] {
						t.Error("duplicate id in snapshot")
						return
					}
					seen[m.ID] = true
				}
			}
		}()
	}
	time.Sleep(30 * time.Millisecond)
	close(stop)
	wg.Wait()
}

// P8 — Registry linearizability: the observable state after a concurrent
// operation mix is equivalent to some legal serial execution.
func TestP8Linearizability(t *testing.T) {
	l := newLoader(t)
	regBuiltin(t, l, "builtin://f", staticBuiltin("f"))

	var wg sync.WaitGroup
	// Two ids; each goroutine performs an independent serial load/unload
	// sequence. Any interleaving must yield a registry whose state is a valid
	// serial outcome: for each id the final presence matches the last operation
	// on that id in ITS goroutine only if it linearized after the other.
	for g := 0; g < 4; g++ {
		wg.Add(1)
		go func(g int) {
			defer wg.Done()
			for i := 0; i < 100; i++ {
				id := fmt.Sprintf("lin-%d", g%2)
				_, _ = l.Load(bgCtx(), art(id, "t", "builtin://f", ""))
				_ = l.Unload(bgCtx(), id)
			}
		}(g)
	}
	wg.Wait()

	// Whatever remains must be a consistent registry: every id present exactly
	// once, and a deterministic cleanup must reach an empty state.
	seen := map[string]bool{}
	for _, m := range l.Snapshot() {
		if seen[m.ID] {
			t.Fatalf("duplicate id %q in registry", m.ID)
		}
		seen[m.ID] = true
	}
	for id := range seen {
		if err := l.Unload(bgCtx(), id); err != nil {
			t.Fatalf("cleanup Unload(%s): %v", id, err)
		}
	}
	if len(l.Snapshot()) != 0 {
		t.Fatalf("registry not empty after cleanup: %+v", l.Snapshot())
	}
}

// Extra: Load honors context cancellation without mutating the registry.
func TestLoadContextCancellation(t *testing.T) {
	l := newLoader(t)
	cancelled, cancel := context.WithCancel(context.Background())
	cancel()
	_, err := l.Load(cancelled, art("x", "t", "builtin://f", ""))
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("Load with canceled ctx = %v, want context.Canceled", err)
	}
	if l.Has("x") {
		t.Fatal("canceled load mutated registry")
	}
}
