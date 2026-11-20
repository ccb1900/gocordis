package patch

import (
	"strings"
	"testing"

	"dynamic-runtime/extensions/config"
)

// EXT-06-1: a document declaring an UNSUPPORTED version must be an error, not
// a silently empty legacy parse.
func TestParsePatchDocRejectsUnsupportedVersion(t *testing.T) {
	_, err := ParsePatchDoc([]byte(`{"version":2,"patches":[{"op":"remove","id":"a"}]}`))
	if err == nil || !strings.Contains(err.Error(), "version") {
		t.Fatalf("unsupported version not rejected: %v", err)
	}
	// Current version still parses.
	ps, err := ParsePatchDoc([]byte(`{"version":1,"patches":[{"op":"remove","id":"a"}]}`))
	if err != nil || len(ps) != 1 {
		t.Fatalf("current version broken: %v", err)
	}
	// Genuine legacy overlay (no version marker) still accepted.
	ps, err = ParsePatchDoc([]byte(`{"removed":[{"id":"a","type":"t"}]}`))
	if err != nil || len(ps) != 1 {
		t.Fatalf("legacy overlay broken: %v", err)
	}
}

// EXT-06-2: a replace patch must not swap in a component with a different ID.
func TestPatchReplaceIDMismatchRejected(t *testing.T) {
	_, err := ParsePatchDoc([]byte(`{"version":1,"patches":[{"op":"replace","id":"a","component":{"id":"b","type":"t"}}]}`))
	if err == nil || !strings.Contains(err.Error(), "id") {
		t.Fatalf("replace ID mismatch not rejected at parse: %v", err)
	}
	cfg := &config.Config{Components: []config.ComponentConfig{{ID: "a", Type: "t"}}}
	err = ApplyPatches(cfg, []Patch{{Op: PatchReplace, ID: "a", Component: &config.ComponentConfig{ID: "b", Type: "t"}}})
	if err == nil {
		t.Fatal("replace ID mismatch not rejected at apply")
	}
	if cfg.Components[0].ID != "a" {
		t.Fatalf("caller config mutated by a failed apply: %+v", cfg.Components[0])
	}
}

// EXT-06-3: ApplyPatches is atomic — a failing layer leaves the caller's
// configuration untouched.
func TestApplyPatchesAtomicOnError(t *testing.T) {
	cfg := &config.Config{Components: []config.ComponentConfig{
		{ID: "a", Type: "t1", Config: map[string]any{"k": "v1"}},
	}}
	layers := []Patch{
		{Op: PatchInsert, ID: "b", Component: &config.ComponentConfig{ID: "b", Type: "t2", Config: map[string]any{"k": "v2"}}},
		{Op: PatchReplace, ID: "missing", Component: &config.ComponentConfig{ID: "missing", Type: "t"}},
	}
	if err := ApplyPatches(cfg, layers); err == nil {
		t.Fatal("expected apply error")
	}
	if len(cfg.Components) != 1 || cfg.Components[0].ID != "a" {
		t.Fatalf("failed apply left partial modifications: %+v", cfg.Components)
	}
	// Success applies everything.
	layers[1] = Patch{Op: PatchInsert, ID: "c", Component: &config.ComponentConfig{ID: "c", Type: "t3"}}
	if err := ApplyPatches(cfg, layers); err != nil {
		t.Fatalf("apply: %v", err)
	}
	if len(cfg.Components) != 3 {
		t.Fatalf("successful apply lost rows: %+v", cfg.Components)
	}
}
