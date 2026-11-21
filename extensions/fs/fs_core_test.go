package fs_test

import (
	"context"
	"io"
	iofs "io/fs"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/fs"
)

// TestFSCoreConformance runs the still-missing JS FS Core semantics against
// BOTH built-in providers through the exact same assertions. Every new option
// API must behave identically whether the mounted provider is local or memory.
func TestFSCoreConformance(t *testing.T) {
	for _, tc := range fsConformanceCases {
		t.Run(tc.name, func(t *testing.T) {
			ctx := tctx(t)
			svc, name := tc.setup(t)
			base := name("app") + "/"

			// ---- Remove {Recursive, Force} ----
			file := name("app/rm/file.txt")
			if err := svc.WriteFile(ctx, file, []byte("x")); err != nil {
				t.Fatal(err)
			}
			missing := name("app/rm/missing.txt")
			if err := svc.RemoveWithOptions(ctx, missing, fs.RemoveOptions{}); err == nil {
				t.Fatal("Remove(force=false) on a missing path succeeded")
			}
			if err := svc.RemoveWithOptions(ctx, missing, fs.RemoveOptions{Force: true}); err != nil {
				t.Fatalf("Remove(force=true) on a missing path: %v", err)
			}
			// Node semantics: Recursive alone does not imply Force. A missing
			// path must still report NotExist unless Force is set.
			missingDir := name("app/rm/missing-dir")
			if err := svc.RemoveWithOptions(ctx, missingDir, fs.RemoveOptions{Recursive: true}); err == nil {
				t.Fatal("Remove(recursive, force=false) on a missing path succeeded")
			}
			if err := svc.RemoveWithOptions(ctx, missingDir, fs.RemoveOptions{Recursive: true, Force: true}); err != nil {
				t.Fatalf("Remove(recursive, force=true) on a missing path: %v", err)
			}
			if err := svc.WriteFile(ctx, name("app/rm/dir/child.txt"), []byte("c")); err != nil {
				t.Fatal(err)
			}
			if err := svc.RemoveWithOptions(ctx, name("app/rm/dir"), fs.RemoveOptions{}); err == nil {
				t.Fatal("Remove(non-recursive) on a non-empty directory succeeded")
			}
			if err := svc.RemoveWithOptions(ctx, name("app/rm/dir"), fs.RemoveOptions{Recursive: true}); err != nil {
				t.Fatalf("Remove(recursive): %v", err)
			}
			if _, err := svc.Stat(ctx, name("app/rm/dir/child.txt")); err == nil {
				t.Fatal("recursive Remove left a child behind")
			}
			// Legacy Remove keeps its signature and still deletes a plain file.
			if err := svc.Remove(ctx, file); err != nil {
				t.Fatalf("legacy Remove: %v", err)
			}
			if _, err := svc.Stat(ctx, file); err == nil {
				t.Fatal("legacy Remove did not delete the file")
			}

			// ---- Mkdir {Recursive} ----
			if err := svc.MkdirWithOptions(ctx, name("app/mk/a/b/c"), fs.MkdirOptions{}); err == nil {
				t.Fatal("Mkdir(non-recursive) with missing parents succeeded")
			}
			if err := svc.MkdirWithOptions(ctx, name("app/mk/a/b/c"), fs.MkdirOptions{Recursive: true}); err != nil {
				t.Fatalf("Mkdir(recursive): %v", err)
			}
			fi, err := svc.Stat(ctx, name("app/mk/a/b/c"))
			if err != nil || !fi.IsDir() {
				t.Fatalf("Mkdir(recursive) stat=%+v err=%v", fi, err)
			}
			if err := svc.Mkdir(ctx, name("app/mk2")); err != nil { // legacy Mkdir
				t.Fatalf("legacy Mkdir: %v", err)
			}

			// ---- Copy {Recursive, Overwrite} ----
			src := name("app/cp/src.txt")
			if err := svc.WriteFile(ctx, src, []byte("hello")); err != nil {
				t.Fatal(err)
			}
			dst := name("app/cp/dst.txt")
			if err := svc.WriteFile(ctx, dst, []byte("old")); err != nil {
				t.Fatal(err)
			}
			if err := svc.CopyWithOptions(ctx, src, dst, fs.CopyOptions{}); err == nil {
				t.Fatal("Copy(overwrite=false) on an existing destination succeeded")
			}
			if err := svc.CopyWithOptions(ctx, src, dst, fs.CopyOptions{Overwrite: true}); err != nil {
				t.Fatalf("Copy(overwrite=true): %v", err)
			}
			if data, err := svc.ReadFile(ctx, dst); err != nil || string(data) != "hello" {
				t.Fatalf("overwritten data=%q err=%v", data, err)
			}
			if err := svc.WriteFile(ctx, name("app/cp/tree/a.txt"), []byte("a")); err != nil {
				t.Fatal(err)
			}
			if err := svc.WriteFile(ctx, name("app/cp/tree/sub/b.txt"), []byte("b")); err != nil {
				t.Fatal(err)
			}
			if err := svc.CopyWithOptions(ctx, name("app/cp/tree"), name("app/cp/copy"), fs.CopyOptions{Recursive: true}); err != nil {
				t.Fatalf("Copy(recursive): %v", err)
			}
			if data, err := svc.ReadFile(ctx, name("app/cp/copy/a.txt")); err != nil || string(data) != "a" {
				t.Fatalf("recursive copy a.txt=%q err=%v", data, err)
			}
			if data, err := svc.ReadFile(ctx, name("app/cp/copy/sub/b.txt")); err != nil || string(data) != "b" {
				t.Fatalf("recursive copy sub/b.txt=%q err=%v", data, err)
			}
			if err := svc.Copy(ctx, src, name("app/cp/legacy.txt")); err != nil { // legacy Copy
				t.Fatalf("legacy Copy: %v", err)
			}

			// ---- Symlink + Readlink ----
			linkTarget := name("app/link/target.txt")
			if err := svc.WriteFile(ctx, linkTarget, []byte("t")); err != nil {
				t.Fatal(err)
			}
			link := name("app/link/l")
			if err := svc.Symlink(ctx, linkTarget, link); err != nil {
				t.Fatalf("Symlink: %v", err)
			}
			li, err := svc.Lstat(ctx, link)
			if err != nil {
				t.Fatal(err)
			}
			si, ok := li.(fs.SymlinkInfo)
			if !ok || !si.IsSymlink() || si.LinkTarget() == "" {
				t.Fatalf("symlink metadata: %+v ok=%v", li, ok)
			}
			gotTarget, err := svc.Readlink(ctx, link)
			if err != nil || gotTarget == "" {
				t.Fatalf("Readlink=%q err=%v", gotTarget, err)
			}
			if data, err := svc.ReadFile(ctx, link); err != nil || string(data) != "t" {
				t.Fatalf("read through symlink=%q err=%v", data, err)
			}
			escape := "http://evil.example/x"
			if tc.name == "local" {
				escape = filepath.Join(t.TempDir(), "outside.txt")
				if err := os.WriteFile(escape, []byte("secret"), 0o644); err != nil {
					t.Fatal(err)
				}
			}
			if err := svc.Symlink(ctx, escape, name("app/link/bad")); err == nil {
				t.Fatalf("Symlink accepted an escaping target %q", escape)
			}

			// ---- ResolveWithBase ----
			resolved, err := svc.ResolveWithBase(ctx, base, "sub/resolved.txt")
			if err != nil {
				t.Fatalf("ResolveWithBase: %v", err)
			}
			if want := name("app/sub/resolved.txt"); resolved != want {
				t.Fatalf("ResolveWithBase=%q want %q", resolved, want)
			}
			if err := svc.WriteFile(ctx, resolved, []byte("r")); err != nil {
				t.Fatal(err)
			}
			if data, err := svc.ReadFile(ctx, name("app/sub/resolved.txt")); err != nil || string(data) != "r" {
				t.Fatalf("resolved write/read=%q err=%v", data, err)
			}
			if _, err := svc.ResolveWithBase(ctx, base, "../../outside.txt"); err == nil {
				t.Fatal("ResolveWithBase accepted an escaping relative path")
			}
			// A relative path that lands on a different mount must be rejected.
			otherPrefix := "memory:///other"
			if tc.name == "local" {
				otherPrefix = "file://" + filepath.Join(filepath.Dir(strings.TrimPrefix(base, "file://")), "other")
			}
			otherSvc := fs.NewMemoryProvider()
			if err := svc.Mount(otherPrefix, otherSvc); err != nil {
				t.Fatal(err)
			}
			if _, err := svc.ResolveWithBase(ctx, base, "../other/x"); err == nil {
				t.Fatal("ResolveWithBase crossed a mount boundary")
			}

			if err := svc.Close(); err != nil {
				t.Fatal(err)
			}
		})
	}
}

