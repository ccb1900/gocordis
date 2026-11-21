package wasm

import (
	"context"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// NEW-10/13/14 end-to-end: the full chain — loader.Load → ResourceBackend →
// handle.Commit → instantiate → loader.Unload → handle.Release — exercised
// through the REAL loader and WASM backend (not a fake).

func TestWASMEndToEndLoaderLifecycle(t *testing.T) {
	ld := loader.NewBuiltinLoader()
	b := newTestBackend()
	if err := ld.RegisterBackend(loader.BackendWASM, b); err != nil {
		t.Fatal(err)
	}
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	mod, err := ld.Load(ctx, loader.Artifact{
		ID:          "e2e",
		BackendType: loader.BackendWASM,
		Source:      "file://" + absFixture(t, "valid.wasm"),
		Version:     "1",
	})
	if err != nil {
		t.Fatal(err)
	}
	if b.CompiledModules() != 1 {
		t.Fatalf("compiled modules after loader.Load = %d, want 1", b.CompiledModules())
	}

	comp, err := mod.Factory.Create(config.ComponentConfig{ID: "e2e-comp", Type: mod.Type})
	if err != nil {
		t.Fatalf("factory create: %v", err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("fiber ready: %v", err)
	}

	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if err := ld.Unload(ctx, "e2e"); err != nil {
		t.Fatalf("unload: %v", err)
	}
	if b.CompiledModules() != 0 {
		t.Fatalf("compiled modules after Unload = %d, want 0 (NEW-10 leak)", b.CompiledModules())
	}
	if err := rt.Close(ctx); err != nil {
		t.Fatal(err)
	}
}

func TestWASMEndToEndConcurrentLoads(t *testing.T) {
	ld := loader.NewBuiltinLoader()
	b := newTestBackend()
	if err := ld.RegisterBackend(loader.BackendWASM, b); err != nil {
		t.Fatal(err)
	}
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	const n = 6
	modules := make([]*loader.Module, n)
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func(i int) {
			defer wg.Done()
			id := "conc-" + string(rune('a'+i))
			mod, err := ld.Load(ctx, loader.Artifact{
				ID:          id,
				BackendType: loader.BackendWASM,
				Source:      "file://" + absFixture(t, "valid.wasm"),
				Version:     "1",
			})
			if err != nil {
				t.Errorf("load %d: %v", i, err)
				return
			}
			modules[i] = mod
		}(i)
	}
	wg.Wait()
	if b.CompiledModules() != n {
		t.Fatalf("compiled modules after concurrent loads = %d, want %d", b.CompiledModules(), n)
	}
	for i := 0; i < n; i++ {
		if err := ld.Unload(ctx, modules[i].ID); err != nil {
			t.Fatalf("unload %d: %v", i, err)
		}
	}
	if b.CompiledModules() != 0 {
		t.Fatalf("compiled modules after all unloads = %d, want 0", b.CompiledModules())
	}
}
