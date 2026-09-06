package runtime_test

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

type noopComponent struct{ name string }

func (c noopComponent) Name() string                  { return c.name }
func (c noopComponent) Inject() []runtime.Dependency  { return nil }
func (c noopComponent) Provide() []runtime.Capability { return nil }
func (c noopComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}

// fakeComponent is a controllable component whose Apply signals invocation and
// returns a configurable result.
type fakeComponent struct {
	name     string
	inject   []runtime.Dependency
	provide  []runtime.Capability
	applyCh  chan struct{}
	cleanup  func() error
	applyErr error
	applyFn  func(*runtime.Context) (runtime.Cleanup, error)
	calls    atomic.Int32
}

func (c *fakeComponent) Inject() []runtime.Dependency  { return c.inject }
func (c *fakeComponent) Provide() []runtime.Capability { return c.provide }

func newFakeComponent(name string) *fakeComponent {
	return &fakeComponent{name: name, applyCh: make(chan struct{}, 16)}
}

func (c *fakeComponent) Name() string { return c.name }
func (c *fakeComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.calls.Add(1)
	select {
	case c.applyCh <- struct{}{}:
	default:
	}
	if c.applyFn != nil {
		return c.applyFn(ctx)
	}
	return c.cleanup, c.applyErr
}

func testTimeout(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newTestRuntime(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New() error: %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return rt
}

// CT7 / P9 (part): Dispose must be idempotent; repeated Dispose calls must not
// panic and must produce a single Gone.
func TestDisposeIdempotentEndsGone(t *testing.T) {
	rt := newTestRuntime(t)
	comp := newFakeComponent("plain")
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}

	if err := f.Dispose(); err != nil {
		t.Fatalf("first Dispose error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("second Dispose error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("third Dispose error: %v", err)
	}

	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone() error: %v", err)
	}
	if got := f.State(); got != runtime.StateGone {
		t.Fatalf("state = %v, want Gone", got)
	}
}

func TestLoadReturnsDistinctStableIDs(t *testing.T) {
	rt := newTestRuntime(t)

	seen := map[runtime.FiberID]bool{}
	for i := 0; i < 8; i++ {
		f, err := rt.Load(noopComponent{name: "c"})
		if err != nil {
			t.Fatalf("Load error: %v", err)
		}
		if f.ID() == 0 {
			t.Fatal("FiberID 0 is reserved as invalid; Load returned it")
		}
		if seen[f.ID()] {
			t.Fatalf("duplicate FiberID %v", f.ID())
		}
		seen[f.ID()] = true
		// ID must be stable across reads.
		if f.ID() != f.ID() {
			t.Fatal("Fiber.ID() not stable")
		}
	}
}

func TestLoadNilComponentRejected(t *testing.T) {
	rt := newTestRuntime(t)
	if _, err := rt.Load(nil); err == nil {
		t.Fatal("Load(nil) must return an error")
	}
}

func TestFiberNameAndInitialErr(t *testing.T) {
	rt := newTestRuntime(t)
	f, err := rt.Load(noopComponent{name: "hello"})
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if f.Name() != "hello" {
		t.Fatalf("Name() = %q, want hello", f.Name())
	}
	if f.Component().Name() != "hello" {
		t.Fatalf("Component().Name() = %q", f.Component().Name())
	}
	if err := f.Err(); err != nil {
		t.Fatalf("fresh fiber Err() = %v, want nil", err)
	}
}

// Ready on a fiber that is disposed before ever activating must report Gone,
// not hang.
func TestReadyAfterDisposeReportsGone(t *testing.T) {
	rt := newTestRuntime(t)
	f, err := rt.Load(noopComponent{name: "x"})
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	err = f.Ready(testTimeout(t))
	if !errors.Is(err, runtime.ErrFiberGone) {
		t.Fatalf("Ready() = %v, want ErrFiberGone", err)
	}
}
