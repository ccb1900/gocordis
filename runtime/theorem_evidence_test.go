package runtime

import (
	"os"
	"strings"
	"testing"
)

// Metatheory evidence ledger (paper §4.3, Theorems 64/68/70/73/80).
//
// This test does NOT claim a mathematical proof. It records, for each theorem,
// (a) its EXACT paper premise, (b) the runtime check that establishes that
// premise, and (c) the existing test that observes the conclusion. It is an
// executable cross-reference between the paper and the runtime, and it
// fails if any of those anchors is removed — so the acceptance claim cannot
// silently drift from the code.

// TestTheoremPremiseAnchors — every theorem's premise has a live anchor.
func TestTheoremPremiseAnchors(t *testing.T) {
	anchors := map[string]struct {
		premise string
		anchor  string
		tests   []string
	}{
		"Thm64 Preservation": {
			premise: "If F_t is well formed then so is F_{t+1}, whichever rule step t applies",
			anchor:  "proof_invariants.checkPreservationInvariants runs per lifecycle step (c2_thm64_every_step_test)",
			tests:   []string{"thm64_preservation_test.go", "c2_thm64_every_step_test.go", "proof_invariants_test.go"},
		},
		"Thm68 Recovery": {
			premise: "applying n's accumulator at u leaves every fiber's table where the other fibers' steps would have left it from b (up to ≃_K)",
			anchor:  "Effect slot two-phase state machine + strict LIFO runInverses + install barrier",
			tests:   []string{"thm68_recovery_test.go", "c1_thm68_min_test.go", "effect_phase_test.go"},
		},
		"Thm70 Ordering": {
			premise: "L-Begin(m) requires gamma_t |= d_m; a consumer's episode is strictly nested in its provider's",
			anchor:  "dependency_graph.resolveDependency + addGraphEdge(ProviderIdentity) + beginWithdrawal wait gates",
			tests:   []string{"thm70_ordering_test.go", "c3_thm70_ordering_test.go"},
		},
		"Thm73 Progress": {
			premise: "≺ acyclic, len(e_n) <= K, names N finite, every step a lifecycle rule => no deadlock + quiescence",
			anchor:  "Preconditions.AcyclicPrecedence + semanticQuiescent effect-aware oracle + submit()/halt admission",
			tests:   []string{"thm73_thm80_test.go", "obs_test.go", "submit_drain_test.go"},
		},
		"Thm80 Confluence": {
			premise: "quiescent gamma_T, every component total on its provision; two sequences with same orchestration steps reach ≃-related states after renaming",
			anchor:  "thm80DetCanonical (drops generative IDs) + Preconditions.ComponentTotal + det interleaving driver",
			tests:   []string{"thm80_det_interleaving_test.go", "revision_thm80_test.go"},
		},
	}
	for name, e := range anchors {
		if strings.TrimSpace(e.premise) == "" || strings.TrimSpace(e.anchor) == "" || len(e.tests) == 0 {
			t.Fatalf("%s: incomplete evidence ledger", name)
		}
		for _, f := range e.tests {
			if !testFileExists(f) {
				t.Fatalf("%s: evidence test file %s missing", name, f)
			}
		}
	}
}

// TestTheoremPreconditionsGate — the paper's premises are CHECKED, not
// assumed: when a premise is false, the conformance harness refuses to claim
// the theorem (Preconditions.All() is false). This is the executable form of
// "test through only covers the premise-satisfying subset".
func TestTheoremPreconditionsGate(t *testing.T) {
	all := Preconditions{
		AcyclicPrecedence: true, ProviderUnique: true,
		PairwiseIndependent: true, ComponentTotal: true,
	}
	if !all.All() {
		t.Fatal("all-premises case must satisfy the gate")
	}
	violations := []Preconditions{
		{AcyclicPrecedence: false, ProviderUnique: true, PairwiseIndependent: true, ComponentTotal: true}, // Thm73/80 need acyclic ≺
		{AcyclicPrecedence: true, ProviderUnique: false, PairwiseIndependent: true, ComponentTotal: true}, // Thm64/70 need unique provider
		{AcyclicPrecedence: true, ProviderUnique: true, PairwiseIndependent: false, ComponentTotal: true}, // §3.4 needs independence
		{AcyclicPrecedence: true, ProviderUnique: true, PairwiseIndependent: true, ComponentTotal: false}, // Thm80 needs totality
	}
	for _, v := range violations {
		if v.All() {
			t.Fatalf("premise violation passed the gate: %s", v.String())
		}
	}
}

// TestTheorem73QuiescenceWithInstallingEffect — paper eq.(49): a fiber with
// an in-flight install is NOT quiescent. The effect-aware oracle must reject
// it, otherwise Progress would certify a state whose effects are still moving.
func TestTheorem73QuiescenceWithInstallingEffect(t *testing.T) {
	fx := []EffectView{{Seq: 1, Kind: EffectKindCustom, State: "Installing"}}
	if err := quiescenceError("f", StateActive, false, 0, fx); err == nil {
		t.Fatal("quiescence oracle ignored an Installing effect (Thm73 violated)")
	}
	if err := quiescenceError("f", StateActive, false, 0, nil); err != nil {
		t.Fatalf("stable Active fiber rejected as non-quiescent: %v", err)
	}
}

// TestTheorem80RenamingEquivalence — paper Lemma 61: generative names (the
// next-actor IDs det-driver assigns) are not semantically observable; the
// confluence canonicalization must drop them. Uses the real model so the
// claim cannot drift.
func TestTheorem80RenamingEquivalence(t *testing.T) {
	model := NewObservationModel(ObservationSpec{
		Key: "svc", Fields: map[ObservedField]bool{ObserveAvailability: true},
	})
	a := RuntimeSnapshot{Providers: []ProviderView{{
		Key: "svc", State: StateActive, OwnerFiberID: 3, OwnerActivationID: 9,
	}}}
	b := RuntimeSnapshot{Providers: []ProviderView{{
		Key: "svc", State: StateActive, OwnerFiberID: 101, OwnerActivationID: 200,
	}}}
	if !model.EquivalentAt(a, b, nil) {
		t.Fatal("generative owner/activation IDs leaked into ≃ (Thm80/Lemma61 violated)")
	}
}

// testFileExists verifies an evidence anchor file is still present in the
// package directory (Go test runs with cwd = package dir).
func testFileExists(name string) bool {
	_, err := os.Stat(name)
	return err == nil
}
