package runtime

import "sync"

// realm is the unit of provider resolution and interception (Phase 3, scoped
// Context). It is an explicit scope / ownership domain:
//
//   - Runtime.Load roots compose in the Runtime root realm (backward
//     compatible single-realm behavior);
//   - ctx.Child defaults to inheriting the parent fiber's realm;
//   - only an explicitly derived scope creates a child realm, which may
//     SHADOW ancestor bindings (lookup walks own -> parent).
//
// Exclusivity is per realm: at most one provider per typed key in a realm's own
// map. Sibling realms may provide the same key in parallel, invisible to each
// other. A realm is not a lifecycle authority — it lives and dies with its
// owning fiber/scope and never mutates Fiber state.
type realm struct {
	// id is the stable scope identity. The Runtime root realm has id 0; child
	// realms (explicit scopes) are assigned increasing IDs at creation. The id
	// is fixed before the realm is published to any fiber, so it is read
	// without a lock once creation completes.
	id ScopeID

	mu     sync.RWMutex
	parent *realm
	own    map[CapabilityKey]*providerRecord
	// intercept maps a capability key to the read-time interceptor chain
	// installed in THIS realm (install order). Ancestor chains apply first.
	intercept map[CapabilityKey][]*interceptEntry
}

func newRealm(parent *realm) *realm {
	return &realm{
		parent:    parent,
		own:       make(map[CapabilityKey]*providerRecord),
		intercept: make(map[CapabilityKey][]*interceptEntry),
	}
}

// interceptEntry is one installed read-time interceptor. apply transforms the
// resolved value; typed wrappers (runtime.Intercept) guarantee T.
type interceptEntry struct {
	key   CapabilityKey
	apply func(value any) (any, error)
}

// addIntercept appends an interceptor for key in this realm (install order).
func (r *realm) addIntercept(e *interceptEntry) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.intercept[e.key] = append(r.intercept[e.key], e)
}

// removeIntercept removes exactly e from this realm's chain (idempotent).
func (r *realm) removeIntercept(e *interceptEntry) {
	r.mu.Lock()
	defer r.mu.Unlock()
	chain := r.intercept[e.key]
	for i, x := range chain {
		if x == e {
			r.intercept[e.key] = append(chain[:i], chain[i+1:]...)
			return
		}
	}
}

// interceptsForKey returns the chain for key in ancestor->descendant order.
func (r *realm) interceptsForKey(key CapabilityKey) []*interceptEntry {
	var out []*interceptEntry
	var walk func(cur *realm)
	walk = func(cur *realm) {
		if cur == nil {
			return
		}
		walk(cur.parent)
		cur.mu.RLock()
		out = append(out, cur.intercept[key]...)
		cur.mu.RUnlock()
	}
	walk(r)
	return out
}

// lookup resolves key along the realm chain (own first, then ancestors).
func (r *realm) lookup(key CapabilityKey) (*providerRecord, bool) {
	for cur := r; cur != nil; cur = cur.parent {
		cur.mu.RLock()
		rec, ok := cur.own[key]
		cur.mu.RUnlock()
		if ok {
			return rec, true
		}
	}
	return nil, false
}

// registerOwn inserts a provider into THIS realm's own map. It fails with
// ErrDuplicateProvider (without disturbing anything) when the key is already
// present in this realm's own map; ancestor bindings do not block registration
// (child realms may shadow).
func (r *realm) registerOwn(key CapabilityKey, identity ProviderIdentity, value any) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, exists := r.own[key]; exists {
		return ErrDuplicateProvider
	}
	r.own[key] = &providerRecord{
		key:      key,
		identity: identity,
		value:    value,
	}
	return nil
}

// removeOwn deletes a provider only when its identity matches (stale-cleanup
// safety). It never deletes a newer activation's or a different realm's record.
//
// It reports whether a record was actually removed and whether that record had
// already been marked retiring (the semantic withdrawal point). A removal
// without prior retirement means the owning activation never became Active
// (e.g. a failed Apply that registered a provider and then unwound).
func (r *realm) removeOwn(key CapabilityKey, identity ProviderIdentity) (removed bool, wasRetiring bool) {
	r.mu.Lock()
	defer r.mu.Unlock()
	rec, ok := r.own[key]
	if !ok || rec.identity != identity {
		return false, false
	}
	wasRetiring = rec.retiring
	delete(r.own, key)
	return true, wasRetiring
}

// markRetiringOwn flags the record for key as retiring when it belongs to
// identity (no-op otherwise). A retiring record no longer satisfies new
// dependents but still blocks duplicate registration until physically removed.
func (r *realm) markRetiringOwn(key CapabilityKey, identity ProviderIdentity) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if rec, ok := r.own[key]; ok && rec.identity == identity {
		rec.retiring = true
	}
}

// recordsOwnedBy returns the records registered into THIS realm's own map by
// identity (an activation only writes into its own fiber's realm).
func (r *realm) recordsOwnedBy(identity ProviderIdentity) []*providerRecord {
	r.mu.RLock()
	defer r.mu.RUnlock()
	var out []*providerRecord
	for _, rec := range r.own {
		if rec.identity == identity {
			out = append(out, rec)
		}
	}
	return out
}

// count returns the number of provider records in this realm's own map
// (diagnostics / tests). Child-realm records are counted on their own realm.
func (r *realm) count() int {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return len(r.own)
}

// providerRecord is one exclusive provider registration for a Capability
// within one realm's own map.
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
