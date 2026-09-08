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
	// meta holds context-carried metadata installments ι(k) (paper Definition
	// 26) for THIS realm; ancestor installments apply first.
	meta map[CapabilityKey][]*metaEntry
}

func newRealm(parent *realm) *realm {
	return &realm{
		parent:    parent,
		own:       make(map[CapabilityKey]*providerRecord),
		intercept: make(map[CapabilityKey][]*interceptEntry),
		meta:      make(map[CapabilityKey][]*metaEntry),
	}
}

// interceptEntry is one installed read-time interceptor. apply transforms the
// resolved value; typed wrappers (runtime.Intercept) guarantee T.
type interceptEntry struct {
	key   CapabilityKey
	apply func(value any) (any, error)
}

// metaEntry is one context-carried metadata installment ι(k) (paper
// Definition 26). nu merges onto the inherited metadata with the key's own
// ⊕ₖ; combine/zero are carried so reads can fold without reifying the key.
type metaEntry struct {
	key     CapabilityKey
	nu      any
	combine func(a, b any) any
}

// addMeta appends a context-carried metadata entry for key in this realm
// (install order).
func (r *realm) addMeta(e *metaEntry) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if r.meta == nil {
		r.meta = make(map[CapabilityKey][]*metaEntry)
	}
	r.meta[e.key] = append(r.meta[e.key], e)
}

// removeMeta removes exactly e from this realm's metadata table (idempotent).
func (r *realm) removeMeta(e *metaEntry) {
	r.mu.Lock()
	defer r.mu.Unlock()
	chain := r.meta[e.key]
	for i, x := range chain {
		if x == e {
			r.meta[e.key] = append(chain[:i], chain[i+1:]...)
			if len(r.meta[e.key]) == 0 {
				delete(r.meta, e.key)
			}
			return
		}
	}
}

// metaForKey folds the context-carried metadata for key along the context
// chain (ancestor -> this realm, install order within a realm): ι = ι ⊕ ν.
// Rightmost (most recent / most derived) installment wins on conflicts.
// Returns false when no installment exists (ι = εₖ).
func (r *realm) metaForKey(key CapabilityKey) (any, bool) {
	var cur any
	found := false
	var walk func(x *realm)
	walk = func(x *realm) {
		if x == nil {
			return
		}
		walk(x.parent)
		x.mu.RLock()
		chain := x.meta[key]
		for _, e := range chain {
			if found {
				cur = e.combine(cur, e.nu)
			} else {
				cur = e.nu
				found = true
			}
		}
		x.mu.RUnlock()
	}
	walk(r)
	return cur, found
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

// lookupOwn resolves key in THIS realm's own map only — one namespace, no
// ancestor fallback (paper §4.4 Isolation: each declared key resolves against
// exactly one realm, the realm of the fiber declaring it; the one shared realm
// is the diagonal case). Which namespace a fiber reads for a key is decided by
// its per-key isolation table (paper Definition 24), not by a realm walk.
func (r *realm) lookupOwn(key CapabilityKey) (*providerRecord, bool) {
	if r == nil {
		return nil, false
	}
	r.mu.RLock()
	rec, ok := r.own[key]
	r.mu.RUnlock()
	return rec, ok
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
