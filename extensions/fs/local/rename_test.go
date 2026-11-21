package local

import (
	"context"
	"io"
	"os"
	"path/filepath"
	"testing"

	"dynamic-runtime/extensions/fs/contract"
)

// testRenameConfined exercises Rename on both toolchain paths: Go 1.25+ uses
// os.Root.Rename, Go 1.24 uses the confined copy/remove fallback. Both must
// confine every path to the root, including directory trees and symlinks.
func testRenameConfined(t *testing.T) {
	t.Helper()
	root := t.TempDir()
	outside := t.TempDir()
	if err := os.WriteFile(filepath.Join(outside, "secret.txt"), []byte("secret"), 0o644); err != nil {
		t.Fatal(err)
	}
	p := NewProvider(root)
	t.Cleanup(func() { _ = p.Close() })
	ctx := context.Background()

	if err := p.Write(ctx, "a.txt", []byte("payload")); err != nil {
		t.Fatal(err)
	}
	if err := p.MkdirWithOptions(ctx, "sub", contract.MkdirOptions{Recursive: true}); err != nil {
		t.Fatal(err)
	}
	if err := p.Rename(ctx, "a.txt", "sub/b.txt"); err != nil {
		t.Fatalf("Rename file: %v", err)
	}
	read := func(name string) string {
		t.Helper()
		f, err := p.Open(ctx, name)
		if err != nil {
			t.Fatal(err)
		}
		defer f.Close()
		data, err := io.ReadAll(f)
		if err != nil {
			t.Fatal(err)
		}
		return string(data)
	}
	if got := read("sub/b.txt"); got != "payload" {
		t.Fatalf("renamed data=%q", got)
	}
	if _, err := p.Stat(ctx, "a.txt"); err == nil {
		t.Fatal("source survived Rename")
	}

	if err := p.Write(ctx, "tree/x.txt", []byte("x")); err != nil {
		t.Fatal(err)
	}
	if err := p.Rename(ctx, "tree", "moved"); err != nil {
		t.Fatalf("Rename directory: %v", err)
	}
	if got := read("moved/x.txt"); got != "x" {
		t.Fatalf("renamed tree data=%q", got)
	}

	if err := os.Symlink(outside, filepath.Join(root, "escape")); err != nil {
		t.Fatal(err)
	}
	if err := p.Rename(ctx, "sub/b.txt", "escape/stolen.txt"); err == nil {
		t.Fatal("Rename escaped through a symlink")
	}
	if data, err := os.ReadFile(filepath.Join(outside, "secret.txt")); err != nil || string(data) != "secret" {
		t.Fatalf("outside changed: %q err=%v", data, err)
	}
}
