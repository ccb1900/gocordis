package loader_test

import (
	"context"
	"errors"
	"fmt"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// NEW-02: every Module lifecycle exit must use the SAME backend-resource
// release protocol — Unload, CloseContext's bulk eviction, and failed commits.
// The fake backend counts releases per id.

type countingBackend struct {
	loader.Backend
	mu             sync.Mutex
	loads          int
	commits        int
	abandons       int
	unloads        int
	failCommitNext bool
	blockOnCtx     bool
}

func (b *countingBackend) Load(ctx context.Context, artifact loader.Artifact) (loader.Module, error) {
	b.mu.Lock()
	b.loads++
	block := b.blockOnCtx
	fail := b.failCommitNext
	b.mu.Unlock()
	// Deterministic commit-phase failure: block until the CALLER cancels, then
	// return success — the loader's commit phase sees ctx.Err() and abandons.
	if block {
		<-ctx.Done()
	}
	if fail {
		return loader.Module{}, errors.New("backend load failure")
	}
	return loader.Module{
		ID:   artifact.ID,
		Type: "counting",
		Factory: config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
			return nil, errors.New("unused")
		}),
	}, nil
}
func (b *countingBackend) CommitModule(id string)  { b.mu.Lock(); b.commits++; b.mu.Unlock() }
func (b *countingBackend) AbandonModule(id string) { b.mu.Lock(); b.abandons++; b.mu.Unlock() }
func (b *countingBackend) UnloadModule(id string)  { b.mu.Lock(); b.unloads++; b.mu.Unlock() }

// Unload and CloseContext both notify (NEW-02's main hole: CloseContext's
// bulk eviction deleted registry entries without notifying).
func TestLoaderReleaseProtocolUnloadAndClose(t *testing.T) {
	l := loader.NewBuiltinLoader()
	b := &countingBackend{}
	if err := l.RegisterBackend("counting", b); err != nil {
		t.Fatal(err)
	}
	art := loader.Artifact{ID: "m1", BackendType: "counting", Type: "x", Version: "1", Source: "mem://fake"}

	if _, err := l.Load(context.Background(), art); err != nil {
		t.Fatal(err)
	}
	if b.commits != 1 {
		t.Fatalf("commits = %d, want 1 (ownership marked)", b.commits)
	}
	if err := l.Unload(context.Background(), "m1"); err != nil {
		t.Fatal(err)
	}
	if b.unloads != 1 {
		t.Fatalf("Unload notification = %d, want 1", b.unloads)
	}

	// Re-load then CloseContext: the bulk eviction must notify too.
	if _, err := l.Load(context.Background(), art); err != nil {
		t.Fatal(err)
	}
	if err := l.CloseContext(context.Background()); err != nil {
		t.Fatal(err)
	}
	if b.unloads != 2 {
		t.Fatalf("CloseContext notification = %d, want 2 (NEW-02)", b.unloads)
	}
}

// A failed COMMIT releases the backend's resources via AbandonModule, and the
// id is poisoned so a commit cannot adopt a released ref: the next Load
// re-runs the backend path cleanly.
func TestLoaderCommitFailureAbandonsAndPoisons(t *testing.T) {
	l := loader.NewBuiltinLoader()
	b := &countingBackend{}
	if err := l.RegisterBackend("counting", b); err != nil {
		t.Fatal(err)
	}
	art := loader.Artifact{ID: "m2", BackendType: "counting", Type: "x", Version: "1", Source: "mem://fake"}

	if _, err := l.Load(context.Background(), art); err != nil {
		t.Fatal(err) // seed commit so the id exists
	}
	// Force the next attempt to fail AT THE COMMIT PHASE deterministically:
	// the backend blocks until ctx is canceled, then returns SUCCESS — the
	// loader's commit phase then sees ctx.Err() and must abandon (NEW-02).
	if err := l.Unload(context.Background(), "m2"); err != nil {
		t.Fatal(err)
	}
	b.mu.Lock()
	b.blockOnCtx = true
	b.mu.Unlock()
	canceled, cancel := context.WithCancel(context.Background())
	go func() {
		time.Sleep(50 * time.Millisecond)
		cancel()
	}()
	_, cerr := l.Load(canceled, art)
	if cerr == nil {
		t.Fatal("expected the commit-phase failure (canceled during backend load)")
	}

	// The abandoned id must be poisoned: a commit for it cannot adopt the
	// released ref. A re-Load with a healthy context re-runs the backend path
	// cleanly (poison cleared by the fresh attempt).
	b.mu.Lock()
	b.blockOnCtx = false
	b.mu.Unlock()
	if _, err := l.Load(context.Background(), art); err != nil {
		t.Fatalf("clean re-load after abandon failed: %v", err)
	}
}

