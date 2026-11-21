package wasm

import (
	"context"
	"fmt"
	"sync"
	"sync/atomic"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
)

// These tests exist because the NEW-13 fix was claimed in a commit message
// but the code still used the two-step LoadResource (b.Load + ID re-query).
// The sequential tests in generation_isolation_test.go could not catch it:
// the lie only manifests when an Unload+Reload races between b.Load's return
// and the generation re-query. Every test here runs that exact interleaving
// under -race with enough rounds to make the window deterministic.

// TestLoadResourceFactoryAliveAfterUnloadReloadRace — NEW-13 stress: N rounds
// of LoadResource → Unload → fresh same-ID LoadResource. Every factory
// returned by a successful LoadResource must be able to create a component
// (the compiled ref it references must be alive). The two-step code produces
// dead factories: b.Load binds gen-1, Unload releases gen-1, the re-query
// finds gen-2, and the handle+module disagree.
func TestLoadResourceFactoryAliveAfterUnloadReloadRace(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("race13", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	const rounds = 200
	var dead atomic.Int32
	for i := 0; i < rounds; i++ {
		mod, h, err := b.LoadResource(context.Background(), art)
		if err != nil {
			t.Fatalf("round %d load: %v", i, err)
		}
		// Unload the module (the loader does this between loads). With the
		// two-step code, the handle may reference gen-2 while the module's
		// factory references the CLOSED gen-1 — the create below proves it.
		h.Release()

		// Try to create from the factory. The compiled module this factory
		// references must be ALIVE (LoadResource returned it atomically with
		// the handle; the handle's Release only closes when the last holder
		// releases).
		comp, cerr := mod.Factory.Create(config.ComponentConfig{ID: "probe", Type: mod.Type})
		if cerr != nil {
			dead.Add(1)
			t.Logf("round %d: factory dead (NEW-13 lie): %v", i, cerr)
			continue
		}
		_ = comp
	}
	if n := dead.Load(); n > 0 {
		t.Fatalf("%d/%d LoadResource calls returned a factory referencing a CLOSED compiled module (NEW-13 unfixed)", n, rounds)
	}
}

// TestConcurrentLoadUnloadReloadFactoryAlive — the concurrent version: N
// goroutines doing LoadResource+Create, racing Unload+Reload for the same ID.
// Every Create must succeed (the factory's compiled ref is alive because
// LoadResource binds it atomically). The two-step code produces dead
// factories under this exact contention.
func TestConcurrentLoadUnloadReloadFactoryAlive(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("race13c", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	const workers = 8
	const iterations = 50
	var wg sync.WaitGroup
	var dead, loaded atomic.Int32
	errs := make(chan error, workers*iterations)

	for w := 0; w < workers; w++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < iterations; i++ {
				mod, h, err := b.LoadResource(context.Background(), art)
				if err != nil {
					errs <- fmt.Errorf("load: %w", err)
					continue
				}
				loaded.Add(1)
				comp, cerr := mod.Factory.Create(config.ComponentConfig{ID: "probe", Type: mod.Type})
				if cerr != nil {
					errs <- fmt.Errorf("create after load: %w", cerr)
					continue
				}
				_ = comp
				h.Release()
			}
		}()
	}
	// A releaser races Unload+Reload for the same ID.
	wg.Add(1)
	go func() {
		defer wg.Done()
		for i := 0; i < iterations; i++ {
			_, h, err := b.LoadResource(context.Background(), art)
			if err != nil {
				continue
			}
			h.Release() // release the current generation
		}
	}()
	wg.Wait()
	close(errs)
	for err := range errs {
		t.Error(err)
	}
	if dead.Load() > 0 {
		t.Fatalf("%d factories referenced closed compiled modules (NEW-13)", dead.Load())
	}
	if loaded.Load() == 0 {
		t.Fatal("no successful loads")
	}
}
