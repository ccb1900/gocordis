package fs_test

import (
	"context"
	"io"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/fs"
)

// expectWatchEvent waits for one event and checks its path/op.
func expectWatchEvent(t *testing.T, ctx context.Context, w fs.Watcher, path, op string) {
	t.Helper()
	for {
		select {
		case ev, ok := <-w.Events():
			if !ok {
				t.Fatalf("events channel closed before %s/%s", op, path)
			}
			if ev.Path == path && ev.Op == op {
				return
			}
			continue // unrelated events (e.g. chmod) are fine
		case <-ctx.Done():
			t.Fatalf("no %s event for %s", op, path)
		}
	}
}

func noWatchEvent(t *testing.T, w fs.Watcher) {
	t.Helper()
	select {
	case ev, ok := <-w.Events():
		if ok {
			t.Fatalf("unexpected event %+v", ev)
		}
	default:
	}
}

// noEventFor drains a short window and fails only if a specific filtered-out
// path shows up. fsnotify can emit additional create+write/chmod pairs for the
// same file, so those must not make the filter assertion flaky.
func noEventFor(t *testing.T, w fs.Watcher, path string) {
	t.Helper()
	timer := time.NewTimer(80 * time.Millisecond)
	defer timer.Stop()
	for {
		select {
		case ev, ok := <-w.Events():
			if !ok {
				return
			}
			if ev.Path == path {
				t.Fatalf("filtered-out event delivered: %+v", ev)
			}
		case <-timer.C:
			return
		}
	}
}

// TestLocalWatcherRecursive checks the JS-style Change/Ready/Error/Dispose
// contract on the local provider: existing and newly created subdirectories
// must be watched, and Dispose must join and close the channels.
func TestLocalWatcherRecursive(t *testing.T) {
	root := t.TempDir()
	p := fs.NewLocalProvider(root)
	t.Cleanup(func() { _ = p.Close() })
	ctx := tctx(t)

	dir := filepath.Join(root, "app")
	if err := os.MkdirAll(filepath.Join(dir, "css"), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(filepath.Join(dir, "root.txt"), []byte("r"), 0o644); err != nil {
		t.Fatal(err)
	}

	sw, ok := p.(fs.StreamWatcher)
	if !ok {
		t.Fatal("local provider does not implement fs.StreamWatcher")
	}
	w, err := sw.WatchStream(ctx, "file://"+dir, "")
	if err != nil {
		t.Fatal(err)
	}
	if w.Events() == nil || w.Errors() == nil || w.Ready() == nil {
		t.Fatal("watcher has a nil Events/Errors/Ready channel")
	}
	select {
	case <-w.Ready():
	case <-ctx.Done():
		t.Fatal("Ready was not signalled")
	}

	// Existing subdirectories are registered by the initial walk.
	if err := os.WriteFile(filepath.Join(dir, "css", "style.txt"), []byte("s"), 0o644); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "file://"+filepath.Join(dir, "css", "style.txt"), "create")

	// A directory created after the watch starts must be registered on Create.
	newDir := filepath.Join(dir, "js")
	if err := os.Mkdir(newDir, 0o755); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "file://"+newDir, "create")
	if err := os.WriteFile(filepath.Join(newDir, "app.txt"), []byte("a"), 0o644); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "file://"+filepath.Join(newDir, "app.txt"), "create")

	if err := w.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := w.Dispose(); err != nil {
		t.Fatalf("Dispose not idempotent: %v", err)
	}
eventsClosed:
	for {
		select {
		case _, ok := <-w.Events():
			if !ok {
				break eventsClosed
			}
		case <-ctx.Done():
			t.Fatal("Events still open after Dispose")
		}
	}
errorsClosed:
	for {
		select {
		case _, ok := <-w.Errors():
			if !ok {
				break errorsClosed
			}
		case <-ctx.Done():
			t.Fatal("Errors still open after Dispose")
		}
	}
	_ = os.WriteFile(filepath.Join(dir, "late.txt"), []byte("l"), 0o644)
	noWatchEvent(t, w)
}

// TestLocalWatcherFilter checks doublestar "**" filtering and invalid patterns.
func TestLocalWatcherFilter(t *testing.T) {
	root := t.TempDir()
	p := fs.NewLocalProvider(root)
	t.Cleanup(func() { _ = p.Close() })
	ctx := tctx(t)
	dir := filepath.Join(root, "app")
	if err := os.MkdirAll(filepath.Join(dir, "sub"), 0o755); err != nil {
		t.Fatal(err)
	}

	sw := p.(fs.StreamWatcher)
	if _, err := sw.WatchStream(ctx, "file://"+dir, "**/["); err == nil {
		t.Fatal("invalid glob pattern was accepted")
	}
	w, err := sw.WatchStream(ctx, "file://"+dir, "**/*.txt")
	if err != nil {
		t.Fatal(err)
	}
	defer w.Dispose()
	<-w.Ready()

	if err := os.WriteFile(filepath.Join(dir, "sub", "a.txt"), []byte("a"), 0o644); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "file://"+filepath.Join(dir, "sub", "a.txt"), "create")
	if err := os.WriteFile(filepath.Join(dir, "sub", "b.css"), []byte("b"), 0o644); err != nil {
		t.Fatal(err)
	}
	noEventFor(t, w, "file://"+filepath.Join(dir, "sub", "b.css"))
}

