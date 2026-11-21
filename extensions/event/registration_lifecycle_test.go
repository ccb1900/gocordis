package event_test

import (
	"context"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	. "dynamic-runtime/extensions/event"
	"dynamic-runtime/runtime"
)

// JS Core parity (cordis 5e2e6c1, packages/core/src/events.ts): ctx.on() /
// ctx.once() return a callable disposer; `once` removes itself BEFORE invoking
// the listener; a listener removed during dispatch is skipped in that same
// batch while one added during dispatch waits for the next batch. These tests
// exercise the REAL runtime registry, effect-owned inverses and dispatch modes
// — no snapshot doubles and no sleep polling.

var (
	regKey      = runtime.NewEventKey[int]("reg.lifecycle")
	regOnceKey  = runtime.NewEventKey[int]("reg.once")
	regSkipKey  = runtime.NewEventKey[int]("reg.skip")
	regAddedKey = runtime.NewEventKey[int]("reg.added")
)

type regSink struct {
	active chan runtime.FiberID
	ended  chan runtime.FiberID
}

func (s *regSink) Emit(ev runtime.RuntimeEvent) {
	switch ev.Type {
	case runtime.EventActivationActive:
		select {
		case s.active <- ev.FiberID:
		default:
		}
	case runtime.EventActivationEnded:
		select {
		case s.ended <- ev.FiberID:
		default:
		}
	}
}

type regRuntime struct {
	rt   *runtime.Runtime
	sink *regSink
}

func newRegRuntime(t *testing.T) *regRuntime {
	t.Helper()
	sink := &regSink{active: make(chan runtime.FiberID, 16), ended: make(chan runtime.FiberID, 16)}
	rt, err := runtime.New(runtime.WithEventSink(sink))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	return &regRuntime{rt: rt, sink: sink}
}

func (r *regRuntime) waitActive(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.NewTimer(8 * time.Second)
	defer deadline.Stop()
	for {
		select {
		case id := <-r.sink.active:
			if id == f.ID() {
				return
			}
		case <-deadline.C:
			t.Fatalf("fiber %s never became Active", f.Name())
		}
	}
}

func (r *regRuntime) waitEnded(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.NewTimer(8 * time.Second)
	defer deadline.Stop()
	for {
		select {
		case id := <-r.sink.ended:
			if id == f.ID() {
				return
			}
		case <-deadline.C:
			t.Fatalf("fiber %s activation never ended", f.Name())
		}
	}
}

type regHost struct {
	name  string
	setup func(*runtime.Context) error
	ctx   *runtime.Context
}

