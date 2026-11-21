package main

import (
	"bytes"
	"go/parser"
	"go/token"
	"os"
	"path/filepath"
	"sort"
	"strings"
	"testing"

	"dynamic-runtime/extensions/pluginmeta"
)

// listFiles returns the regular files immediately inside dir, sorted.
func listFiles(t *testing.T, dir string) []string {
	t.Helper()
	ents, err := os.ReadDir(dir)
	if err != nil {
		t.Fatal(err)
	}
	var names []string
	for _, e := range ents {
		if e.IsDir() {
			continue
		}
		names = append(names, e.Name())
	}
	sort.Strings(names)
	return names
}

func assertFileSet(t *testing.T, dir string, want ...string) {
	t.Helper()
	sort.Strings(want)
	got := listFiles(t, dir)
	if strings.Join(got, ",") != strings.Join(want, ",") {
		t.Fatalf("file set = %v, want %v", got, want)
	}
}

// assertManifestParses checks that the generated TOML decodes and validates,
// and that its backend matches the requested one.
func assertManifestParses(t *testing.T, dir, backend string) pluginmeta.Manifest {
	t.Helper()
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
	if string(m.Backend) != backend {
		t.Fatalf("manifest backend = %q, want %q", m.Backend, backend)
	}
	return m
}

// assertGoParses checks every generated .go file is syntactically valid Go.
func assertGoParses(t *testing.T, dir string, names ...string) {
	t.Helper()
	for _, name := range names {
		p := filepath.Join(dir, name)
		if _, err := parser.ParseFile(token.NewFileSet(), p, nil, parser.AllErrors); err != nil {
			t.Fatalf("generated %s does not parse: %v", name, err)
		}
	}
}

func readGenerated(t *testing.T, dir, name string) string {
	t.Helper()
	b, err := os.ReadFile(filepath.Join(dir, name))
	if err != nil {
		t.Fatal(err)
	}
	return string(b)
}

// TestGenerateBackendFileSets is the core P0-3 regression: each backend gets
// its own project structure, and a WASM/Proc scaffold must NOT ship a
// Go-native runtime.Component.
func TestGenerateBackendFileSets(t *testing.T) {
	cases := []struct {
		backend string
		want    []string
	}{
		{"native", []string{"README.md", "component.go", "go.mod", "plugin.toml"}},
		{"wasm", []string{"Makefile", "README.md", "go.mod", "main.go", "plugin.toml"}},
		{"proc", []string{"README.md", "go.mod", "main.go", "plugin.toml"}},
	}
	for _, tc := range cases {
		t.Run(tc.backend, func(t *testing.T) {
			id := tc.backend + "-plugin"
			dir := filepath.Join(t.TempDir(), id)
			if err := Generate(dir, CreateOptions{ID: id, Name: "Test " + tc.backend, Backend: tc.backend}); err != nil {
				t.Fatal(err)
			}
			assertFileSet(t, dir, tc.want...)
			assertManifestParses(t, dir, tc.backend)

			goFile := "component.go"
			if tc.backend != "native" {
				goFile = "main.go"
			}
			assertGoParses(t, dir, goFile)

			body := readGenerated(t, dir, goFile)
			hasComponent := strings.Contains(body, "runtime.Component")
			if (tc.backend == "native") != hasComponent {
				t.Fatalf("backend %s: runtime.Component presence = %v", tc.backend, hasComponent)
			}
		})
	}
}

func TestGenerateNativeScaffold(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "myplugin")
	if err := Generate(dir, CreateOptions{ID: "my-plugin", Name: "My Plugin", Backend: "native"}); err != nil {
		t.Fatal(err)
	}
	m := assertManifestParses(t, dir, "native")
	if m.ID != "my-plugin" || m.Name != "My Plugin" {
		t.Fatalf("unexpected manifest identity: %+v", m)
	}
	assertGoParses(t, dir, "component.go")
	readme := readGenerated(t, dir, "README.md")
	if !strings.Contains(readme, "requires build-time registration") {
		t.Fatalf("README does not document the sandbox boundary: %s", readme)
	}
}

func TestGenerateWASMContract(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "wasmp")
	if err := Generate(dir, CreateOptions{ID: "sensor", Name: "Sensor", Backend: "wasm"}); err != nil {
		t.Fatal(err)
	}
	assertFileSet(t, dir, "Makefile", "README.md", "go.mod", "main.go", "plugin.toml")
	assertManifestParses(t, dir, "wasm")
	assertGoParses(t, dir, "main.go")

	src := readGenerated(t, dir, "main.go")
	for _, marker := range []string{
		"//export runtime_component_create",
		"//export runtime_component_destroy",
		"func runtime_component_create()",
		"func runtime_component_destroy()",
		"package main",
	} {
		if !strings.Contains(src, marker) {
			t.Fatalf("wasm source missing %q:\n%s", marker, src)
		}
	}
	if strings.Contains(src, "dynamic-runtime/runtime") {
		t.Fatalf("wasm guest must not depend on runtime.Component:\n%s", src)
	}
	if _, err := os.Stat(filepath.Join(dir, "component.go")); !os.IsNotExist(err) {
		t.Fatalf("wasm scaffold leaked a component.go: %v", err)
	}

	// Build instructions must match the real wasm backend contract: the wazero
	// loader reads the "module_type" custom section and the two ABI exports,
	// and provides no WASI/host API.
	readme := readGenerated(t, dir, "README.md") + readGenerated(t, dir, "Makefile")
	for _, marker := range []string{"wazero", "module_type", "tinygo", "wasm-objcopy", "sensor"} {
		if !strings.Contains(readme, marker) {
			t.Fatalf("wasm build docs missing %q:\n%s", marker, readme)
		}
	}
}

