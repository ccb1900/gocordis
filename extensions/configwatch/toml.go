package configwatch

import (
	"context"
	"fmt"

	toml "github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/bundle"
	"dynamic-runtime/extensions/config"
)

// NewTOMLParser returns the default Parser, backed by a full TOML
// implementation (github.com/pelletier/go-toml/v2).
//
// The mapping targets the documented config.Config shape:
//
//	bundles = ["app-core", "app-console"]
//
//	[[components]]
//	id = "camera"
//	type = "builtin.camera"
//
//	[components.config]
//	device = "cam-01"
//	interval = "1s"
//
// Named bundles expand into their component rows first (extensions/bundle
// registry); explicit [[components]] rows then replace a bundle row with the
// same id in place, or append. The bundles key must appear at the TOP of the
// file — a top-level key after any [[table]] silently attaches to that table
// in TOML.
//
// Any TOML-syntax error, an unsupported top-level table/key, a non-array
// "components", or a component missing id/type is reported as an error
// wrapping ErrInvalidSource. Non-reserved keys on a component (anything other
// than id/type/config) are tolerated and merged into that component's Config
// map, matching the earlier constrained-parser behavior.
func NewTOMLParser() Parser { return &tomlParser{} }

type tomlParser struct{}

func (p *tomlParser) Parse(ctx context.Context, source Source, data []byte) (config.Config, error) {
	if err := ctx.Err(); err != nil {
		return config.Config{}, err
	}

	var doc map[string]any
	if err := toml.Unmarshal(data, &doc); err != nil {
		return config.Config{}, fmt.Errorf("%w: source %q toml parse: %v", ErrInvalidSource, source.ID, err)
	}

	// Only the components array-of-tables and the bundles preset reference
	// are mapped; any other top-level key or table is outside the
	// config.Config contract.
	for key := range doc {
		if key != "components" && key != "bundles" {
			return config.Config{}, fmt.Errorf("%w: source %q unsupported top-level key/table %q", ErrInvalidSource, source.ID, key)
		}
	}

	presetRows, err := parseBundleReferences(source, doc["bundles"])
	if err != nil {
		return config.Config{}, err
	}

	out := config.Config{}
	rawComponents, ok := doc["components"]
	if ok && rawComponents != nil {
		components, perr := parseComponents(source, rawComponents)
		if perr != nil {
			return config.Config{}, perr
		}
		out.Components = bundle.MergeRows(presetRows, components)
	} else {
		out.Components = presetRows
	}

	for i := range out.Components {
		if out.Components[i].ID == "" || out.Components[i].Type == "" {
			return config.Config{}, fmt.Errorf("%w: source %q component #%d missing id/type", ErrInvalidSource, source.ID, i)
		}
	}
	return out, nil
}

// parseBundleReferences reads the `bundles = ["name", ...]` preset
// references and expands them through the framework registry.
func parseBundleReferences(source Source, raw any) ([]config.ComponentConfig, error) {
	if raw == nil {
		return nil, nil
	}
	list, ok := raw.([]any)
	if !ok {
		return nil, fmt.Errorf("%w: source %q \"bundles\" must be an array of preset names", ErrInvalidSource, source.ID)
	}
	names := make([]string, 0, len(list))
	for i, item := range list {
		name, ok := item.(string)
		if !ok {
			return nil, fmt.Errorf("%w: source %q bundles #%d must be a string", ErrInvalidSource, source.ID, i)
		}
		names = append(names, name)
	}
	rows, err := bundle.Expand(names)
	if err != nil {
		return nil, fmt.Errorf("%w: source %q: %v", ErrInvalidSource, source.ID, err)
	}
	return rows, nil
}

func parseComponents(source Source, rawComponents any) ([]config.ComponentConfig, error) {
	components, ok := rawComponents.([]any)
	if !ok {
		return nil, fmt.Errorf("%w: source %q \"components\" must be an array of tables ([[components]])", ErrInvalidSource, source.ID)
	}

	out := make([]config.ComponentConfig, 0, len(components))
	for i, raw := range components {
		el, ok := raw.(map[string]any)
		if !ok {
			return nil, fmt.Errorf("%w: source %q component #%d is not a table", ErrInvalidSource, source.ID, i)
		}
		cc := config.ComponentConfig{}
		for k, v := range el {
			switch k {
			case "id":
				s, ok := v.(string)
				if !ok {
					return nil, fmt.Errorf("%w: source %q component #%d id must be a string", ErrInvalidSource, source.ID, i)
				}
				cc.ID = s
			case "type":
				s, ok := v.(string)
				if !ok {
					return nil, fmt.Errorf("%w: source %q component #%d type must be a string", ErrInvalidSource, source.ID, i)
				}
				cc.Type = s
			case "enabled":
				b, ok := v.(bool)
				if !ok {
					return nil, fmt.Errorf("%w: source %q component #%d enabled must be a bool", ErrInvalidSource, source.ID, i)
				}
				enabled := b
				cc.Enabled = &enabled
			case "config":
				m, ok := v.(map[string]any)
				if !ok {
					return nil, fmt.Errorf("%w: source %q component #%d config must be a table", ErrInvalidSource, source.ID, i)
				}
				if len(m) == 0 {
					continue
				}
				if cc.Config == nil {
					cc.Config = make(map[string]any, len(m))
				}
				for ck, cv := range m {
					cc.Config[ck] = cv
				}
			default:
				// Tolerate extra component-level keys by merging them into the
				// component's Config map (legacy behavior).
				if cc.Config == nil {
					cc.Config = make(map[string]any)
				}
				cc.Config[k] = v
			}
		}
		out = append(out, cc)
	}
	return out, nil
}
