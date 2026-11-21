// Package adapters projects the metadata of the existing plugin ecosystems
// (native loader artifacts / config component configs, WASM modules and
// proc-plugins) into the unified pluginmeta.Manifest descriptor.
//
// The result is descriptive metadata only. Adapters never load a module,
// register a capability or change lifecycle: Runtime, Loader and Config keep
// sole ownership of component identity and enablement (AGENTS.md §1).
package adapters

import (
	"strings"

	"dynamic-runtime/extensions/console/procplugin"
	"dynamic-runtime/extensions/pluginmeta"
)

// FromNative builds a manifest for a statically linked (native/builtin)
// component. id and componentType correspond to config.ComponentConfig.ID /
// .Type (and loader.Artifact.ID / logical Module.Type); cfg is the factory
// config map. requires and provides are capability key names used for
// documentation only — no runtime.Dependency is created.
func FromNative(id, componentType, version string, cfg map[string]any, requires, provides []string) pluginmeta.Manifest {
	return pluginmeta.Manifest{
		ID:          id,
		Name:        id,
		Version:     version,
		Description: describe("native component", componentType),
		Backend:     pluginmeta.BackendNative,
		ModuleType:  componentType,
		Requires:    toRequirements(requires),
		Provides:    toDescriptors(provides, "native component"),
		Config:      cfg,
		Source:      pluginmeta.Source{Kind: string(pluginmeta.BackendNative), Location: componentType},
	}
}

// FromWASM builds a manifest for a WASM module. moduleType is the logical
// Module Type declared by the module itself (loader.Artifact.Type must not be
// copied into it); the adapter inputs carry no source URL or checksum, so
// Source records only the kind. requires/provides remain descriptive.
func FromWASM(id, version, moduleType string, requires, provides []string) pluginmeta.Manifest {
	return pluginmeta.Manifest{
		ID:          id,
		Name:        id,
		Version:     version,
		Description: describe("wasm module", moduleType),
		Backend:     pluginmeta.BackendWASM,
		ModuleType:  moduleType,
		Requires:    toRequirements(requires),
		Provides:    toDescriptors(provides, "wasm module"),
		Source:      pluginmeta.Source{Kind: string(pluginmeta.BackendWASM)},
	}
}

// FromProc projects a procplugin.PluginManifest into the unified descriptor.
// Name becomes the stable ID and ModuleType, Title becomes the human-facing
// Name; backend/client/pages are retained in Config and the backend (or the
// client for frontend-only plugins) is recorded as Source.Location.
// Queries, commands and pages become descriptive provided keys prefixed with
// "query:", "command:" and "page:" (pages also contribute human-readable
// detail to Description). proc manifests carry no version, so Version uses
// the "0.0.0" sentinel for "unknown"; callers that know the release should
// override it. None of this affects lifecycle — the plugin's fibers are still
// created and reconciled by Config/Runtime through DiscoverPlugins.
func FromProc(pm procplugin.PluginManifest) pluginmeta.Manifest {
	name := pm.Title
	if name == "" {
		name = pm.Name
	}
	provides := make([]pluginmeta.Descriptor, 0, len(pm.Queries)+len(pm.Commands)+len(pm.Pages))
	for _, q := range pm.Queries {
		provides = append(provides, pluginmeta.Descriptor{Key: "query:" + q, Description: "hub query served by the plugin backend"})
	}
	for _, c := range pm.Commands {
		provides = append(provides, pluginmeta.Descriptor{Key: "command:" + c, Description: "hub command served by the plugin backend"})
	}
	pageNotes := make([]string, 0, len(pm.Pages))
	for _, p := range pm.Pages {
		note := p.Title
		if p.Description != "" {
			if note != "" {
				note += ": "
			}
			note += p.Description
		}
		if p.Route != "" {
			note += " (" + p.Route + ")"
		}
		if note != "" {
			pageNotes = append(pageNotes, note)
		}
		key := p.ID
		if key == "" {
			key = p.Route
		}
		if key != "" {
			provides = append(provides, pluginmeta.Descriptor{Key: "page:" + key, Description: describe("declarative page", p.Title)})
		}
	}
	location := pm.Backend
	if location == "" {
		location = pm.Client
	}
	return pluginmeta.Manifest{
		ID:          pm.Name,
		Name:        name,
		Version:     "0.0.0", // proc manifests have no version field
		Description: strings.Join(pageNotes, "; "),
		Backend:     pluginmeta.BackendProc,
		ModuleType:  pm.Name,
		Provides:    provides,
		Config: map[string]any{
			"backend": pm.Backend,
			"client":  pm.Client,
			"pages":   pm.Pages,
		},
		Source: pluginmeta.Source{Kind: string(pluginmeta.BackendProc), Location: location},
	}
}

func toRequirements(keys []string) []pluginmeta.Requirement {
	out := make([]pluginmeta.Requirement, 0, len(keys))
	for _, k := range keys {
		out = append(out, pluginmeta.Requirement{Key: k})
	}
	return out
}

func toDescriptors(keys []string, description string) []pluginmeta.Descriptor {
	out := make([]pluginmeta.Descriptor, 0, len(keys))
	for _, k := range keys {
		out = append(out, pluginmeta.Descriptor{Key: k, Description: description})
	}
	return out
}

func describe(prefix, subject string) string {
	if subject == "" {
		return prefix
	}
	return prefix + ": " + subject
}
