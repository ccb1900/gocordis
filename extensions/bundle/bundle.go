// Package bundle: named presets of declarative component rows — the
// declarative layer's reuse mechanism, the same shape as shared source
// profiles generalized beyond sources (dsh: profiles and bundles). A preset
// emits the full component rows it contributes; a configuration references
// bundles by name (`bundles = ["app-core"]`) and overrides individual rows
// with explicit [[components]] declarations (whole-row replace by id).
// Presets keep the audit story intact: whatever dumps or serves the
// effective configuration always prints the EXPANDED rows.
package bundle

import (
	"fmt"
	"sort"
	"sync"

	"dynamic-runtime/extensions/config"
)

var (
	mu      sync.RWMutex
	presets = map[string]func() []config.ComponentConfig{}
)

// Register installs a named preset. Re-registering a name replaces the
// previous preset — bundles are process-wide built-ins, last definition
// wins.
func Register(name string, emit func() []config.ComponentConfig) error {
	mu.Lock()
	defer mu.Unlock()
	if name == "" {
		return fmt.Errorf("bundle: empty name")
	}
	if emit == nil {
		return fmt.Errorf("bundle %q: nil emit function", name)
	}
	presets[name] = emit
	return nil
}

// MustRegister is Register for init()-time definitions; a programming error
// (empty name, nil emit) panics.
func MustRegister(name string, emit func() []config.ComponentConfig) {
	if err := Register(name, emit); err != nil {
		panic(err)
	}
}

// Names lists the registered preset names (sorted, for diagnostics).
func Names() []string {
	mu.RLock()
	defer mu.RUnlock()
	out := make([]string, 0, len(presets))
	for name := range presets {
		out = append(out, name)
	}
	sort.Strings(out)
	return out
}

// Expand emits the named presets in order. A later preset replaces an
// earlier preset's row with the same id in place; an unknown name is an
// error that lists what IS registered.
func Expand(names []string) ([]config.ComponentConfig, error) {
	mu.RLock()
	missing := []string{}
	for _, name := range names {
		if _, ok := presets[name]; !ok {
			missing = append(missing, name)
		}
	}
	mu.RUnlock()
	if len(missing) > 0 {
		return nil, fmt.Errorf("unknown bundle(s) %v; registered: %v", missing, Names())
	}
	rows := []config.ComponentConfig{}
	for _, name := range names {
		mu.RLock()
		emit := presets[name]
		mu.RUnlock()
		rows = MergeRows(rows, emit())
	}
	return rows, nil
}

// MergeRows overlays overlay rows over base rows: a row whose id already
// exists replaces the base row in place (declaration order preserved),
// unknown ids append.
func MergeRows(base, overlay []config.ComponentConfig) []config.ComponentConfig {
	out := append([]config.ComponentConfig(nil), base...)
	for _, row := range overlay {
		replaced := false
		for i := range out {
			if out[i].ID == row.ID {
				out[i] = row
				replaced = true
				break
			}
		}
		if !replaced {
			out = append(out, row)
		}
	}
	return out
}

// Row is the ergonomic shape for preset definitions.
func Row(id, typ string, cfg map[string]any) config.ComponentConfig {
	return config.ComponentConfig{ID: id, Type: typ, Config: cfg}
}
