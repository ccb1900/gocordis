package event_test

import (
	"context"
	. "dynamic-runtime/extensions/event"
	"errors"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Bail dispatch conformance (Cordis stop-on-first-error semantics, ADR-0004):
// handlers run sequentially in registration order; the FIRST handler error
// (or contained panic) stops the dispatch and is returned as-is — not
// aggregated. Cancellation before a handler starts returns the cancellation
// error.

var bailKey = runtime.NewEventKey[string]("event.bail")

func TestBailStopsAtFirstError(t *testing.T) {
	rt := newBailRuntime(t)
	defer rt.Close(context.Background())

	rec := &bailRec{}
	want := errors.New("bail here")
	b, err := rt.Load(&bailHost{
		key:  bailKey,
		rec:  rec,
		self: make(chan *runtime.Context, 1),
		handlers: []bailHandler{
			{tag: "ok1"},
			{tag: "boom", err: want},
			{tag: "after"}, // must NOT run
		},
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := b.Ready(bailCtx(t)); err != nil {
		t.Fatal(err)
	}
	ctx := <-b.Component().(*bailHost).self

	got := rec.run(ctx)
	if got != "ok1,boom,err" {
		t.Fatalf("bail dispatch calls = %q, want ok1,boom,err (error marker, stopped)", got)
	}
}

func TestBailPureSuccessRunsAll(t *testing.T) {
	rt := newBailRuntime(t)
	defer rt.Close(context.Background())

	rec := &bailRec{}
	b, err := rt.Load(&bailHost{
		key:  bailKey,
		rec:  rec,
		self: make(chan *runtime.Context, 1),
		handlers: []bailHandler{
			{tag: "a"},
			{tag: "b"},
			{tag: "c"},
		},
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := b.Ready(bailCtx(t)); err != nil {
		t.Fatal(err)
	}
	ctx := <-b.Component().(*bailHost).self

	if got := rec.run(ctx); got != "a,b,c" {
		t.Fatalf("bail dispatch calls = %q, want a,b,c", got)
	}
}

func TestBailPanicStopsDispatch(t *testing.T) {
	rt := newBailRuntime(t)
	defer rt.Close(context.Background())

	rec := &bailRec{}
	b, err := rt.Load(&bailHost{
		key:  bailKey,
		rec:  rec,
		self: make(chan *runtime.Context, 1),
		handlers: []bailHandler{
			{tag: "ok"},
			{tag: "panic"},
			{tag: "after"},
		},
	})
	if err != nil {
		t.Fatal(err)
	}
	if err := b.Ready(bailCtx(t)); err != nil {
		t.Fatal(err)
	}
	ctx := <-b.Component().(*bailHost).self

	bailErr := Bail(context.Background(), ctx, bailKey, "p")
	if bailErr == nil || !errors.Is(bailErr, runtime.ErrEventHandlerPanic) {
		t.Fatalf("bail error = %v, want ErrEventHandlerPanic", err)
	}
	if got := rec.got(); len(got) != 2 {
		t.Fatalf("calls = %v, want [ok panic] (dispatch stopped)", got)
	}
}

// ---------------------------------------------------------------------------
// fixtures
// ---------------------------------------------------------------------------

type bailHandler struct {
	tag string
	err error
}

type bailHost struct {
	key      runtime.EventKey[string]
	rec      *bailRec
	handlers []bailHandler
	self     chan *runtime.Context
	selfCtx  *runtime.Context
}

func (c *bailHost) Name() string { return "bail-host" }
func (c *bailHost) Inject() []runtime.Dependency {
	return nil
}
func (c *bailHost) Provide() []runtime.Capability { return nil }
func (c *bailHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	c.selfCtx = ctx
	select {
	case c.self <- ctx:
	default:
	}
	for _, h := range c.handlers {
		h := h
		if err := runtime.On(ctx, c.key, func(_ context.Context, p string) error {
			c.rec.add(h.tag)
			if h.tag == "panic" {
				panic("handler panic")
			}
			return h.err
		}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

type bailRec struct {
	mu    sync.Mutex
	calls []string
}

func (r *bailRec) add(tag string) {
	r.mu.Lock()
	r.calls = append(r.calls, tag)
	r.mu.Unlock()
}

func (r *bailRec) got() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.calls...)
}

// run dispatches through Bail and returns the call trace.
func (r *bailRec) run(ctx *runtime.Context) string {
	err := Bail(context.Background(), ctx, bailKey, "x")
	calls := r.got()
	if err != nil {
		calls = append(calls, "err")
	}
	out := ""
	for i, c := range calls {
		if i > 0 {
			out += ","
		}
		out += c
	}
	return out
}

func newBailRuntime(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New(): %v", err)
	}
	return rt
}

func bailCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	t.Cleanup(cancel)
	return ctx
}
