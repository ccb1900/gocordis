package configwatch

import (
	"context"
	"fmt"

	toml "github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/config"
)

// NewTOMLParser returns the default Parser, backed by a full TOML
// implementation (github.com/pelletier/go-toml/v2).
//
// The mapping targets the documented config.Config shape:
//
//	[[components]]
//	id = "camera"
//	type = "builtin.camera"
//
//	[components.config]
//	device = "cam-01"
//	interval = "1s"
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

	// Only the components array-of-tables is mapped; any other top-level key
	// or table is outside the config.Config contract.
	for key := range doc {
		if key != "components" {
			return config.Config{}, fmt.Errorf("%w: source %q unsupported top-level key/table %q", ErrInvalidSource, source.ID, key)
		}
	}

	out := config.Config{}
	rawComponents, ok := doc["components"]
	if !ok {
		return out, nil
	}
	components, ok := rawComponents.([]any)
	if !ok {
		return config.Config{}, fmt.Errorf("%w: source %q \"components\" must be an array of tables ([[components]])", ErrInvalidSource, source.ID)
	}

	for i, raw := range components {
		el, ok := raw.(map[string]any)
		if !ok {
			return config.Config{}, fmt.Errorf("%w: source %q component #%d is not a table", ErrInvalidSource, source.ID, i)
		}
		cc := config.ComponentConfig{}
		for k, v := range el {
			switch k {
			case "id":
				s, ok := v.(string)
				if !ok {
					return config.Config{}, fmt.Errorf("%w: source %q component #%d id must be a string", ErrInvalidSource, source.ID, i)
				}
				cc.ID = s
			case "type":
				s, ok := v.(string)
				if !ok {
					return config.Config{}, fmt.Errorf("%w: source %q component #%d type must be a string", ErrInvalidSource, source.ID, i)
				}
				cc.Type = s
			case "enabled":
				b, ok := v.(bool)
				if !ok {
					return config.Config{}, fmt.Errorf("%w: source %q component #%d enabled must be a bool", ErrInvalidSource, source.ID, i)
				}
				enabled := b
				cc.Enabled = &enabled
			case "config":
				m, ok := v.(map[string]any)
				if !ok {
					return config.Config{}, fmt.Errorf("%w: source %q component #%d config must be a table", ErrInvalidSource, source.ID, i)
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
		out.Components = append(out.Components, cc)
	}

	for i := range out.Components {
		if out.Components[i].ID == "" || out.Components[i].Type == "" {
			return config.Config{}, fmt.Errorf("%w: source %q component #%d missing id/type", ErrInvalidSource, source.ID, i)
		}
	}
	return out, nil
}
