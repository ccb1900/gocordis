package runtime_test

import (
	"context"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Event value-registration contract (second-batch Event item, docs/r1.md):
// OnValue installs a value-returning handler as the SAME reversible Effect in
// the SAME registry as On/OnWaterfall. The value projection is exposed on the
// EventBinding read model; a plain On binding has a nil ValueHandler.

func newEventValueRuntime(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

type valueProbe struct {
	name  string
	setup func(*runtime.Context) error
	ctx   *runtime.Context
}

func (c *valueProbe) Name() string                  { return c.name }
func (c *valueProbe) Inject() []runtime.Dependency  { return nil }
func (c *valueProbe) Provide() []runtime.Capability { return nil }
func (c *valueProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.ctx = ctx
	if c.setup != nil {
		if err := c.setup(ctx); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func loadValueProbe(t *testing.T, rt *runtime.Runtime, name string, setup func(*runtime.Context) error) (*runtime.Fiber, *valueProbe) {
	t.Helper()
	c := &valueProbe{name: name, setup: setup}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	if err := f.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	return f, c
}

func disposeValueFiber(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
}

// TestOnValueBindingCarriesValueProjection: an OnValue registration is visible
// as ONE binding whose ValueHandler is non-nil; a plain On binding's is nil.
func TestOnValueBindingCarriesValueProjection(t *testing.T) {
	rt := newEventValueRuntime(t)
	key := runtime.NewEventKey[string]("value.shape")
	plainKey := runtime.NewEventKey[string]("value.shape.plain")

	f, p := loadValueProbe(t, rt, "shape-host", func(c *runtime.Context) error {
		if err := runtime.OnValue(c, key, func(context.Context, string) (int, bool, error) {
			return 42, true, nil
		}); err != nil {
			return err
		}
		return runtime.On(c, plainKey, func(context.Context, string) error { return nil })
	})
	defer disposeValueFiber(t, f)

	vb := p.ctx.EventBindings(key.ID())
	if len(vb) != 1 {
		t.Fatalf("value bindings = %d, want 1", len(vb))
	}
	if vb[0].ValueHandler == nil {
		t.Fatal("OnValue binding must expose a non-nil ValueHandler")
	}
	raw, present, err := vb[0].ValueHandler(context.Background(), "x")
	if err != nil || !present || raw != 42 {
		t.Fatalf("ValueHandler = (%v, %v, %v), want (42, true, nil)", raw, present, err)
	}
	// The error projection is still installed so other dispatch modes cannot
	// invoke a nil handler.
	if vb[0].Handler == nil {
		t.Fatal("OnValue binding must keep a non-nil error projection")
	}
	if vb[0].Chain != nil {
		t.Fatal("OnValue binding must not carry a waterfall chain")
	}

	pb := p.ctx.EventBindings(plainKey.ID())
	if len(pb) != 1 {
		t.Fatalf("plain bindings = %d, want 1", len(pb))
	}
	if pb[0].ValueHandler != nil {
		t.Fatal("plain On binding must have a nil ValueHandler")
	}
}

// TestOnValueWithHandleDisposeIsIdempotent: the optional handle retires exactly
// this value registration, twice, and the activation inverse stays harmless.
func TestOnValueWithHandleDisposeIsIdempotent(t *testing.T) {
	rt := newEventValueRuntime(t)
	key := runtime.NewEventKey[int]("value.handle")
	var h *runtime.EventRegistration
	f, p := loadValueProbe(t, rt, "handle-host", func(c *runtime.Context) error {
		var err error
		h, err = runtime.OnValueWithHandle(c, key, func(context.Context, int) (string, bool, error) {
			return "v", true, nil
		})
		return err
	})

	if len(p.ctx.EventBindings(key.ID())) != 1 {
		t.Fatal("value registration not visible after Active")
	}
	h.Dispose()
	h.Dispose() // idempotent
	if n := len(p.ctx.EventBindings(key.ID())); n != 0 {
		t.Fatalf("bindings after dispose = %d, want 0", n)
	}
	disposeValueFiber(t, f) // unwind inverse must not double-remove or panic
}

// TestOnValueRegistrationRemovedOnFiberDispose: value registration is owned by
// the activation Effect, so disposing the component removes it from the shared
// registry (observed from a still-live sibling context).
func TestOnValueRegistrationRemovedOnFiberDispose(t *testing.T) {
	rt := newEventValueRuntime(t)
	key := runtime.NewEventKey[int]("value.dispose")

	_, observer := loadValueProbe(t, rt, "observer", nil)
	valueFiber, _ := loadValueProbe(t, rt, "value-host", func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			return 1, true, nil
		})
	})
	if n := len(observer.ctx.EventBindings(key.ID())); n != 1 {
		t.Fatalf("bindings before dispose = %d, want 1", n)
	}
	disposeValueFiber(t, valueFiber)
	if n := len(observer.ctx.EventBindings(key.ID())); n != 0 {
		t.Fatalf("bindings after component dispose = %d, want 0", n)
	}
}

// TestOnValueRejectsBadArguments: additive API keeps the same declaration-/
// nil-safety guards as On.
func TestOnValueRejectsBadArguments(t *testing.T) {
	rt := newEventValueRuntime(t)
	_, p := loadValueProbe(t, rt, "args-host", nil)
	h := func(context.Context, int) (int, bool, error) { return 0, true, nil }
	if err := runtime.OnValue[int, int](nil, runtime.NewEventKey[int]("k"), h); err == nil {
		t.Fatal("nil context must be rejected")
	}
	if err := runtime.OnValue(p.ctx, runtime.EventKey[int]{}, h); err == nil {
		t.Fatal("zero-value EventKey must be rejected")
	}
	if err := runtime.OnValue[int, int](p.ctx, runtime.NewEventKey[int]("k"), nil); err == nil {
		t.Fatal("nil handler must be rejected")
	}
}