// TestNewFSAdapterIOFS checks the additive io/fs.FS adapter: path validation,
// scheme rejection, Open/Stat/ReadDir/ReadFile and a http.FileServerFS-style
// serve path.
func TestNewFSAdapterIOFS(t *testing.T) {
	_, _, svc := startFS(t)
	ctx := tctx(t)
	if _, err := svc.MountMemory(""); err != nil {
		t.Fatal(err)
	}
	if err := svc.WriteFile(ctx, "dir/a.txt", []byte("alpha")); err != nil {
		t.Fatal(err)
	}
	if err := svc.WriteFile(ctx, "dir/sub/b.txt", []byte("beta")); err != nil {
		t.Fatal(err)
	}
	fsys := fs.NewFS(svc, ctx)

	f, err := fsys.Open("dir/a.txt")
	if err != nil {
		t.Fatal(err)
	}
	data, err := io.ReadAll(f)
	_ = f.Close()
	if err != nil || string(data) != "alpha" {
		t.Fatalf("ReadFile via Open=%q err=%v", data, err)
	}
	fi, err := iofs.Stat(fsys, "dir/sub/b.txt")
	if err != nil || fi.Name() != "b.txt" || fi.Size() != 4 {
		t.Fatalf("Stat=%+v err=%v", fi, err)
	}
	entries, err := iofs.ReadDir(fsys, "dir")
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 2 || entries[0].Name() != "a.txt" || entries[1].Name() != "sub" {
		t.Fatalf("ReadDir=%v", entries)
	}
	for _, bad := range []string{"../secret", "/abs", "memory:///dir/a.txt", "a/../b"} {
		if _, err := fsys.Open(bad); err == nil {
			t.Fatalf("Open accepted invalid path %q", bad)
		}
	}

	mux := http.NewServeMux()
	mux.Handle("/", http.FileServerFS(fsys))
	rec := httptest.NewRecorder()
	mux.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/dir/a.txt", nil))
	if rec.Code != http.StatusOK || rec.Body.String() != "alpha" {
		t.Fatalf("FileServerFS status=%d body=%q", rec.Code, rec.Body.String())
	}
	rec = httptest.NewRecorder()
	mux.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, "/dir/", nil))
	if rec.Code != http.StatusOK || !strings.Contains(rec.Body.String(), "a.txt") {
		t.Fatalf("FileServerFS dir status=%d body=%q", rec.Code, rec.Body.String())
	}
}

