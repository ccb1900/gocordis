package runtime

import "testing"

// Paper §3.3.2 conformance, R1+R2:
//   - observation boundaries are DERIVED FROM OPERATIONS (Def 29/31/32), not
//     hand-declared fields; an undeclared key is NOT silently weakened;
//   - bindings are (key, realm) — two realms providing the same key are two
//     distinct observations (Def 24; audit R1);
//   - ≃_S = same domain∩S + per-binding ≃ₖ (Def 33); relation properties.
//
// These tests use REAL ProviderViews from a live runtime snapshot where the
// multi-realm shape matters; single-realm relation algebra uses typed views.

func obsKind() CapabilityKey { return NewKey[string]("obs.kind").Capability() }

func TestObservationModelValidateRejectsEmptyAndUnknownOps(t *testing.T) {
	k := obsKind()
	empty := NewObservationModel(ObservationSpec{Key: k, Ops: nil})
	if err := empty.Validate(); err == nil {
		t.Fatal("empty operation set accepted — R2 boundary not tied to operations")
	}
	unknown := NewObservationModel(ObservationSpec{Key: k, Ops: []Operation{"bogus"}})
	if err := unknown.Validate(); err == nil {
		t.Fatal("unknown operation accepted")
	}
	valid := NewObservationModel(ObservationSpec{Key: k, Ops: []Operation{OpRequire, OpBindIdentity}})
	if err := valid.Validate(); err != nil {
		t.Fatalf("valid spec rejected: %v", err)
	}
}

func TestObservationUndeclaredKeyUsesConservativeDefault(t *testing.T) {
	// R2: no spec for a key must NOT default to availability-only. The runtime
	// genuinely binds identity and routes realms; those changes must be seen.
	m := NewObservationModel() // no specs
	ops := m.OperationsFor(obsKind())
	if len(ops) < 4 {
		t.Fatalf("undeclared key default weakened to %v", ops)
	}
	a := ProviderView{Key: obsKind().String(), State: StateActive,
		OwnerFiberID: 1, OwnerActivationID: 1, ScopeID: 7}
	b := ProviderView{Key: obsKind().String(), State: StateActive,
		OwnerFiberID: 99, OwnerActivationID: 42, ScopeID: 8}
	if m.EquivalentValues(obsKind(), a, b) {
		t.Fatal("undeclared key ignored identity/realm changes (R2 violation)")
	}
}

func TestObservationEquivalenceIsRelation(t *testing.T) {
	// A key with only provide/require observes availability, not identity.
	k := NewKey[string]("obs.rel").Capability()
	m := NewObservationModel(ObservationSpec{Key: k, Ops: []Operation{OpProvide, OpRequire}})
	a := ProviderView{Key: k.String(), State: StateActive, OwnerFiberID: 1, OwnerActivationID: 1, ScopeID: 7}
	b := ProviderView{Key: k.String(), State: StateActive, OwnerFiberID: 88, OwnerActivationID: 99, ScopeID: 7}
	c := ProviderView{Key: k.String(), State: StateActive, OwnerFiberID: 7, OwnerActivationID: 8, ScopeID: 7}
	dead := ProviderView{Key: k.String(), State: StateUnloading, Retiring: true, OwnerFiberID: 1, OwnerActivationID: 1, ScopeID: 7}

	if !m.EquivalentValues(k, a, a) || !m.EquivalentValues(k, a, b) || !m.EquivalentValues(k, b, a) ||
		!m.EquivalentValues(k, a, c) || !m.EquivalentValues(k, b, c) {
		t.Fatal("≃ₖ not a reflexive/symmetric/transitive relation")
	}
	if m.EquivalentValues(k, a, dead) {
		t.Fatal("≃ₖ erased an observed availability difference")
	}
}

func TestObservationEquivalentAtDomainRestriction(t *testing.T) {
	// Def 33: ≃_S forgets keys outside S. Build two snapshots whose provider
	// sets differ only in an out-of-S key.
	m := NewObservationModel()
	kA := NewKey[string]("obs.A").Capability()
	kB := NewKey[string]("obs.B").Capability()
	base := RuntimeSnapshot{Providers: []ProviderView{
		{Key: kA.String(), State: StateActive, OwnerFiberID: 1, OwnerActivationID: 1},
	}}
	withB := RuntimeSnapshot{Providers: []ProviderView{
		{Key: kA.String(), State: StateActive, OwnerFiberID: 1, OwnerActivationID: 1},
		{Key: kB.String(), State: StateActive, OwnerFiberID: 2, OwnerActivationID: 2},
	}}
	if !m.EquivalentAt(base, withB, []CapabilityKey{kA}, nil) {
		t.Fatal("≃_{A} should forget key B outside S")
	}
	if m.EquivalentAt(base, withB, nil, nil) {
		t.Fatal("≃ (full) must compare dom equality")
	}
}

func TestObservationMultiRealmBindingsNotMerged(t *testing.T) {
	// R1: two realms providing the same key are distinct bindings; a change
	// to one must be observable only through an observer bound to that realm.
	k := NewKey[string]("obs.multi").Capability()
	ks := k.String()
	m := NewObservationModel()
	before := RuntimeSnapshot{Providers: []ProviderView{
		{Key: ks, State: StateActive, OwnerFiberID: 1, OwnerActivationID: 1, ScopeID: 10},
		{Key: ks, State: StateActive, OwnerFiberID: 2, OwnerActivationID: 2, ScopeID: 20},
	}}
	mutated := RuntimeSnapshot{Providers: []ProviderView{
		{Key: ks, State: StateActive, OwnerFiberID: 1, OwnerActivationID: 1, ScopeID: 11}, // realm A moved
		{Key: ks, State: StateActive, OwnerFiberID: 2, OwnerActivationID: 2, ScopeID: 20},
	}}
	// Bindings must be two distinct (key, scope) pairs, not merged by key.
	if len(ObservableBindings(before, nil)) != 2 {
		t.Fatalf("same-key/two-realm bindings were merged: %v", ObservableBindings(before, nil))
	}
	visA := map[CapabilityKey]ScopeID{k: 10}
	if m.EquivalentAt(before, mutated, nil, visA) {
		t.Fatal("observer in realm A missed its own binding change (R1)")
	}
	visB := map[CapabilityKey]ScopeID{k: 20}
	if !m.EquivalentAt(before, mutated, nil, visB) {
		t.Fatal("observer in realm B should ignore realm A's change")
	}
}