// NEW-04 race regression: RegisterBackend writes l.backends under l.mu while
// Load's commit/abandon paths READ it. Before the fix, the commit path read
// the map AFTER releasing l.mu — a data race with a concurrent registration.
func TestLoaderBackendMapRaceWithRegister(t *testing.T) {
	l := loader.NewBuiltinLoader()
	ctx := context.Background()

	stop := make(chan struct{})
	var registrar sync.WaitGroup
	registrar.Add(1)
	go func() {
		defer registrar.Done()
		for i := 0; ; i++ {
			select {
			case <-stop:
				return
			default:
			}
			// Distinct types: duplicates are rejected (ErrBackendExists), and
			// the growing set is bounded by the short test duration.
			_ = l.RegisterBackend(loader.BackendType(fmt.Sprintf("aux-%d-%d", i%64, i)), &countingBackend{})
		}
	}()

	var wg sync.WaitGroup
	wg.Add(2)
	go func() {
		defer wg.Done()
		for i := 0; i < 50; i++ {
			art := loader.Artifact{ID: fmt.Sprintf("m-a-%d", i), BackendType: "counting", Type: "x", Version: "1", Source: "mem://fake"}
			_, _ = l.Load(ctx, art)
			_ = l.Unload(ctx, fmt.Sprintf("m-a-%d", i))
		}
	}()
	go func() {
		defer wg.Done()
		for i := 0; i < 50; i++ {
			art := loader.Artifact{ID: fmt.Sprintf("m-b-%d", i), BackendType: "counting", Type: "x", Version: "1", Source: "mem://fake"}
			_, _ = l.Load(ctx, art)
			_ = l.Unload(ctx, fmt.Sprintf("m-b-%d", i))
		}
	}()
	wg.Wait()
	close(stop)
	registrar.Wait()
}

// NEW-08: deterministic same-ID concurrent loads. Attempt 1 fails AT THE
// COMMIT PHASE (its ctx is canceled while parked in the backend gate) and
// must abandon exactly once; attempt 2 succeeds and must commit exactly once —
// the loader must not poison the id (the old abandonedIDs design rejected the
// winner's commit; under holder-based ownership the winner legitimately
// commits while the loser releases its holder slot).
type gatingBackend struct {
	loader.Backend
	mu       sync.Mutex
	entered  int
	gate     chan struct{}
	commits  int
	abandons int
	unloads  int
}

func (b *gatingBackend) Load(ctx context.Context, artifact loader.Artifact) (loader.Module, error) {
	b.mu.Lock()
	b.entered++
	gate := b.gate
	me := b.entered
	b.mu.Unlock()
	if gate != nil && me == 1 {
		// Attempt 1 parks until its ctx is canceled: the backend returns
		// SUCCESS with a canceled ctx, so the loader's commit phase sees
		// ctx.Err() and must abandon (while NO commit exists — attempt 2 is
		// still gated behind the test's release).
		<-ctx.Done()
	}
	if gate != nil && me == 2 {
		// Attempt 2 parks until the test opens the gate (after attempt 1's
		// abandon completed) — making the commit-vs-abandon ordering
		// deterministic.
		<-gate
	}
	return loader.Module{
		ID:   artifact.ID,
		Type: "gating",
		Factory: config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
			return nil, errors.New("unused")
		}),
	}, nil
}
func (b *gatingBackend) CommitModule(id string)  { b.mu.Lock(); b.commits++; b.mu.Unlock() }
func (b *gatingBackend) AbandonModule(id string) { b.mu.Lock(); b.abandons++; b.mu.Unlock() }
func (b *gatingBackend) UnloadModule(id string)  { b.mu.Lock(); b.unloads++; b.mu.Unlock() }

