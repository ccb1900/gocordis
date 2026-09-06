package runtime

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
}

// Requires builds a required-dependency declaration from a typed key.
func Requires[T any](key Key[T]) Dependency {
	return Dependency{Key: key.Capability()}
}
