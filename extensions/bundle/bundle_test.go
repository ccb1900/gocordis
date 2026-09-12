package bundle

import (
	"strings"
	"testing"

	"dynamic-runtime/extensions/config"
)

func TestRegisterExpandAndOverride(t *testing.T) {
	if err := Register("test-alpha", func() []config.ComponentConfig {
		return []config.ComponentConfig{
			Row("a", "type-a", map[string]any{"x": 1}),
			Row("b", "type-b", nil),
		}
	}); err != nil {
		t.Fatal(err)
	}
	if err := Register("test-beta", func() []config.ComponentConfig {
		// Same id as alpha's "b": the later bundle replaces it in place.
		return []config.ComponentConfig{
			Row("b", "type-b2", map[string]any{"y": 2}),
			Row("c", "type-c", nil),
		}
	}); err != nil {
		t.Fatal(err)
	}
	rows, err := Expand([]string{"test-alpha", "test-beta"})
	if err != nil {
		t.Fatal(err)
	}
	ids := make([]string, 0, len(rows))
	for _, r := range rows {
		ids = append(ids, r.ID)
	}
	if got, want := strings.Join(ids, ","), "a,b,c"; got != want {
		t.Fatalf("expand order/override: got %s want %s", got, want)
	}
	if rows[1].Type != "type-b2" {
		t.Fatalf("later bundle must win for the same id, got %s", rows[1].Type)
	}
}

func TestExpandUnknownNamesError(t *testing.T) {
	if _, err := Expand([]string{"test-does-not-exist"}); err == nil || !strings.Contains(err.Error(), "unknown bundle") {
		t.Fatalf("unknown bundle must error with guidance, got %v", err)
	}
}

func TestExpandEmpty(t *testing.T) {
	rows, err := Expand(nil)
	if err != nil || len(rows) != 0 {
		t.Fatalf("no bundles must expand to no rows, got %v %v", rows, err)
	}
}

func TestRegisterValidation(t *testing.T) {
	if err := Register("", func() []config.ComponentConfig { return nil }); err == nil {
		t.Fatal("empty name must be rejected")
	}
	if err := Register("test-nil-emit", nil); err == nil {
		t.Fatal("nil emit must be rejected")
	}
	defer func() {
		if recover() == nil {
			t.Fatal("MustRegister must panic on invalid input")
		}
	}()
	MustRegister("", nil)
}

func TestMergeRows(t *testing.T) {
	base := []config.ComponentConfig{
		Row("keep", "t", nil),
		Row("swap", "old", nil),
	}
	out := MergeRows(base, []config.ComponentConfig{
		Row("swap", "new", map[string]any{"k": "v"}),
		Row("added", "t", nil),
	})
	if len(out) != 3 {
		t.Fatalf("merge: expected 3 rows, got %d", len(out))
	}
	if out[1].Type != "new" || out[1].ID != "swap" {
		t.Fatalf("merge must replace in place, got %+v", out[1])
	}
	if out[2].ID != "added" {
		t.Fatalf("merge must append unknown ids, got %+v", out[2])
	}
	// The base slice must not be aliased by later mutations.
	out[0].Type = "mutated"
	if base[0].Type == "mutated" {
		t.Fatal("MergeRows must copy the base slice")
	}
}
