package local

import (
	"context"
	"errors"
	"os"
	"path/filepath"
	"testing"

	"dynamic-runtime/extensions/fs/contract"
)

// TestSymlinkParentEscapeRejected pins defect 1: the link's parent must be
// confined through os.Root before the underlying symlink is created. A parent
// that is a symlink to a directory outside the root must be rejected.
func TestSymlinkParentEscapeRejected(t *testing.T) {
	root, outside := t.TempDir(), t.TempDir()
	if err := os.Symlink(outside, filepath.Join(root, "escape")); err != nil {
		t.Fatal(err)
	}
	p := NewProvider(root)
	t.Cleanup(func() { _ = p.Close() })
	ctx := context.Background()

	// Target inside the same (symlinked) parent so the relative-target check
	// passes; only parent confinement can reject this call.
	target := "file://" + filepath.Join(root, "escape", "target.txt")
	link := "file://" + filepath.Join(root, "escape", "newlink")
	if err := p.Symlink(ctx, target, link); err == nil {
		t.Fatal("Symlink created through a symlinked parent outside the root")
	}
	if _, err := os.Lstat(filepath.Join(outside, "newlink")); !errors.Is(err, os.ErrNotExist) {
		t.Fatalf("outside link was created: %v", err)
	}
}

// TestWatchRejectsSymlinkOutsideRoot pins defect 2: neither Watch nor
// WatchStream may attach fsnotify to a directory reached through a symlink
// that leaves the root.
func TestWatchRejectsSymlinkOutsideRoot(t *testing.T) {
	root, outside := t.TempDir(), t.TempDir()
	if err := os.WriteFile(filepath.Join(outside, "secret.txt"), []byte("secret"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(outside, filepath.Join(root, "escape")); err != nil {
		t.Fatal(err)
	}
	p := NewProvider(root)
	t.Cleanup(func() { _ = p.Close() })
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()

	name := "file://" + filepath.Join(root, "escape")
	if inv, err := p.Watch(ctx, name, func(contract.WatchEvent) error { return nil }); err == nil {
		_ = inv()
		t.Fatal("Watch attached to a symlinked directory outside the root")
	}
	sw, ok := any(p).(contract.StreamWatcher)
	if !ok {
		t.Fatal("local provider is not a StreamWatcher")
	}
	if w, err := sw.WatchStream(ctx, name, ""); err == nil {
		_ = w.Dispose()
		t.Fatal("WatchStream attached to a symlinked directory outside the root")
	}
}

// TestRenameChecksContext pins defect 5: the build-tagged internal rename must
// honor a cancelled context on both toolchains.
func TestRenameChecksContext(t *testing.T) {
	root := t.TempDir()
	p := NewProvider(root)
	t.Cleanup(func() { _ = p.Close() })
	if err := p.Write(context.Background(), "a.txt", []byte("x")); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithCancel(context.Background())
	cancel()
	if err := p.rename(ctx, "a.txt", "b.txt"); !errors.Is(err, context.Canceled) {
		t.Fatalf("rename with cancelled context: %v", err)
	}
}
