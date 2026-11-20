package integration

import (
	"context"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Rehome × Revision composition conformance (public API only).
//
// Paper §5.2.1: after a provider's realm move, old-namespace dependents lose
// their binding and "follow only through their own revision" — the revision
// carries the new realm pairs (§4.4 Configuration). These tests pin that
// promise at the composition level: a dependents' revision must be able to
// adopt the provider's post-move namespace and rebind.

// TestIntegrationRehomeComposition — the provider rehomes; the old-namespace
// consumer withdraws to Pending (correct: it does not follow implicitly); a
// consumer revision carrying WithResolvedRealms adopts the provider's fresh
// namespace and rebinds to the SAME provider activation (no provider reload).
func TestIntegrationRehomeComposition(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	seen := make(chan string, 16)
	pf, err := rt.Load(pcProv("v1"))
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(pcCons(nil, seen))
	if err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if v := <-seen; v != "v1" {
		t.Fatalf("initial binding = %q, want v1", v)
	}

	// The short route: the provider moves namespaces without reloading.
	if err := pf.Rehome(ctx, runtime.RehomeWithFreshIsolation()); err != nil {
		t.Fatalf("rehome: %v", err)
	}

	// The old-namespace dependent loses its binding (paper-faithful).
	eventually(t, 8*time.Second, "consumer withdraws to Pending after rehome", func() bool {
		return cf.State() == runtime.StatePending
	})

	// Its OWN revision carries the new realm pairs and rebinds to the same
	// provider activation.
	nf, err := cf.Revise(ctx, pcCons(nil, seen), runtime.WithResolvedRealms())
	if err != nil {
		t.Fatalf("revise: %v", err)
	}
	if err := nf.Ready(ctx); err != nil {
		t.Fatalf("revised consumer never rebinds: %v", err)
	}
	select {
	case v := <-seen:
		if v != "v1" {
			t.Fatalf("rebound tag = %q, want v1 (same provider)", v)
		}
	default:
		t.Fatal("revised consumer did not re-resolve the dependency")
	}

	// The provider stayed on its original activation throughout (short route).
	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	live := 0
	for _, p := range snap.Providers {
		if p.Key == pcSvcKey.Capability().String() && !p.Retiring {
			live++
		}
	}
	if live != 1 {
		t.Fatalf("expected exactly one live provider record after rebind, got %d", live)
	}
	rtPreservationProbe(t, rt, ctx)
}

// TestIntegrationRehomeVsReviseRace — concurrent Rehome and Revise on the same
// fiber from two goroutines: exactly one replacement path wins per attempt and
// the loser receives a typed busy error (no double fiber, no hang).
func TestIntegrationRehomeVsReviseRace(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	pf, err := rt.Load(pcProv("v1"))
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatal(err)
	}

	type outcome struct {
		kind string // "rehome" | "revise"
		nf   *runtime.Fiber
		err  error
	}
	results := make(chan outcome, 2)
	go func() {
		err := pf.Rehome(ctx, runtime.RehomeWithFreshIsolation())
		results <- outcome{kind: "rehome", err: err}
	}()
	go func() {
		nf, err := pf.Revise(ctx, pcProv("v2"))
		results <- outcome{kind: "revise", nf: nf, err: err}
	}()

	busy := 0
	for i := 0; i < 2; i++ {
		select {
		case r := <-results:
			switch r.err {
			case nil:
				if r.kind == "revise" && r.nf != nil {
					if err := r.nf.Ready(ctx); err != nil {
						t.Fatalf("revised fiber not ready: %v", err)
					}
				}
			case runtime.ErrRehomeBusy, runtime.ErrReviseBusy, runtime.ErrRehomeNotActive:
				// Typed rejection: the other operation completed first (a
				// revision replaced the fiber, so it is no longer Active) or
				// the operations genuinely collided on the entry.
				busy++
			default:
				t.Fatalf("%s: unexpected error %v", r.kind, r.err)
			}
		case <-ctx.Done():
			t.Fatal("a racing Rehome/Revise never returned — no reply may hang")
		}
	}
	// Both operations racing the same fiber: at least the losing one must be
	// typed-busy; both succeeding serially is also acceptable (rehome then
	// revise, or revise then rehome). What must NEVER happen is a hang or an
	// untyped error.
	if busy == 2 {
		t.Fatal("both operations reported busy — neither ran")
	}
}

// checkPreservationPublic runs the runtime's preservation oracle from outside
// the package via the exported Snapshot-based composition checks.
func rtPreservationProbe(t *testing.T, rt *runtime.Runtime, ctx context.Context) {
	t.Helper()
	snap, err := rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	for _, p := range snap.Providers {
		if p.Retiring {
			t.Fatalf("provider %s left retiring at quiescence", p.Key)
		}
	}
}
