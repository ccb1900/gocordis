// Dispatch modes for kernel-registered Event handlers (ADR-0004).
//
// The kernel owns Event REGISTRATION as a reversible effect (runtime.On /
// runtime.OnWaterfall) and the registry read model
// (runtime.Context.EventBindings). Everything about HOW a dispatch runs is
// extension policy and lives here:
//
//	Emit      — fire-and-observe: every visible handler runs; errors aggregate.
//	Serial    — strictly sequential in registration order; errors aggregate.
//	Parallel  — concurrent start, ordered aggregation, synchronous return.
//	Bail      — Serial with fail-fast: the first handler error stops dispatch.
//	Waterfall — synchronous middleware chain driven by the handlers themselves.
//
// All modes share the kernel's guarantees per dispatch: one snapshot of the
// visible bindings (emitter realm path already resolved, deterministic
// registration order), owner-validity at snapshot time, cooperative
// cancellation before each handler starts, and unified panic containment via
// runtime.GuardEventHandler.
package event

import (
	"context"
	"errors"
	"sync"

	"dynamic-runtime/runtime"
)

// ErrWaterfallNextTwice is returned when a waterfall chain node calls next()
// more than once (a handler contract violation; the violation surfaces even
// when the handler ignores next()'s error). It is the kernel's sentinel: the
// Next contract is part of the registration surface.
var ErrWaterfallNextTwice = runtime.ErrWaterfallNextTwice

// dispatchCancelErr returns the first cancellation error in effect: the
// dispatch context or the emitter activation context.
func dispatchCancelErr(ctx context.Context, c *runtime.Context) error {
	if ctx != nil {
		if err := ctx.Err(); err != nil {
			return err
		}
	}
	if c != nil {
		if err := c.Context().Err(); err != nil {
			return err
		}
	}
	return nil
}

// Emit dispatches payload to every handler visible from c, in registration
// order, on the caller goroutine. It is not fail-fast: a handler's error (or
// contained panic) does not stop later handlers. Emitter cancellation is
// checked before each handler.
func Emit[T any](c *runtime.Context, key runtime.EventKey[T], payload T) error {
	if c == nil {
		return errors.New("event: nil context")
	}
	if !key.Valid() {
		return errors.New("event: zero-value EventKey (use runtime.NewEventKey)")
	}
	if err := dispatchCancelErr(nil, c); err != nil {
		return err
	}
	snap := c.EventBindings(key.ID())
	var errs []error
	for _, b := range snap {
		if err := dispatchCancelErr(nil, c); err != nil {
			return errors.Join(append(errs, err)...)
		}
		if err := runtime.GuardEventHandler(b.Handler, c.Context(), payload); err != nil {
			errs = append(errs, err)
		}
	}
	return errors.Join(errs...)
}

