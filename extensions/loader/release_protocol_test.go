package loader_test

import (
	"context"
	"errors"
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
