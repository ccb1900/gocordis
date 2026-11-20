package patch

import (
	"encoding/json"
	"fmt"
	"os"

	"dynamic-runtime/extensions/config"
)

// PatchOp enumerates the row-level composition operations. Patches are the
// desired-state overlay: an ordered list of edits applied over the base
// configuration, where a later operation on the same component id overrides
// an earlier one (the dsh profiles/bundles/patches layering, applied to one
// base file).
type PatchOp string

const (
	// PatchRemove drops the component row from the composition. The row's
	// definition is kept inside the patch so Install can restore it.
	PatchRemove PatchOp = "remove"
	// PatchReplace swaps one composition row wholesale (id, type, config).
	// Removing the row from the base file while a replace patch exists is a
	// reconciliation error — the conflict surfaces instead of silently
	// dropping a console decision.
	PatchReplace PatchOp = "replace"
	// PatchInsert appends a row the base composition does not declare.
	PatchInsert PatchOp = "insert"
)

const Version = 1

// Patch is one ordered composition edit.
type Patch struct {
	Op        PatchOp                 `json:"op"`
	ID        string                  `json:"id"`
	Component *config.ComponentConfig `json:"component,omitempty"`
}

// Doc is the on-disk patch file shape.
type Doc struct {
	Version int     `json:"version"`
	Patches []Patch `json:"patches"`
}

// ParsePatchDoc reads a patch file. The current format is
// {"version":1,"patches":[{"op":"remove","id":...}, ...]}; the legacy
// console overlay {"removed":[...],"modified":[...]} is still accepted and
// converted — modified rows first, then removed rows — so an id present in
// both stays uninstalled, exactly as the old map-based overlay behaved.
func ParsePatchDoc(data []byte) ([]Patch, error) {
	// EXT-06: a document that DECLARES a version must match the supported one.
	// The old code fell through to the legacy parser for any mismatch, so
	// {"version":2,...} parsed as an empty legacy overlay instead of failing.
	var probe struct {
		Version *int `json:"version"`
	}
	if err := json.Unmarshal(data, &probe); err == nil && probe.Version != nil {
		if *probe.Version != Version {
			return nil, fmt.Errorf("patch file: unsupported format version %d (supported: %d)", *probe.Version, Version)
		}
		var doc Doc
		if err := json.Unmarshal(data, &doc); err != nil {
			return nil, fmt.Errorf("patch file: %w", err)
		}
		for _, p := range doc.Patches {
			if p.ID == "" {
				return nil, fmt.Errorf("patch %q: missing component id", p.Op)
			}
			if (p.Op == PatchReplace || p.Op == PatchInsert) && (p.Component == nil || p.Component.ID == "") {
				return nil, fmt.Errorf("patch %s %q: missing component definition", p.Op, p.ID)
			}
			// EXT-06: a replace patch locates the row by p.ID — swapping in a
			// row with a DIFFERENT id would rename the component instead of
			// replacing it.
			if p.Op == PatchReplace && p.Component.ID != p.ID {
				return nil, fmt.Errorf("patch replace %q: component definition has mismatched id %q", p.ID, p.Component.ID)
			}
		}
		return doc.Patches, nil
	}
	// Legacy console overlay (pre-patch format, no version marker).
	var legacy struct {
		Removed  []config.ComponentConfig `json:"removed"`
		Modified []config.ComponentConfig `json:"modified"`
	}
	if err := json.Unmarshal(data, &legacy); err != nil {
		return nil, fmt.Errorf("patch file: neither patch nor legacy overlay format: %w", err)
	}
	var out []Patch
	for _, cc := range legacy.Modified {
		if cc.ID == "" {
			continue
		}
		def := cc
		out = append(out, Patch{Op: PatchReplace, ID: cc.ID, Component: &def})
	}
	for _, cc := range legacy.Removed {
		if cc.ID == "" {
			continue
		}
		def := cc
		out = append(out, Patch{Op: PatchRemove, ID: cc.ID, Component: &def})
	}
	return out, nil
}

// LoadPatchFile reads and parses a patch file from disk.
func LoadPatchFile(path string) ([]Patch, error) {
	data, err := os.ReadFile(path)
	if err != nil {
		return nil, fmt.Errorf("patch file %s: %w", path, err)
	}
	patches, err := ParsePatchDoc(data)
	if err != nil {
		return nil, fmt.Errorf("patch file %s: %w", path, err)
	}
	return patches, nil
}

func indexOfComponent(components []config.ComponentConfig, id string) int {
	for i := range components {
		if components[i].ID == id {
			return i
		}
	}
	return -1
}

// ApplyPatches applies patch layers in order over a base composition. Every
// operation of one layer applies before the next layer starts; inside a
// layer the declaration order rules. Removing an already-absent row is
// idempotent; replacing an absent row and inserting a duplicate row are
// errors, because both mean the base file and the patch disagree about the
// composition's shape and the conflict must surface.
// ApplyPatches applies patch layers in order over a deep copy of the base
// composition and commits the result only on FULL success (EXT-06): a failing
// layer leaves the caller's cfg untouched instead of half-applied.
func ApplyPatches(cfg *config.Config, layers ...[]Patch) error {
	if cfg == nil {
		return fmt.Errorf("patch apply: nil config")
	}
	work := cloneConfig(cfg)
	for _, layer := range layers {
		for _, p := range layer {
			switch p.Op {
			case PatchRemove:
				if idx := indexOfComponent(work.Components, p.ID); idx >= 0 {
					work.Components = append(work.Components[:idx], work.Components[idx+1:]...)
				}
			case PatchReplace:
				// EXT-06: defense in depth for programmatically built patches.
				if p.Component == nil || p.Component.ID != p.ID {
					return fmt.Errorf("patch replace %q: component definition missing or has mismatched id", p.ID)
				}
				idx := indexOfComponent(work.Components, p.ID)
				if idx < 0 {
					return fmt.Errorf("patch replace %q: component not in base composition", p.ID)
				}
				work.Components[idx] = *p.Component
			case PatchInsert:
				if p.Component == nil {
					return fmt.Errorf("patch insert %q: missing component definition", p.ID)
				}
				if indexOfComponent(work.Components, p.Component.ID) >= 0 {
					return fmt.Errorf("patch insert %q: component already in composition", p.Component.ID)
				}
				work.Components = append(work.Components, *p.Component)
			default:
				return fmt.Errorf("patch %q: unknown op %q", p.ID, p.Op)
			}
		}
	}
	*cfg = *work
	return nil
}

// cloneConfig deep-copies a composition: rows and their Config maps are
// duplicated so patch application can never alias caller-owned maps.
func cloneConfig(cfg *config.Config) *config.Config {
	out := &config.Config{Components: make([]config.ComponentConfig, 0, len(cfg.Components))}
	for _, cc := range cfg.Components {
		row := cc
		if cc.Config != nil {
			row.Config = make(map[string]any, len(cc.Config))
			for k, v := range cc.Config {
				row.Config[k] = v
			}
		}
		out.Components = append(out.Components, row)
	}
	return out
}
