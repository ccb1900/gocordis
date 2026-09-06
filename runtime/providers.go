package runtime

import "sync"

// providerRecord is one exclusive provider registration for a Capability.
type providerRecord struct {
	key CapabilityKey

	// identity is the provider generation: owner Fiber + owner activation.
	identity ProviderIdentity

	value any

	// retiring is set by the orchestrator the moment the owning activation is
	// decided to withdraw. A retiring record is no longer satisfiable by
	// dependents but still blocks duplicate registration until the owning
	// activation's inverse physically removes it (withdraw-then-load).
	retiring bool
}

// providerRegistry is the Runtime's provider registry.
//
// All registration/removal operations are atomic under the registry lock.
// Registration (check + insert) happens in a single critical section so two
// concurrent providers can never both win the same exclusive Capability.
type providerRegistry struct {
	mu        sync.RWMutex
	providers map[CapabilityKey]*providerRecord
}

func newProviderRegistry() *providerRegistry {
	return &providerRegistry{providers: make(map[CapabilityKey]*providerRecord)}
}

// register atomically checks for an existing provider and inserts the new one.
// A duplicate registration fails with ErrDuplicateProvider and never disturbs
// the existing provider.
func (r *providerRegistry) register(key CapabilityKey, identity ProviderIdentity, value any) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, exists := r.providers[key]; exists {
		return ErrDuplicateProvider
	}
	r.providers[key] = &providerRecord{
		key:      key,
		identity: identity,
		value:    value,
	}
	return nil
}

// lookup returns the current record for a capability, if any.
func (r *providerRegistry) lookup(key CapabilityKey) (*providerRecord, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	rec, ok := r.providers[key]
	return rec, ok
}

// remove deletes a provider record only when its identity matches. This is the
// second line of defense against stale cleanup: an old activation's inverse can
// never delete a newer activation's provider.
func (r *providerRegistry) remove(key CapabilityKey, identity ProviderIdentity) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if rec, ok := r.providers[key]; ok && rec.identity == identity {
		delete(r.providers, key)
	}
}

// markRetiring flags the record of the given identity as retiring. It is a
// no-op if the current record belongs to a different identity.
func (r *providerRegistry) markRetiring(key CapabilityKey, identity ProviderIdentity) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if rec, ok := r.providers[key]; ok && rec.identity == identity {
		rec.retiring = true
	}
}

// providesBy returns the capability keys currently registered by the given
// owner activation.
func (r *providerRegistry) providesBy(owner FiberID, act ActivationID) []CapabilityKey {
	r.mu.RLock()
	defer r.mu.RUnlock()
	var out []CapabilityKey
	for key, rec := range r.providers {
		if rec.identity.FiberID == owner && rec.identity.ActivationID == act {
			out = append(out, key)
		}
	}
	return out
}

// count returns the number of registered providers (tests/diagnostics).
func (r *providerRegistry) count() int {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return len(r.providers)
}
