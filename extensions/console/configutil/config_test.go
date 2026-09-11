package configutil_test

import (
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/configutil"
)

func cc(kv ...any) config.ComponentConfig {
	m := map[string]any{}
	for i := 0; i+1 < len(kv); i += 2 {
		m[kv[i].(string)] = kv[i+1]
	}
	return config.ComponentConfig{ID: "c", Type: "c", Config: m}
}

func TestOptionalString(t *testing.T) {
	if got := configutil.OptionalString(cc("k", "v"), "k", "def"); got != "v" {
		t.Fatalf("got %q", got)
	}
	if got := configutil.OptionalString(cc(), "k", "def"); got != "def" {
		t.Fatalf("missing key: got %q", got)
	}
	// Non-string scalars are Sprint-ed (tolerant input).
	if got := configutil.OptionalString(cc("k", 42), "k", "def"); got != "42" {
		t.Fatalf("scalar: got %q", got)
	}
}

func TestRequiredString(t *testing.T) {
	if got, err := configutil.RequiredString(cc("k", "v"), "k"); err != nil || got != "v" {
		t.Fatalf("got %q err %v", got, err)
	}
	if _, err := configutil.RequiredString(cc("k", ""), "k"); err == nil {
		t.Fatal("empty string must be an error")
	}
	if _, err := configutil.RequiredString(cc(), "k"); err == nil {
		t.Fatal("missing key must be an error")
	}
}

func TestOptionalInt(t *testing.T) {
	cases := []struct {
		v    any
		want int
	}{
		{7, 7}, {int64(9), 9}, {float64(5), 5}, {"12", 12},
	}
	for _, c := range cases {
		if got := configutil.OptionalInt(cc("k", c.v), "k", 0); got != c.want {
			t.Fatalf("OptionalInt(%v) = %d, want %d", c.v, got, c.want)
		}
	}
	if got := configutil.OptionalInt(cc(), "k", 3); got != 3 {
		t.Fatalf("missing: got %d", got)
	}
	if got := configutil.OptionalInt(cc("k", "not-a-number"), "k", 3); got != 3 {
		t.Fatalf("unparsable: got %d, want default", got)
	}
}

func TestOptionalBool(t *testing.T) {
	if !configutil.OptionalBool(cc("k", true), "k", false) {
		t.Fatal("bool true")
	}
	if configutil.OptionalBool(cc("k", "false"), "k", true) {
		t.Fatal("string \"false\"")
	}
	if !configutil.OptionalBool(cc("k", "true"), "k", false) {
		t.Fatal("string \"true\"")
	}
	if configutil.OptionalBool(cc(), "k", false) {
		t.Fatal("missing: want default false")
	}
}

func TestOptionalStringSlice(t *testing.T) {
	got := configutil.OptionalStringSlice(cc("k", []any{"a", "b"}), "k", nil)
	if len(got) != 2 || got[0] != "a" || got[1] != "b" {
		t.Fatalf("[]any: %v", got)
	}
	got = configutil.OptionalStringSlice(cc("k", "solo"), "k", nil)
	if len(got) != 1 || got[0] != "solo" {
		t.Fatalf("scalar promoted: %v", got)
	}
	def := []string{"d"}
	got = configutil.OptionalStringSlice(cc(), "k", def)
	if len(got) != 1 || got[0] != "d" {
		t.Fatalf("missing: %v", got)
	}
}
