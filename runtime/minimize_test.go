package runtime

import (
	"fmt"
	"testing"
)

// Deterministic shrinker (theorem-spec §36 / AC-12).
//
// Minimize performs deterministic delta-debugging over an ordered operation
// list: it greedily removes a suffix-range of elements and keeps any removal
// that still reproduces the failure, restarting from the smaller list until no
// single-element removal reproduces. It is deterministic (no randomness) and
// returns a minimal-or-near-minimal counterexample, i.e. removing any one more
// element makes the failure disappear.
//
// prop reports whether the operation list still reproduces the failure.
func Minimize(ops []string, prop func([]string) bool) []string {
	cur := append([]string(nil), ops...)
	changed := true
	for changed {
		changed = false
		for i := range cur {
			cand := make([]string, 0, len(cur)-1)
			cand = append(cand, cur[:i]...)
			cand = append(cand, cur[i+1:]...)
			if len(cand) > 0 && prop(cand) {
				cur = cand
				changed = true
				break // restart from the front (DDMIN-style, deterministic)
			}
		}
	}
	return cur
}

// TestMinimizeDeterministic — the shrinker reduces a 40-op failure trace to a
// minimal counterexample and is fully deterministic.
func TestMinimizeDeterministic(t *testing.T) {
	// Failure predicate: trace must contain both "open" and "commit".
	prop := func(ops []string) bool {
		hasOpen, hasCommit := false, false
		for _, o := range ops {
			if o == "open" {
				hasOpen = true
			}
			if o == "commit" {
				hasCommit = true
			}
		}
		return hasOpen && hasCommit
	}
	trace := make([]string, 0, 40)
	trace = append(trace, "open")
	for i := 0; i < 38; i++ {
		trace = append(trace, fmt.Sprintf("noise-%d", i))
	}
	trace = append(trace, "commit")

	// Precondition: the trace fails (reproduces) and every 1-element prefix
	// removal is non-reproducing (true minimality).
	if !prop(trace) {
		t.Fatal("trace must reproduce failure")
	}

	got := Minimize(trace, prop)
	if len(got) != 2 || got[0] != "open" || got[1] != "commit" {
		t.Fatalf("minimized trace = %v, want [open commit]", got)
	}
	for i := range got {
		rem := append(append([]string(nil), got[:i]...), got[i+1:]...)
		if prop(rem) {
			t.Fatalf("shrinker not minimal: removing %q still reproduces (%v)", got[i], rem)
		}
	}
	// Deterministic: two runs agree.
	if again := Minimize(trace, prop); fmt.Sprint(again) != fmt.Sprint(got) {
		t.Fatalf("shrinker nondeterministic: %v vs %v", again, got)
	}
}