// Serial dispatches strictly sequentially in registration order (each handler
// completes before the next starts). Not fail-fast: errors aggregate with
// errors.Join. The caller-supplied ctx is the dispatch context; cancellation
// prevents further handlers from starting.
func Serial[T any](ctx context.Context, c *runtime.Context, key runtime.EventKey[T], payload T) error {
	if c == nil {
		return errors.New("event: nil context")
	}
	if !key.Valid() {
		return errors.New("event: zero-value EventKey (use runtime.NewEventKey)")
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if err := dispatchCancelErr(ctx, c); err != nil {
		return err
	}
	snap := c.EventBindings(key.ID())
	var errs []error
	for _, b := range snap {
		if err := dispatchCancelErr(ctx, c); err != nil {
			return errors.Join(append(errs, err)...)
		}
		if err := runtime.GuardEventHandler(b.Handler, ctx, payload); err != nil {
			errs = append(errs, err)
		}
	}
	return errors.Join(errs...)
}

// Bail dispatches sequentially in registration order and stops at the FIRST
// handler error (or contained panic): fail-fast serial. Cancellation before a
// handler starts returns the cancellation error. The returned error is the
// first failure, not an aggregation.
//
// Divergence note (ADR-0004): JS Cordis's `bail` is value-short-circuit
// (stop at the first handler returning a non-undefined value). That semantics
// requires a value-returning handler shape, which the kernel's
// error-returning registration surface does not carry; the paper defines no
// dispatch modes. Bail here is therefore defined independently as fail-fast
// serial, and the name is kept only as the conventional word for
// stop-on-condition.
func Bail[T any](ctx context.Context, c *runtime.Context, key runtime.EventKey[T], payload T) error {
	if c == nil {
		return errors.New("event: nil context")
	}
	if !key.Valid() {
		return errors.New("event: zero-value EventKey (use runtime.NewEventKey)")
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if err := dispatchCancelErr(ctx, c); err != nil {
		return err
	}
	snap := c.EventBindings(key.ID())
	for _, b := range snap {
		if err := dispatchCancelErr(ctx, c); err != nil {
			return err
		}
		if err := runtime.GuardEventHandler(b.Handler, ctx, payload); err != nil {
			return err
		}
	}
	return nil
}

// Parallel starts every visible handler in its own goroutine and returns only
// after all started handlers complete. Start and completion order are out of
// contract; error AGGREGATION follows snapshot registration order (so the
// joined error is deterministic), and cancellation-skipped handlers are
// reported as ctx.Err() joined with the collected handler errors.
func Parallel[T any](ctx context.Context, c *runtime.Context, key runtime.EventKey[T], payload T) error {
	if c == nil {
		return errors.New("event: nil context")
	}
	if !key.Valid() {
		return errors.New("event: zero-value EventKey (use runtime.NewEventKey)")
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if err := dispatchCancelErr(ctx, c); err != nil {
		return err
	}
	snap := c.EventBindings(key.ID())
	errs := make([]error, len(snap))
	var wg sync.WaitGroup
	started := 0
	for i := range snap {
		if err := dispatchCancelErr(ctx, c); err != nil {
			break
		}
		wg.Add(1)
		go func(i int, h func(context.Context, any) error) {
			defer wg.Done()
			errs[i] = runtime.GuardEventHandler(h, ctx, payload)
		}(i, snap[i].Handler)
		started++
	}
	wg.Wait()

	var joined []error
	for _, e := range errs {
		if e != nil {
			joined = append(joined, e)
		}
	}
	if started < len(snap) {
		if err := dispatchCancelErr(ctx, c); err != nil {
			joined = append(joined, err)
		}
	}
	return errors.Join(joined...)
}

// Waterfall dispatches payload through an ordered, synchronous middleware
// chain driven by the handlers themselves:
//
//	snapshot [A, B, C]
//	A.before → A.next() → B.before → B.next() → C → B.after → A.after
//
// Not calling next() short-circuits the chain (a node that handled the event)
// — a nil result, never conflated with cancellation. next() may advance the
// chain at most once (a second call returns ErrWaterfallNextTwice, and the
// violation surfaces in the dispatch result). Errors follow the chain, not
// aggregation: a downstream error propagates back through every upstream
// next(). A plain On registration holds no chain authority and runs as a
// transparent node (the chain continues through it; its error still fails the
// dispatch). Cancellation is checked before the snapshot and at every
// next() boundary.
func Waterfall[T any](ctx context.Context, c *runtime.Context, key runtime.EventKey[T], payload T) error {
	if c == nil {
		return errors.New("event: nil context")
	}
	if !key.Valid() {
		return errors.New("event: zero-value EventKey (use runtime.NewEventKey)")
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if err := dispatchCancelErr(ctx, c); err != nil {
		return err
	}
	snap := c.EventBindings(key.ID())
	return runWaterfallChain(ctx, c, snap, 0, payload)
}

// runWaterfallChain drives the snapshot starting at index i. All chain state
// lives on this call stack: nested/reentrant dispatches capture their own
// snapshots and never touch an outer chain's next or index.
func runWaterfallChain(ctx context.Context, c *runtime.Context, snap []runtime.EventBinding, i int, payload any) error {
	if i >= len(snap) {
		return nil
	}
	if err := dispatchCancelErr(ctx, c); err != nil {
		return err
	}
	b := snap[i]
	if b.Chain == nil {
		// Plain On registration: a transparent node; the chain continues
		// automatically, but its error still fails the dispatch.
		if err := runtime.GuardEventHandler(b.Handler, ctx, payload); err != nil {
			return err
		}
		return runWaterfallChain(ctx, c, snap, i+1, payload)
	}

	calls := 0
	next := func() error {
		calls++
		if calls > 1 {
			return ErrWaterfallNextTwice
		}
		return runWaterfallChain(ctx, c, snap, i+1, payload)
	}
	err := runtime.GuardEventHandler(func(hctx context.Context, p any) error {
		return b.Chain(hctx, p, next)
	}, ctx, payload)

	if calls > 1 && !errors.Is(err, ErrWaterfallNextTwice) {
		if err == nil {
			err = ErrWaterfallNextTwice
		} else {
			err = errors.Join(ErrWaterfallNextTwice, err)
		}
	}
	return err
}
