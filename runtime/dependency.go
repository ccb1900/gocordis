package runtime

// ProviderIdentity identifies one provider generation of a Capability.
//
// A provider generation is exactly one activation cycle: replacing a provider
// (even with a new activation of the same Fiber) changes the identity, and any
// consumer whose snapshot no longer matches is stale.
type ProviderIdentity struct {
	// FiberID is the owner Fiber.
	FiberID FiberID
	// ActivationID is the owner activation (the provider generation).
	ActivationID ActivationID
}

// DependencySnapshot is the provider identity captured when a Fiber enters
// Loading for one required capability.
type DependencySnapshot struct {
	Key      CapabilityKey
	Provider ProviderIdentity
}
