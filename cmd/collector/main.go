// Command collector is a runnable CSV file collector built on the Dynamic
// Composable Runtime.
//
// It shows the division of labour:
//
//	business code (you):  parse CSV rows, print/sink them (standard library)
//	framework code:       component lifecycle, capability injection, failure
//	                      isolation, and replacement of the running collector
//	                      when the desired config changes.
//
// What runs:
//
//	sinkComp        - a stable capability "RowSink" (accumulates/prints rows)
//	collectorComp   - a plugin: on Apply it opens the configured CSV file,
//	                  parses it and feeds every row to RowSink
//
// Flow:
//  1. reconcile manifest v1 -> collector reads sample1.csv
//  2. reconcile manifest v2 -> point the collector at sample2.csv
//     the config change makes the framework REPLACE the collector with a
//     new execution body (new fiber), which re-collects the new file;
//     the sink stays put and keeps counting.
//  3. manifest v3 adds a collector whose file is missing -> that component
//     becomes Failed, the healthy ones keep running (failure isolation).
//  4. shutdown.
package main

import (
	"context"
	"encoding/csv"
	"fmt"
	"io"
	"log"
	"os"
	"path/filepath"
	"strings"
	"sync/atomic"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Capability: where collected rows go
// ---------------------------------------------------------------------------

// RowSink consumes one parsed CSV row.
type RowSink interface{ Sink(row []string) }

var rowSinkKey = runtime.NewKey[RowSink]("rowsink")

// sinkComp provides the RowSink capability. Its identity is stable: the same
// fiber keeps counting across collector replacements.
type sinkComp struct {
	count atomic.Int32
}

func (c *sinkComp) Name() string                 { return "rowsink" }
func (c *sinkComp) Inject() []runtime.Dependency { return nil }
func (c *sinkComp) Provide() []runtime.Capability {
	return []runtime.Capability{rowSinkKey.Capability()}
}
func (c *sinkComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, rowSinkKey, RowSink(&countingSink{c: &c.count})); err != nil {
		return nil, err
	}
	fmt.Println("   [sink] active")
	return func() error {
		fmt.Println("   [sink] cleanup")
		return nil
	}, nil
}

type countingSink struct{ c *atomic.Int32 }

func (s *countingSink) Sink(row []string) {
	n := s.c.Add(1)
	fmt.Printf("   row #%d: %s\n", n, strings.Join(row, " | "))
}

// ---------------------------------------------------------------------------
// Plugin: the CSV collector
// ---------------------------------------------------------------------------

// collectorComp is the "business plugin": on activation it reads the CSV file
// named by its config and pushes every row into RowSink.
type collectorComp struct {
	id   string
	path string
}

func (c *collectorComp) Name() string { return "collector:" + c.id }
func (c *collectorComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(rowSinkKey)}
}
func (c *collectorComp) Provide() []runtime.Capability { return nil }
func (c *collectorComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	sink, err := runtime.Require(ctx, rowSinkKey)
	if err != nil {
		return nil, err
	}
	fmt.Printf("   [collector %s] collecting %s\n", c.id, c.path)

	f, err := os.Open(c.path)
	if err != nil {
		// A missing/unreadable file is a component (lifecycle) failure: the
		// fiber becomes Failed and is isolated from the rest of the system.
		return nil, fmt.Errorf("open %s: %w", c.path, err)
	}
	r := csv.NewReader(f)
	n := 0
	for {
		row, err := r.Read()
		if err == io.EOF {
			break
		}
		if err != nil {
			f.Close()
			return nil, fmt.Errorf("parse %s: %w", c.path, err)
		}
		sink.Sink(row)
		n++
	}
	if err := f.Close(); err != nil {
		return nil, err
	}
	fmt.Printf("   [collector %s] done: %d rows from %s\n", c.id, n, filepath.Base(c.path))
	return func() error {
		fmt.Printf("   [collector %s] cleanup\n", c.id)
		return nil
	}, nil
}

// ---------------------------------------------------------------------------
// Host: factories + Config Controller + manifests
// ---------------------------------------------------------------------------

type adapterFactory struct {
	build func(config.ComponentConfig) (runtime.Component, error)
}

func (a *adapterFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return a.build(cc)
}

type collector struct {
	rt   *runtime.Runtime
	ctrl *config.Controller
	sink *sinkComp
}

func newCollector() (*collector, error) {
	rt, err := runtime.New()
	if err != nil {
		return nil, err
	}
	reg := config.NewFactoryRegistry()
	// Factory registration: how to build each plugin type from a manifest
	// entry. Note the collector's business config (path) comes from
	// ComponentConfig.Config.
	register := func(typ string, build func(config.ComponentConfig) (runtime.Component, error)) error {
		return reg.Register(typ, &adapterFactory{build: build})
	}
	if err := register("rowsink", func(cc config.ComponentConfig) (runtime.Component, error) {
		return &sinkComp{}, nil
	}); err != nil {
		return nil, err
	}
	if err := register("csvcollector", func(cc config.ComponentConfig) (runtime.Component, error) {
		path, _ := cc.Config["path"].(string)
		return &collectorComp{id: cc.ID, path: path}, nil
	}); err != nil {
		return nil, err
	}
	return &collector{rt: rt, ctrl: config.NewController(rt, reg), sink: &sinkComp{}}, nil
}

