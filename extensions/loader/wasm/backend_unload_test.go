package wasm

import (
	"context"
	"path/filepath"
	"sync"
	"testing"

	"dynamic-runtime/extensions/loader"
)

// absFixture returns an absolute source path: Backend.Load resolves file://
// sources against the process working directory, not the package dir.
func absFixture(t *testing.T, name string) string {
	t.Helper()
	abs, err := filepath.Abs(fixturePath(name))
	if err != nil {
		t.Fatal(err)
	}
	return abs
}

// EXT-05: the backend must release its compiled module when the Loader
// discards the registry entry (UnloadModule notification), and concurrent
// duplicate loads must share one compilation (the registry-commit loser must
// not retain a second CompiledModule).
func TestBackendUnloadModuleReleasesCompiled(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("v01", "file://"+fixturePath("valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")
	_, hA, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatalf("load: %v", err)
	}
	if n := b.CompiledModules(); n != 1 {
		t.Fatalf("compiled modules retained after load = %d, want 1", n)
	}
	hA.Release() // NEW-10: Unload discards through the attempt handle
	if n := b.CompiledModules(); n != 0 {
		t.Fatalf("compiled modules retained after UnloadModule = %d, want 0 (EXT-05)", n)
	}
}

func TestBackendConcurrentDuplicateLoadSharesCompilation(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("v01", "file://"+fixturePath("valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	const n = 8
	var wg sync.WaitGroup
	errs := make(chan error, n)
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			_, err := b.Load(context.Background(), art)
			errs <- err
		}()
	}
	wg.Wait()
	close(errs)
	for err := range errs {
		if err != nil {
			t.Fatalf("concurrent load: %v", err)
		}
	}
	if got := b.CompiledModules(); got != 1 {
		t.Fatalf("concurrent duplicate loads retained %d compiled modules, want 1 (EXT-05)", got)
	}
}
