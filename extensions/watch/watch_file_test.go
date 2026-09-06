package watch_test

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/watch"
)

func fileCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func newFileWatcher(t *testing.T) *watch.FileWatcher {
	t.Helper()
	w := watch.NewFileWatcher()
	t.Cleanup(func() { _ = w.CloseContext(fileCtx(t)) })
	return w
}

func mustWatchFile(t *testing.T, w *watch.FileWatcher, id, path string) watch.Subscription {
	t.Helper()
	src := watch.Source{ID: id, Kind: "file", URI: "file://" + path}
	sub, err := w.Watch(fileCtx(t), src)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("native file watching unsupported on this platform")
		}
		t.Fatalf("Watch: %v", err)
	}
	return sub
}

// waitChange reads changes until one satisfies pred or the timeout elapses.
func waitChange(t *testing.T, sub watch.Subscription, d time.Duration, pred func(watch.Change) bool) (watch.Change, bool) {
	t.Helper()
	deadline := time.Now().Add(d)
	for time.Now().Before(deadline) {
		select {
		case c, ok := <-sub.Changes():
			if !ok {
				return watch.Change{}, false
			}
			if pred(c) {
				return c, true
			}
		case <-time.After(50 * time.Millisecond):
		}
	}
	return watch.Change{}, false
}

// W1/W13 — watching an existing file succeeds and produces no initial change.
func TestW1WatchExistingNoInitialChange(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "conf.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := newFileWatcher(t)
	sub := mustWatchFile(t, w, "conf", path)
	time.Sleep(300 * time.Millisecond) // let the native watcher settle
	select {
	case c := <-sub.Changes():
		t.Fatalf("unexpected initial change: %+v", c)
	default:
	}
	_ = sub.Close()
}

// W2 — modifying a file produces a Modified change.
func TestW2FileModification(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "conf.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := newFileWatcher(t)
	sub := mustWatchFile(t, w, "conf", path)
	time.Sleep(200 * time.Millisecond)

	if err := os.WriteFile(path, []byte("two"), 0o644); err != nil {
		t.Fatal(err)
	}
	c, ok := waitChange(t, sub, 8*time.Second, func(c watch.Change) bool {
		return c.Kind == watch.ChangeModified
	})
	if !ok {
		t.Fatal("no Modified change observed")
	}
	if !c.Current.Exists {
		t.Fatal("Modified change must have Current.Exists=true")
	}
	_ = sub.Close()
}

// W3 — removing a file produces a Removed change and the subscription survives.
func TestW3FileRemoval(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "conf.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := newFileWatcher(t)
	sub := mustWatchFile(t, w, "conf", path)
	time.Sleep(200 * time.Millisecond)

	if err := os.Remove(path); err != nil {
		t.Fatal(err)
	}
	c, ok := waitChange(t, sub, 8*time.Second, func(c watch.Change) bool {
		return c.Kind == watch.ChangeRemoved
	})
	if !ok {
		t.Fatal("no Removed change observed")
	}
	if !c.Previous.Exists || c.Current.Exists {
		t.Fatalf("Removed change invalid: %+v", c)
	}

	// W4/W16 — recreating the file produces an Added change on the SAME
	// subscription.
	if err := os.WriteFile(path, []byte("again"), 0o644); err != nil {
		t.Fatal(err)
	}
	c2, ok := waitChange(t, sub, 8*time.Second, func(c watch.Change) bool {
		return c.Kind == watch.ChangeAdded
	})
	if !ok {
		t.Fatal("no Added change on file reappearance")
	}
	if c2.Previous.Exists || !c2.Current.Exists {
		t.Fatalf("Added change invalid: %+v", c2)
	}
	_ = sub.Close()
}

// W17 — atomic save (write temp + rename over target) produces a Modified
// change for the target file.
func TestW17AtomicSave(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "conf.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := newFileWatcher(t)
	sub := mustWatchFile(t, w, "conf", path)
	time.Sleep(200 * time.Millisecond)

	tmp := filepath.Join(dir, ".conf.txt.tmp")
	if err := os.WriteFile(tmp, []byte("three"), 0o644); err != nil {
		t.Fatal(err)
	}
	if err := os.Rename(tmp, path); err != nil {
		t.Fatal(err)
	}
	_, ok := waitChange(t, sub, 8*time.Second, func(c watch.Change) bool {
		return c.Kind == watch.ChangeModified && c.Current.Exists
	})
	if !ok {
		t.Fatal("no Modified change observed after atomic save")
	}
	_ = sub.Close()
}

// W9 — duplicate Watch on the same source yields independent subscriptions:
// each subscription independently observes modifications. (The watcher is
// event-driven; individual OS events may occasionally coalesce, so a few
// sequential writes are used until both independent subscriptions have
// observed at least one modification.)
func TestW9DuplicateWatch(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "conf.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := newFileWatcher(t)
	s1 := mustWatchFile(t, w, "conf", path)
	s2 := mustWatchFile(t, w, "conf", path)
	time.Sleep(300 * time.Millisecond)

	saw1, saw2 := false, false
	for i := 0; i < 6 && (!saw1 || !saw2); i++ {
		if err := os.WriteFile(path, []byte(fmt.Sprintf("content-%d", i)), 0o644); err != nil {
			t.Fatal(err)
		}
		if !saw1 {
			if _, ok := waitChange(t, s1, 3*time.Second, func(c watch.Change) bool { return c.Kind == watch.ChangeModified }); ok {
				saw1 = true
			}
		}
		if !saw2 {
			if _, ok := waitChange(t, s2, 3*time.Second, func(c watch.Change) bool { return c.Kind == watch.ChangeModified }); ok {
				saw2 = true
			}
		}
	}
	if !saw1 {
		t.Fatal("s1 never observed a modification")
	}
	if !saw2 {
		t.Fatal("s2 never observed a modification")
	}
	_ = s1.Close()
	_ = s2.Close()
}

// W11 — Watch Close closes every subscription.
func TestW11WatchClose(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "conf.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := watch.NewFileWatcher()
	sub := mustWatchFile(t, w, "conf", path)
	if err := w.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	if err := w.Close(); err != nil {
		t.Fatalf("second Close: %v", err)
	}
	select {
	case _, open := <-sub.Changes():
		if open {
			t.Fatal("subscription still open after Watch Close")
		}
	case <-time.After(3 * time.Second):
		t.Fatal("subscription channel did not close after Watch Close")
	}
	if _, err := w.Watch(fileCtx(t), watch.Source{ID: "x", Kind: "file", URI: "file://" + path}); !errors.Is(err, watch.ErrWatchClosed) {
		t.Fatalf("Watch after Close = %v, want ErrWatchClosed", err)
	}
}