// TestWatcherChannelsCloseOnDispose pins defect 4: after Dispose joins all
// senders, Events and Errors must be closed so range/<- never blocks forever.
func TestWatcherChannelsCloseOnDispose(t *testing.T) {
	check := func(t *testing.T, w fs.Watcher) {
		t.Helper()
		if err := w.Dispose(); err != nil {
			t.Fatal(err)
		}
		if err := w.Dispose(); err != nil {
			t.Fatalf("idempotent Dispose: %v", err)
		}
		watchdog, cancel := context.WithTimeout(context.Background(), 2*time.Second)
		defer cancel()
	eventsClosed:
		for {
			select {
			case _, ok := <-w.Events():
				if !ok {
					break eventsClosed
				}
			case <-watchdog.Done():
				t.Fatal("Events not closed after Dispose")
			}
		}
	errorsClosed:
		for {
			select {
			case _, ok := <-w.Errors():
				if !ok {
					break errorsClosed
				}
			case <-watchdog.Done():
				t.Fatal("Errors not closed after Dispose")
			}
		}
	}

	t.Run("memory provider", func(t *testing.T) {
		ctx := tctx(t)
		p := fs.NewMemoryProvider()
		sw := p.(fs.StreamWatcher)
		w, err := sw.WatchStream(ctx, "memory:///app", "")
		if err != nil {
			t.Fatal(err)
		}
		check(t, w)
	})
	t.Run("service", func(t *testing.T) {
		ctx := tctx(t)
		_, _, svc := startFS(t)
		if _, err := svc.MountMemory(""); err != nil {
			t.Fatal(err)
		}
		w, err := svc.WatchStream(ctx, "memory:///app", "")
		if err != nil {
			t.Fatal(err)
		}
		check(t, w)
	})
}
