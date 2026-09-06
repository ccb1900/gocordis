package runtime

import "errors"

// Sentinel errors exposed by the Kernel.
//
// Use errors.Is(...) for comparisons; never compare error strings.
var (
	// ErrRuntimeClosed is returned when a lifecycle operation targets a
	// Runtime that is Closing or Closed.
	ErrRuntimeClosed = errors.New("runtime closed")
	// ErrDuplicateProvider is returned when a second exclusive provider
	// attempts to register the same Capability.
	ErrDuplicateProvider = errors.New("duplicate provider")
	// ErrDependencyMissing is returned when a required dependency cannot be
	// resolved.
	ErrDependencyMissing = errors.New("dependency missing")
	// ErrDependencyCycle is returned when registering a dependency would
	// create a strong dependency cycle.
	ErrDependencyCycle = errors.New("dependency cycle")
	// ErrInvalidState is returned when an operation is illegal in the
	// current Fiber state.
	ErrInvalidState = errors.New("invalid fiber state")
	// ErrContextClosed is returned when an operation targets a Context that
	// is no longer active.
	ErrContextClosed = errors.New("context closed")
	// ErrInvariantViolation indicates internal Runtime invariant corruption.
	// This is a Runtime-fatal condition; it must never be caused by ordinary
	// Component errors.
	ErrInvariantViolation = errors.New("runtime invariant violation")
	// ErrFiberGone is returned by waiters when the target Fiber reached Gone
	// without carrying a lifecycle error.
	ErrFiberGone = errors.New("fiber gone")
)
