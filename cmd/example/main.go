// Command example demonstrates how to use the Dynamic Composable Runtime.
//
// Part 1 - Kernel: the canonical dependency chain
//
//		Logger
//		  ↑
//		Database
//		  ↑
//		Service
//
//	  - components are declared (Inject/Provide) and their Apply uses Context
//	    to Require dependencies and Provide capabilities;
//	  - the Kernel resolves dependencies, withdraws consumers first, and lands a
//	    mounted fiber in Pending when its dependency disappears;
//	  - recovery is a normal new activation.
//
// Part 2 - Loader + HMR: a running component implementation is replaced
// (warm replacement) using the Loader Extension and the HMR Extension.
package main

import (
	"context"
	"fmt"
	"log"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

func main() {
	if err := kernelDemo(); err != nil {
		log.Fatalf("kernel demo: %v", err)
	}
	fmt.Println()
	if err := hmrDemo(); err != nil {
		log.Fatalf("hmr demo: %v", err)
	}
}

// ---------------------------------------------------------------------------
// Shared types for the kernel demo
// ---------------------------------------------------------------------------

// Logger is the capability provided by the Logger component.
type Logger interface{ Log(msg string) }

type consoleLogger struct{ prefix string }

func (l consoleLogger) Log(msg string) {
	fmt.Printf("   [log %s] %s\n", l.prefix, msg)
}

// Database is the capability provided by the Database component.
type Database interface {
	Query() string
}

type fakeDB struct{ addr string }

func (d fakeDB) Query() string { return "rows from " + d.addr }

var (
	loggerKey   = runtime.NewKey[Logger]("logger")
	databaseKey = runtime.NewKey[Database]("database")
)

func step(name string) {
	fmt.Printf("\n== %s ==\n", name)
}

func waitActive(f *runtime.Fiber) error {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	return f.Ready(ctx)
}

func waitInactive(f *runtime.Fiber) error {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	return f.WaitInactive(ctx)
}

func waitGone(f *runtime.Fiber) error {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	return f.Gone(ctx)
}

// loggerComp provides Logger.
type loggerComp struct{}

func (loggerComp) Name() string                  { return "logger" }
func (loggerComp) Inject() []runtime.Dependency  { return nil }
func (loggerComp) Provide() []runtime.Capability { return []runtime.Capability{loggerKey.Capability()} }
func (loggerComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	// Provide is a reversible effect: it is unregistered when the fiber
	// withdraws.
	if err := runtime.Provide(ctx, loggerKey, Logger(consoleLogger{prefix: "logger"})); err != nil {
		return nil, err
	}
	fmt.Println("   [logger] active")
	return func() error {
		fmt.Println("   [logger] cleanup")
		return nil
	}, nil
}

// dbComp requires Logger and provides Database.
type dbComp struct{}

func (dbComp) Name() string                  { return "database" }
func (dbComp) Inject() []runtime.Dependency  { return []runtime.Dependency{runtime.Requires(loggerKey)} }
func (dbComp) Provide() []runtime.Capability { return []runtime.Capability{databaseKey.Capability()} }
func (dbComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	lg, err := runtime.Require(ctx, loggerKey)
	if err != nil {
		return nil, err
	}
	lg.Log("opening database connection")
	if err := runtime.Provide(ctx, databaseKey, Database(fakeDB{addr: "db.primary:5432"})); err != nil {
		return nil, err
	}
	fmt.Println("   [database] active")
	return func() error {
		fmt.Println("   [database] cleanup")
		return nil
	}, nil
}

// svcComp requires Database.
type svcComp struct{}

func (svcComp) Name() string { return "service" }
func (svcComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(databaseKey)}
}
func (svcComp) Provide() []runtime.Capability { return nil }
func (svcComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	db, err := runtime.Require(ctx, databaseKey)
	if err != nil {
		return nil, err
	}
	fmt.Printf("   [service] active, db says %q\n", db.Query())
	return func() error {
		fmt.Println("   [service] cleanup")
		return nil
	}, nil
}

func kernelDemo() error {
	step("Kernel: Logger -> Database -> Service")

	rt, err := runtime.New()
	if err != nil {
		return err
	}
	defer rt.Close(context.Background())

	// Load the three components. Load is asynchronous: fibers may rest in
	// Pending until their dependencies are satisfied.
	loggerF, err := rt.Load(loggerComp{})
	if err != nil {
		return err
	}
	dbF, err := rt.Load(dbComp{})
	if err != nil {
		return err
	}
	svcF, err := rt.Load(svcComp{})
	if err != nil {
		return err
	}

	// The Kernel resolves the dependency chain; each fiber becomes Active in
	// dependency order (logger -> database -> service).
	for _, f := range []*runtime.Fiber{loggerF, dbF, svcF} {
		if err := waitActive(f); err != nil {
			return fmt.Errorf("%s did not activate: %w", f.Name(), err)
		}
	}
	fmt.Printf("   states: logger=%s database=%s service=%s\n",
		loggerF.State(), dbF.State(), svcF.State())

	// Provider loss: disposing the database withdraws the service first
	// (consumer-first). The service is still Mounted, so it lands in Pending -
	// not Failed, not Gone.
	step("Dependency loss: dispose database")
	if err := dbF.Dispose(); err != nil {
		return err
	}
	if err := waitInactive(svcF); err != nil {
		return err
	}
	fmt.Printf("   after db disposal: service=%s (err=%v)\n", svcF.State(), svcF.Err())
	if err := waitGone(dbF); err != nil {
		return err
	}
	fmt.Printf("   database=%s\n", dbF.State())

	// Recovery: remounting the database is a brand-new activation; the service
	// reactivates onto the new provider generation.
	step("Recovery: remount database")
	if err := dbF.Load(); err != nil {
		return err
	}
	if err := waitActive(dbF); err != nil {
		return err
	}
	if err := waitActive(svcF); err != nil {
		return err
	}
	fmt.Printf("   after recovery: database=%s service=%s\n", dbF.State(), svcF.State())

	// Tear down and close the runtime.
	step("Shutdown")
	for _, f := range []*runtime.Fiber{svcF, dbF, loggerF} {
		_ = f.Dispose()
	}
	for _, f := range []*runtime.Fiber{svcF, dbF, loggerF} {
		if err := waitGone(f); err != nil {
			return err
		}
	}
	if err := rt.Close(context.Background()); err != nil {
		return err
	}
	fmt.Println("   runtime closed")
	return nil
}