func (c *regHost) Name() string          { return c.name }
func (c *regHost) Inject() []Dependency  { return nil }
func (c *regHost) Provide() []Capability { return nil }
func (c *regHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.ctx = ctx
	if c.setup != nil {
		if err := c.setup(ctx); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func (r *regRuntime) loadHost(t *testing.T, h *regHost) *runtime.Fiber {
	t.Helper()
	f, err := r.rt.Load(h)
	if err != nil {
		t.Fatal(err)
	}
	r.waitActive(t, f)
	return f
}

// TestOnWithHandleManualDisposeIsIdempotentAcrossUnwind: a handler can be
// unregistered early by its owner, twice, and the activation's own inverse
// must not double-remove or panic.
func TestOnWithHandleManualDisposeIsIdempotentAcrossUnwind(t *testing.T) {
	r := newRegRuntime(t)
	var handle *runtime.EventRegistration
	calls := 0
	host := &regHost{name: "handle-host", setup: func(c *runtime.Context) error {
		var err error
		handle, err = runtime.OnWithHandle(c, regKey, func(context.Context, int) error {
			calls++
			return nil
		})
		return err
	}}
	f := r.loadHost(t, host)

	if len(host.ctx.EventBindings(regKey.ID())) != 1 {
		t.Fatal("registration not visible after Active")
	}
	if err := Emit(host.ctx, regKey, 1); err != nil || calls != 1 {
		t.Fatalf("emit: err=%v calls=%d", err, calls)
	}
	handle.Dispose()
	if len(host.ctx.EventBindings(regKey.ID())) != 0 {
		t.Fatal("manual dispose did not remove the binding")
	}
	handle.Dispose() // idempotent
	if err := Emit(host.ctx, regKey, 2); err != nil || calls != 1 {
		t.Fatalf("emit after dispose: err=%v calls=%d", err, calls)
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	r.waitEnded(t, f)
	if calls != 1 {
		t.Fatalf("unwind changed invocation count: %d", calls)
	}
}

// TestOnOnceRemovesBeforeInvocation: the binding is gone while the listener
// runs, so recursive emit in the same call stack cannot re-enter it.
func TestOnOnceRemovesBeforeInvocation(t *testing.T) {
	r := newRegRuntime(t)
	calls := 0
	bindingsInside := -1
	var reentrant error
	host := &regHost{name: "once-host", setup: func(c *runtime.Context) error {
		return runtime.OnOnce(c, regOnceKey, func(ctx context.Context, n int) error {
			calls++
			bindingsInside = len(c.EventBindings(regOnceKey.ID()))
			if n == 1 {
				reentrant = Emit(c, regOnceKey, 2)
			}
			return nil
		})
	}}
	r.loadHost(t, host)

	if err := Emit(host.ctx, regOnceKey, 1); err != nil {
		t.Fatal(err)
	}
	if calls != 1 {
		t.Fatalf("once listener ran %d times, want 1", calls)
	}
	if bindingsInside != 0 {
		t.Fatalf("once listener saw %d bindings, want 0 (remove-before-invoke)", bindingsInside)
	}
	if reentrant != nil {
		t.Fatalf("recursive emit failed: %v", reentrant)
	}
	if err := Emit(host.ctx, regOnceKey, 3); err != nil || calls != 1 {
		t.Fatalf("second emit: err=%v calls=%d", err, calls)
	}
}

// TestDispatchSkipsListenerRemovedInSameBatch: the snapshot is captured once,
// but each binding stays live-checked, matching JS events.ts:110-112.
func TestDispatchSkipsListenerRemovedInSameBatch(t *testing.T) {
	r := newRegRuntime(t)
	var bHandle *runtime.EventRegistration
	aCalls, bCalls := 0, 0
	host := &regHost{name: "skip-host", setup: func(c *runtime.Context) error {
		if err := runtime.On(c, regSkipKey, func(context.Context, int) error {
			aCalls++
			bHandle.Dispose()
			return nil
		}); err != nil {
			return err
		}
		var err error
		bHandle, err = runtime.OnWithHandle(c, regSkipKey, func(context.Context, int) error {
			bCalls++
			return nil
		})
		return err
	}}
	r.loadHost(t, host)

	if err := Emit(host.ctx, regSkipKey, 1); err != nil {
		t.Fatal(err)
	}
	if aCalls != 1 || bCalls != 0 {
		t.Fatalf("first batch: A=%d B=%d, want 1/0", aCalls, bCalls)
	}
	if err := Emit(host.ctx, regSkipKey, 2); err != nil {
		t.Fatal(err)
	}
	if aCalls != 2 || bCalls != 0 {
		t.Fatalf("second batch: A=%d B=%d, want 2/0", aCalls, bCalls)
	}
}

// TestAllDispatchModesSkipRemovedListener: parity is per mode, not just Emit.
// Parallel starts handlers concurrently, so a listener removed BY another
// listener is scheduling-dependent (JS parallel starts in a synchronous map
// pass, Go starts goroutines). Its live check is therefore tested with a
// deterministic pre-dispatch removal; ordered modes remove mid-batch.
func TestAllDispatchModesSkipRemovedListener(t *testing.T) {
	for _, tc := range []struct {
		name       string
		key        runtime.EventKey[int]
		preDispose bool
		dispatch   func(*runtime.Context, runtime.EventKey[int]) error
	}{
		{"serial", runtime.NewEventKey[int]("reg.mode.serial"), false, func(c *runtime.Context, k runtime.EventKey[int]) error {
			return Serial(context.Background(), c, k, 1)
		}},
		{"parallel", runtime.NewEventKey[int]("reg.mode.parallel"), true, func(c *runtime.Context, k runtime.EventKey[int]) error {
			return Parallel(context.Background(), c, k, 1)
		}},
		{"bail", runtime.NewEventKey[int]("reg.mode.bail"), false, func(c *runtime.Context, k runtime.EventKey[int]) error {
			return Bail(context.Background(), c, k, 1)
		}},
		{"waterfall", runtime.NewEventKey[int]("reg.mode.waterfall"), false, func(c *runtime.Context, k runtime.EventKey[int]) error {
			return Waterfall(context.Background(), c, k, 1)
		}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			r := newRegRuntime(t)
			var second *runtime.EventRegistration
			firstCalls, secondCalls := 0, 0
			host := &regHost{name: "mode-host", setup: func(c *runtime.Context) error {
				if err := runtime.On(c, tc.key, func(context.Context, int) error {
					firstCalls++
					if !tc.preDispose {
						second.Dispose()
					}
					return nil
				}); err != nil {
					return err
				}
				var err error
				second, err = runtime.OnWithHandle(c, tc.key, func(context.Context, int) error {
					secondCalls++
					return nil
				})
				if err != nil {
					return err
				}
				if tc.preDispose {
					second.Dispose()
				}
				return nil
			}}
			r.loadHost(t, host)
			if err := tc.dispatch(host.ctx, tc.key); err != nil {
				t.Fatal(err)
			}
			if firstCalls != 1 || secondCalls != 0 {
				t.Fatalf("%s: first=%d second=%d, want 1/0", tc.name, firstCalls, secondCalls)
			}
		})
	}
}

// TestWaterfallContinuesPastRemovedNode: a removed plain node must not break
// the chain (JS events.spec.ts:200).
func TestWaterfallContinuesPastRemovedNode(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("reg.waterfall.skip")
	var removed *runtime.EventRegistration
	lastCalls := 0
	host := &regHost{name: "wf-host", setup: func(c *runtime.Context) error {
		if err := runtime.On(c, key, func(context.Context, int) error {
			removed.Dispose()
			return nil
		}); err != nil {
			return err
		}
		var err error
		removed, err = runtime.OnWithHandle(c, key, func(context.Context, int) error {
			t.Error("removed waterfall node must not run")
			return nil
		})
		if err != nil {
			return err
		}
		return runtime.OnWaterfall(c, key, func(_ context.Context, _ int, next runtime.Next) error {
			lastCalls++
			return next()
		})
	}}
	r.loadHost(t, host)
	if err := Waterfall(context.Background(), host.ctx, key, 1); err != nil {
		t.Fatal(err)
	}
	if lastCalls != 1 {
		t.Fatalf("terminal waterfall node ran %d times, want 1", lastCalls)
	}
}

// TestDispatchDefersListenerAddedInSameBatch: additions after the snapshot
// only take effect on the next dispatch (JS events.spec.ts:177,219).
func TestDispatchDefersListenerAddedInSameBatch(t *testing.T) {
	for _, tc := range []struct {
		name     string
		key      runtime.EventKey[int]
		dispatch func(*runtime.Context, runtime.EventKey[int], int) error
	}{
		{"emit", runtime.NewEventKey[int]("reg.added.emit"), func(c *runtime.Context, k runtime.EventKey[int], n int) error {
			return Emit(c, k, n)
		}},
		{"serial", runtime.NewEventKey[int]("reg.added.serial"), func(c *runtime.Context, k runtime.EventKey[int], n int) error {
			return Serial(context.Background(), c, k, n)
		}},
		{"bail", runtime.NewEventKey[int]("reg.added.bail"), func(c *runtime.Context, k runtime.EventKey[int], n int) error {
			return Bail(context.Background(), c, k, n)
		}},
		{"parallel", runtime.NewEventKey[int]("reg.added.parallel"), func(c *runtime.Context, k runtime.EventKey[int], n int) error {
			return Parallel(context.Background(), c, k, n)
		}},
		{"waterfall", runtime.NewEventKey[int]("reg.added.waterfall"), func(c *runtime.Context, k runtime.EventKey[int], n int) error {
			return Waterfall(context.Background(), c, k, n)
		}},
	} {
		t.Run(tc.name, func(t *testing.T) {
			r := newRegRuntime(t)
			addedCalls := 0
			var added *runtime.EventRegistration
			host := &regHost{name: "added-host", setup: func(c *runtime.Context) error {
				return runtime.On(c, tc.key, func(context.Context, int) error {
					if added == nil {
						var err error
						added, err = runtime.OnWithHandle(c, tc.key, func(context.Context, int) error {
							addedCalls++
							return nil
						})
						if err != nil {
							return err
						}
					}
					return nil
				})
			}}
			r.loadHost(t, host)

			if err := tc.dispatch(host.ctx, tc.key, 1); err != nil {
				t.Fatal(err)
			}
			if addedCalls != 0 {
				t.Fatalf("listener added mid-dispatch ran in the same batch: %d", addedCalls)
			}
			if err := tc.dispatch(host.ctx, tc.key, 2); err != nil {
				t.Fatal(err)
			}
			if addedCalls != 1 {
				t.Fatalf("listener added mid-dispatch ran %d times in the next batch, want 1", addedCalls)
			}
			added.Dispose()
		})
	}
}

// TestOnOnceConcurrentDispatchExactlyOnce: remove-before-invoke alone is a
// TOCTOU race with concurrent dispatchers (JS events.spec.ts:149). The atomic
// one-shot claim must make the user callback run exactly once.
func TestOnOnceConcurrentDispatchExactlyOnce(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("reg.once.concurrent")
	var calls atomic.Int64
	host := &regHost{name: "once-concurrent", setup: func(c *runtime.Context) error {
		return runtime.OnOnce(c, key, func(context.Context, int) error {
			calls.Add(1)
			return nil
		})
	}}
	r.loadHost(t, host)

	const n = 64
	start := make(chan struct{})
	var wg sync.WaitGroup
	for i := 0; i < n; i++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			<-start
			if err := Serial(context.Background(), host.ctx, key, 1); err != nil {
				t.Errorf("concurrent Serial: %v", err)
			}
		}()
	}
	close(start)
	wg.Wait()
	if got := calls.Load(); got != 1 {
		t.Fatalf("once handler fired %d times under concurrent dispatch, want 1", got)
	}
}

