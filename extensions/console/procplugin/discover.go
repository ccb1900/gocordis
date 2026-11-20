package procplugin

import (
	"fmt"
	"os"
	"path/filepath"
	"sort"
	"strings"

	toml "github.com/pelletier/go-toml/v2"

	"dynamic-runtime/extensions/config"
)

// PluginsManifestName is the per-plugin declaration file: one plugin, one
// directory, one manifest.
const PluginsManifestName = "manifest.toml"

// PluginManifest is the declarative face of a self-contained plugin:
// everything the framework needs to instantiate it, discovered from
// plugins/<name>/manifest.toml.
type PluginManifest struct {
	Name     string   `toml:"name"`
	Title    string   `toml:"title"`
	Backend  string   `toml:"backend"`  // executable path (relative to the plugin dir); empty = frontend-only
	Client   string   `toml:"client"`   // frontend entry (default ui.js); empty = backend-only
	Queries  []string `toml:"queries"`  // hub named queries the backend serves
	Commands []string `toml:"commands"` // hub named commands the backend serves

	// Pages contribute declarative pages (renderer = the plugin's own client
	// renderer or a built-in kind).
	Pages []PluginPage `toml:"pages"`
}

// PluginPage is one page declaration inside a manifest.
type PluginPage struct {
	ID          string `toml:"id"`
	Title       string `toml:"title"`
	Route       string `toml:"route"`
	Description string `toml:"description"`
	Renderer    string `toml:"renderer"`
	Icon        string `toml:"icon"`
	Order       int    `toml:"order"`
}

// DiscoverPlugins scans dir for plugin directories (plugins/<name>/manifest.toml)
// and expands each into its component rows:
//
//	backend → a proc-plugin component (id = the plugin name)
//	client  → a ui-client component (id = <name>:client)
//	pages   → ui-page components (id = <name>:page:<page id>)
//
// When a plugin has a backend, its UI rows carry follows=<name>: disabling
// the backend withdraws the UI through the Runtime's dependency rules —
// one business unit, one lifecycle (host.PluginAliveKey).
//
// Missing directories yield no rows; malformed manifests are errors — a
// broken declaration must surface, not vanish. A manifest that exists but
// cannot be read, parsed, or whose declared name does not match its
// directory expands to ONE plugin-error row (id = directory name) instead:
// the broken plugin stays visible in the composition and the explorer in a
// Failed activation, never silently removed from the desired set (a
// corrupted config must not read as an operator uninstall). Repairing the
// manifest changes the row and the reconcile replaces the error component —
// re-enablement by fixing the declaration, the documented recovery path.
//
// Applications that enable discovery (ComposeOptions.PluginDir) must
// register factories for BOTH procplugin.NewErrorComponent (type
// "plugin-error") and the plugin's real row types; an unregistered type
// fails composition validation loudly, never silently.
func DiscoverPlugins(dir string) ([]config.ComponentConfig, error) {
	entries, err := os.ReadDir(dir)
	if err != nil {
		return nil, nil // no plugin directory: the convention has nothing to offer
	}
	var names []string
	for _, entry := range entries {
		if entry.IsDir() {
			names = append(names, entry.Name())
		}
	}
	sort.Strings(names)

	out := []config.ComponentConfig{}
	for _, name := range names {
		manifestPath := filepath.Join(dir, name, PluginsManifestName)
		data, err := os.ReadFile(manifestPath)
		if err != nil {
			if os.IsNotExist(err) {
				continue // not a plugin directory
			}
			out = append(out, errorRow(name, fmt.Sprintf("manifest unreadable: %v", err)))
			continue // present-but-unreadable: surface, do not treat as absent
		}
		var m PluginManifest
		if err := toml.Unmarshal(data, &m); err != nil {
			out = append(out, errorRow(name, fmt.Sprintf("manifest parse failed: %v", err)))
			continue
		}
		if m.Name == "" {
			m.Name = name
		}
		if m.Name != name {
			out = append(out, errorRow(name, fmt.Sprintf("manifest name %q does not match directory %q", m.Name, name)))
			continue
		}
		pluginDir := filepath.Join(dir, name)
		rows := m.componentRows(pluginDir)
		out = append(out, rows...)
	}
	return out, nil
}

// errorRow is the loud placeholder one broken plugin expands to.
func errorRow(name, reason string) config.ComponentConfig {
	return config.ComponentConfig{
		ID: name, Type: "plugin-error",
		Config: map[string]any{"reason": reason},
	}
}

func (m *PluginManifest) componentRows(pluginDir string) []config.ComponentConfig {
	row := func(id, typ string, cfg map[string]any) config.ComponentConfig {
		return config.ComponentConfig{ID: id, Type: typ, Config: cfg}
	}
	var out []config.ComponentConfig
	if m.Backend != "" {
		out = append(out, row(m.Name, "proc-plugin", map[string]any{
			"dir":      pluginDir,
			"backend":  m.Backend,
			"queries":  toAnySlice(m.Queries),
			"commands": toAnySlice(m.Commands),
		}))
	}
	if m.Client != "" {
		cfg := map[string]any{
			"module": m.Name,
			"path":   filepath.Join(pluginDir, m.Client),
		}
		if m.Backend != "" {
			cfg["follows"] = m.Name
		}
		out = append(out, row(m.Name+":client", "ui-client", cfg))
	}
	for i, p := range m.Pages {
		id := p.ID
		if id == "" {
			id = fmt.Sprintf("page-%d", i)
		}
		cfg := map[string]any{
			"page_id":     id,
			"title":       p.Title,
			"route":       p.Route,
			"description": p.Description,
			"renderer":    p.Renderer,
			"icon":        p.Icon,
			"order":       p.Order,
		}
		if m.Backend != "" {
			cfg["follows"] = m.Name
		}
		out = append(out, row(m.Name+":page:"+id, "ui-page", cfg))
	}
	return out
}

func toAnySlice(list []string) []any {
	out := make([]any, 0, len(list))
	for _, s := range list {
		out = append(out, s)
	}
	return out
}

// PluginDirFor returns the conventional plugin directory relative to the
// configuration file's directory's parent (the application root). Configs
// live in configs/; plugins/ is the sibling deployment directory.
func PluginDirFor(configPath string) string {
	dir := filepath.Dir(configPath)
	if strings.HasSuffix(filepath.Clean(dir), "configs") {
		dir = filepath.Dir(dir)
	}
	return filepath.Join(dir, "plugins")
}

// PluginsDirForConfig derives the conventional plugin directory from an
// application configuration path: configs live in configs/, plugins are the
// sibling deployment directory.
func PluginsDirForConfig(configPath string) string {
	dir := filepath.Dir(configPath)
	if strings.HasSuffix(filepath.Clean(dir), "configs") {
		dir = filepath.Dir(dir)
	}
	return filepath.Join(dir, "plugins")
}
