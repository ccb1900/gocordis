package wasm

import (
	"fmt"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// wasmFactory is the config.Factory produced by the WASM Backend for one
// validated module. It is the only abstraction Config/Runtime sees from the
// WASM world.
//
// Factory responsibilities are strictly bounded: Create builds a Component; it
// never creates a Fiber, never calls runtime.Load, never touches Provider /
// Registry / Config state.
type wasmFactory struct {
	backend *Backend
	ref     *moduleRef
}

// componentConfigKeys are ComponentConfig.Config keys interpreted by the
// WASM-backed Component (v0.1 test/diagnostics knobs only).
const (
	// cfgKeyFailApply, when true, makes Apply fail AFTER the WASM instance was
	// materialized, so the Kernel unwind must destroy the instance (no leak).
	cfgKeyFailApply = "fail_apply"
)

func (f *wasmFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	if cc.ID == "" {
		return nil, fmt.Errorf("%w: empty component id", ErrWASMFactory)
	}
	fail, _ := cc.Config[cfgKeyFailApply].(bool)
	return &wasmComponent{
		id:        cc.ID,
		backend:   f.backend,
		ref:       f.ref,
		failApply: fail,
	}, nil
}

// wasmComponent is a Runtime Component whose activation materializes exactly
// one fresh WASM instance owned by that activation, and drives the minimal
// runtime_component_create / runtime_component_destroy ABI on it.
//
// Lifecycle wiring (Kernel is the only authority):
//
//	Apply
//	  └── ctx.Effect(install = instantiate + create,
//	                  inverse  = destroy + close instance)
//
// Unloading unwinds the effect -> instance destroyed -> Gone/Failed/Pending is
// decided entirely by the Kernel.
type wasmComponent struct {
	id        string
	backend   *Backend
	ref       *moduleRef
	failApply bool
}

func (c *wasmComponent) Name() string                  { return "wasm:" + c.id }
func (c *wasmComponent) Inject() []runtime.Dependency  { return nil }
func (c *wasmComponent) Provide() []runtime.Capability { return nil }

func (c *wasmComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	// Materialize the per-activation WASM instance and begin the component
	// activation as one Runtime-managed effect. If instantiation or create
	// fails nothing is committed (and a partial instance is destroyed
	// immediately, so no orphan remains); if the effect is committed the
	// Kernel runs Destroy exactly once when the activation ends.
	if err := ctx.Effect(func() (func() error, error) {
		inst, err := c.backend.instantiate(ctx.Context(), c.ref)
		if err != nil {
			return nil, err
		}
		if err := inst.begin(ctx.Context()); err != nil {
			_ = inst.Destroy() // partial cleanup: never leave an orphan instance
			return nil, err
		}
		return inst.Destroy, nil
	}); err != nil {
		return nil, err
	}

	// Diagnostic knob: fail after instance init to prove the Kernel unwind
	// destroys the instance (no leak) and the Fiber lands in Failed.
	if c.failApply {
		return nil, fmt.Errorf("wasm component %q: injected apply failure after instance init", c.id)
	}
	return nil, nil
}
