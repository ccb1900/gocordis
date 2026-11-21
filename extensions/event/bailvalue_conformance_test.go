package event_test

import (
	"context"
	"errors"
	"strings"
	"sync"
	"testing"

	. "dynamic-runtime/extensions/event"
	"dynamic-runtime/runtime"
)

// JS bail() parity via the additive value API (docs/r1.md second batch):
// value-returning handlers are registered with runtime.OnValue in the SAME
// runtime registry; BailValue walks one deterministic snapshot, ignores plain
// On bindings, and stops at the first handler reporting present=true (JS's
// "non-undefined result"). Handler errors are fail-fast and returned.

type valueCallTrace struct {
	mu    sync.Mutex
	calls []string
}

func (t *valueCallTrace) add(tag string) {
	t.mu.Lock()
	t.calls = append(t.calls, tag)
	t.mu.Unlock()
}

func (t *valueCallTrace) got() string {
	t.mu.Lock()
	defer t.mu.Unlock()
	return strings.Join(t.calls, ",")
}

func disposeValueHost(t *testing.T, r *regRuntime, f *runtime.Fiber) {
	t.Helper()
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	r.waitEnded(t, f)
}

// TestBailValueSecondHandlerWins: first value handler reports present=false
// (JS undefined), so dispatch continues and the second one's result wins.
// The two handlers live on DISTINCT component fibers.
func TestBailValueSecondHandlerWins(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[string]("value.bail.second-wins")
	trace := &valueCallTrace{}

	a := &regHost{name: "value-a", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, string) (string, bool, error) {
			trace.add("a")
			return "from-a", false, nil
		})
	}}
	b := &regHost{name: "value-b", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, string) (string, bool, error) {
			trace.add("b")
			return "from-b", true, nil
		})
	}}
	fa := r.loadHost(t, a)
	fb := r.loadHost(t, b)
	defer disposeValueHost(t, r, fa)
	defer disposeValueHost(t, r, fb)

	got, present, err := BailValue[string, string](context.Background(), b.ctx, key, "x")
	if err != nil || !present || got != "from-b" {
		t.Fatalf("BailValue = (%q, %v, %v), want (from-b, true, nil)", got, present, err)
	}
	if want := "a,b"; trace.got() != want {
		t.Fatalf("calls = %q, want %q", trace.got(), want)
	}
}

// TestBailValueFirstHandlerShortCircuits: first present=true result stops the
// chain; the second value handler must never run.
func TestBailValueFirstHandlerShortCircuits(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[string]("value.bail.first-wins")
	trace := &valueCallTrace{}

	a := &regHost{name: "value-a", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, string) (string, bool, error) {
			trace.add("a")
			return "from-a", true, nil
		})
	}}
	b := &regHost{name: "value-b", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, string) (string, bool, error) {
			trace.add("b")
			return "from-b", true, nil
		})
	}}
	fa := r.loadHost(t, a)
	fb := r.loadHost(t, b)
	defer disposeValueHost(t, r, fa)
	defer disposeValueHost(t, r, fb)

	got, present, err := BailValue[string, string](context.Background(), a.ctx, key, "x")
	if err != nil || !present || got != "from-a" {
		t.Fatalf("BailValue = (%q, %v, %v), want (from-a, true, nil)", got, present, err)
	}
	if want := "a"; trace.got() != want {
		t.Fatalf("calls = %q, want %q (second handler must not run)", trace.got(), want)
	}
}

// TestBailValueHandlerErrorStopsDispatch: a non-nil error is fail-fast and
// returned with present=false and the zero result.
func TestBailValueHandlerErrorStopsDispatch(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("value.bail.error")
	trace := &valueCallTrace{}
	want := errors.New("value handler failed")

	a := &regHost{name: "value-a", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("a")
			return 0, false, want
		})
	}}
	b := &regHost{name: "value-b", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("b")
			return 2, true, nil
		})
	}}
	fa := r.loadHost(t, a)
	fb := r.loadHost(t, b)
	defer disposeValueHost(t, r, fa)
	defer disposeValueHost(t, r, fb)

	got, present, err := BailValue[int, int](context.Background(), b.ctx, key, 1)
	if !errors.Is(err, want) || present || got != 0 {
		t.Fatalf("BailValue = (%d, %v, %v), want (0, false, handler error)", got, present, err)
	}
	if want := "a"; trace.got() != want {
		t.Fatalf("calls = %q, want %q", trace.got(), want)
	}
}

