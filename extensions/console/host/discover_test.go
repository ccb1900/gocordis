package host

import (
	"os"
	"path/filepath"
	"testing"
)

func TestDiscoverClientModules(t *testing.T) {
	dir := t.TempDir()
	write := func(name string) {
		if err := os.WriteFile(filepath.Join(dir, name), []byte("// module"), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	write("alarm-server.ui.js")
	write("fleet-panel.ui.mjs")
	write("backend-no-frontend")     // backend artifact without a module: ignored
	write("readme.md")               // not a module: ignored
	if err := os.Mkdir(filepath.Join(dir, "sub.ui.js.dir"), 0o755); err != nil {
		t.Fatal(err)
	}

	mods := DiscoverClientModules(dir)
	if len(mods) != 2 {
		t.Fatalf("expected 2 modules, got %+v", mods)
	}
	if mods[0].Name != "alarm-server" || mods[0].Path != filepath.Join(dir, "alarm-server.ui.js") {
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
		{Name: "alarm-server", Path: "plugins/alarm-server.ui.js"},
		{Name: "zulu", Path: "plugins/zulu.ui.js"},
	}
	explicit := []ClientModule{
		// Same name as a discovered module: the explicit row overrides it.
		{Name: "alarm-server", Path: "/opt/alt/alarm-server.ui.js"},
		{Name: "alpha", Path: "/opt/alpha.ui.js"},
	}
	merged := MergeClientModules(explicit, discovered)
	if len(merged) != 3 {
		t.Fatalf("merged = %+v", merged)
	}
	if merged[0].Name != "alarm-server" || merged[0].Path != "/opt/alt/alarm-server.ui.js" {
		t.Fatalf("explicit row must win: %+v", merged[0])
	}
	if merged[1].Name != "alpha" || merged[2].Name != "zulu" {
		t.Fatalf("merge order = %+v", merged)
	}
}
