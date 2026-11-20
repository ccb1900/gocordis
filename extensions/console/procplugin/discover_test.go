package procplugin

import (
	"context"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
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
	// 声明名与目录不符 / TOML 不可解析：不再静默跳过，展开为 plugin-error 行。
	mk("mismatch", `name = "not-mismatch"`+"\n")
	mk("broken", `name = "broken" backend = `+"\n")

	rows, err := DiscoverPlugins(dir)
	if err != nil {
		t.Fatal(err)
	}
	var ids []string
	byID := map[string]config.ComponentConfig{}
	for _, r := range rows {
		ids = append(ids, r.ID+"|"+r.Type)
		byID[r.ID] = r
	}
	want := strings.Join([]string{
		"alarm|proc-plugin", "alarm:client|ui-client", "alarm:page:alarms|ui-page",
		"broken|plugin-error", "frontend-only:client|ui-client", "mismatch|plugin-error",
	}, ",")
	if got := strings.Join(ids, ","); got != want {
		t.Fatalf("rows = %s, want %s", got, want)
	}

	// 插件业务单元：有后端的 UI 行带 follows，纯前端行不带（否则永远 Pending）。
	if byID["alarm:client"].Config["follows"] != "alarm" {
		t.Fatalf("ui-client row must follow the backend: %+v", byID["alarm:client"].Config)
	}
	if byID["alarm:page:alarms"].Config["follows"] != "alarm" {
		t.Fatalf("ui-page row must follow the backend: %+v", byID["alarm:page:alarms"].Config)
	}
	if _, ok := byID["frontend-only:client"].Config["follows"]; ok {
		t.Fatalf("frontend-only row must NOT carry follows: %+v", byID["frontend-only:client"].Config)
	}

	// 错误行携带可读原因。
	if reason, _ := byID["mismatch"].Config["reason"].(string); !strings.Contains(reason, "does not match") {
		t.Fatalf("mismatch error row reason = %q", reason)
	}
	if reason, _ := byID["broken"].Config["reason"].(string); !strings.Contains(reason, "parse failed") {
		t.Fatalf("broken error row reason = %q", reason)
	}
}

func TestErrorComponentActivationRaises(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	t.Cleanup(func() { _ = rt.Close(ctx) })

	comp, err := NewErrorComponent(config.ComponentConfig{
		ID: "broken", Type: "plugin-error",
		Config: map[string]any{"reason": "manifest parse failed: boom"},
	})
	if err != nil {
		t.Fatal(err)
	}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) && f.State() != runtime.StateFailed {
		time.Sleep(5 * time.Millisecond)
	}
	if f.State() != runtime.StateFailed {
		t.Fatalf("state = %v, want Failed", f.State())
	}
	if f.Err() == nil || !strings.Contains(f.Err().Error(), "manifest parse failed") {
		t.Fatalf("failure must carry the reason, got %v", f.Err())
	}
}

func TestErrorComponentDefaults(t *testing.T) {
	comp, err := NewErrorComponent(config.ComponentConfig{ID: "x", Type: "plugin-error"})
	if err != nil {
		t.Fatal(err)
	}
	if comp.Reason() != "malformed plugin manifest" {
		t.Fatalf("default reason = %q", comp.Reason())
	}
}

func TestDiscoverPluginsMissingDir(t *testing.T) {
	rows, err := DiscoverPlugins(filepath.Join(t.TempDir(), "nope"))
	if err != nil || len(rows) != 0 {
		t.Fatalf("missing dir must yield nothing, got %v %v", rows, err)
	}
}

// 目录里没有 manifest.toml 就不是插件目录——约定不变，不产错误行。
func TestDiscoverPluginsNoManifestIsNotAPlugin(t *testing.T) {
	dir := t.TempDir()
	if err := os.MkdirAll(filepath.Join(dir, "data"), 0o755); err != nil {
		t.Fatal(err)
	}
	rows, err := DiscoverPlugins(dir)
	if err != nil || len(rows) != 0 {
		t.Fatalf("manifest-less dir must yield nothing, got %v %v", rows, err)
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

// 插件根目录存在但不可读不能静默变成"没有插件"：调和会据此撤销既有组件。
func TestDiscoverPluginsUnreadableDirErrors(t *testing.T) {
	if os.Geteuid() == 0 {
		t.Skip("root bypasses directory permission bits")
	}
	dir := t.TempDir()
	sub := filepath.Join(dir, "locked")
	if err := os.MkdirAll(sub, 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.Chmod(sub, 0o000); err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = os.Chmod(sub, 0o755) })
	rows, err := DiscoverPlugins(dir)
	if err != nil {
		return // surfaced as an error — also an acceptable loud outcome
	}
	// Or the unreadable dir is expanded into a plugin-error row (inner
	// ReadFile failure) — never omitted from the desired composition.
	found := false
	for _, r := range rows {
		if r.ID == "locked" && r.Type == "plugin-error" {
			found = true
		}
	}
	if !found {
		t.Fatalf("unreadable plugin dir must surface (error row or returned error), rows=%+v", rows)
	}
}

// 目录存在但就是普通文件：ReadDir 返回 ENOTDIR，同样不能静默。
func TestDiscoverPluginsDirIsFile(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "notdir"), []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}
	if _, err := DiscoverPlugins(filepath.Join(dir, "notdir")); err == nil {
		t.Fatal("ENOTDIR must be reported")
	}
}