func TestGenerateProcContract(t *testing.T) {
	dir := filepath.Join(t.TempDir(), "procp")
	if err := Generate(dir, CreateOptions{ID: "codec", Name: "Codec", Backend: "proc"}); err != nil {
		t.Fatal(err)
	}
	assertFileSet(t, dir, "README.md", "go.mod", "main.go", "plugin.toml")
	assertManifestParses(t, dir, "proc")
	assertGoParses(t, dir, "main.go")

	src := readGenerated(t, dir, "main.go")
	for _, marker := range []string{
		"GORIDIS-PROC-PLUGIN 1",
		`"shutdown"`,
		"encoding/json",
		`"jsonrpc"`,
		"package main",
	} {
		if !strings.Contains(src, marker) {
			t.Fatalf("proc source missing %q:\n%s", marker, src)
		}
	}
	if strings.Contains(src, "dynamic-runtime/runtime") {
		t.Fatalf("proc process must not depend on runtime.Component:\n%s", src)
	}
	if _, err := os.Stat(filepath.Join(dir, "component.go")); !os.IsNotExist(err) {
		t.Fatalf("proc scaffold leaked a component.go: %v", err)
	}

	readme := readGenerated(t, dir, "README.md")
	for _, marker := range []string{"GORIDIS-PROC-PLUGIN 1", "shutdown", "JSON-RPC", "codec"} {
		if !strings.Contains(readme, marker) {
			t.Fatalf("proc README missing %q:\n%s", marker, readme)
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
		dir := filepath.Join(t.TempDir(), "p")
		if err := Generate(dir, opts); err == nil {
			t.Errorf("case %d: invalid options accepted: %+v", i, opts)
		} else if _, statErr := os.Stat(dir); statErr == nil {
			if got := listFiles(t, dir); len(got) > 0 {
				t.Errorf("case %d: rejection left files behind: %v", i, got)
			}
		}
	}
}

func TestGenerateRefusesOverwriteUnlessForce(t *testing.T) {
	for _, backend := range []string{"native", "wasm", "proc"} {
		t.Run(backend, func(t *testing.T) {
			dir := t.TempDir()
			const original = "keep me\n"
			if err := os.WriteFile(filepath.Join(dir, "plugin.toml"), []byte(original), 0o644); err != nil {
				t.Fatal(err)
			}
			opts := CreateOptions{ID: "p", Name: "P", Backend: backend}
			if err := Generate(dir, opts); err == nil {
				t.Fatal("overwrite without --force was accepted")
			}
			data, err := os.ReadFile(filepath.Join(dir, "plugin.toml"))
			if err != nil || string(data) != original {
				t.Fatalf("existing file was modified: %q, %v", data, err)
			}
			if got := listFiles(t, dir); len(got) != 1 || got[0] != "plugin.toml" {
				t.Fatalf("other files were written after refusal: %v", got)
			}

			opts.Force = true
			if err := Generate(dir, opts); err != nil {
				t.Fatalf("force overwrite failed: %v", err)
			}
			data, err = os.ReadFile(filepath.Join(dir, "plugin.toml"))
			if err != nil || string(data) == original {
				t.Fatalf("force overwrite did not replace content: %q, %v", data, err)
			}
		})
	}
}

func TestGenerateRollsBackOnFailure(t *testing.T) {
	cases := []struct {
		backend string
		want    []string
	}{
		{"native", []string{"plugin.toml", "component.go", "go.mod", "README.md"}},
		{"wasm", []string{"plugin.toml", "main.go", "go.mod", "Makefile", "README.md"}},
		{"proc", []string{"plugin.toml", "main.go", "go.mod", "README.md"}},
	}
	for _, tc := range cases {
		t.Run(tc.backend, func(t *testing.T) {
			dir := filepath.Join(t.TempDir(), "nested", "plugin")
			original := postWriteHook
			postWriteHook = func(name string) error {
				if name == "README.md" {
					return os.ErrPermission
				}
				return nil
			}
			defer func() { postWriteHook = original }()

			if err := Generate(dir, CreateOptions{ID: "p", Name: "P", Backend: tc.backend}); err == nil {
				t.Fatal("injected failure was ignored")
			}
			for _, name := range tc.want {
				if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
					t.Fatalf("%s leaked after rollback: %v", name, err)
				}
			}
			if _, err := os.Stat(filepath.Dir(dir)); !os.IsNotExist(err) {
				t.Fatalf("created parent directory was not removed: %v", err)
			}
		})
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