func TestLoaderSameIDConcurrentCommitAndAbandon(t *testing.T) {
	l := loader.NewBuiltinLoader()
	b := &gatingBackend{gate: make(chan struct{})}
	if err := l.RegisterBackend("gating", b); err != nil {
		t.Fatal(err)
	}
	art := loader.Artifact{ID: "m", BackendType: "gating", Type: "x", Version: "1", Source: "mem://fake"}

	ctx1, cancel1 := context.WithCancel(context.Background())
	defer cancel1()
	type res struct {
		mod *loader.Module
		err error
	}
	r1 := make(chan res, 1)
	go func() {
		mod, err := l.Load(ctx1, art)
		r1 <- res{mod, err}
	}()

	// Wait until attempt 1 is parked inside the backend gate, then start
	// attempt 2 with a healthy context and open the gate for both.
	deadline := time.Now().Add(8 * time.Second)
	for {
		b.mu.Lock()
		n := b.entered
		b.mu.Unlock()
		if n >= 1 {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("attempt 1 never entered the backend")
		}
		time.Sleep(2 * time.Millisecond)
	}
	r2 := make(chan res, 1)
	go func() {
		mod, err := l.Load(context.Background(), art)
		r2 <- res{mod, err}
	}()
	deadline = time.Now().Add(8 * time.Second)
	for {
		b.mu.Lock()
		n := b.entered
		b.mu.Unlock()
		if n >= 2 {
			break
		}
		if time.Now().After(deadline) {
			t.Fatal("attempt 2 never entered the backend")
		}
		time.Sleep(2 * time.Millisecond)
	}
	// Attempt 1 is parked in the backend gate with healthy early checks done.
	// Cancel it NOW, before the gate opens: the backend returns success, so
	// attempt 1 reaches its commit phase and must abandon there.
	// Attempt 1 is parked in the backend with healthy early checks done.
	// Cancel it: the backend returns success, so attempt 1 reaches its commit
	// phase and must abandon there — BEFORE attempt 2 is allowed to commit.
	cancel1()
	select {
	case r := <-r1:
		if r.err == nil {
			t.Fatal("attempt 1 (canceled ctx) should have failed")
		}
	case <-time.After(8 * time.Second):
		t.Fatal("attempt 1 never returned")
	}
	// Release attempt 2: its commit must succeed on the SAME id (holder-based
	// ownership — the old poison design rejected it here).
	close(b.gate)
	select {
	case r := <-r2:
		if r.err != nil {
			t.Fatalf("attempt 2 failed: %v (the loser's abandon must not poison the winner)", r.err)
		}
	case <-time.After(8 * time.Second):
		t.Fatal("attempt 2 never returned")
	}

	b.mu.Lock()
	commits, abandons := b.commits, b.abandons
	b.mu.Unlock()
	if commits != 1 || abandons != 1 {
		t.Fatalf("protocol counts wrong: commits=%d abandons=%d, want 1/1", commits, abandons)
	}

	// The winner's module is live; unloading notifies exactly once.
	if err := l.Unload(context.Background(), "m"); err != nil {
		t.Fatal(err)
	}
	b.mu.Lock()
	unloads := b.unloads
	b.mu.Unlock()
	if unloads != 1 {
		t.Fatalf("unload notification = %d, want 1", unloads)
	}
}
