// Package integration contains end-to-end integration and semantic-hardening
// tests. It composes the real Runtime Kernel and every Extension without
// modifying any production package.
package integration

import (
	"context"
	"fmt"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/registry"
	"dynamic-runtime/runtime"
)

// Keys for cross-component capabilities used by the fixture components.
var (
	capKey  = runtime.NewKey[capVal]("cap")
	capAKey = runtime.NewKey[capVal]("capA")
	regKey  = runtime.NewKey[*registry.Registry[string]]("reg")
)

// aComp requires capKey and provides capAKey (mid-level dependency).
type aComp struct {
	kit *kit
	id  string
}

func (c *aComp) Name() string                  { return "A:" + c.id }
func (c *aComp) Inject() []runtime.Dependency  { return []runtime.Dependency{runtime.Requires(capKey)} }
func (c *aComp) Provide() []runtime.Capability { return []runtime.Capability{capAKey.Capability()} }
func (c *aComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.kit != nil {
		c.kit.rec()
	}
	if _, err := runtime.Require(ctx, capKey); err != nil {
		return nil, err
	}
	if err := runtime.Provide(ctx, capAKey, capVal{tag: "A"}); err != nil {
		return nil, err
	}
	return func() error {
		if c.kit != nil {
			c.kit.done()
		}
		return nil
	}, nil
}

// bComp requires capAKey (leaf consumer of the mid-level provider).
type bComp struct {
	kit *kit
	id  string
}

func (c *bComp) Name() string                  { return "B:" + c.id }
func (c *bComp) Inject() []runtime.Dependency  { return []runtime.Dependency{runtime.Requires(capAKey)} }
func (c *bComp) Provide() []runtime.Capability { return nil }
func (c *bComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.kit != nil {
		c.kit.rec()
	}
	if _, err := runtime.Require(ctx, capAKey); err != nil {
		return nil, err
	}
	return func() error {
		if c.kit != nil {
			c.kit.done()
		}
		return nil
	}, nil
}

type capVal struct{ tag string }

// kit carries shared observables for one component kind.
type kit struct {
	applies  atomic.Int32
	inverses atomic.Int32
	seen     chan string
}

func (k *kit) rec()        { k.applies.Add(1) }
func (k *kit) done() int32 { return k.inverses.Add(1) }

// testComp is the minimal test component model: it can provide capKey, require
// capKey, be independent, or own a registry capability.
type testComp struct {
	kit *kit

	id        string
	tag       string
	kind      string // provider | consumer | independent | regowner | regconsumer
	provide   []runtime.Capability
	inject    []runtime.Dependency
	reg       *registry.Registry[string]
	failApply bool
}

func (c *testComp) Name() string                  { return c.kind + ":" + c.id + ":" + c.tag }
func (c *testComp) Inject() []runtime.Dependency  { return c.inject }
func (c *testComp) Provide() []runtime.Capability { return c.provide }
func (c *testComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.kit != nil {
		c.kit.rec()
	}
	switch c.kind {
	case "provider":
		if err := runtime.Provide(ctx, capKey, capVal{tag: c.tag}); err != nil {
			return nil, err
		}
	case "consumer":
		v, err := runtime.Require(ctx, capKey)
		if err != nil {
			return nil, err
		}
		if c.kit != nil && c.kit.seen != nil {
			select {
			case c.kit.seen <- v.tag:
			default:
			}
		}
	case "regowner":
		reg := registry.New[string]()
		c.reg = reg
		if err := runtime.Provide(ctx, regKey, reg); err != nil {
			return nil, err
		}
	case "regconsumer":
		if _, err := runtime.Require(ctx, regKey); err != nil {
			return nil, err
		}
	}
	if c.failApply {
		return nil, fmt.Errorf("injected apply failure for %s", c.id)
	}
	return func() error {
		if c.kit != nil {
			c.kit.done()
		}
		return nil
	}, nil
}

func providerComp(kit *kit, id, tag string, fail bool) *testComp {
	return &testComp{kit: kit, id: id, tag: tag, kind: "provider", provide: []runtime.Capability{capKey.Capability()}, failApply: fail}
}
func consumerComp(kit *kit, id string) *testComp {
	return &testComp{kit: kit, id: id, kind: "consumer", inject: []runtime.Dependency{runtime.Requires(capKey)}}
}
func independentComp(kit *kit, id string, fail bool) *testComp {
	return &testComp{kit: kit, id: id, kind: "independent", failApply: fail}
}
func regOwnerComp(kit *kit, id string) *testComp {
	return &testComp{kit: kit, id: id, kind: "regowner", provide: []runtime.Capability{regKey.Capability()}}
}
func regConsumerComp(kit *kit, id string) *testComp {
	return &testComp{kit: kit, id: id, kind: "regconsumer", inject: []runtime.Dependency{runtime.Requires(regKey)}}
}

// adapterFactory adapts a builder func to config.Factory.
type adapterFactory struct {
	build func(config.ComponentConfig) (runtime.Component, error)
}

func (a *adapterFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return a.build(cc)
}

// env bundles a runtime + config controller bound to a factory registry.
type env struct {
	rt   *runtime.Runtime
	reg  config.FactoryRegistry
	ctrl *config.Controller
}

func newEnv(t *testing.T, factory func(cc config.ComponentConfig) (runtime.Component, error), types ...string) *env {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	reg := config.NewFactoryRegistry()
	for _, typ := range types {
		if err := reg.Register(typ, &adapterFactory{build: factory}); err != nil {
			t.Fatal(err)
		}
	}
	ctrl := config.NewController(rt, reg)
	e := &env{rt: rt, reg: reg, ctrl: ctrl}
	t.Cleanup(func() {
		_ = ctrl.CloseContext(shortCtx())
		_ = rt.Close(context.Background())
	})
	return e
}

func ctxT(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func shortCtx() context.Context {
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	_ = cancel
	return ctx
}

// eventually polls cond until it holds or the timeout elapses.
func eventually(t *testing.T, d time.Duration, what string, cond func() bool) {
	t.Helper()
	deadline := time.Now().Add(d)
	for time.Now().Before(deadline) {
		if cond() {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("condition not met within %v: %s", d, what)
}

func waitActive(t *testing.T, fs ...*runtime.Fiber) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	for _, f := range fs {
		if err := f.Ready(ctx); err != nil {
			t.Fatalf("%s not active: %v", f.Name(), err)
		}
	}
}

func fiberOf(t *testing.T, e *env, id string) *runtime.Fiber {
	t.Helper()
	for _, o := range e.ctrl.Owned() {
		if o.ID == id {
			return o.Fiber
		}
	}
	t.Fatalf("component %q not owned", id)
	return nil
}

func count(e *env) int { return len(e.ctrl.Owned()) }
