// Command host is a minimal "plugin host" built on the Runtime Kernel and the
// Config Extension.
//
// The idea:
//
//	you write plugins (a Component + a Factory)        <- "写插件"
//	you declare a manifest (which plugins, what config) <- "期望状态"
//	the host reconciles it; the framework drives the      <- "框架管执行主体"
//	plugin lifecycles, dependencies and replacement.
//
// Run:
//
//	go run ./cmd/host
//
// What you will see:
//  1. reconcile manifest v1  -> camera + recorder start (dependency order);
//  2. reconcile manifest v2  -> camera config changed  => replaced by a NEW
//     fiber (same ID, new execution body); recorder follows automatically
//     because it depends on the camera's capability (Kernel-driven);
//     a metrics plugin is added;
//  3. reconcile manifest v3  -> metrics removed;
//  4. close -> everything is withdrawn and the Runtime closes.
package main

import (
	"context"
	"fmt"
	"log"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// 1. The plugins (this is the part you write per-plugin)
// ---------------------------------------------------------------------------

// frame is the capability produced by the camera plugin.
type frame interface{ Name() string }

type frameV struct{ name string }

func (f frameV) Name() string { return f.name }

var framesKey = runtime.NewKey[frame]("frames")

func ctxT() (context.Context, context.CancelFunc) {
	return context.WithTimeout(context.Background(), 15*time.Second)
}

func waitActive(f *runtime.Fiber) error {
	ctx, cancel := ctxT()
	defer cancel()
	return f.Ready(ctx)
}

// cameraComp produces frames. Its Config["mode"] selects the implementation
// behavior; changing the config is what makes the host replace it.
type cameraComp struct {
	id   string
	mode string
}

func (c *cameraComp) Name() string                 { return "camera:" + c.id }
func (c *cameraComp) Inject() []runtime.Dependency { return nil }
func (c *cameraComp) Provide() []runtime.Capability {
	return []runtime.Capability{framesKey.Capability()}
}
func (c *cameraComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, framesKey, frame(frameV{name: c.id + "[" + c.mode + "]"})); err != nil {
		return nil, err
	}
	fmt.Printf("   [camera %s] active (mode=%s)\n", c.id, c.mode)
	return func() error {
		fmt.Printf("   [camera %s] cleanup\n", c.id)
		return nil
	}, nil
}

// recorderComp consumes the camera's frames.
type recorderComp struct{ id string }

func (c *recorderComp) Name() string { return "recorder:" + c.id }
func (c *recorderComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(framesKey)}
}
func (c *recorderComp) Provide() []runtime.Capability { return nil }
func (c *recorderComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fr, err := runtime.Require(ctx, framesKey)
	if err != nil {
		return nil, err
	}
	fmt.Printf("   [recorder %s] active, recording %q\n", c.id, fr.Name())
	return func() error {
		fmt.Printf("   [recorder %s] cleanup\n", c.id)
		return nil
	}, nil
}

// metricsComp is an independent plugin.
type metricsComp struct{ id string }

func (c *metricsComp) Name() string                  { return "metrics:" + c.id }
func (c *metricsComp) Inject() []runtime.Dependency  { return nil }
func (c *metricsComp) Provide() []runtime.Capability { return nil }
func (c *metricsComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fmt.Printf("   [metrics %s] active\n", c.id)
	return func() error {
		fmt.Printf("   [metrics %s] cleanup\n", c.id)
		return nil
	}, nil
}

// ---------------------------------------------------------------------------
// 2. The host: factories + Config Controller
// ---------------------------------------------------------------------------

// host is the "application shell": it owns a Runtime, a Config Factory
// Registry and a Config Controller. It turns manifests into reality.
type host struct {
	rt   *runtime.Runtime
	reg  config.FactoryRegistry
	ctrl *config.Controller
}

func newHost() (*host, error) {
	rt, err := runtime.New()
	if err != nil {
		return nil, err
	}
	reg := config.NewFactoryRegistry()

	// Register how to build each plugin Type. Factories are the bridge between
	// "a manifest entry" and "a running Component".
	for _, f := range []struct {
		typ string
		reg func(config.ComponentConfig) (runtime.Component, error)
	}{
		{"camera", func(cc config.ComponentConfig) (runtime.Component, error) {
			mode, _ := cc.Config["mode"].(string)
			if mode == "" {
				mode = "default"
			}
			return &cameraComp{id: cc.ID, mode: mode}, nil
		}},
		{"recorder", func(cc config.ComponentConfig) (runtime.Component, error) {
			return &recorderComp{id: cc.ID}, nil
		}},
		{"metrics", func(cc config.ComponentConfig) (runtime.Component, error) {
			return &metricsComp{id: cc.ID}, nil
		}},
	} {
		factory := &adapterFactory{build: f.reg}
		if err := reg.Register(f.typ, factory); err != nil {
			return nil, err
		}
	}
	return &host{rt: rt, reg: reg, ctrl: config.NewController(rt, reg)}, nil
}

