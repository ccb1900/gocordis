package main

import (
	"go/parser"
	"go/token"
	"os"
	"path/filepath"
	"strings"
	"testing"
)

func TestGenerateScaffold(t *testing.T) {
	dir := t.TempDir()
	if err := Generate(dir, "example.com/myapp"); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"go.mod", "manifest.toml", "main.go", "internal/app/component.go"} {
		if _, err := os.Stat(filepath.Join(dir, name)); err != nil {
			t.Fatalf("missing %s: %v", name, err)
		}
	}
	mod, err := os.ReadFile(filepath.Join(dir, "go.mod"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(mod), "module example.com/myapp") {
		t.Fatalf("go.mod: %s", mod)
	}
	manifest, err := os.ReadFile(filepath.Join(dir, "manifest.toml"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(manifest), `[[components]]`) || !strings.Contains(string(manifest), "enabled = true") {
		t.Fatalf("manifest.toml: %s", manifest)
	}
	main, err := os.ReadFile(filepath.Join(dir, "main.go"))
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(main), "config.NewController") {
		t.Fatalf("main.go lacks runtime composition root: %s", main)
	}
}

func TestGenerateRejectsInvalidInput(t *testing.T) {
	if err := Generate(t.TempDir(), ""); err == nil {
		t.Fatal("empty module path accepted")
	}
	if err := Generate(t.TempDir(), "bad path"); err == nil {
		t.Fatal("invalid module path accepted")
	}
}

func TestGeneratedGoFilesParse(t *testing.T) {
	dir := t.TempDir()
	if err := Generate(dir, "example.com/generated"); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"main.go", "internal/app/component.go"} {
		if _, err := parser.ParseFile(token.NewFileSet(), filepath.Join(dir, name), nil, parser.AllErrors); err != nil {
			t.Fatalf("%s does not parse: %v", name, err)
		}
	}
}

func TestGenerateRollsBackOnConflict(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "manifest.toml"), []byte("keep"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := Generate(dir, "example.com/rollback"); err == nil {
		t.Fatal("existing file did not block generation")
	}
	for _, name := range []string{"go.mod", "main.go", "internal/app/component.go"} {
		if _, err := os.Stat(filepath.Join(dir, name)); !os.IsNotExist(err) {
			t.Fatalf("%s leaked after failed generation: %v", name, err)
		}
	}
}