// ---------------------------------------------------------------------------
// Loader + HMR demo
// ---------------------------------------------------------------------------

type greeter interface{ Greet() string }

type greeterV struct{ tag string }

func (g greeterV) Greet() string { return "hello from " + g.tag }

// greeterComponent is built by the Loader module factories.
type greeterComponent struct {
	id  string
	tag string
}

func (c *greeterComponent) Name() string                  { return c.tag + ":" + c.id }
func (c *greeterComponent) Inject() []runtime.Dependency  { return nil }
func (c *greeterComponent) Provide() []runtime.Capability { return nil }
func (c *greeterComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	fmt.Printf("   [greeter:%s] active\n", c.tag)
	return func() error {
		fmt.Printf("   [greeter:%s] cleanup\n", c.tag)
		return nil
	}, nil
}

// versionedFactory builds a config.Factory producing tagged greeter components.
type versionedFactory struct{ tag string }

func (f *versionedFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return &greeterComponent{id: cc.ID, tag: f.tag}, nil
}

func hmrDemo() error {
	step("Loader + HMR: warm replacement of a running implementation")

	rt, err := runtime.New()
	if err != nil {
		return err
	}
	defer rt.Close(context.Background())

	ld := loader.NewBuiltinLoader()
	defer ld.CloseContext(context.Background())

	// Register two in-process implementations ("versions").
	if err := ld.RegisterBuiltin("builtin://greeter-v1", func() config.Factory {
		return &versionedFactory{tag: "v1"}
	}); err != nil {
		return err
	}
	if err := ld.RegisterBuiltin("builtin://greeter-v2", func() config.Factory {
		return &versionedFactory{tag: "v2"}
	}); err != nil {
		return err
	}

	// Load the v1 module, create a component from its Factory, and load the
	// fiber (this is the "old implementation").
	m1, err := ld.Load(context.Background(), loader.Artifact{
		ID: "greeter-v1", Type: "greeter", Source: "builtin://greeter-v1", Version: "1",
	})
	if err != nil {
		return err
	}
	comp, err := m1.Factory.Create(config.ComponentConfig{ID: "greeter", Type: m1.Type})
	if err != nil {
		return err
	}
	oldFiber, err := rt.Load(comp)
	if err != nil {
		return err
	}
	if err := waitActive(oldFiber); err != nil {
		return err
	}
	fmt.Printf("   running v1 fiber=%s component=%q\n", oldFiber.ID(), oldFiber.Component().Name())

	// HMR target: the desired NEW implementation is v2.
	h := hmr.New(rt, ld, ld.Usage())
	defer h.CloseContext(context.Background())

	tgt := hmr.Target{
		ID:          "greeter",
		ComponentID: "greeter",
		Artifact: loader.Artifact{
			ID: "greeter-v2", Type: "greeter", Source: "builtin://greeter-v2", Version: "2",
		},
	}
	if err := h.Register(tgt); err != nil {
		return err
	}
	// Bind the currently running implementation (old module + fiber).
	if err := h.Bind("greeter", *m1, oldFiber); err != nil {
		return err
	}

	// Warm replacement: the new module is loaded, its fiber reaches Active,
	// and only then is the old fiber unloaded.
	if err := h.Replace(context.Background(), tgt); err != nil {
		return err
	}
	b, ok := h.CurrentBinding("greeter")
	if !ok {
		return fmt.Errorf("no binding after replacement")
	}
	fmt.Printf("   replaced: old fiber=%s (state=%s) -> new fiber=%s (state=%s)\n",
		oldFiber.ID(), oldFiber.State(), b.Fiber.ID(), b.Fiber.State())
	fmt.Printf("   new module=%s component=%q\n", b.Module.ID, b.Fiber.Component().Name())
	fmt.Printf("   old module still in use by HMR? %v\n", ld.Usage().InUse("greeter-v1"))

	// Shut HMR down: it releases its Loader usage.
	if err := h.Close(); err != nil {
		return err
	}
	fmt.Printf("   after HMR close: old module in use? %v\n", ld.Usage().InUse("greeter-v1"))
	return nil
}