// adapterFactory adapts a plain builder func to config.Factory.
type adapterFactory struct {
	build func(config.ComponentConfig) (runtime.Component, error)
}

func (a *adapterFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return a.build(cc)
}

func manifest(comps ...config.ComponentConfig) config.Config {
	return config.Config{Components: comps}
}

func (h *host) reconcile(label string, cfg config.Config) error {
	fmt.Printf("\n== reconcile: %s ==\n", label)
	ctx, cancel := ctxT()
	defer cancel()
	if err := h.ctrl.Reconcile(ctx, cfg); err != nil {
		return err
	}
	// Wait until every owned plugin reaches a settled Active state (Reconcile
	// is async: it hands components to the Kernel, which drives their
	// lifecycle - including dependency ordering - on its own).
	for _, o := range h.ctrl.Owned() {
		if err := waitActive(o.Fiber); err != nil {
			return fmt.Errorf("%s not active after %s: %w", o.ID, label, err)
		}
	}
	// Show what is actually running after reconciliation.
	for _, o := range h.ctrl.Owned() {
		fmt.Printf("   owned id=%-8s type=%-8s fiber=%-9s state=%s\n", o.ID, o.Type, o.Fiber.ID(), o.Fiber.State())
	}
	return nil
}

func (h *host) close() error {
	if err := h.ctrl.Close(); err != nil {
		return err
	}
	return h.rt.Close(context.Background())
}

func main() {
	if err := run(); err != nil {
		log.Fatalf("host: %v", err)
	}
}

func run() error {
	fmt.Println("Dynamic Composable Runtime - minimal plugin host")
	fmt.Println("(plugins are Components; the host reconciles a manifest)")

	h, err := newHost()
	if err != nil {
		return err
	}
	defer h.close()

	// --- manifest v1: camera + recorder (recorder depends on camera) -------
	cameraV1 := config.ComponentConfig{ID: "camera", Type: "camera", Config: map[string]any{"mode": "night"}}
	recorder := config.ComponentConfig{ID: "rec", Type: "recorder"}
	if err := h.reconcile("manifest v1: camera(mode=night) + recorder", manifest(cameraV1, recorder)); err != nil {
		return err
	}
	fmt.Println("   (see above: recorder became active only after the camera provided frames)")
	cam1, _ := owned(h, "camera")
	rec1, _ := owned(h, "rec")

	// --- manifest v2: change camera config => the host REPLACES camera with a
	// NEW fiber; the recorder (a consumer of the camera capability) follows
	// through the Kernel. An independent metrics plugin is added. -----------
	cameraV2 := config.ComponentConfig{ID: "camera", Type: "camera", Config: map[string]any{"mode": "day"}}
	metrics := config.ComponentConfig{ID: "m1", Type: "metrics"}
	if err := h.reconcile("manifest v2: camera(mode=day) + recorder + metrics", manifest(cameraV2, recorder, metrics)); err != nil {
		return err
	}
	cam2, _ := owned(h, "camera")
	rec2, _ := owned(h, "rec")
	m1, _ := owned(h, "m1")

	fmt.Println()
	fmt.Printf("   camera replaced?     old fiber=%s -> new fiber=%s (%v)\n", cam1.Fiber.ID(), cam2.Fiber.ID(), cam1.Fiber.ID() != cam2.Fiber.ID())
	fmt.Printf("   camera old gone?     %v\n", cam1.Fiber.State() == runtime.StateGone)
	fmt.Printf("   recorder fiber:      %s (same fiber, NEW activation - see its re-apply above)\n", rec2.Fiber.ID())
	fmt.Printf("   recorder rebound?    old gone=%v, active=%s\n", rec1.Fiber.State() == runtime.StateGone, rec2.Fiber.State())
	fmt.Printf("   metrics added?       fiber=%s state=%s\n", m1.Fiber.ID(), m1.Fiber.State())

	// --- manifest v3: remove metrics ---------------------------------------
	if err := h.reconcile("manifest v3: remove metrics", manifest(cameraV2, recorder)); err != nil {
		return err
	}
	fmt.Printf("   metrics removed: state=%s\n", m1.Fiber.State())

	fmt.Println("\n== shutdown ==")
	return nil
}

func owned(h *host, id string) (config.OwnedComponent, bool) {
	for _, o := range h.ctrl.Owned() {
		if o.ID == id {
			return o, true
		}
	}
	return config.OwnedComponent{}, false
}
