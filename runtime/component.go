package runtime

import "errors"

// Component is the declarative definition of a composable capability.
//
// A Component is NOT a running instance: the Runtime instantiates it into a
// Fiber, and each activation cycle receives a fresh Context.
//
// A Component MUST NOT:
//   - mutate Fiber lifecycle state,
//   - mutate the provider registry directly,
//   - mutate other Fibers,
//   - implement lifecycle-level dependency waiting.
//
// It participates in the Runtime only through the Context passed to Apply.
type Component interface {
	// Name is for diagnostics, logging, and observability only. It is NOT a
	// capability identity.
	Name() string

	// Inject declares the capabilities this Component requires while Active.
	Inject() []Dependency

	// Provide declares the capabilities this Component may provide while
	// Active. Actual registration happens at runtime through Context.Provide.
	Provide() []Capability

	// Apply performs one activation. It MAY create resources, register
	// capabilities/handlers, or start goroutines. Every Runtime-managed
	// mutation MUST be recorded through Context.Effect so it can be reversed.
	//
	// A non-nil Cleanup is automatically converted into the activation's last
	// effect (it is therefore unwound first). Prefer Context.Effect when a
	// single activation performs several independent reversible operations.
	Apply(ctx *Context) (Cleanup, error)
}

// Cleanup reverses resources created by an activation.
type Cleanup func() error

// Capability identifies a capability. In v0.1 every capability is exclusive:
// at most one provider may be Active for a given CapabilityKey.
type Capability = CapabilityKey

// Dependency is a required dependency declaration.
//
// v0.1 supports only required dependencies: a Fiber may only enter Loading
// when every declared dependency is satisfied by a valid Active provider.
type Dependency struct {
	// Key identifies the required capability.
	Key CapabilityKey

	// Meta is the component-DECLARED metadata d(k) of paper Definition 26 (a
	// declaration on a MetaKey). It is merged (⊕ₖ, right-biased) with the
	// context-carried metadata ι(k) at read time and interpreted by the
	// provider. Nil means εₖ (no declared metadata). Resolution never reads
	// it: interception affects how a binding is used, not whether it is
	// satisfied (paper §6.3), so declaring metadata cannot gate activation.
	Meta any
}

// Requires builds a required-dependency declaration from a typed key.
func Requires[T any](key Key[T]) Dependency {
	return Dependency{Key: key.Capability()}
}

// RequiresMeta declares a dependency on a metadata-interpreting key with the
// component-declared metadata d(k) (paper Definition 26).
func RequiresMeta[T, M any](key MetaKey[T, M], declared M) Dependency {
	return Dependency{Key: key.Capability(), Meta: declared}
}

// ErrDiverted is returned by the iterator's yield when the fiber's target view
// turned after the previous step landed (inertial L-Divert, paper §4.2/§4.4):
// the component must stop without installing further work. A divert is a
// routing decision, not a Component failure — the activation unwinds with the
// inverses accumulated so far and the fiber is NOT marked Failed.
var ErrDiverted = errors.New("runtime: activation diverted (target view turned)")

// IterComponent is the optional effect-iterator form of a component activation
// (paper §3.1.3 effect iterators; §4.2.2 L-Iter/L-Finish/L-Divert).
//
// Each yield executes ONE iteration: the runtime runs the step on the
// activation goroutine, registers the step's Cleanup as its inverse on the
// LIFO unwind stack, and then probes the orchestrator for a divert. If the
// target view turned (dependency loss, dispose, replace, close), yield returns
// an error wrapping ErrDiverted after the last landing step — the inertial
// landing alternative of L-Divert (paper §4.4 Asynchrony: an iteration in
// flight cannot be declined, so a divert can only fall BETWEEN iterations).
//
// A step error is a paper raise: the activation unwinds what it installed and
// the fiber becomes Failed (no automatic retry).
//
// Components implement EITHER Apply (single-iteration special case) OR
// ApplyIter; when ApplyIter is present the runtime NEVER calls Apply (the
// iterator takes precedence at activation setup). Apply remains part of the
// embedded Component interface; iterator components give it a stub body.
type IterComponent interface {
	Component
	ApplyIter(ctx *Context, yield func(step func() (Cleanup, error)) error) error
}
