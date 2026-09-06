// Command wasmhmr is a runnable end-to-end example of the WASM + HMR
// integration:
//
//	camera-v1.wasm
//	  → WASM Backend (wazero) → Loader Module (logical type industrial.camera)
//	  → Factory → Runtime Fiber A (Active)
//	  → HMR Replace
//	  → camera-v2.wasm → Loader Module (industrial.camera)
//	  → Factory → Runtime Fiber B (Active), Fiber A Gone, module v1 unloaded
//
// It also demonstrates that a logical-type mismatch (industrial.camera ->
// industrial.sensor) is rejected while the current fiber stays Active.
//
// Run from the repository root:
//
//	go run ./cmd/wasmhmr
package main

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/wasm"
	"dynamic-runtime/runtime"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintf(os.Stderr, "wasmhmr: %v\n", err)
		os.Exit(1)
	}
	fmt.Println("\nwasmhmr: all demos finished cleanly.")
}

// printObserver prints every WASM instance materialization/destruction. It is a
// diagnostics seam of the WASM Backend; it never influences lifecycle.
type printObserver struct {
	created   int
	destroyed int
}

func (o *printObserver) InstanceCreated(moduleID string, instanceID uint64) {
	o.created++
	fmt.Printf("    [wasm] instance %s#%d CREATED\n", moduleID, instanceID)
}

func (o *printObserver) InstanceDestroyed(moduleID string, instanceID uint64) {
	o.destroyed++
	fmt.Printf("    [wasm] instance %s#%d destroyed\n", moduleID, instanceID)
}

func ctxT(secs time.Duration) (context.Context, context.CancelFunc) {
	return context.WithTimeout(context.Background(), secs*time.Second)
}

func run() error {
	obs := &printObserver{}
	rt, err := runtime.New()
	if err != nil {
		return err
	}
	defer rt.Close(context.Background())

	ld := loader.NewBuiltinLoader()
	wb := wasm.NewBackend(wasm.WithObserver(obs))
	if err := ld.RegisterBackend(loader.BackendWASM, wb); err != nil {
		return err
	}
	defer func() { _ = wb.Close() }()

	h := hmr.New(rt, ld, ld.Usage())
	defer func() { _ = h.CloseContext(context.Background()) }()
	defer func() { _ = ld.CloseContext(context.Background()) }()

	cam1, cam2 := fixture("camera-v1", "valid.wasm"), fixture("camera-v2", "camera-v2.wasm")
	sensor := fixture("sensor-v1", "sensor.wasm")

	ctx, cancel := ctxT(20)
	defer cancel()

	fmt.Println("== 1. Load camera-v1.wasm -> Module(industrial.camera) ==")
	m1, err := ld.Load(ctx, wasmArtifact(cam1))
	if err != nil {
		return fmt.Errorf("load camera v1: %w", err)
	}
	fmt.Printf("    Module ID=%s Type=%s (BackendType=wasm, ModuleType is logical)\n", m1.ID, m1.Type)

	fmt.Println("\n== 2. Factory -> Component -> Runtime Fiber A (Active) ==")
	comp1, err := m1.Factory.Create(config.ComponentConfig{ID: "camera", Type: m1.Type})
	if err != nil {
		return err
	}
	fiberA, err := rt.Load(comp1)
	if err != nil {
		return err
	}
	if err := fiberA.Ready(ctx); err != nil {
		return fmt.Errorf("fiber A not active: %w", err)
	}
	fmt.Printf("    Fiber A id=%d state=%v component=%s\n", fiberA.ID(), fiberA.State(), comp1.Name())

	fmt.Println("\n== 3. Register + Bind HMR target ==")
	target := hmr.Target{ID: "cam", ComponentID: "camera", Artifact: wasmArtifact(cam2)}
	if err := h.Register(target); err != nil {
		return err
	}
	if err := h.Bind(target.ID, *m1, fiberA); err != nil {
		return err
	}
	b, _ := h.CurrentBinding("cam")
	fmt.Printf("    binding: module=%s type=%s fiber=%d\n", b.Module.ID, b.ModuleType, b.Fiber.ID())
	if !ld.Usage().InUse(m1.ID) {
		return errors.New("expected HMR usage to be acquired after Bind")
	}
	fmt.Printf("    HMR usage acquired on module %s (Unload is now blocked)\n", m1.ID)

	fmt.Println("\n== 4. HMR Replace camera-v1 -> camera-v2 (same logical type) ==")
	if err := h.Replace(ctx, target); err != nil {
		return fmt.Errorf("replace to v2 failed: %w", err)
	}
	b, _ = h.CurrentBinding("cam")
	fmt.Printf("    new binding: module=%s type=%s fiber=%d\n", b.Module.ID, b.ModuleType, b.Fiber.ID())
	fmt.Printf("    Fiber A state=%v (must be Gone)\n", fiberA.State())
	fmt.Printf("    old module %s loaded=%v usage=%v\n", cam1.id,
		ld.Has(cam1.id), ld.Usage().InUse(cam1.id))
	fmt.Printf("    new module %s usage held=%v\n", b.Module.ID, ld.Usage().InUse(b.Module.ID))

	fmt.Println("\n== 5. Reject incompatible type: industrial.camera -> industrial.sensor ==")
	sensorTarget := hmr.Target{ID: "cam", ComponentID: "camera", Artifact: wasmArtifact(sensor)}
	err = h.Replace(ctx, sensorTarget)
	fmt.Printf("    Replace error = %v\n", err)
	if !errors.Is(err, hmr.ErrIncompatibleModule) {
		return fmt.Errorf("expected ErrIncompatibleModule, got %v", err)
	}
	cur, _ := h.CurrentBinding("cam")
	fmt.Printf("    current fiber state=%v module=%s (unchanged, still Active)\n", cur.Fiber.State(), cur.Module.ID)
	if cur.Fiber.State() != runtime.StateActive {
		return errors.New("current fiber lost Active after rejected replacement")
	}

	fmt.Println("\n== 6. Teardown: close HMR/Loader/Runtime ==")
	if err := h.Close(); err != nil {
		return err
	}
	if err := ld.Close(); err != nil {
		return err
	}
	if err := rt.Close(context.Background()); err != nil {
		return err
	}
	fmt.Printf("    instances created=%d destroyed=%d\n", obs.created, obs.destroyed)
	if obs.created != obs.destroyed {
		return fmt.Errorf("wasm instance leak: created=%d destroyed=%d", obs.created, obs.destroyed)
	}
	return nil
}

type fixtureRef struct {
	id   string
	path string
	ver  string
}

func fixture(id, file string) fixtureRef {
	return fixtureRef{id: id, path: filepath.Join("extensions", "loader", "wasm", "testdata", file), ver: "1"}
}

func wasmArtifact(f fixtureRef) loader.Artifact {
	return loader.Artifact{ID: f.id, BackendType: loader.BackendWASM, Source: mustFileURI(f.path), Version: f.ver}
}

// mustFileURI converts a local path to a file:// URI. Backend accepts plain
// paths too; the URI keeps the demo on the canonical Source form.
func mustFileURI(path string) string {
	abs, err := filepath.Abs(path)
	if err != nil {
		panic(err)
	}
	return "file://" + abs
}
