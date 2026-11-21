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
//	BailValue — value short-circuit (JS bail): stop at first present result.
//	Waterfall — synchronous middleware chain driven by the handlers themselves.
//
// All modes share the kernel's guarantees per dispatch: one snapshot of the
// visible bindings (emitter realm path already resolved, deterministic
// registration order), owner-validity at snapshot time, a live Active check
// immediately before each invocation (a handler retired before its check is
// skipped; one added mid-batch waits for the next snapshot), cooperative
// cancellation before each handler starts, and unified panic containment via
// runtime.GuardEventHandler. Parallel checks Active when starting each
// goroutine, so a removal by a concurrently running handler is
// scheduling-dependent — unlike the ordered modes.
package event

import (
	"context"
	"errors"
	"fmt"
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
		if b.Active != nil && !b.Active() {
			continue
		}
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
		if b.Active != nil && !b.Active() {
			continue
		}
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
// Divergence note (ADR-0004, docs/r1.md second-batch Event item): JS Cordis's
// `bail` is value-short-circuit (stop at the first handler returning a
// non-undefined value), which this error-returning Bail never did. The additive
// value API (runtime.OnValue + BailValue below) now provides that semantics
// without breaking this function: Bail REMAINS fail-fast-on-error, and callers
// wanting JS bail() behaviour use BailValue. (A FailFast alias would name this
// same behaviour explicitly; the original name is kept for compatibility.)
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
		if b.Active != nil && !b.Active() {
			continue
		}
		if err := dispatchCancelErr(ctx, c); err != nil {
			return err
		}
		if err := runtime.GuardEventHandler(b.Handler, ctx, payload); err != nil {
			return err
		}
	}
	return nil
}

// BailValue dispatches payload to the VALUE bindings only (runtime.OnValue),
// in one deterministic registration-order snapshot, and stops at the first
// handler reporting present=true, returning its result. This is JS Cordis
// bail() semantics: present=false is the explicit spelling of JS's `undefined`
// and lets dispatch keep looking for a result.
//
// Contract (shared kernel guarantees, ADR-0004):
//   - A handler error stops dispatch and is returned with present=false and
//     the zero result (fail-fast); panics are contained via the same
//     runtime.GuardEventHandler boundary as every other mode.
//   - One snapshot is captured before dispatch; a binding disposed earlier in
//     the batch is skipped via its live Active check; plain On/OnWaterfall
//     bindings (nil ValueHandler) are never invoked.
//   - Cancellation is checked before the snapshot and before each handler.
//
// R cannot be inferred from the arguments (it appears only in the result), so
// callers specify both type parameters, e.g. BailValue[string, int](...).
func BailValue[T any, R any](ctx context.Context, c *runtime.Context, key runtime.EventKey[T], payload T) (R, bool, error) {
	var zero R
	if c == nil {
		return zero, false, errors.New("event: nil context")
	}
	if !key.Valid() {
		return zero, false, errors.New("event: zero-value EventKey (use runtime.NewEventKey)")
	}
	if ctx == nil {
		ctx = context.Background()
	}
	if err := dispatchCancelErr(ctx, c); err != nil {
		return zero, false, err
	}

	snap := c.EventBindings(key.ID())
	for _, b := range snap {
		if b.Active != nil && !b.Active() {
			continue
		}
		if b.ValueHandler == nil {
			continue
		}
		if err := dispatchCancelErr(ctx, c); err != nil {
			return zero, false, err
		}

		var result any
		var present bool
		err := runtime.GuardEventHandler(func(hctx context.Context, p any) error {
			var handlerErr error
			result, present, handlerErr = b.ValueHandler(hctx, p)
			return handlerErr
		}, ctx, payload)
		if err != nil {
			return zero, false, err
		}
		if !present {
			continue
		}
		// The kernel's read model erases R to any; recover the typed result.
		// A nil projection result is the zero of R=any (a JS null-ish result);
		// a mismatched R is a caller contract violation, surfaced as an error
		// instead of a panic.
		if result == nil {
			return zero, true, nil
		}
		v, ok := result.(R)
		if !ok {
			return zero, false, fmt.Errorf("event: BailValue result type mismatch: got %T, want %T", result, zero)
		}
		return v, true, nil
	}
	return zero, false, nil
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
		if snap[i].Active != nil && !snap[i].Active() {
			continue
		}
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
	// A node disposed earlier in this batch is skipped, and the chain advances
	// to the next still-live node (JS events.spec.ts:200).
	for i < len(snap) && snap[i].Active != nil && !snap[i].Active() {
		i++
	}
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
