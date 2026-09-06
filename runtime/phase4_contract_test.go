package runtime_test

import (
	"errors"
	"sync"
	"testing"

	"dynamic-runtime/runtime"
)

// eventRecorder records ordered lifecycle events from component code.
type eventRecorder struct {
	mu     sync.Mutex
	events []string
}

func (r *eventRecorder) add(e string) {
	r.mu.Lock()
	r.events = append(r.events, e)
	r.mu.Unlock()
}

func (r *eventRecorder) all() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	out := make([]string, len(r.events))
	copy(out, r.events)
	return out
}

func equalStrings(a, b []string) bool {
	if len(a) != len(b) {
		return false
	}
	for i := range a {
		if a[i] != b[i] {
			return false
		}
	}
	return true
}

// installEffect registers a named effect whose inverse appends "undo:<name>".
func installEffect(t *testing.T, ctx *runtime.Context, rec *eventRecorder, name string, invErr error) error {
	t.Helper()
	return ctx.Effect(func() (func() error, error) {
		return func() error {
			rec.add("undo:" + name)
			return invErr
		}, nil
	})
}

// Plan Test: Effects unwind in strict LIFO order.
func TestEffectsUnwindLIFO(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	comp := newFakeComponent("lifo")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := installEffect(t, ctx, rec, "A", nil); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, rec, "B", nil); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, rec, "C", nil); err != nil {
			return nil, err
		}
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	want := []string{"undo:C", "undo:B", "undo:A"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("unwind order = %v, want %v", got, want)
	}
}

// CT2: Partial Apply cleanup. Effect A ok, B ok, C install fails: B and A must
// be unwound (LIFO), C must have no inverse, and the fiber ends Failed.
func TestPartialApplyCleanup(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	boom := errors.New("install C failed")
	comp := newFakeComponent("partial")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := installEffect(t, ctx, rec, "A", nil); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, rec, "B", nil); err != nil {
			return nil, err
		}
		err := ctx.Effect(func() (func() error, error) {
			return nil, boom
		})
		return nil, err
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); !errors.Is(err, boom) {
		t.Fatalf("Ready() = %v, want install failure %v", err, boom)
	}
	if got := f.State(); got != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", got)
	}

	want := []string{"undo:B", "undo:A"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("cleanup events = %v, want %v", got, want)
	}
}

// CT3: Cleanup failure must not stop the remaining inverses; errors aggregate.
func TestCleanupContinuesAfterError(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	errA := errors.New("cleanup A")
	errC := errors.New("cleanup C")
	comp := newFakeComponent("agg")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := installEffect(t, ctx, rec, "A", errA); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, rec, "B", nil); err != nil {
			return nil, err
		}
		if err := installEffect(t, ctx, rec, "C", errC); err != nil {
			return nil, err
		}
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}

	goneErr := f.Gone(testTimeout(t))
	if !errors.Is(goneErr, errA) || !errors.Is(goneErr, errC) {
		t.Fatalf("Gone() = %v, want both cleanup errors %v and %v", goneErr, errA, errC)
	}

	// All three inverses must have executed (C, then B, then A).
	want := []string{"undo:C", "undo:B", "undo:A"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("cleanup events = %v, want %v", got, want)
	}
}

// A Cleanup returned by Apply is the activation's last effect, so it unwinds
// before effects registered through ctx.Effect.
func TestReturnedCleanupUnwindsBeforeEffects(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	comp := newFakeComponent("cleanup-first")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := installEffect(t, ctx, rec, "E1", nil); err != nil {
			return nil, err
		}
		return func() error {
			rec.add("undo:returned-cleanup")
			return nil
		}, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	want := []string{"undo:returned-cleanup", "undo:E1"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("cleanup events = %v, want %v", got, want)
	}
}

// Effect is rejected once its Context is no longer active.
func TestEffectRejectedAfterContextClosed(t *testing.T) {
	rt := newTestRuntime(t)
	ctxCh := make(chan *runtime.Context, 1)
	comp := newFakeComponent("closed")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		ctxCh <- ctx
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	ctx := <-ctxCh
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	if err := ctx.Effect(func() (func() error, error) { return nil, nil }); !errors.Is(err, runtime.ErrContextClosed) {
		t.Fatalf("Effect on closed context = %v, want ErrContextClosed", err)
	}
}

// Plan Test 8 / Effect race: an install that completes after the context began
// unwinding must run its inverse exactly once and must not leak as a
// persistent effect.
func TestEffectLateCommitRunsInverseExactlyOnce(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}

	startInstall := make(chan struct{})
	installing := make(chan struct{})
	releaseInstall := make(chan struct{})
	installResult := make(chan error, 1)

	comp := newFakeComponent("late")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		go func() {
			<-startInstall
			err := ctx.Effect(func() (func() error, error) {
				close(installing)
				<-releaseInstall
				return func() error {
					rec.add("undo:late")
					return nil
				}, nil
			})
			installResult <- err
		}()
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}

	// Begin a late install, then unwind the activation while it is in flight.
	close(startInstall)
	<-installing

	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	// Now let the install complete; its inverse must run exactly once.
	close(releaseInstall)
	if err := <-installResult; err != nil {
		t.Fatalf("Effect() returned %v, want nil", err)
	}

	want := []string{"undo:late"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("events = %v, want %v", got, want)
	}
}

// Every inverse runs exactly once even across a full dispose/remount cycle
// (effects of an old activation never leak into the new one).
func TestEffectsExactlyOnceAcrossRemount(t *testing.T) {
	rt := newTestRuntime(t)
	rec := &eventRecorder{}
	comp := newFakeComponent("once")
	comp.applyFn = func(ctx *runtime.Context) (runtime.Cleanup, error) {
		if err := installEffect(t, ctx, rec, "X", nil); err != nil {
			return nil, err
		}
		return nil, nil
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}
	// Second activation registers a fresh effect.
	if err := f.Load(); err != nil {
		t.Fatalf("Load error: %v", err)
	}
	if err := f.Ready(testTimeout(t)); err != nil {
		t.Fatalf("Ready error: %v", err)
	}
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose error: %v", err)
	}
	if err := f.Gone(testTimeout(t)); err != nil {
		t.Fatalf("Gone error: %v", err)
	}

	want := []string{"undo:X", "undo:X"}
	if got := rec.all(); !equalStrings(got, want) {
		t.Fatalf("events = %v, want %v", got, want)
	}
}
