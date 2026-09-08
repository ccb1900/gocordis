package integration

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	goruntime "runtime"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/registry"
	"dynamic-runtime/extensions/scheduler"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// E2E-12 — Watch + HMR via an explicit test-only adapter (never core).
func TestE2E12WatchHMR(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	ld := loader.NewBuiltinLoader()
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(ctxT(t))
	defer ld.CloseContext(ctxT(t))
	fw := watch.NewFileWatcher()
	defer fw.Close()

	for _, v := range []string{"v1", "v2"} {
		tag := v
		ld.RegisterBuiltin("builtin://w-"+v, func() config.Factory {
			return &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
				return independentComp(&kit{}, cc.ID, false), nil
			}}
		})
		_ = tag
	}
	m1, err := ld.Load(ctxT(t), artifact("w-v1", "ind", "builtin://w-v1", "1"))
	if err != nil {
		t.Fatal(err)
	}
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "w", Type: m1.Type})
	if err != nil {
		t.Fatal(err)
	}
	f1, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f1)
	h.Register(hmr.Target{ID: "w", ComponentID: "w", Artifact: artifact("w-v1", "ind", "builtin://w-v1", "1")})
	h.Bind("w", *m1, f1)

	dir := t.TempDir()
	sel := filepath.Join(dir, "sel.txt")
	if err := os.WriteFile(sel, []byte("v1"), 0o644); err != nil {
		t.Fatal(err)
	}
	sub, err := fw.Watch(ctxT(t), watch.Source{ID: "sel", Kind: "file", URI: "file://" + sel})
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer sub.Close()

	// Explicit test-only adapter: Watch Change -> HMR.Replace with the module
	// selected by the file content.
	go func() {
		for range sub.Changes() {
			data, err := os.ReadFile(sel)
			if err != nil {
				continue
			}
			ver := string(data)
			_ = h.Replace(context.Background(), hmr.Target{ID: "w", ComponentID: "w", Artifact: artifact("w-"+ver, "ind", "builtin://w-"+ver, "1")})
		}
	}()

	writeFile := func(v string) {
		if err := os.WriteFile(sel, []byte(v), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	// Retry distinct writes until HMR observed v2 (OS events may coalesce).
	for i := 0; i < 10; i++ {
		writeFile("v2")
		deadline := time.Now().Add(1500 * time.Millisecond)
		for time.Now().Before(deadline) {
			if b, ok := h.CurrentBinding("w"); ok && b.Module.ID == "w-v2" && b.Fiber.State() == runtime.StateActive {
				return
			}
			time.Sleep(5 * time.Millisecond)
		}
		// Alternate content to force fresh revisions on retries.
		writeFile("v1")
		time.Sleep(50 * time.Millisecond)
	}
	t.Fatal("HMR was not triggered by Watch through the test adapter")
}

// E2E-17 — concurrent config changes converge to the latest valid state.
func TestE2E17ConcurrentConfigChanges(t *testing.T) {
	e, _ := newCfgEnv(t)
	pv := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	versions := []string{"a", "b", "c", "d"}
	var wg sync.WaitGroup
	for _, v := range versions {
		wg.Add(1)
		go func(v string) {
			defer wg.Done()
			_ = e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv(v)}})
		}(v)
	}
	wg.Wait()
	// Converge to E.
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv("e")}}); err != nil {
		t.Fatal(err)
	}
	waitActive(t, fiberOf(t, e, "p"))
	eventually(t, 8*time.Second, "applied == E", func() bool {
		for _, o := range e.ctrl.Owned() {
			if tag, _ := o.Config["tag"].(string); tag == "e" {
				return true
			}
		}
		return false
	})
}

// E2E-21 — Close during Config Reconcile: no panic/deadlock/leak.
func TestE2E21CloseDuringConfigReconcile(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	_ = reg.Register("independent", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		return independentComp(&kit{}, cc.ID, false), nil
	}})
	ctrl := config.NewController(rt, reg)

	var wg sync.WaitGroup
	stop := make(chan struct{})
	for g := 0; g < 4; g++ {
		wg.Add(1)
		go func(g int) {
			defer wg.Done()
			i := 0
			for {
				select {
				case <-stop:
					return
				default:
				}
				id := fmt.Sprintf("c%d-%d", g, i)
				ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
				_ = ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{{ID: id, Type: "independent"}}})
				cancel()
				i++
			}
		}(g)
	}
	time.Sleep(10 * time.Millisecond)
	cl, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	if err := rt.Close(cl); err != nil {
		t.Fatalf("runtime Close: %v", err)
	}
	cancel()
	close(stop)
	wg.Wait()
	_ = ctrl.CloseContext(ctxT(t))
}