func TestMemoryWatcherContract(t *testing.T) {
	p := fs.NewMemoryProvider()
	ctx := tctx(t)
	sw, ok := p.(fs.StreamWatcher)
	if !ok {
		t.Fatal("memory provider does not implement fs.StreamWatcher")
	}
	w, err := sw.WatchStream(ctx, "memory:///app", "**/*.txt")
	if err != nil {
		t.Fatal(err)
	}
	select {
	case <-w.Ready():
	case <-ctx.Done():
		t.Fatal("Ready was not signalled")
	}
	if err := p.Write(ctx, "memory:///app/sub/a.txt", []byte("a")); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "memory:///app/sub/a.txt", "create")
	if err := p.Write(ctx, "memory:///app/sub/b.css", []byte("b")); err != nil {
		t.Fatal(err)
	}
	noEventFor(t, w, "memory:///app/sub/b.css")
	if err := w.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := p.Write(ctx, "memory:///app/c.txt", []byte("c")); err != nil {
		t.Fatal(err)
	}
	noWatchEvent(t, w)
}

// TestProviderCloseAndOwnership pins down P1-05: Close is part of Provider,
// the service closes only providers it created, and Unmount never closes a
// provider it does not own.
func TestProviderCloseAndOwnership(t *testing.T) {
	ctx := tctx(t)
	root := t.TempDir()

	// Direct provider Close releases the os.Root so the name is no longer usable.
	local := fs.NewLocalProvider(root)
	if err := local.Close(); err != nil {
		t.Fatal(err)
	}
	if err := local.Close(); err != nil {
		t.Fatalf("double Close: %v", err)
	}
	if _, err := local.Open(ctx, "file://"+root); err == nil {
		t.Fatal("Open succeeded after local provider Close")
	}

	_, _, svc := startFS(t)

	// Externally supplied provider: neither Unmount nor service Close may close it.
	external := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///external", external); err != nil {
		t.Fatal(err)
	}
	if err := svc.Unmount("memory:///external"); err != nil {
		t.Fatal(err)
	}
	if err := svc.Mount("memory:///external", external); err != nil {
		t.Fatal(err)
	}
	if err := svc.Close(); err != nil {
		t.Fatal(err)
	}
	if err := external.Write(ctx, "memory:///external/still-alive", []byte("x")); err != nil {
		t.Fatalf("externally supplied provider was closed: %v", err)
	}
}

// TestOwnedProviderClosedByService checks that a provider created by the
// service is closed by Service.Close, but not by Unmount.
func TestOwnedProviderClosedByService(t *testing.T) {
	ctx := tctx(t)
	root := t.TempDir()
	_, _, svc := startFS(t)

	prefix := "file://" + filepath.Join(root, "data")
	owned, err := svc.MountLocal(prefix, root)
	if err != nil {
		t.Fatal(err)
	}
	name := prefix + "/x.txt"
	if err := svc.WriteFile(ctx, name, []byte("x")); err != nil {
		t.Fatal(err)
	}
	if err := svc.Unmount(prefix); err != nil {
		t.Fatal(err)
	}
	if _, err := owned.Open(ctx, name); err != nil {
		t.Fatalf("owned provider was closed by Unmount: %v", err)
	}
	if err := svc.Mount(prefix, owned); err != nil {
		t.Fatal(err)
	}
	if err := svc.Close(); err != nil {
		t.Fatal(err)
	}
	if _, err := owned.Open(ctx, name); err == nil {
		t.Fatal("owned provider was not closed by Service.Close")
	}
}

// ---------------------------------------------------------------------------
// shared conformance suite: identical behaviour for local and memory providers
// ---------------------------------------------------------------------------

type fsConformanceCase struct {
	name  string
	setup func(t *testing.T) (fs.Service, func(string) string)
}

var fsConformanceCases = []fsConformanceCase{
	{
		name: "local",
		setup: func(t *testing.T) (fs.Service, func(string) string) {
			root := t.TempDir()
			_, _, svc := startFS(t)
			if _, err := svc.MountLocal("", root); err != nil {
				t.Fatal(err)
			}
			return svc, func(rel string) string {
				return "file://" + filepath.Join(root, filepath.FromSlash(rel))
			}
		},
	},
	{
		name: "memory",
		setup: func(t *testing.T) (fs.Service, func(string) string) {
			_, _, svc := startFS(t)
			if _, err := svc.MountMemory(""); err != nil {
				t.Fatal(err)
			}
			return svc, func(rel string) string { return "memory:///" + rel }
		},
	},
}

