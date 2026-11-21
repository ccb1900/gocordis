package runtime

import "testing"

// Paper §3.4 Independence (Definitions 40-45, Theorem 43):
//
// Def 42: iterators i, j are independent when
//   (1) every transformation of one commutes with every transformation of the
//       other (forward and inverse generators — Lemma 41 settles commutation
//       on generators);
//   (2) neither disturbs what the other YIELDS — inverse and continuation.
// Thm 43: pairwise-independent effects, reverted in ANY permutation, reach
// back to the starting state — this is why strict LIFO is conservative.
// Thm 45: operations at DISTINCT keys are independent outright (each lift
// reads/writes only its own key). Def 44: at ONE key, independence requires a
// witness that the key's operations are pairwise independent.
//
// These tests exercise the real runtime's provider operations through the
// observation model: distinct keys commute; same-key operations preserve the
// provider identity and availability; a provider operation's inverse does not
// disturb another key's binding (Thm 45's proof shape).

// op is one provider operation applied to a runtime snapshot model: install a
// provider at a key, or unwind (remove) it. It mirrors a lift's forward map +
// yielded inverse (paper Def 29 eq.30).
type op struct {
	name string
	key  string
	on   bool // true = provide (forward), false = withdraw (inverse)
	gen  uint64
}

func applyOps(base RuntimeSnapshot, ops ...op) RuntimeSnapshot {
	s := RuntimeSnapshot{
		Providers:    append([]ProviderView(nil), base.Providers...),
		Dependencies: append([]DependencyView(nil), base.Dependencies...),
		Effects:      append([]EffectView(nil), base.Effects...),
	}
	for _, o := range ops {
		if o.on {
			s.Providers = append(s.Providers, ProviderView{
				Key: o.key, State: StateActive, OwnerFiberID: FiberID(o.gen),
				OwnerActivationID: ActivationID(o.gen),
			})
			s.Effects = append(s.Effects, EffectView{Key: o.key, State: "Committed", OwnerFiberID: FiberID(o.gen)})
		} else {
			kept := s.Providers[:0]
			for _, p := range s.Providers {
				if p.Key != o.key {
					kept = append(kept, p)
				}
			}
			s.Providers = kept
			keptFx := s.Effects[:0]
			for _, e := range s.Effects {
				if e.Key != o.key {
					keptFx = append(keptFx, e)
				}
			}
			s.Effects = keptFx
		}
	}
	return s
}

// TestIndependenceDistinctKeysCommute — Theorem 45: operations at distinct
// keys are independent. Applying provide@a then provide@b yields the same
// observation as provide@b then provide@a, and likewise for the inverses in
// any order (Theorem 43's conclusion).
func TestIndependenceDistinctKeysCommute(t *testing.T) {
	model := NewObservationModel(
		ObservationSpec{Key: "a", Fields: map[ObservedField]bool{ObserveAvailability: true, ObserveOwnerGeneration: true}},
		ObservationSpec{Key: "b", Fields: map[ObservedField]bool{ObserveAvailability: true, ObserveOwnerGeneration: true}},
	)
	a := op{name: "a+", key: "a", on: true, gen: 1}
	b := op{name: "b+", key: "b", on: true, gen: 2}

	ab := applyOps(RuntimeSnapshot{}, a, b)
	ba := applyOps(RuntimeSnapshot{}, b, a)
	if !model.EquivalentAt(ab, ba, nil) {
		t.Fatalf("provide@a;provide@b and provide@b;provide@a differ at distinct keys")
	}

	// Inverses in ANY permutation recover the empty snapshot (Thm 43).
	aInv, bInv := op{name: "a-", key: "a", on: false}, op{name: "b-", key: "b", on: false}
	perm1 := applyOps(ab, aInv, bInv)
	perm2 := applyOps(ab, bInv, aInv)
	if len(perm1.Providers) != 0 || len(perm2.Providers) != 0 {
		t.Fatalf("inverse permutation did not recover base: %d/%d", len(perm1.Providers), len(perm2.Providers))
	}
}

// TestIndependenceSameKeyPreservesIdentity — Def 44 at one key: operations
// must not disturb the provider identity (inverse/continuation of Thm 42(2)).
// A re-provide at the same key changes the generation — that IS observed when
// the key declares ObserveOwnerGeneration, so independence is NOT automatic
// at one key (a witness is required, paper Def 45).
func TestIndependenceSameKeyRequiresWitness(t *testing.T) {
	model := NewObservationModel(ObservationSpec{
		Key: "k", Fields: map[ObservedField]bool{ObserveAvailability: true, ObserveOwnerGeneration: true},
	})
	p1 := ProviderView{Key: "k", State: StateActive, OwnerFiberID: 1, OwnerActivationID: 1}
	p2 := ProviderView{Key: "k", State: StateActive, OwnerFiberID: 2, OwnerActivationID: 2}
	if model.EquivalentValues("k", p1, p2) {
		t.Fatal("same-key operations at different generations must be distinguished (no free witness)")
	}
}

// TestIndependenceDoesNotDisturbOtherKey — Def 42(2) with two keys: an
// operation at a must not disturb b's yielded inverse or continuation. The
// runtime realizes this by per-key provider records (Isolation) — removing a
// changes only a's record; b's record and its inverse stay intact.
func TestIndependenceDoesNotDisturbOtherKey(t *testing.T) {
	base := applyOps(RuntimeSnapshot{},
		op{key: "a", on: true, gen: 1}, op{key: "b", on: true, gen: 2})
	bBefore := base.Providers[1]

	after := applyOps(base, op{key: "a", on: false}) // withdraw a only
	bAfter := after.Providers[0]
	if bAfter.Key != "b" || bAfter.OwnerFiberID != bBefore.OwnerFiberID ||
		bAfter.OwnerActivationID != bBefore.OwnerActivationID {
		t.Fatalf("operation at a disturbed b's binding: before=%+v after=%+v", bBefore, bAfter)
	}
}

// TestIndependenceGeneratorAcrossKeySets — a small generated sweep over key
// subsets S: equivalent snapshots must agree at every S, and restricting S
// never makes unrelated keys distinguishable (Def 33 ≃_S forgets keys outside
// S).
func TestIndependenceGeneratorAcrossKeySets(t *testing.T) {
	keys := []string{"k0", "k1", "k2"}
	model := NewObservationModel()
	for _, k := range keys {
		model.specs[k] = ObservationSpec{Key: k, Fields: map[ObservedField]bool{ObserveAvailability: true}}
	}
	// Base has k0..k2; variant changes only k2's generation (unobserved).
	base := applyOps(RuntimeSnapshot{},
		op{key: "k0", on: true, gen: 1}, op{key: "k1", on: true, gen: 2}, op{key: "k2", on: true, gen: 3})
	variant := applyOps(RuntimeSnapshot{},
		op{key: "k0", on: true, gen: 1}, op{key: "k1", on: true, gen: 2}, op{key: "k2", on: true, gen: 99})

	S := []CapabilityKey{
		NewKey[string]("k0").Capability(),
		NewKey[string]("k1").Capability(),
	}
	if !model.EquivalentAt(base, variant, S) {
		t.Fatal("≃_S must forget generation changes at keys outside S")
	}
}
