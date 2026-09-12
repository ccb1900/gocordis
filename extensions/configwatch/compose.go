package configwatch

import (
	"context"
	"fmt"

	toml "github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/bundle"
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/procplugin"
)

// Layer is one application/domain contribution to the composition pipeline.
// The pipeline assembles the composition in fixed layers, later layers
// overriding earlier ones by component id:
//
//	bundles (extensions/bundle registry, from the "bundles" key)
//	  → plugin discovery ("plugins/<name>/manifest.toml", when PluginDir set)
//	    → registered Layers, in order (domain expansions, e.g. [[sources]])
//	      → explicit [[components]] rows
//
// A Layer reads the parsed document's own tables (it declares the top-level
// keys it consumes so unknown-key validation stays strict) and returns
// additional rows. PostMerge runs once after the full merge for hooks that
// enrich EXISTING rows (e.g. injecting discovered source definitions into
// the composition's query provider) — it may mutate the composition in
// place; anything it cannot reconcile must be an error.
type Layer struct {
	Name         string
	ConsumedKeys []string
	Expand       func(ctx context.Context, doc map[string]any) ([]config.ComponentConfig, error)
	PostMerge    func(cfg *config.Config) error
}

// ComposeOptions configures ComposeDocument.
type ComposeOptions struct {
	// PluginDir enables the plugin manifest discovery layer
	// (extensions/console/procplugin) rooted at this directory. Empty skips it.
	PluginDir string
	// Layers are the application/domain layers, applied in order between
	// plugin discovery and the explicit [[components]] rows.
	Layers []Layer
}

// ComposeDocument is the canonical composition pipeline shared by every
// parser: parse the document, expand bundles, discover plugins, run the
// registered domain layers, merge the explicit rows on top, run PostMerge
// hooks, and validate row identities. Patches are intentionally NOT part of
// the pipeline — they are desired-state overlays applied by the host over
// the expanded composition (console overlay and --patch operator layers).
func ComposeDocument(ctx context.Context, data []byte, opts ComposeOptions) (config.Config, error) {
	doc := map[string]any{}
	if err := toml.Unmarshal(data, &doc); err != nil {
		return config.Config{}, fmt.Errorf("toml parse: %w", err)
	}
	known := map[string]bool{"bundles": true, "components": true}
	for _, layer := range opts.Layers {
		for _, k := range layer.ConsumedKeys {
			known[k] = true
		}
	}
	for key := range doc {
		if !known[key] {
			return config.Config{}, fmt.Errorf("unsupported top-level key/table %q", key)
		}
	}

	presetRows, err := expandBundleRows(doc["bundles"])
	if err != nil {
		return config.Config{}, err
	}
	var discovered []config.ComponentConfig
	if opts.PluginDir != "" {
		discovered, err = procplugin.DiscoverPlugins(opts.PluginDir)
		if err != nil {
			return config.Config{}, err
		}
	}
	components, err := parseComponentRows(doc["components"])
	if err != nil {
		return config.Config{}, err
	}
	cfg := config.Config{Components: presetRows}
	cfg.Components = bundle.MergeRows(cfg.Components, discovered)
	for _, layer := range opts.Layers {
		var rows []config.ComponentConfig
		if layer.Expand != nil {
			rows, err = layer.Expand(ctx, doc)
			if err != nil {
				return config.Config{}, fmt.Errorf("layer %q: %w", layer.Name, err)
			}
		}
		cfg.Components = bundle.MergeRows(cfg.Components, rows)
	}
	cfg.Components = bundle.MergeRows(cfg.Components, components)

	for _, layer := range opts.Layers {
		if layer.PostMerge != nil {
			if err := layer.PostMerge(&cfg); err != nil {
				return config.Config{}, fmt.Errorf("layer %q post-merge: %w", layer.Name, err)
			}
		}
	}

	for i := range cfg.Components {
		if cfg.Components[i].ID == "" || cfg.Components[i].Type == "" {
			return config.Config{}, fmt.Errorf("component #%d missing id/type", i)
		}
	}
	return cfg, nil
}

// expandBundleRows reads and expands the `bundles = ["name", ...]` preset
// references; shape errors are loud, not silently skipped.
func expandBundleRows(raw any) ([]config.ComponentConfig, error) {
	if raw == nil {
		return nil, nil
	}
	list, ok := raw.([]any)
	if !ok {
		return nil, fmt.Errorf(`"bundles" must be an array of preset names`)
	}
	names := make([]string, 0, len(list))
	for i, item := range list {
		name, ok := item.(string)
		if !ok {
			return nil, fmt.Errorf("bundles #%d must be a string", i)
		}
		names = append(names, name)
	}
	return bundle.Expand(names)
}

// parseComponentRows maps the explicit [[components]] array-of-tables.
func parseComponentRows(raw any) ([]config.ComponentConfig, error) {
	rawComponents, ok := raw.([]any)
	if !ok {
		if raw == nil {
			return nil, nil
		}
		return nil, fmt.Errorf("\"components\" must be an array of tables ([[components]])")
	}
	out := make([]config.ComponentConfig, 0, len(rawComponents))
	for i, el := range rawComponents {
		table, ok := el.(map[string]any)
		if !ok {
			return nil, fmt.Errorf("component #%d is not a table", i)
		}
		cc := config.ComponentConfig{}
		for k, v := range table {
			switch k {
			case "id":
				s, ok := v.(string)
				if !ok {
					return nil, fmt.Errorf("component #%d id must be a string", i)
				}
				cc.ID = s
			case "type":
				s, ok := v.(string)
				if !ok {
					return nil, fmt.Errorf("component #%d type must be a string", i)
				}
				cc.Type = s
			case "enabled":
				b, ok := v.(bool)
				if !ok {
					return nil, fmt.Errorf("component #%d enabled must be a bool", i)
				}
				enabled := b
				cc.Enabled = &enabled
			case "config":
				m, ok := v.(map[string]any)
				if !ok {
					return nil, fmt.Errorf("component #%d config must be a table", i)
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
