package fs_test

import (
	"context"
	"errors"
	"io"
	"os"
	"path/filepath"
	"strings"
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
	// symlink creates a real symlink at linkURL pointing to target. Providers
	// with no symlink notion leave it nil; those cases only assert that
	// ordinary metadata reports "not a link".
	symlink func(t *testing.T, linkURL, target string)
	// escapes lists names every extended operation must reject.
	escapes []string
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
		symlink: func(t *testing.T, linkURL, target string) {
			t.Helper()
			if err := os.Symlink(target, strings.TrimPrefix(linkURL, "file://")); err != nil {
				t.Fatal(err)
			}
		},
		escapes: []string{"file:///../../etc/passwd", "../../etc/passwd", "file:///etc/passwd"},
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
		// memory has no host paths to escape; a foreign scheme normalizes to
		// an empty key and must be rejected by every operation.
		escapes: []string{"http://example.com/x", "ftp://host/y"},
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

// ---------------------------------------------------------------------------
// extended operation surface: stream/range read, Lstat + symlink metadata and
// the ExtendedProvider operations. The same suite runs against both providers.
// ---------------------------------------------------------------------------

// minimalFSProvider exposes only the base Provider methods, so the service must
// fall back to ErrNotSupported for every optional operation.
type minimalFSProvider struct{ inner fs.Provider }

func (p *minimalFSProvider) Scheme() string { return p.inner.Scheme() }
func (p *minimalFSProvider) Open(ctx context.Context, n string) (fs.File, error) {
	return p.inner.Open(ctx, n)
}
func (p *minimalFSProvider) Stat(ctx context.Context, n string) (fs.FileInfo, error) {
	return p.inner.Stat(ctx, n)
}
func (p *minimalFSProvider) ReadDir(ctx context.Context, n string) ([]fs.FileInfo, error) {
	return p.inner.ReadDir(ctx, n)
}
func (p *minimalFSProvider) Write(ctx context.Context, n string, d []byte) error {
	return p.inner.Write(ctx, n, d)
}
func (p *minimalFSProvider) Remove(ctx context.Context, n string) error {
	return p.inner.Remove(ctx, n)
}
func (p *minimalFSProvider) Close() error { return p.inner.Close() }
func (p *minimalFSProvider) Watch(ctx context.Context, n string, cb func(fs.WatchEvent) error) (func() error, error) {
	return p.inner.Watch(ctx, n, cb)
}

func TestOptionalOperationsNotSupported(t *testing.T) {
	ctx := tctx(t)
	_, _, svc := startFS(t)
	if err := svc.Mount("memory:///", &minimalFSProvider{inner: fs.NewMemoryProvider()}); err != nil {
		t.Fatal(err)
	}
	check := func(op string, err error) {
		t.Helper()
		var ns *fs.ErrNotSupported
		if !errors.As(err, &ns) {
			t.Fatalf("%s: got %v, want *fs.ErrNotSupported", op, err)
		}
		if ns.Op != op {
			t.Fatalf("%s: ErrNotSupported.Op=%q", op, ns.Op)
		}
	}
	_, err := svc.OpenStream(ctx, "memory:///x", 0, -1)
	check("OpenStream", err)
	_, err = svc.ReadRange(ctx, "memory:///x", 0, -1)
	check("ReadRange", err)
	_, err = svc.Lstat(ctx, "memory:///x")
	check("Lstat", err)
	check("Mkdir", svc.Mkdir(ctx, "memory:///x"))
	check("Rename", svc.Rename(ctx, "memory:///x", "memory:///y"))
	check("Copy", svc.Copy(ctx, "memory:///x", "memory:///y"))
	check("Access", svc.Access(ctx, "memory:///x"))
}

func TestProviderExtendedConformance(t *testing.T) {
	for _, tc := range fsConformanceCases {
		t.Run(tc.name, func(t *testing.T) {
			ctx := tctx(t)
			svc, name := tc.setup(t)

			// Stream/range reads: offset, limit, EOF and invalid range.
			if err := svc.WriteFile(ctx, name("range.txt"), []byte("0123456789")); err != nil {
				t.Fatal(err)
			}
			check := func(offset, limit int64, want string) {
				t.Helper()
				r, err := svc.OpenStream(ctx, name("range.txt"), offset, limit)
				if err != nil {
					t.Fatalf("OpenStream(%d,%d): %v", offset, limit, err)
				}
				got, err := io.ReadAll(r)
				_ = r.Close()
				if err != nil || string(got) != want {
					t.Fatalf("OpenStream(%d,%d)=%q err=%v, want %q", offset, limit, got, err, want)
				}
				got, err = svc.ReadRange(ctx, name("range.txt"), offset, limit)
				if err != nil || string(got) != want {
					t.Fatalf("ReadRange(%d,%d)=%q err=%v, want %q", offset, limit, got, err, want)
				}
			}
			check(0, -1, "0123456789")
			check(2, 4, "2345")
			check(0, 0, "")
			check(8, 100, "89")
			check(10, 5, "")
			check(30, -1, "")
			if _, err := svc.ReadRange(ctx, name("range.txt"), -1, 4); err == nil {
				t.Fatal("negative offset was accepted")
			}

			// Lstat metadata: every provider's FileInfo implements SymlinkInfo;
			// ordinary files are never reported as links.
			fi, err := svc.Lstat(ctx, name("range.txt"))
			if err != nil {
				t.Fatal(err)
			}
			si, ok := fi.(fs.SymlinkInfo)
			if !ok || si.IsSymlink() || si.LinkTarget() != "" {
				t.Fatalf("regular file symlink metadata: %+v ok=%v", fi, ok)
			}
			if tc.symlink != nil {
				tc.symlink(t, name("link.txt"), "range.txt")
				li, err := svc.Lstat(ctx, name("link.txt"))
				if err != nil {
					t.Fatal(err)
				}
				lsi, ok := li.(fs.SymlinkInfo)
				if !ok || !lsi.IsSymlink() || lsi.LinkTarget() != "range.txt" {
					t.Fatalf("symlink metadata: %+v ok=%v", li, ok)
				}
				fi, err = svc.Stat(ctx, name("link.txt")) // Stat follows the link
				if err != nil {
					t.Fatal(err)
				}
				if sfi := fi.(fs.SymlinkInfo); sfi.IsSymlink() {
					t.Fatal("Stat reported a followed symlink as a link")
				}
			}

			// Mkdir + Access, including duplicate/missing paths.
			if err := svc.Mkdir(ctx, name("sub")); err != nil {
				t.Fatal(err)
			}
			if err := svc.Access(ctx, name("sub")); err != nil {
				t.Fatal(err)
			}
			if err := svc.Access(ctx, name("absent")); err == nil {
				t.Fatal("Access succeeded on a missing path")
			}
			if err := svc.Mkdir(ctx, name("sub")); err == nil {
				t.Fatal("duplicate Mkdir was accepted")
			}

			// Rename + Copy, with an independence check after a later write.
			if err := svc.Rename(ctx, name("range.txt"), name("sub/renamed.txt")); err != nil {
				t.Fatal(err)
			}
			if _, err := svc.Stat(ctx, name("range.txt")); err == nil {
				t.Fatal("Stat succeeded on the renamed-away source")
			}
			data, err := svc.ReadFile(ctx, name("sub/renamed.txt"))
			if err != nil || string(data) != "0123456789" {
				t.Fatalf("renamed data=%q err=%v", data, err)
			}
			if err := svc.Copy(ctx, name("sub/renamed.txt"), name("copy.txt")); err != nil {
				t.Fatal(err)
			}
			data, err = svc.ReadFile(ctx, name("copy.txt"))
			if err != nil || string(data) != "0123456789" {
				t.Fatalf("copied data=%q err=%v", data, err)
			}
			if err := svc.WriteFile(ctx, name("copy.txt"), []byte("edited")); err != nil {
				t.Fatal(err)
			}
			data, err = svc.ReadFile(ctx, name("sub/renamed.txt"))
			if err != nil || string(data) != "0123456789" {
				t.Fatalf("source changed after copy edit: %q err=%v", data, err)
			}

			// Every new operation must reject escaping/foreign names.
			for _, bad := range tc.escapes {
				label := "escape " + bad
				if _, err := svc.OpenStream(ctx, bad, 0, 1); err == nil {
					t.Fatalf("%s: OpenStream accepted", label)
				}
				if _, err := svc.Lstat(ctx, bad); err == nil {
					t.Fatalf("%s: Lstat accepted", label)
				}
				if err := svc.Mkdir(ctx, bad); err == nil {
					t.Fatalf("%s: Mkdir accepted", label)
				}
				if err := svc.Rename(ctx, name("sub/renamed.txt"), bad); err == nil {
					t.Fatalf("%s: Rename accepted", label)
				}
				if err := svc.Copy(ctx, name("sub/renamed.txt"), bad); err == nil {
					t.Fatalf("%s: Copy accepted", label)
				}
				if err := svc.Access(ctx, bad); err == nil {
					t.Fatalf("%s: Access accepted", label)
				}
			}
			if err := svc.Close(); err != nil {
				t.Fatal(err)
			}
		})
	}
}

// TestExtendedOperationsRejectSymlinkEscape pins the os.Root confinement of the
// new operations: a symlink inside the root must not let any of them touch the
// outside directory, even for metadata-only calls.
func TestExtendedOperationsRejectSymlinkEscape(t *testing.T) {
	root, outside := t.TempDir(), t.TempDir()
	secret := filepath.Join(outside, "secret.txt")
	if err := os.WriteFile(secret, []byte("secret"), 0o644); err != nil {
		t.Fatal(err)
	}
	_, _, svc := startFS(t)
	if _, err := svc.MountLocal("file:///", root); err != nil {
		t.Fatal(err)
	}
	ctx := tctx(t)
	inside := "file://" + filepath.Join(root, "inside.txt")
	if err := svc.WriteFile(ctx, inside, []byte("inside")); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(outside, filepath.Join(root, "escape")); err != nil {
		t.Fatal(err)
	}
	escaped := func(rel string) string { return "file://" + filepath.Join(root, "escape", rel) }

	if _, err := svc.OpenStream(ctx, escaped("secret.txt"), 0, -1); err == nil {
		t.Fatal("OpenStream escaped through symlink")
	}
	if _, err := svc.Lstat(ctx, escaped("secret.txt")); err == nil {
		t.Fatal("Lstat escaped through symlink")
	}
	if err := svc.Mkdir(ctx, escaped("made")); err == nil {
		t.Fatal("Mkdir escaped through symlink")
	}
	if err := svc.Rename(ctx, inside, escaped("moved.txt")); err == nil {
		t.Fatal("Rename escaped through symlink")
	}
	if err := svc.Copy(ctx, inside, escaped("copied.txt")); err == nil {
		t.Fatal("Copy escaped through symlink")
	}
	if err := svc.Copy(ctx, escaped("secret.txt"), escaped("copied-away.txt")); err == nil {
		t.Fatal("Copy escaped through symlink (source)")
	}
	if err := svc.Access(ctx, escaped("secret.txt")); err == nil {
		t.Fatal("Access escaped through symlink")
	}
	entries, err := os.ReadDir(outside)
	if err != nil || len(entries) != 1 || entries[0].Name() != "secret.txt" {
		t.Fatalf("outside directory was modified: entries=%v err=%v", entries, err)
	}
	data, err := os.ReadFile(secret)
	if err != nil || string(data) != "secret" {
		t.Fatalf("outside secret changed: %q err=%v", data, err)
	}
}
