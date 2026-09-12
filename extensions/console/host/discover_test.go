package host

import (
	"os"
	"path/filepath"
	"testing"
)

// One plugin, one directory: the backend artifact and its frontend module
// live side by side; the module name is the plugin directory name.
func TestDiscoverClientModules(t *testing.T) {
	dir := t.TempDir()
	mkdir := func(name string) {
		if err := os.Mkdir(filepath.Join(dir, name), 0o755); err != nil {
			t.Fatal(err)
		}
	}
	write := func(name, file string) {
		if err := os.WriteFile(filepath.Join(dir, name, file), []byte("// module"), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	mkdir("alarm")
	write("alarm", "ui.js")
	write("alarm", "alarm-server") // the backend artifact, same directory
	mkdir("fleet-panel")
	write("fleet-panel", "ui.mjs")
	mkdir("empty-plugin")    // a plugin dir without a frontend module: no module
	write("stray.ui.js", "") // a flat file is not the convention: ignored
	_ = os.WriteFile(filepath.Join(dir, "readme.md"), []byte("x"), 0o644)

	mods := DiscoverClientModules(dir)
	if len(mods) != 2 {
		t.Fatalf("expected 2 modules, got %+v", mods)
	}
	if mods[0].Name != "alarm" || mods[0].Path != filepath.Join(dir, "alarm", "ui.js") {
		t.Fatalf("module[0] = %+v", mods[0])
	}
	if mods[1].Name != "fleet-panel" {
		t.Fatalf("module[1] = %+v", mods[1])
	}
}

func TestDiscoverClientModulesMissingDirIsEmpty(t *testing.T) {
	mods := DiscoverClientModules(filepath.Join(t.TempDir(), "does-not-exist"))
	if len(mods) != 0 {
		t.Fatalf("missing dir must yield no modules, got %+v", mods)
	}
}

func TestMergeClientModulesExplicitWins(t *testing.T) {
	discovered := []ClientModule{
		{Name: "alarm", Path: "plugins/alarm/ui.js"},
		{Name: "zulu", Path: "plugins/zulu/ui.js"},
	}
	explicit := []ClientModule{
		// Same name as a discovered module: the explicit row overrides it.
		{Name: "alarm", Path: "/opt/alt/alarm/ui.js"},
		{Name: "alpha", Path: "/opt/alpha/ui.js"},
	}
	merged := MergeClientModules(explicit, discovered)
	if len(merged) != 3 {
		t.Fatalf("merged = %+v", merged)
	}
	if merged[0].Name != "alarm" || merged[0].Path != "/opt/alt/alarm/ui.js" {
		t.Fatalf("explicit row must win: %+v", merged[0])
	}
	if merged[1].Name != "alpha" || merged[2].Name != "zulu" {
		t.Fatalf("merge order = %+v", merged)
	}
}