// TestBailValueSkipsMidDispatchDisposal: one snapshot is taken, but each value
// binding is live-checked; a handle retired by an earlier handler is skipped.
func TestBailValueSkipsMidDispatchDisposal(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("value.bail.mid-dispose")
	trace := &valueCallTrace{}
	var bHandle *runtime.EventRegistration

	a := &regHost{name: "value-a", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("a")
			bHandle.Dispose() // retire B before the snapshot reaches it
			return 0, false, nil
		})
	}}
	b := &regHost{name: "value-b", setup: func(c *runtime.Context) error {
		var err error
		bHandle, err = runtime.OnValueWithHandle(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("b")
			return 2, true, nil
		})
		return err
	}}
	fa := r.loadHost(t, a)
	fb := r.loadHost(t, b)
	defer disposeValueHost(t, r, fa)
	defer disposeValueHost(t, r, fb)

	got, present, err := BailValue[int, int](context.Background(), b.ctx, key, 1)
	if err != nil || present || got != 0 {
		t.Fatalf("BailValue = (%d, %v, %v), want (0, false, nil) [absent result]", got, present, err)
	}
	if want := "a"; trace.got() != want {
		t.Fatalf("calls = %q, want %q (disposed B must be skipped)", trace.got(), want)
	}
}

// TestBailValueIgnoresPlainOnBindings: only value bindings participate; a
// plain On handler on the same key is never invoked by BailValue.
func TestBailValueIgnoresPlainOnBindings(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("value.bail.mixed")
	trace := &valueCallTrace{}

	plain := &regHost{name: "plain-host", setup: func(c *runtime.Context) error {
		return runtime.On(c, key, func(context.Context, int) error {
			trace.add("plain")
			return nil
		})
	}}
	value := &regHost{name: "value-host", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (string, bool, error) {
			trace.add("value")
			return "v", true, nil
		})
	}}
	fp := r.loadHost(t, plain)
	fv := r.loadHost(t, value)
	defer disposeValueHost(t, r, fp)
	defer disposeValueHost(t, r, fv)

	got, present, err := BailValue[int, string](context.Background(), value.ctx, key, 1)
	if err != nil || !present || got != "v" {
		t.Fatalf("BailValue = (%q, %v, %v), want (v, true, nil)", got, present, err)
	}
	if want := "value"; trace.got() != want {
		t.Fatalf("calls = %q, want %q (plain On must be ignored)", trace.got(), want)
	}
}

// TestBailValueComponentDisposeRemovesRegistration: after the owning component
// is disposed its value handler is gone from the snapshot.
func TestBailValueComponentDisposeRemovesRegistration(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("value.bail.component-dispose")
	trace := &valueCallTrace{}

	observer := &regHost{name: "observer", setup: nil}
	value := &regHost{name: "value-host", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("value")
			return 1, true, nil
		})
	}}
	fo := r.loadHost(t, observer)
	fv := r.loadHost(t, value)
	defer disposeValueHost(t, r, fo)

	got, present, err := BailValue[int, int](context.Background(), observer.ctx, key, 1)
	if err != nil || !present || got != 1 {
		t.Fatalf("before dispose: (%d, %v, %v), want (1, true, nil)", got, present, err)
	}
	disposeValueHost(t, r, fv)

	got, present, err = BailValue[int, int](context.Background(), observer.ctx, key, 1)
	if err != nil || present || got != 0 {
		t.Fatalf("after dispose: (%d, %v, %v), want (0, false, nil)", got, present, err)
	}
	if want := "value"; trace.got() != want {
		t.Fatalf("calls = %q, want %q", trace.got(), want)
	}
}

// TestBailValuePanicContained: a panicking value handler is converted to an
// ErrEventHandlerPanic error and stops dispatch (GuardEventHandler parity).
func TestBailValuePanicContained(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("value.bail.panic")
	trace := &valueCallTrace{}

	a := &regHost{name: "value-a", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("a-panic")
			panic("value boom")
		})
	}}
	b := &regHost{name: "value-b", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("b")
			return 2, true, nil
		})
	}}
	fa := r.loadHost(t, a)
	fb := r.loadHost(t, b)
	defer disposeValueHost(t, r, fa)
	defer disposeValueHost(t, r, fb)

	_, _, err := BailValue[int, int](context.Background(), b.ctx, key, 1)
	if err == nil || !errors.Is(err, runtime.ErrEventHandlerPanic) {
		t.Fatalf("BailValue panic error = %v, want ErrEventHandlerPanic", err)
	}
	if want := "a-panic"; trace.got() != want {
		t.Fatalf("calls = %q, want %q", trace.got(), want)
	}
}

// TestBailValueCancellation: cooperative cancellation is checked before the
// snapshot and before each handler.
func TestBailValueCancellation(t *testing.T) {
	r := newRegRuntime(t)
	key := runtime.NewEventKey[int]("value.bail.cancel")
	trace := &valueCallTrace{}

	host := &regHost{name: "value-host", setup: func(c *runtime.Context) error {
		return runtime.OnValue(c, key, func(context.Context, int) (int, bool, error) {
			trace.add("value")
			return 1, true, nil
		})
	}}
	f := r.loadHost(t, host)
	defer disposeValueHost(t, r, f)

	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	_, _, err := BailValue[int, int](ctx, host.ctx, key, 1)
	if !errors.Is(err, context.Canceled) {
		t.Fatalf("BailValue canceled error = %v, want context.Canceled", err)
	}
	if trace.got() != "" {
		t.Fatalf("handler ran despite cancellation: %q", trace.got())
	}
}
