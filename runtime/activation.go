package runtime

import "context"

// activation is one lifecycle instance ("activation cycle") of a Fiber.
//
// A Fiber may run many activations over its lifetime; each activation is a
// completely isolated object with its own Context, dependency snapshot, and
// provider generation. An activation of one cycle MUST never be mutated by
// async completions belonging to a different cycle.
type activation struct {
	id ActivationID

	fiber *Fiber

	// ctx is the activation-scoped Context (created in later phases together
	// with the cancellation wiring).
	ctx *Context

	// cancel cancels the activation-scoped context. Cooperative only.
	cancel context.CancelFunc

	// deps is the dependency snapshot captured when this activation entered
	// Loading (populated by the Provider/Dependency phase).
	deps []DependencySnapshot

	// applyErr is the Component Apply error, if any. A non-nil applyErr with a
	// still-Mounted intent classifies the activation as Failed.
	applyErr error
}
