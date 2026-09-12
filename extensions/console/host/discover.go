package host

import (
	"os"
	"path/filepath"
	"sort"

	appui "dynamic-runtime/extensions/console/registry"
)

// DefaultPluginsDir is the conventional location for plugin deployments.
// The convention is one plugin, one directory: everything a plugin consists
// of — its backend artifact and its frontend module — lives in the same
// directory, and nothing needs to be registered anywhere:
//
//	plugins/
//	└── alarm/                 ← the plugin: name = directory name
//	    ├── alarm-server       ← backend artifact (executable / wasm / ...)
//	    └── ui.js              ← frontend module, served as
//	                              /client-modules/alarm and loaded by the
//	                              console at boot
//
// Convention over configuration: an explicit client_modules row remains as
// an override for layouts that cannot follow this.
const DefaultPluginsDir = "plugins"

// frontendModuleNames are the conventional filenames of a plugin's frontend
// module inside its plugin directory.
var frontendModuleNames = []string{"ui.js", "ui.mjs"}

// DiscoverClientModules scans plugin deployment directories for the
// convention above: plugins/<plugin>/ui.js (or ui.mjs). The module NAME is
// the plugin directory name. Missing directories are not an error — the
// convention is optional. Results are sorted by name for deterministic
// manifests.
func DiscoverClientModules(dirs ...string) []appui.ClientModule {
	var out []appui.ClientModule
	seen := map[string]bool{}
	for _, dir := range dirs {
		entries, err := os.ReadDir(dir)
		if err != nil {
			continue // no such dir: the convention simply has nothing to offer
		}
		for _, entry := range entries {
			if !entry.IsDir() {
				continue // the convention is one directory per plugin
			}
			name := entry.Name()
			if seen[name] {
				continue
			}
			for _, candidate := range frontendModuleNames {
				modPath := filepath.Join(dir, name, candidate)
				if st, err := os.Stat(modPath); err != nil || st.IsDir() {
					continue
				}
				seen[name] = true
				out = append(out, appui.ClientModule{Name: name, Path: modPath})
				break
			}
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}

// MergeClientModules layers discovered (convention) modules under explicit
// (config) ones: an explicit row with the same name wins. Explicit rows are
// exceptions and overrides — the convention is the default.
func MergeClientModules(explicit, discovered []appui.ClientModule) []appui.ClientModule {
	out := append([]appui.ClientModule(nil), discovered...)
	for _, m := range explicit {
		replaced := false
		for i := range out {
			if out[i].Name == m.Name {
				out[i] = m
				replaced = true
				break
			}
		}
		if !replaced {
			out = append(out, m)
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}
