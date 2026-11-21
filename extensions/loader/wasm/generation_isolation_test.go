package wasm

import (
	"context"
	"testing"

	"dynamic-runtime/extensions/loader"
)

// NEW-12/13/14 acceptance tests (audit round): resource-identity isolation
// for the WASM backend's generation protocol. Every test is deterministic —
// channel/channel-free choreography through the handle protocol, no sleeps.

// TestStaleReleaseAfterReloadDoesNotTouchNewGeneration — NEW-12: a handle
// whose generation was superseded (Unload + fresh same-ID load) must NOT
// close the new generation's resources on a late Release.
func TestStaleReleaseAfterReloadDoesNotTouchNewGeneration(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("gen", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	// Attempt A: gen-1.
	_, hA, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatal(err)
	}
	hA.Commit() // registry owns gen-1

	// Registry discards gen-1 (Unload via handle Release).
	hA.Release()
	if b.CompiledModules() != 0 {
		t.Fatalf("gen-1 not released: %d", b.CompiledModules())
	}

	// Fresh same-ID load: gen-2 created.
	_, hC, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatal(err)
	}
	if b.CompiledModules() != 1 {
		t.Fatalf("gen-2 not tracked: %d", b.CompiledModules())
	}

	// The stale gen-1 handle's repeat Release must be a no-op (state machine:
	// already Released) — it must NOT close the fresh gen-2.
	hA.Release() // stale double-release: no-op
	if b.CompiledModules() != 1 {
		t.Fatalf("stale double-release closed gen-2 (NEW-12): %d", b.CompiledModules())
	}

	// gen-2 remains usable; its own handle releases it.
	hC.Release()
	if b.CompiledModules() != 0 {
		t.Fatalf("gen-2 not released by its own handle: %d", b.CompiledModules())
	}
}

// TestLoadResourceBindsCorrectGeneration — NEW-13: Load → Unload → fresh
// same-ID Load must produce a handle bound to the NEW generation, never the
// old one (the Module and its handle come from one critical section).
func TestLoadResourceBindsCorrectGeneration(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("bind", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	_, h1, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatal(err)
	}
	h1.Release()

	_, h2, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatal(err)
	}
	// h2's Release must succeed — proving h2 is bound to the CURRENT gen (a
	// stale gen-1 binding would be an identity-checked no-op and leave the
	// fresh generation leaked).
	h2.Release()
	if b.CompiledModules() != 0 {
		t.Fatalf("fresh handle did not release its own generation: %d", b.CompiledModules())
	}
}

// TestHandleSingleTransition — NEW-14: each handle performs at most one of
// Commit/Abandon; repeat calls are no-ops that never decrement another
// attempt's holder slot.
func TestHandleSingleTransition(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("single", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	// Two attempts share one generation (holders = 2).
	_, hA, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatal(err)
	}
	_, hB, err := b.LoadResource(context.Background(), art)
	if err != nil {
		t.Fatal(err)
	}

	// A commits, then (erroneously) abandons again: the state machine must
	// make the second transition a no-op — the committed attempt's claim is
	// not double-released.
	hA.Commit()
	hA.Abandon() // post-commit abandon: no-op
	if b.CompiledModules() != 1 {
		t.Fatalf("double transition closed the committed generation: %d", b.CompiledModules())
	}

	// B abandons: it releases its holder slot, but the REGISTRY owns the
	// generation through A's committed handle — B's abandon must NOT close it
	// (the committed generation is closed by the registry's own Release).
	hB.Abandon()
	if b.CompiledModules() != 1 {
		t.Fatalf("B's abandon closed a registry-owned generation: %d", b.CompiledModules())
	}

	// B's repeat abandon: no-op (already abandoned).
	hB.Abandon()
	if b.CompiledModules() != 1 {
		t.Fatalf("repeat abandon changed state: %d", b.CompiledModules())
	}

	// The registry discards via A's handle: now the generation closes.
	hA.Release()
	if b.CompiledModules() != 0 {
		t.Fatalf("registry Release did not close the generation: %d", b.CompiledModules())
	}

	// B's post-release abandon: no-op.
	hB.Abandon()
	if b.CompiledModules() != 0 {
		t.Fatalf("post-release abandon resurrected state: %d", b.CompiledModules())
	}
}

// TestCommitAbandonInterleave — Commit and Abandon on different handles of
// the same generation must never double-transfer or double-release: exactly
// one holder decrement per handle, exactly one close at zero.
func TestCommitAbandonInterleave(t *testing.T) {
	b := newTestBackend()
	art := wasmArtifact("inter", "file://"+absFixture(t, "valid.wasm"))
	art.BackendType = loader.BackendWASM
	art.Source = "file://" + absFixture(t, "valid.wasm")

	_, hA, _ := b.LoadResource(context.Background(), art)
	_, hB, _ := b.LoadResource(context.Background(), art)
	_, hC, _ := b.LoadResource(context.Background(), art)
	if b.CompiledModules() != 1 {
		t.Fatalf("dedupe broken: %d", b.CompiledModules())
	}

	// Interleaved transitions.
	hB.Abandon() // holders 3->2
	hA.Commit()  // holders 2->1, committed
	hC.Abandon() // holders 1->0; committed -> registry owns; NO close
	if b.CompiledModules() != 1 {
		t.Fatalf("committed generation closed while registry owns it: %d", b.CompiledModules())
	}
	hA.Release() // registry discards: close
	if b.CompiledModules() != 0 {
		t.Fatalf("registry discard did not release: %d", b.CompiledModules())
	}
}
