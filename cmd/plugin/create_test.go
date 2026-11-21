package main

import (
	"bytes"
	"go/parser"
	"go/token"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"dynamic-runtime/extensions/pluginmeta"
)

func TestGenerateScaffold(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "myplugin")
	if err := Generate(dir, CreateOptions{ID: "my-plugin", Name: "My Plugin", Backend: "wasm"}); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"plugin.toml", "component.go", "README.md", "go.mod"} {
		if _, err := os.Stat(filepath.Join(dir, name)); err != nil {
			t.Fatalf("missing %s: %v", name, err)
		}
	}

	data, err := os.ReadFile(filepath.Join(dir, "plugin.toml"))
	if err != nil {
		t.Fatal(err)
	}
	m, err := pluginmeta.Parse(data, "toml")
	if err != nil {
		t.Fatalf("generated plugin.toml does not parse: %v\n%s", err, data)
	}
	if err := pluginmeta.Validate(m); err != nil {
		t.Fatalf("generated manifest is invalid: %v", err)
	}
	if m.ID != "my-plugin" || m.Name != "My Plugin" || m.Backend != pluginmeta.BackendWASM {
		t.Fatalf("unexpected manifest identity: %+v", m)
	}

	if _, err := parser.ParseFile(token.NewFileSet(), filepath.Join(dir, "component.go"), nil, parser.AllErrors); err != nil {
		t.Fatalf("generated component.go does not parse: %v", err)
	}
	readme, err := os.ReadFile(filepath.Join(dir, "README.md"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(readme), "requires build-time registration") {
		t.Fatalf("README does not document the sandbox boundary: %s", readme)
	}
}

func TestGenerateAllBackends(t *testing.T) {
	for _, backend := range []string{"native", "wasm", "proc"} {
		dir := filepath.Join(t.TempDir(), backend)
		if err := Generate(dir, CreateOptions{ID: backend + "-plugin", Name: backend, Backend: backend}); err != nil {
			t.Fatalf("backend %s: %v", backend, err)
		}
		data, err := os.ReadFile(filepath.Join(dir, "plugin.toml"))
		if err != nil {
			t.Fatal(err)
		}
		m, err := pluginmeta.Parse(data, "toml")
		if err != nil {
			t.Fatalf("backend %s manifest: %v", backend, err)
		}
		if string(m.Backend) != backend {
			t.Fatalf("backend %s: got %s", backend, m.Backend)
		}
	}
}

func TestGenerateRejectsInvalidInput(t *testing.T) {
	cases := []CreateOptions{
		{ID: "", Name: "n", Backend: "native"},
		{ID: "bad id!", Name: "n", Backend: "native"},
		{ID: "ok", Name: "", Backend: "native"},
		{ID: "ok", Name: "n", Backend: "magic"},
	}
	for i, opts := range cases {
		if err := Generate(filepath.Join(t.TempDir(), "p"), opts); err == nil {
			t.Errorf("case %d: invalid options accepted: %+v", i, opts)
		}
	}
}

func TestGenerateRefusesOverwriteUnlessForce(t *testing.T) {
	dir := t.TempDir()
	const original = "keep me\n"
	if err := os.WriteFile(filepath.Join(dir, "plugin.toml"), []byte(original), 0o644); err != nil {
		t.Fatal(err)
	}
	opts := CreateOptions{ID: "p", Name: "P", Backend: "native"}
	if err := Generate(dir, opts); err == nil {
		t.Fatal("overwrite without --force was accepted")
	}
	data, err := os.ReadFile(filepath.Join(dir, "plugin.toml"))
	if err != nil || string(data) != original {
		t.Fatalf("existing file was modified: %q, %v", data, err)
	}
	if _, err := os.Stat(filepath.Join(dir, "component.go")); !os.IsNotExist(err) {
		t.Fatalf("other files were written after refusal: %v", err)
	}

	opts.Force = true
	if err := Generate(dir, opts); err != nil {
		t.Fatalf("force overwrite failed: %v", err)
	}
	data, err = os.ReadFile(filepath.Join(dir, "plugin.toml"))
	if err != nil || string(data) == original {
		t.Fatalf("force overwrite did not replace content: %q, %v", data, err)
	}
}

func TestGenerateRollsBackOnFailure(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "nested", "plugin")
	original := postWriteHook
	postWriteHook = func(name string) error {
		if name == "README.md" {
			return os.ErrPermission
		}
		return nil
	}
	defer func() { postWriteHook = original }()

	if err := Generate(dir, CreateOptions{ID: "p", Name: "P", Backend: "native"}); err == nil {
		t.Fatal("injected failure was ignored")
	}
	for _, name := range []string{"plugin.toml", "component.go", "README.md"} {
		if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
			t.Fatalf("%s leaked after rollback: %v", name, err)
		}
	}
	if _, err := os.Stat(filepath.Dir(dir)); !os.IsNotExist(err) {
		t.Fatalf("created parent directory was not removed: %v", err)
	}
}

func TestCreateCommandDispatch(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "cli")
	var out, errOut bytes.Buffer
	args := []string{"create", dir, "--id", "cli-plugin", "--name", "CLI Plugin", "--backend", "native"}
	if code := run(args, &out, &errOut); code != 0 {
		t.Fatalf("create exited %d: %s", code, errOut.String())
	}
	if !strings.Contains(out.String(), dir) {
		t.Fatalf("unexpected output: %s", out.String())
	}
	if code := run(args, &out, &errOut); code == 0 {
		t.Fatal("second create without --force succeeded")
	}
	args = append(args, "--force")
	if code := run(args, &out, &errOut); code != 0 {
		t.Fatalf("create --force exited %d: %s", code, errOut.String())
	}
}

func TestCreateForceFlagBeforePositional(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "flags")
	var out, errOut bytes.Buffer
	args := []string{"create", "--id", "p", "--name", "P", "--backend", "native", "--force", dir}
	if code := run(args, &out, &errOut); code != 0 {
		t.Fatalf("create with flags before dir exited %d: %s", code, errOut.String())
	}
}
