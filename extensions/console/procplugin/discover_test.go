package procplugin

import (
	"os"
	"path/filepath"
	"testing"
)

func TestDiscoverPlugins(t *testing.T) {
	dir := t.TempDir()
	mk := func(name, manifest string) {
		if err := os.MkdirAll(filepath.Join(dir, name), 0o755); err != nil {
			t.Fatal(err)
		}
		if err := os.WriteFile(filepath.Join(dir, name, "manifest.toml"), []byte(manifest), 0o644); err != nil {
			t.Fatal(err)
		}
	}
	mk("alarm", `
name = "alarm"
backend = "./alarm"
queries = ["alarms"]
commands = ["ack"]
client = "ui.js"

[[pages]]
id = "alarms"
title = "报警"
route = "/alarms"
renderer = "alarm-console"
order = 50
`)
	mk("frontend-only", `name = "frontend-only"
client = "ui.js"`)
	mk("mismatch", `name = "not-mismatch"` + "\n") // name != 目录名：报错
	if err := os.WriteFile(filepath.Join(dir, "stray.toml"), []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}

	if _, err := DiscoverPlugins(dir); err == nil {
		t.Fatal("manifest name mismatch must error")
	}
	if err := os.WriteFile(filepath.Join(dir, "mismatch", "manifest.toml"), []byte(`name = "mismatch"`), 0o644); err != nil {
		t.Fatal(err)
	}

	rows, err := DiscoverPlugins(dir)
	if err != nil {
		t.Fatal(err)
	}
	var ids []string
	for _, r := range rows {
		ids = append(ids, r.ID+"|"+r.Type)
	}
	want := "alarm|proc-plugin,alarm:client|ui-client,alarm:page:alarms|ui-page,frontend-only:client|ui-client"
	got := ""
	for i, id := range ids {
		if i > 0 {
			got += ","
		}
		got += id
	}
	if got != want {
		t.Fatalf("rows = %s, want %s", got, want)
	}
}

func TestDiscoverPluginsMissingDir(t *testing.T) {
	rows, err := DiscoverPlugins(filepath.Join(t.TempDir(), "nope"))
	if err != nil || len(rows) != 0 {
		t.Fatalf("missing dir must yield nothing, got %v %v", rows, err)
	}
}

func TestPluginsDirForConfig(t *testing.T) {
	if got := PluginsDirForConfig("configs/desktop.toml"); got != filepath.Join("plugins") {
		t.Fatalf("got %q", got)
	}
	if got := PluginsDirForConfig("app.toml"); got != filepath.Join("plugins") {
		t.Fatalf("got %q", got)
	}
}
