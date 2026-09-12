package configwatch_test

import (
	"context"
	"strings"
	"testing"

	"dynamic-runtime/extensions/bundle"
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
)

func init() {
	bundle.MustRegister("compose-test-core", func() []config.ComponentConfig {
		return []config.ComponentConfig{
			bundle.Row("provider", "type.provider", nil),
		}
	})
}

// The pipeline assembles in fixed layers: bundles, then registered domain
// layers, then explicit rows — later layers replace same-id rows.
func TestComposeDocumentLayers(t *testing.T) {
	layer := configwatch.Layer{
		Name:         "domain",
		ConsumedKeys: []string{"sources"},
		Expand: func(_ context.Context, doc map[string]any) ([]config.ComponentConfig, error) {
			if _, ok := doc["sources"]; !ok {
				return nil, nil
			}
			return []config.ComponentConfig{
				bundle.Row("unit-from-source", "type.unit", nil),
			}, nil
		},
	}
	body := `
bundles = ["compose-test-core"]

sources = {}

[[components]]
id = "provider"
type = "type.explicit"
`
	cfg, err := configwatch.ComposeDocument(context.Background(), []byte(body), configwatch.ComposeOptions{
		Layers: []configwatch.Layer{layer},
	})
	if err != nil {
		t.Fatal(err)
	}
	var ids []string
	for _, c := range cfg.Components {
		ids = append(ids, c.ID+"|"+c.Type)
	}
	want := "provider|type.explicit,unit-from-source|type.unit"
	if strings.Join(ids, ",") != want {
		t.Fatalf("components = %v, want %v", ids, want)
	}
}

// PostMerge runs after the full merge, so it can enrich existing rows.
func TestComposeDocumentPostMerge(t *testing.T) {
	layer := configwatch.Layer{
		Name: "enriching",
		PostMerge: func(cfg *config.Config) error {
			for i := range cfg.Components {
				if cfg.Components[i].ID == "provider" {
					if cfg.Components[i].Config == nil {
						cfg.Components[i].Config = map[string]any{}
					}
					cfg.Components[i].Config["enriched"] = true
				}
			}
			return nil
		},
	}
	body := `bundles = ["compose-test-core"]`
	cfg, err := configwatch.ComposeDocument(context.Background(), []byte(body), configwatch.ComposeOptions{
		Layers: []configwatch.Layer{layer},
	})
	if err != nil {
		t.Fatal(err)
	}
	if cfg.Components[0].Config["enriched"] != true {
		t.Fatalf("post-merge enrichment missing: %+v", cfg.Components[0])
	}
}

func TestComposeDocumentStrictKeys(t *testing.T) {
	_, err := configwatch.ComposeDocument(context.Background(), []byte("mystery = 1"), configwatch.ComposeOptions{})
	if err == nil || !strings.Contains(err.Error(), "unsupported top-level key") {
		t.Fatalf("unknown key must error, got %v", err)
	}
	// A layer that consumes the key makes it legal.
	layer := configwatch.Layer{Name: "mystery-layer", ConsumedKeys: []string{"mystery"}}
	if _, err := configwatch.ComposeDocument(context.Background(), []byte("mystery = 1"), configwatch.ComposeOptions{
		Layers: []configwatch.Layer{layer},
	}); err != nil {
		t.Fatalf("consumed key must be accepted: %v", err)
	}
}