func (c *collector) reconcile(label string, comps ...config.ComponentConfig) error {
	fmt.Printf("\n== reconcile: %s ==\n", label)
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	if err := c.ctrl.Reconcile(ctx, config.Config{Components: comps}); err != nil {
		return err
	}
	// The framework drives lifecycle asynchronously; wait until everything
	// settles to Active before showing state.
	for _, o := range c.ctrl.Owned() {
		if err := o.Fiber.Ready(ctx); err != nil {
			return fmt.Errorf("%s not active: %w", o.ID, err)
		}
	}
	c.printOwned()
	return nil
}

func (c *collector) printOwned() {
	for _, o := range c.ctrl.Owned() {
		fmt.Printf("   owned id=%-10s type=%-12s fiber=%-9s state=%s\n", o.ID, o.Type, o.Fiber.ID(), o.Fiber.State())
	}
}

func (c *collector) close() error {
	_ = c.ctrl.Close()
	return c.rt.Close(context.Background())
}

func main() {
	if err := run(); err != nil {
		log.Fatalf("collector: %v", err)
	}
}

func writeSample(tag, rows string) string {
	dir, err := os.MkdirTemp("", "csvcollect-*")
	if err != nil {
		log.Fatal(err)
	}
	path := filepath.Join(dir, tag)
	if err := os.WriteFile(path, []byte(rows), 0o644); err != nil {
		log.Fatal(err)
	}
	return path
}

func run() error {
	// Prepare two CSV files the collector will ingest.
	sample1 := writeSample("sample1.csv", "id,name,score\n1,alice,90\n2,bob,85\n3,carol,95\n")
	sample2 := writeSample("sample2.csv", "id,name,score\n4,dave,88\n5,eve,92\n")

	c, err := newCollector()
	if err != nil {
		return err
	}
	defer c.close()

	sink := config.ComponentConfig{ID: "sink", Type: "rowsink"}
	colV1 := config.ComponentConfig{ID: "col", Type: "csvcollector", Config: map[string]any{"path": sample1}}
	colV2 := config.ComponentConfig{ID: "col", Type: "csvcollector", Config: map[string]any{"path": sample2}}

	// 1) manifest v1: sink + collector(sample1)
	if err := c.reconcile("manifest v1: collect sample1.csv", sink, colV1); err != nil {
		return err
	}

	// 2) manifest v2: point the same collector at sample2.csv. The config
	// change makes the framework replace the collector with a NEW execution
	// body (new fiber re-collects sample2); the sink fiber is untouched and
	// its counter keeps running.
	col1, _ := owned(c, "col")
	sink1, _ := owned(c, "sink")
	if err := c.reconcile("manifest v2: switch collector to sample2.csv", sink, colV2); err != nil {
		return err
	}
	col2, _ := owned(c, "col")
	sink2, _ := owned(c, "sink")
	fmt.Println()
	fmt.Printf("   collector replaced? old fiber=%s -> new fiber=%s (%v)\n", col1.Fiber.ID(), col2.Fiber.ID(), col1.Fiber.ID() != col2.Fiber.ID())
	fmt.Printf("   sink untouched?     fiber=%s == %s (%v)\n", sink1.Fiber.ID(), sink2.Fiber.ID(), sink1.Fiber.ID() == sink2.Fiber.ID())

	// 3) manifest v3: add a collector whose file is missing. Note: Reconcile
	// succeeds here - it only means the component was handed to the Runtime
	// (Applied). The Apply-time failure happens asynchronously on the fiber:
	// the collector becomes Failed, which you observe through the fiber (e.g.
	// Ready returns the error). This is component failure isolation: the
	// healthy collector keeps running.
	missing := filepath.Join(filepath.Dir(sample1), "does-not-exist.csv")
	bad := config.ComponentConfig{ID: "col-bad", Type: "csvcollector", Config: map[string]any{"path": missing}}
	fmt.Println("\n== reconcile: manifest v3 (add collector with a missing file) ==")
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	if err := c.ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{sink, colV2, bad}}); err != nil {
		return fmt.Errorf("reconcile: %w", err)
	}
	fmt.Println("   (Reconcile returned nil: the config was applied; the failure will show on the fiber)")
	good, _ := owned(c, "col")
	badF, ok := owned(c, "col-bad")
	if !ok {
		return fmt.Errorf("bad collector should be present (applied but failing)")
	}
	// Waiting for the bad collector surfaces its Apply error (-> Failed).
	if err := badF.Fiber.Ready(ctx); err == nil {
		return fmt.Errorf("bad collector should have failed")
	} else {
		fmt.Printf("   bad collector Ready error: %v\n", err)
	}
	fmt.Printf("   healthy collector state=%s (unaffected)\n", good.Fiber.State())
	fmt.Printf("   bad collector      state=%s (isolated failure)\n", badF.Fiber.State())

	// 4) drop the bad collector and shut down.
	if err := c.ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{sink, colV2}}); err != nil {
		return err
	}
	fmt.Println("\n== shutdown ==")
	return nil
}

func owned(c *collector, id string) (config.OwnedComponent, bool) {
	for _, o := range c.ctrl.Owned() {
		if o.ID == id {
			return o, true
		}
	}
	return config.OwnedComponent{}, false
}
