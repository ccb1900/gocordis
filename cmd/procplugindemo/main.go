// Command procplugindemo is the runnable demo for the process-external plugin
// backend (extensions/loader/proc): the HOST application links a plugin that
// lives in a SEPARATE OS PROCESS and talks JSON-RPC over stdin/stdout.
//
// Layout (two parts):
//
//	cmd/procplugindemo/main.go        host: contract + assembly + consumer
//	cmd/procplugindemo/pluginecho     plugin: standalone executable (proc.Serve)
//	cmd/procplugindemo/manifest.toml  declaration store: which plugins are on
//
// Run:
//
//	go build -o build/plugin-echo ./cmd/procplugindemo/pluginecho
//	go run ./cmd/procplugindemo -plugin ./build/plugin-echo
//
// While it runs (a call every 2s through the process boundary), try:
//   - pkill -f plugin-echo                  -> calls fail with ErrPluginUnavailable
//     (no auto-restart; flip the switch to recover)
//   - edit manifest.toml: enabled = false   -> plugin process stops, the
//     consumer withdraws to Pending (gating), then re-enables on flip back.
package main

import (
	"context"
	"flag"
	"fmt"
	"log"
	"os"
	"os/signal"
	"path/filepath"
	"sync"
	"syscall"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/extensions/loader/proc"
	"dynamic-runtime/extensions/watch"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Contract — lives in the HOST and is shared with plugin authors. The plugin
// implements the same METHOD NAMES over JSON-RPC; the host turns an RPC
// Caller into this interface. This is the only thing host and plugin share.
// ---------------------------------------------------------------------------

type Codec interface {
	Encode(text string) (string, error)
}

var codecKey = runtime.NewKey[Codec]("demo.codec")

func bindCodec(call proc.Caller) Codec { return &remoteCodec{call: call} }

type remoteCodec struct{ call proc.Caller }

func (r *remoteCodec) Encode(text string) (string, error) {
	var out struct {
		Encoded string `json:"encoded"`
	}
	err := r.call.Call(context.Background(), "encode", map[string]string{"text": text}, &out)
	if err != nil {
		return "", fmt.Errorf("plugin call: %w", err)
	}
	return out.Encoded, nil
}

// ---------------------------------------------------------------------------
// A host component that CONSUMES the plugin capability. It gates automatically:
// plugin off -> this withdraws to Pending; plugin on -> it re-activates.
// ---------------------------------------------------------------------------

type consumer struct {
	mu   sync.Mutex
	wg   sync.WaitGroup
	stop chan struct{}
}

func (c *consumer) Name() string { return "consumer" }
func (c *consumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(codecKey)}
}
func (c *consumer) Provide() []runtime.Capability { return nil }

func (c *consumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	codec, err := runtime.Require(ctx, codecKey)
	if err != nil {
		return nil, err
	}
	c.mu.Lock()
	c.stop = make(chan struct{})
	stop := c.stop
	c.mu.Unlock()
	c.wg.Add(1)

	go func() {
		defer c.wg.Done()
		tick := time.NewTicker(2 * time.Second)
		defer tick.Stop()
		for {
			select {
			case <-ctx.Done(): // activation ending: stop calling
				return
			case <-stop:
				return
			case <-tick.C:
				out, err := codec.Encode("hello")
				if err != nil {
					log.Printf("[consumer] call failed: %v", err)
					continue
				}
				log.Printf("[consumer] plugin says: %s", out)
			}
		}
	}()

	return func() error {
		close(stop)
		c.wg.Wait()
		return nil
	}, nil
}

// ---------------------------------------------------------------------------
// Assembly
// ---------------------------------------------------------------------------

