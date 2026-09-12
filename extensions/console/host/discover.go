package host

import (
	"os"
	"path/filepath"
	"sort"
	"strings"
)

// DefaultPluginsDir is the conventional location for plugin deployments:
// the backend artifact and its frontend module sit side by side
// (plugins/alarm-server + plugins/alarm-server.ui.js). Convention over
// configuration — no config row is needed for a module that follows it.
const DefaultPluginsDir = "plugins"

// DiscoverClientModules scans dirs for the plugin frontend module
// convention: files named <name>.ui.js (or .ui.mjs) live next to the
// plugin backend they belong to. The module NAME is the filename minus
// the .ui suffix. Missing directories are not an error — the convention
// is optional. Results are sorted by name for deterministic manifests.
func DiscoverClientModules(dirs ...string) []ClientModule {
	var out []ClientModule
	seen := map[string]bool{}
	for _, dir := range dirs {
		entries, err := os.ReadDir(dir)
		if err != nil {
			continue // no such dir: the convention simply has nothing to offer
		}
		for _, entry := range entries {
			if entry.IsDir() {
				continue
			}
			name := entry.Name()
			var modName string
			switch {
			case strings.HasSuffix(name, ".ui.js"):
				modName = strings.TrimSuffix(name, ".ui.js")
			case strings.HasSuffix(name, ".ui.mjs"):
				modName = strings.TrimSuffix(name, ".ui.mjs")
			default:
				continue
			}
			if modName == "" || seen[modName] {
				continue
			}
			seen[modName] = true
			out = append(out, ClientModule{
				Name: modName,
				Path: filepath.Join(dir, name),
			})
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].Name < out[j].Name })
	return out
}

// MergeClientModules layers discovered (convention) modules under explicit
// (config) ones: an explicit row with the same name wins. Explicit rows are
// exceptions and overrides — the convention is the default.
func MergeClientModules(explicit, discovered []ClientModule) []ClientModule {
	out := append([]ClientModule(nil), discovered...)
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
