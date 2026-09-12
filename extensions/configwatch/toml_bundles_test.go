package configwatch_test

import (
	"strings"
	"testing"

	"dynamic-runtime/extensions/bundle"
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
)

func init() {
	bundle.MustRegister("toml-parser-test-core", func() []config.ComponentConfig {
		return []config.ComponentConfig{
			bundle.Row("core-a", "type.a", map[string]any{"keep": true}),
			bundle.Row("swap", "type.old", nil),
		}
	})
}

// Bundles expand first; an explicit component row replaces the bundle row
// with the same id wholesale; unknown ids append.
func TestTOMLParserBundles(t *testing.T) {
	body := `
bundles = ["toml-parser-test-core"]

[[components]]
id = "swap"
type = "type.new"

[components.config]
override = true

[[components]]
id = "own"
type = "type.own"
`
	p := configwatch.NewTOMLParser()
	got, err := p.Parse(t.Context(), configwatch.Source{ID: "cfg", Path: "/v/app.toml", Format: configwatch.FormatTOML}, []byte(body))
	if err != nil {
		t.Fatalf("Parse: %v", err)
	}
	ids := make([]string, 0, len(got.Components))
	for _, c := range got.Components {
		ids = append(ids, c.ID)
	}
	if want := "core-a,swap,own"; strings.Join(ids, ",") != want {
		t.Fatalf("components = %v, want %v", ids, want)
	}
	// The explicit row wins wholesale.
	if got.Components[1].Type != "type.new" || got.Components[1].Config["override"] != true {
		t.Fatalf("explicit override = %+v", got.Components[1])
	}
	// The untouched bundle row keeps its preset config.
	if got.Components[0].Config["keep"] != true {
		t.Fatalf("bundle row = %+v", got.Components[0])
	}
}

// A bundles-only file is valid: presets alone drive the composition.
func TestTOMLParserBundlesOnly(t *testing.T) {
	p := configwatch.NewTOMLParser()
	got, err := p.Parse(t.Context(), configwatch.Source{ID: "cfg", Path: "/v/app.toml", Format: configwatch.FormatTOML}, []byte(`bundles = ["toml-parser-test-core"]`))
	if err != nil {
		t.Fatalf("Parse: %v", err)
	}
	if len(got.Components) != 2 {
		t.Fatalf("components = %d, want 2", len(got.Components))
	}
}

func TestTOMLParserBundlesErrors(t *testing.T) {
	cases := []struct {
		name string
		body string
		want string
	}{
		{"unknown bundle", `bundles = ["nope"]`, "unknown bundle"},
		{"wrong shape", `bundles = "x"`, "must be an array"},
		{"non-string name", `bundles = [1]`, "must be a string"},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			p := configwatch.NewTOMLParser()
			_, err := p.Parse(t.Context(), configwatch.Source{ID: "cfg", Path: "/v/app.toml", Format: configwatch.FormatTOML}, []byte(tc.body))
			if err == nil || !strings.Contains(err.Error(), tc.want) {
				t.Fatalf("err = %v, want containing %q", err, tc.want)
			}
		})
	}
}

// Without the key nothing changes: legacy files parse exactly as before.
func TestTOMLParserWithoutBundles(t *testing.T) {
	body := "[[components]]\nid = \"x\"\ntype = \"t\"\n"
	p := configwatch.NewTOMLParser()
	got, err := p.Parse(t.Context(), configwatch.Source{ID: "cfg", Path: "/v/app.toml", Format: configwatch.FormatTOML}, []byte(body))
	if err != nil {
		t.Fatalf("Parse: %v", err)
	}
	if len(got.Components) != 1 || got.Components[0].ID != "x" {
		t.Fatalf("components = %+v", got.Components)
	}
}