// E2E-23 — close during a watch storm: no panic/deadlock/send-on-closed.
func TestE2E23CloseDuringWatchStorm(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "s.toml")
	if err := os.WriteFile(path, []byte(tomlOf(config.ComponentConfig{ID: "p", Type: "independent"})), 0o644); err != nil {
		t.Fatal(err)
	}
	e, _ := newCfgEnv(t)
	// newCfgEnv already owns its runtime cleanup; we just need a FileWatcher +
	// adapter on the same controller. Reuse e.rt for Close.
	fw := watch.NewFileWatcher()
	adapter, err := configwatch.New(configwatch.Source{ID: "s", Path: path, Format: configwatch.FormatTOML}, e.ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	runCtx, runCancel := context.WithCancel(context.Background())
	go adapter.Run(runCtx)

	stop := make(chan struct{})
	var wg sync.WaitGroup
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
			body := tomlOf(config.ComponentConfig{ID: "p", Type: "independent", Config: map[string]any{"tag": fmt.Sprintf("t%d", i)}})
			_ = os.WriteFile(path, []byte(body), 0o644)
			i++
		}
	}()

	time.Sleep(30 * time.Millisecond)
	_ = adapter.CloseContext(ctxT(t))
	runCancel()
	_ = fw.Close()
	close(stop)
	wg.Wait()
	_ = e.rt.Close(context.Background())
	_ = e.ctrl.CloseContext(ctxT(t))
}

// E2E-28 — repeated lifecycle churn (100 cycles) without leaks.
func TestE2E28RepeatedLifecycleChurn(t *testing.T) {
	e, _ := newCfgEnv(t)
	before := goruntime.NumGoroutine()
	pv := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	for i := 0; i < 100; i++ {
		if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv(fmt.Sprintf("v%d", i))}}); err != nil {
			t.Fatal(err)
		}
		waitActive(t, fiberOf(t, e, "p"))
	}
	if len(e.ctrl.Owned()) != 1 {
		t.Fatal("owned != 1 after churn")
	}
	eventually(t, 10*time.Second, "goroutines settle", func() bool {
		return goruntime.NumGoroutine() <= before+8
	})
}

// E2E-29 — full chain: TOML -> Watch -> Adapter -> Config -> Provider ->
// RegistryOwner -> Consumer -> Active; then provider replacement.
func TestE2E29FullChain(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "full.toml")
	cfg := func(tag string) string {
		return "[[components]]\nid=\"prov\"\ntype=\"provider\"\n\n[components.config]\ntag=\"" + tag + "\"\n\n" +
			"[[components]]\nid=\"reg\"\ntype=\"regowner2\"\n\n" +
			"[[components]]\nid=\"cons\"\ntype=\"regconsumer\"\n"
	}
	if err := os.WriteFile(path, []byte(cfg("a")), 0o644); err != nil {
		t.Fatal(err)
	}

	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	defer rt.Close(context.Background())
	reg := config.NewFactoryRegistry()
	consKit := &kit{}
	factory := func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		switch cc.Type {
		case "provider":
			return providerComp(&kit{}, cc.ID, tag, false), nil
		case "regowner2":
			return &regDepComp{id: cc.ID}, nil
		case "regconsumer":
			return regConsumerComp(consKit, cc.ID), nil
		}
		return nil, fmt.Errorf("unknown %s", cc.Type)
	}
	for _, typ := range []string{"provider", "regowner2", "regconsumer"} {
		if err := reg.Register(typ, &adapterFactory{build: factory}); err != nil {
			t.Fatal(err)
		}
	}
	ctrl := config.NewController(rt, reg)
	defer ctrl.CloseContext(ctxT(t))
	fw := watch.NewFileWatcher()
	defer fw.Close()
	adapter, err := configwatch.New(configwatch.Source{ID: "full", Path: path, Format: configwatch.FormatTOML}, ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	defer adapter.CloseContext(ctxT(t))
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	waitAll := func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		for _, o := range ctrl.Owned() {
			if err := o.Fiber.Ready(ctx); err != nil {
				t.Fatalf("%s not active: %v", o.ID, err)
			}
		}
	}
	waitAll()
	prov1 := fiberOf(t, &env{ctrl: ctrl}, "prov")

	runCtx, runCancel := context.WithCancel(context.Background())
	defer runCancel()
	go adapter.Run(runCtx)

	// Replace the provider: regowner2 (depends on provider) and the consumer
	// recover through the kernel.
	for i := 0; i < 10; i++ {
		if err := os.WriteFile(path, []byte(cfg(fmt.Sprintf("b%d", i))), 0o644); err != nil {
			t.Fatal(err)
		}
		deadline := time.Now().Add(1500 * time.Millisecond)
		for time.Now().Before(deadline) {
			if cur := fiberOf(t, &env{ctrl: ctrl}, "prov"); cur.ID() != prov1.ID() && cur.State() == runtime.StateActive {
				// consumer & regowner must also be active
				all := true
				for _, o := range ctrl.Owned() {
					if o.Fiber.State() != runtime.StateActive {
						all = false
					}
				}
				if all {
					return
				}
			}
			time.Sleep(5 * time.Millisecond)
		}
	}
	t.Fatal("full chain did not converge after provider replacement")
}