func TestProviderConformance(t *testing.T) {
	for _, tc := range fsConformanceCases {
		t.Run(tc.name, func(t *testing.T) {
			ctx := tctx(t)
			svc, name := tc.setup(t)

			// WriteFile + Open + ReadFile
			if err := svc.WriteFile(ctx, name("dir/file.txt"), []byte("hello")); err != nil {
				t.Fatal(err)
			}
			f, err := svc.Open(ctx, name("dir/file.txt"))
			if err != nil {
				t.Fatal(err)
			}
			got, err := io.ReadAll(f)
			_ = f.Close()
			if err != nil || string(got) != "hello" {
				t.Fatalf("Open/Read data=%q err=%v", got, err)
			}
			got, err = svc.ReadFile(ctx, name("dir/file.txt"))
			if err != nil || string(got) != "hello" {
				t.Fatalf("ReadFile data=%q err=%v", got, err)
			}

			// Stat + ReadDir
			fi, err := svc.Stat(ctx, name("dir/file.txt"))
			if err != nil || fi.Name() != "file.txt" || fi.Size() != 5 || fi.IsDir() {
				t.Fatalf("Stat=%+v err=%v", fi, err)
			}
			entries, err := svc.ReadDir(ctx, name("dir"))
			if err != nil || len(entries) != 1 || entries[0].Name() != "file.txt" {
				t.Fatalf("ReadDir=%+v err=%v", entries, err)
			}

			// Watch (callback API) and WatchStream must both observe writes.
			cbEvents := make(chan fs.WatchEvent, 8)
			inv, err := svc.Watch(ctx, name("dir/file.txt"), func(ev fs.WatchEvent) error {
				cbEvents <- ev
				return nil
			})
			if err != nil {
				t.Fatal(err)
			}
			w, err := svc.WatchStream(ctx, name("dir"), "**/*.txt")
			if err != nil {
				t.Fatal(err)
			}
			select {
			case <-w.Ready():
			case <-ctx.Done():
				t.Fatal("WatchStream Ready was not signalled")
			}
			if err := svc.WriteFile(ctx, name("dir/file.txt"), []byte("v2")); err != nil {
				t.Fatal(err)
			}
			select {
			case ev := <-cbEvents:
				if ev.Op != "write" {
					t.Fatalf("callback op=%q", ev.Op)
				}
			case <-ctx.Done():
				t.Fatal("callback Watch did not deliver")
			}
			if err := svc.WriteFile(ctx, name("dir/other.txt"), []byte("o")); err != nil {
				t.Fatal(err)
			}
			expectWatchEvent(t, ctx, w, name("dir/other.txt"), "create")
			if err := inv(); err != nil {
				t.Fatal(err)
			}
			if err := w.Dispose(); err != nil {
				t.Fatal(err)
			}
			for len(cbEvents) > 0 { // drain events delivered before the inverse
				<-cbEvents
			}
			if err := svc.WriteFile(ctx, name("dir/file.txt"), []byte("v3")); err != nil {
				t.Fatal(err)
			}
			select {
			case ev := <-cbEvents:
				t.Fatalf("callback event after inverse: %+v", ev)
			default:
			}

			// Remove
			if err := svc.Remove(ctx, name("dir/file.txt")); err != nil {
				t.Fatal(err)
			}
			if _, err := svc.Stat(ctx, name("dir/file.txt")); err == nil {
				t.Fatal("Stat succeeded after Remove")
			}

			// Close (service-owned provider must be released)
			if err := svc.Close(); err != nil {
				t.Fatal(err)
			}
		})
	}
}

// TestWatchStreamReroutesOnMountChange covers the JS-style watcher's takeover
// when a mount change moves its path to a different provider.
func TestWatchStreamReroutesOnMountChange(t *testing.T) {
	ctx := tctx(t)
	_, _, svc := startFS(t)
	general := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///", general); err != nil {
		t.Fatal(err)
	}
	w, err := svc.WatchStream(ctx, "memory:///app/f", "")
	if err != nil {
		t.Fatal(err)
	}
	defer w.Dispose()
	select {
	case <-w.Ready():
	case <-ctx.Done():
		t.Fatal("Ready was not signalled")
	}

	if err := general.Write(ctx, "memory:///app/f", []byte("g1")); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "memory:///app/f", "create")

	specific := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///app", specific); err != nil {
		t.Fatal(err)
	}
	if err := specific.Write(ctx, "memory:///app/f", []byte("s1")); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "memory:///app/f", "create")
	if err := general.Write(ctx, "memory:///app/f", []byte("g2")); err != nil {
		t.Fatal(err)
	}
	noEventFor(t, w, "memory:///app/f")

	if err := svc.Unmount("memory:///app"); err != nil {
		t.Fatal(err)
	}
	if err := general.Write(ctx, "memory:///app/f", []byte("g3")); err != nil {
		t.Fatal(err)
	}
	expectWatchEvent(t, ctx, w, "memory:///app/f", "write")
}
