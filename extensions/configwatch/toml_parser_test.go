package configwatch_test

import (
	"context"
	"errors"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
)

func parseTOML(t *testing.T, body string) (config.Config, error) {
	t.Helper()
	p := configwatch.NewTOMLParser()
	return p.Parse(context.Background(), configwatch.Source{ID: "cfg", Path: "/v/app.toml", Format: configwatch.FormatTOML}, []byte(body))
}

// T1 — the documented mapping shape parses to the expected Config.
func TestTOMLParserMapping(t *testing.T) {
	body := `[[components]]

id = "camera"
type = "builtin.camera"

[components.config]
device = "cam-01"
interval = "1s"
`
	got, err := parseTOML(t, body)
	if err != nil {
		t.Fatalf("Parse: %v", err)
	}
	if len(got.Components) != 1 {
		t.Fatalf("components = %d, want 1", len(got.Components))
	}
	c := got.Components[0]
	if c.ID != "camera" || c.Type != "builtin.camera" {
		t.Fatalf("component = %+v", c)
	}
	if c.Config["device"] != "cam-01" || c.Config["interval"] != "1s" {
		t.Fatalf("config = %v", c.Config)
	}
}

// T2 — multiple components, each with its own config table.
func TestTOMLParserMultipleComponents(t *testing.T) {
	body := `[[components]]
id = "a"
type = "ta"

[components.config]
mode = "1"

[[components]]
id = "b"
type = "tb"

[components.config]
mode = "2"
`
	got, err := parseTOML(t, body)
	if err != nil {
		t.Fatalf("Parse: %v", err)
	}
	if len(got.Components) != 2 {
		t.Fatalf("components = %d, want 2", len(got.Components))
	}
	if got.Components[0].ID != "a" || got.Components[1].ID != "b" {
		t.Fatalf("order/id lost: %+v", got.Components)
	}
	if got.Components[0].Config["mode"] != "1" || got.Components[1].Config["mode"] != "2" {
		t.Fatalf("per-component config wrong: %v %v", got.Components[0].Config, got.Components[1].Config)
	}
}

// T3 — full TOML value coverage (scalars, arrays, inline tables, datetimes).
func TestTOMLParserFullValues(t *testing.T) {
	body := `[[components]]
id = "a"
type = "t"
empty = []

[components.config]
str = "line1\nline2"
multiline = """
hello
world"""
int = 42
neg = -7
float = 3.5
bool = true
arr = [1, "two", 3.0]
inline = { x = 1, y = "z" }
when = 2024-01-02T03:04:05Z
`
	got, err := parseTOML(t, body)
	if err != nil {
		t.Fatalf("Parse: %v", err)
	}
	c := got.Components[0]
	// Legacy tolerance: component-level "empty" scalar merges into Config.
	if _, ok := c.Config["empty"]; !ok {
		t.Fatal("component-level extra key not tolerated")
	}
	if c.Config["str"] != "line1\nline2" {
		t.Fatalf("escaped string = %#v", c.Config["str"])
	}
	if c.Config["multiline"] != "hello\nworld" {
		t.Fatalf("multiline = %#v", c.Config["multiline"])
	}
	if v, ok := c.Config["int"].(int64); !ok || v != 42 {
		t.Fatalf("int = %#v (%T)", c.Config["int"], c.Config["int"])
	}
	if v, ok := c.Config["arr"].([]any); !ok || len(v) != 3 {
		t.Fatalf("arr = %#v (%T)", c.Config["arr"], c.Config["arr"])
	}
	if _, ok := c.Config["inline"].(map[string]any); !ok {
		t.Fatalf("inline = %#v (%T)", c.Config["inline"], c.Config["inline"])
	}
	if _, ok := c.Config["when"].(time.Time); !ok {
		t.Fatalf("datetime = %#v (%T)", c.Config["when"], c.Config["when"])
	}
}

// T4 — invalid TOML is a parse error (wraps ErrInvalidSource).
func TestTOMLParserInvalidRejected(t *testing.T) {
	cases := []string{
		"[[components]\nid = \"cam\"\n",         // unclosed array-of-tables header
		"[[components]]\nid = \"cam\"",          // missing type
		"[[components]]\ntype = \"t\"",          // missing id
		"[components]\nid = \"cam\"",            // single table, not array-of-tables
		"id = 42\n",                             // scalar before any components
		"[[components]]\nid = 42\ntype = \"t\"", // id not a string
		"server = \"x\"\n",                      // unsupported top-level key
		"[server]\nport = 1\n",                  // unsupported top-level table
	}
	for i, body := range cases {
		if _, err := parseTOML(t, body); !errors.Is(err, configwatch.ErrInvalidSource) {
			t.Fatalf("case %d: Parse = %v, want ErrInvalidSource", i, err)
		}
	}
}

// T5 — empty and comment-only files map to an empty Config.
func TestTOMLParserEmpty(t *testing.T) {
	for _, body := range []string{"", "# only a comment\n\n"} {
		got, err := parseTOML(t, body)
		if err != nil {
			t.Fatalf("Parse(%q): %v", body, err)
		}
		if len(got.Components) != 0 {
			t.Fatalf("components = %d, want 0", len(got.Components))
		}
	}
}

// T-enabled — the plugin switch parses from the declaration store and defaults
// to enabled when absent (paper §5.2.1: the declarative layer may disable and
// later re-enable a component).
func TestTOMLParserEnabledFlag(t *testing.T) {
	cfg, err := parseTOML(t, `
[[components]]
id = "live"
type = "svc"

[[components]]
id = "off"
type = "svc"
enabled = false

[[components]]
id = "explicit"
type = "svc"
enabled = true
`)
	if err != nil {
		t.Fatal(err)
	}
	if len(cfg.Components) != 3 {
		t.Fatalf("components = %d, want 3", len(cfg.Components))
	}
	for _, c := range cfg.Components {
		switch c.ID {
		case "live":
			if c.Enabled != nil {
				t.Fatalf("live: Enabled = %v, want nil (default enabled)", *c.Enabled)
			}
		case "off":
			if c.Enabled == nil || *c.Enabled {
				t.Fatalf("off: Enabled = %v, want false", c.Enabled)
			}
		case "explicit":
			if c.Enabled == nil || !*c.Enabled {
				t.Fatalf("explicit: Enabled = %v, want true", c.Enabled)
			}
		}
	}
}