// regDepComp requires capKey and provides regKey (registry owner depending on
// the provider) - used by the full-chain test.
type regDepComp struct{ id string }

func (c *regDepComp) Name() string { return "regdep:" + c.id }
func (c *regDepComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(capKey)}
}
func (c *regDepComp) Provide() []runtime.Capability { return []runtime.Capability{regKey.Capability()} }
func (c *regDepComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if _, err := runtime.Require(ctx, capKey); err != nil {
		return nil, err
	}
	return nil, runtime.Provide(ctx, regKey, registry.New[string]())
}

// E2E-30 — full shutdown: every extension reaches its terminal state; no leaks.
func TestE2E30FullShutdown(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "s.toml")
	if err := os.WriteFile(path, []byte(tomlOf(config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": "v1"}})), 0o644); err != nil {
		t.Fatal(err)
	}

	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	_ = reg.Register("provider", &adapterFactory{build: func(cc config.ComponentConfig) (runtime.Component, error) {
		tag, _ := cc.Config["tag"].(string)
		return providerComp(&kit{}, cc.ID, tag, false), nil
	}})
	ctrl := config.NewController(rt, reg)

	fw := watch.NewFileWatcher()
	adapter, err := configwatch.New(configwatch.Source{ID: "s", Path: path, Format: configwatch.FormatTOML}, ctrl, fw)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatal(err)
	}
	if err := adapter.Sync(ctxT(t)); err != nil {
		t.Fatal(err)
	}
	runCtx, runCancel := context.WithCancel(context.Background())
	go adapter.Run(runCtx)
	waitActive(t, fiberOf(t, &env{ctrl: ctrl}, "p"))

	// Independent extension instances.
	sch := scheduler.New()
	_ = sch.Add(scheduler.Job{ID: "j", Schedule: scheduler.Interval{Every: 5 * time.Millisecond}, Task: func(context.Context) error { return nil }})

	// Shutdown everything in a sane order.
	_ = sch.Close()
	runCancel()
	_ = adapter.CloseContext(ctxT(t))
	_ = fw.Close()
	_ = ctrl.CloseContext(ctxT(t))
	if err := rt.Close(context.Background()); err != nil {
		t.Fatal(err)
	}
	if len(ctrl.Owned()) != 0 {
		t.Fatal("controller still owns fibers after shutdown")
	}
}

// P-01 — lifecycle conservation: one replacement never leaves two Active fibers
// for the same logical component.
func TestP01LifecycleConservation(t *testing.T) {
	e, _ := newCfgEnv(t)
	pv := func(tag string) config.ComponentConfig {
		return config.ComponentConfig{ID: "p", Type: "provider", Config: map[string]any{"tag": tag}}
	}
	if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv("v1")}}); err != nil {
		t.Fatal(err)
	}
	waitActive(t, fiberOf(t, e, "p"))
	for i := 0; i < 20; i++ {
		if err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{pv(fmt.Sprintf("v%d", i))}}); err != nil {
			t.Fatal(err)
		}
		waitActive(t, fiberOf(t, e, "p"))
		if len(e.ctrl.Owned()) != 1 {
			t.Fatalf("owned = %d at cycle %d", len(e.ctrl.Owned()), i)
		}
	}
}

// P-08 — Close truthfulness: a timeout never claims Closed while work is alive.
func TestP08CloseTruthfulness(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	release := make(chan struct{})
	comp := &stubbornComp{release: release}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, f)
	_ = f.Dispose()
	short, cancel := context.WithTimeout(context.Background(), 100*time.Millisecond)
	err = rt.Close(short)
	cancel()
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("Close = %v, want deadline exceeded (work still alive)", err)
	}
	close(release)
	cl, cancel2 := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel2()
	if err := rt.Close(cl); err != nil {
		t.Fatalf("Close after release: %v", err)
	}
}

type stubbornComp struct {
	release chan struct{}
}

func (c *stubbornComp) Name() string                  { return "stubborn" }
func (c *stubbornComp) Inject() []runtime.Dependency  { return nil }
func (c *stubbornComp) Provide() []runtime.Capability { return nil }
func (c *stubbornComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return func() error {
		<-c.release // ignores cancellation
		return nil
	}, nil
}