// TestOneShotHandleCanCancelPending: JS once() returns a Disposable too.
func TestOneShotHandleCanCancelPending(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("reg.once.cancel")
	calls := 0
	var handle *runtime.EventRegistration
	host := &regHost{name: "once-cancel", setup: func(c *runtime.Context) error {
		var err error
		handle, err = runtime.OnOnceWithHandle(c, key, func(context.Context, int) error {
			calls++
			return nil
		})
		return err
	}}
	r.loadHost(t, host)
	if handle.Disposed() {
		t.Fatal("fresh one-shot handle is already disposed")
	}
	handle.Dispose()
	if err := Emit(host.ctx, key, 1); err != nil {
		t.Fatal(err)
	}
	if calls != 0 {
		t.Fatalf("cancelled one-shot listener ran %d times", calls)
	}
}

// TestWaterfallHandleCanDisposeNode: every registration mode has a handle.
func TestWaterfallHandleCanDisposeNode(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("reg.wf.handle")
	calls := 0
	host := &regHost{name: "wf-handle", setup: func(c *runtime.Context) error {
		h, err := runtime.OnWaterfallWithHandle(c, key, func(_ context.Context, _ int, next runtime.Next) error {
			calls++
			return next()
		})
		if err != nil {
			return err
		}
		h.Dispose()
		return nil
	}}
	r.loadHost(t, host)
	if err := Waterfall(context.Background(), host.ctx, key, 1); err != nil {
		t.Fatal(err)
	}
	if calls != 0 {
		t.Fatalf("disposed waterfall node ran %d times", calls)
	}
}