func main() {
	pluginPath := flag.String("plugin", "./build/plugin-echo", "plugin executable path")
	manifest := flag.String("manifest", "manifest.toml", "declaration store (demo-dir relative; repo-root runs auto-fallback)")
	flag.Parse()
	// Manifest resolution: as given -> relative to the executable (demo-dir
	// runs) -> relative to the repo root. Loud failure when missing: the
	// adapter treats a missing source as "empty declaration" (delete
	// semantics), which must never happen silently for the demo.
	manifestPath := *manifest
	if _, statErr := os.Stat(manifestPath); statErr != nil {
		// Fallback for repo-root runs: the demo default is demo-dir relative.
		alt := filepath.Join("cmd", "procplugindemo", "manifest.toml")
		if _, e2 := os.Stat(alt); e2 == nil {
			manifestPath = alt
		}
	}
	manifestAbs, err := filepath.Abs(manifestPath)
	if err != nil {
		log.Fatalf("manifest: %v", err)
	}
	if _, err := os.Stat(manifestAbs); err != nil {
		log.Fatalf("manifest %q not readable: %v (the adapter would treat a missing file as an empty declaration)", manifestAbs, err)
	}
	log.Printf("[assembly] manifest: %s", manifestAbs)

	log.SetFlags(log.Ltime)
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	rt, err := runtime.New()
	if err != nil {
		log.Fatalf("runtime: %v", err)
	}

	// 1. The proc backend bound to the contract: every plugin module it loads
	//    provides demo.codec with an RPC-backed implementation.
	backend := proc.NewBackend(codecKey, bindCodec)
	defer backend.Close()

	// 2. Load the plugin executable as a Module (validates: exists+executable).
	ld := loader.NewBuiltinLoader()
	if err := ld.RegisterBackend(proc.BackendType, backend); err != nil {
		log.Fatalf("register backend: %v", err)
	}
	m, err := ld.Load(ctx, loader.Artifact{
		ID:          "echo",
		BackendType: proc.BackendType,
		Source:      *pluginPath,
		Version:     "v1",
	})
	if err != nil {
		log.Fatalf("load plugin: %v", err)
	}

	// 3. Factories: the plugin's Factory (from the Module) + the host's own
	//    consumer component.
	reg := config.NewFactoryRegistry()
	// Register the plugin's factory under its LOGICAL name (manifest `type`):
	// BackendType ("proc") is the loading path, never what the component is.
	must(reg.Register("echo", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return m.Factory.Create(cc)
	})))
	must(reg.Register("consumer", config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &consumer{}, nil
	})))
	ctrl := config.NewController(rt, reg)
	defer ctrl.CloseContext(ctx)

	// 4. Declaration store -> reconciliation. Enabled=true here starts the
	//    plugin PROCESS; flipping it in the file stops/restarts everything.
	fw := watch.NewFileWatcher()
	adapter, err := configwatch.New(configwatch.Source{
		ID: "app", Path: manifestAbs, Format: configwatch.FormatTOML,
	}, ctrl, fw)
	if err != nil {
		log.Fatalf("configwatch: %v", err)
	}
	syncErr := adapter.Sync(ctx)
	log.Printf("[assembly] sync err=%v owned=%d", syncErr, len(ctrl.Owned()))
	for _, o := range ctrl.Owned() {
		if o.Fiber == nil {
			log.Printf("[assembly] %s: disabled (declared, no fiber)", o.ID)
			continue
		}
		log.Printf("[assembly] %s: %s %v", o.ID, o.Fiber.State(), o.Fiber.Err())
	}
	go adapter.Run(ctx)

	// Assembly report: every owned entry with its live state.
	for _, o := range ctrl.Owned() {
		if o.Fiber == nil {
			log.Printf("[assembly] %s: disabled (declared, no fiber)", o.ID)
			continue
		}
		log.Printf("[assembly] %s: %s %v", o.ID, o.Fiber.State(), o.Fiber.Err())
	}

	// 5. Run until interrupted; shutdown order: sources -> controller -> runtime.
	sig := make(chan os.Signal, 1)
	signal.Notify(sig, os.Interrupt, syscall.SIGTERM)
	<-sig
	log.Println("shutting down…")
	runCancel := func() { cancel() }
	runCancel()
	_ = adapter.CloseContext(ctx)
	_ = ctrl.CloseContext(ctx)
	_ = rt.Close(ctx)
}

func must(err error) {
	if err != nil {
		log.Fatal(err)
	}
}
