package main

import (
	"context"
	"fmt"
	"os"
	"path/filepath"

	"github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
)

// platformDesiredStore is the application's declaration store for the console
// lifecycle. It persists to the same TOML manifest the composition root reads;
// no Fiber is touched here.
type platformDesiredStore struct {
	path          string
	owned         func() []config.OwnedComponent
	clearOverride func()
}

func (s *platformDesiredStore) Load(ctx context.Context) ([]config.ComponentConfig, error) {
	data, err := os.ReadFile(s.path)
	if err != nil {
		return nil, err
	}
	source := configwatch.Source{ID: "platform-manifest", Path: s.path, Format: configwatch.FormatTOML}
	desired, err := configwatch.NewTOMLParser().Parse(ctx, source, data)
	if err != nil {
		return nil, err
	}
	// The manifest is the persisted declaration, but Market can add desired
	// rows at runtime. Overlay controller-owned rows that are not in the file
	// so a console switch never silently removes them.
	byID := make(map[string]struct{}, len(desired.Components))
	for _, cc := range desired.Components {
		byID[cc.ID] = struct{}{}
	}
	if s.owned != nil {
		for _, o := range s.owned() {
			if _, exists := byID[o.ID]; exists || o.ID == "" {
				continue
			}
			cc := config.ComponentConfig{ID: o.ID, Type: o.Type, Config: o.Config}
			if o.Fiber == nil {
				disabled := false
				cc.Enabled = &disabled
			}
			desired.Components = append(desired.Components, cc)
			byID[o.ID] = struct{}{}
		}
	}
	return desired.Components, nil
}

func (s *platformDesiredStore) Save(_ context.Context, comps []config.ComponentConfig) error {
	rows := make([]map[string]any, 0, len(comps))
	for _, c := range comps {
		row := map[string]any{"id": c.ID, "type": c.Type}
		if c.Enabled != nil {
			row["enabled"] = *c.Enabled
		}
		if len(c.Config) > 0 {
			row["config"] = c.Config
		}
		rows = append(rows, row)
	}
	data, err := toml.Marshal(map[string]any{"components": rows})
	if err != nil {
		return fmt.Errorf("encode manifest: %w", err)
	}
	tmp := s.path + ".tmp"
	if err := os.WriteFile(tmp, data, 0o644); err != nil {
		return err
	}
	if err := os.Rename(tmp, s.path); err != nil {
		_ = os.Remove(tmp)
		return err
	}
	if s.clearOverride != nil {
		s.clearOverride()
	}
	return nil
}

// cloneComponentConfigs deep-copies declaration rows before an in-place switch
// so the memoized override is never aliased with the persisted desired set.
func cloneComponentConfigs(in []config.ComponentConfig) []config.ComponentConfig {
	out := make([]config.ComponentConfig, 0, len(in))
	for _, cc := range in {
		row := cc
		if cc.Config != nil {
			row.Config = make(map[string]any, len(cc.Config))
			for k, v := range cc.Config {
				row.Config[k] = v
			}
		}
		out = append(out, row)
	}
	return out
}

// platformReconciler resolves declaration-side paths before handing the
// desired set to the one lifecycle authority, matching reconcileManifest.
type platformReconciler struct{ app *app }

func (r platformReconciler) Reconcile(ctx context.Context, desired config.Config) error {
	resolveDeclarationPaths(desired, filepath.Dir(r.app.manifestPath))
	r.app.mu.Lock()
	copyDesired := desired
	r.app.override = &copyDesired
	r.app.mu.Unlock()
	if err := r.app.ctrl.Reconcile(ctx, desired); err != nil {
		return err
	}
	if r.app.explorer != nil {
		r.app.explorer.SetDesired(desired)
	}
	return nil
}
