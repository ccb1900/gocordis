package static

import (
	"bytes"
	"context"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/fs"
	"dynamic-runtime/extensions/server"
	"dynamic-runtime/runtime"
)

func testCtx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type services struct {
	serverCh chan server.Service
	fsCh     chan fs.Service
}

func (c *services) Name() string { return "test-services-capture" }
func (c *services) Inject() []runtime.Dependency {
	return []runtime.Dependency{
		runtime.Requires(server.ServiceKey),
		runtime.Requires(fs.ServiceKey),
	}
}
func (c *services) Provide() []runtime.Capability { return nil }
func (c *services) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, server.ServiceKey)
	if err != nil {
		return nil, err
	}
	fsSvc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	c.serverCh <- svc
	c.fsCh <- fsSvc
	return nil, nil
}

func loadHarness(t *testing.T) (*runtime.Runtime, server.Service, fs.Service) {
	t.Helper()
	ctx := testCtx(t)
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() {
		if err := rt.Close(testCtx(t)); err != nil {
			t.Errorf("rt.Close: %v", err)
		}
	})
	cap := &services{serverCh: make(chan server.Service, 1), fsCh: make(chan fs.Service, 1)}
	for _, c := range []runtime.Component{&server.Component{}, &fs.Component{}, cap} {
		f, err := rt.Load(c)
		if err != nil {
			t.Fatalf("load %s: %v", c.Name(), err)
		}
		if err := f.Ready(ctx); err != nil {
			t.Fatalf("%s Ready: %v", c.Name(), err)
		}
	}
	select {
	case svc := <-cap.serverCh:
		fsSvc := <-cap.fsCh
		return rt, svc, fsSvc
	case <-ctx.Done():
		t.Fatal("service never delivered")
		return nil, nil, nil
	}
}

func serve(svc server.Service, target string) *httptest.ResponseRecorder {
	req := httptest.NewRequest(http.MethodGet, target, nil)
	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, req)
	return rec
}

func TestStaticServesMountedFileAnd404(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "hello.txt"), []byte("hello static"), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/static", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}

	rec := serve(svc, "/static/hello.txt")
	if rec.Code != http.StatusOK || rec.Body.String() != "hello static" {
		t.Fatalf("GET hello.txt: code=%d body=%q", rec.Code, rec.Body.String())
	}
	if rec := serve(svc, "/static/missing.txt"); rec.Code != http.StatusNotFound {
		t.Fatalf("missing file: code=%d, want 404", rec.Code)
	}
}

// TestStaticRangeRequestStreamsLargeFile requires stdlib Range handling: the
// old ReadFile handler always answered 200 with the whole file, while a
// seekable io/fs.File lets http.FileServerFS answer 206 with just the slice.
func TestStaticRangeRequestStreamsLargeFile(t *testing.T) {
	dir := t.TempDir()
	const size = 1 << 20 // 1 MiB, far larger than a single Range response
	data := make([]byte, size)
	for i := range data {
		data[i] = byte(i % 251)
	}
	if err := os.WriteFile(filepath.Join(dir, "large.bin"), data, 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}

	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/static", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}

	req := httptest.NewRequest(http.MethodGet, "/static/large.bin", nil)
	req.Header.Set("Range", "bytes=1024-1151")
	rec := httptest.NewRecorder()
	svc.ServeHTTP(rec, req)

	if rec.Code != http.StatusPartialContent {
		t.Fatalf("Range request: code=%d, want 206 (body len=%d)", rec.Code, rec.Body.Len())
	}
	if got, want := rec.Header().Get("Content-Range"), "bytes 1024-1151/1048576"; got != want {
		t.Fatalf("Content-Range = %q, want %q", got, want)
	}
	if got, want := rec.Body.Bytes(), data[1024:1152]; !bytes.Equal(got, want) {
		t.Fatalf("partial body = %d bytes, want the requested 128-byte slice", len(got))
	}
	if rec.Body.Len() != 128 {
		t.Fatalf("partial body len = %d, want 128", rec.Body.Len())
	}
}

func TestStaticServesNestedPathsEncodedAndEmpty(t *testing.T) {
	dir := t.TempDir()
	files := map[string]string{
		"a.css":              "A",
		"css/a.css":          "CSS-A",
		"css/theme/dark.css": "DARK",
	}
	for name, content := range files {
		p := filepath.Join(dir, filepath.FromSlash(name))
		if err := os.MkdirAll(filepath.Dir(p), 0o755); err != nil {
			t.Fatalf("mkdir: %v", err)
		}
		if err := os.WriteFile(p, []byte(content), 0o644); err != nil {
			t.Fatalf("write fixture: %v", err)
		}
	}

	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/static", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}

	cases := map[string]string{
		"/static/a.css":              "A",
		"/static/css/a.css":          "CSS-A",
		"/static/css/theme/dark.css": "DARK",
		"/static/css/a%2Ecss":        "CSS-A", // percent-escaped path
	}
	for target, want := range cases {
		rec := serve(svc, target)
		if rec.Code != http.StatusOK || rec.Body.String() != want {
			t.Fatalf("GET %s: code=%d body=%q, want 200 %q", target, rec.Code, rec.Body.String(), want)
		}
	}
	if rec := serve(svc, "/static/"); rec.Code != http.StatusNotFound {
		t.Fatalf("empty /static/: code=%d, want 404", rec.Code)
	}
}

func TestStaticRejectsNestedTraversal(t *testing.T) {
	dir := t.TempDir()
	if err := os.MkdirAll(filepath.Join(dir, "css"), 0o755); err != nil {
		t.Fatalf("mkdir: %v", err)
	}
	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/s", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}
	for _, target := range []string{"/s/css/%2e%2e/secret.txt", "/s/a/%2e%2e/b"} {
		if rec := serve(svc, target); rec.Code != http.StatusNotFound {
			t.Fatalf("traversal %s: code=%d, want 404", target, rec.Code)
		}
	}
}

func TestStaticRejectsTraversal(t *testing.T) {
	dir := t.TempDir()
	outside := filepath.Join(filepath.Dir(dir), "outside-secret.txt")
	if err := os.WriteFile(outside, []byte("secret"), 0o644); err != nil {
		t.Fatalf("write outside fixture: %v", err)
	}
	t.Cleanup(func() { _ = os.Remove(outside) })
	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/s", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}
	// %2e%2e decodes to ".." and is captured by the {path...} segment.
	for _, target := range []string{
		"/s/%2e%2e",
		"/s/%2e%2e%2foutside-secret.txt",
		"/s/%2Fetc/passwd",            // absolute path
		"/s/dir%5Cfile.txt",           // backslash separator
		"/s/http:%2F%2Fexample.com/a", // foreign URL scheme
	} {
		if rec := serve(svc, target); rec.Code != http.StatusNotFound {
			t.Fatalf("rejected path %s: code=%d, want 404", target, rec.Code)
		}
	}
}

// TestStaticBareRootMountPrefix guards the "file:///" / "memory:///" form:
// trimming every trailing slash would turn Root into "memory:" and the joined
// name into "memory:/nested/...", which no longer resolves through the mount.
func TestStaticBareRootMountPrefix(t *testing.T) {
	const root = "memory:///"
	rt, svc, fsSvc := loadHarness(t)
	if _, err := fsSvc.MountMemory(root); err != nil {
		t.Fatalf("MountMemory: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	if err := fsSvc.WriteFile(ctx, root+"nested/deep/file.txt", []byte("bare-root-ok")); err != nil {
		t.Fatalf("WriteFile: %v", err)
	}
	comp, err := rt.Load(&Component{Route: "/static", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}
	rec := serve(svc, "/static/nested/deep/file.txt")
	if rec.Code != http.StatusOK || rec.Body.String() != "bare-root-ok" {
		t.Fatalf("bare-root nested file: code=%d body=%q, want 200 bare-root-ok", rec.Code, rec.Body.String())
	}
}

// TestStaticDirectoryIndexIsStdlib verifies that a trailing slash reaches the
// stdlib file server (which serves index.html) instead of being redirected.
func TestStaticDirectoryIndexIsStdlib(t *testing.T) {
	dir := t.TempDir()
	if err := os.MkdirAll(filepath.Join(dir, "docs"), 0o755); err != nil {
		t.Fatalf("mkdir: %v", err)
	}
	if err := os.WriteFile(filepath.Join(dir, "docs", "index.html"), []byte("<h1>docs</h1>"), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}

	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/static", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}
	rec := serve(svc, "/static/docs/")
	if rec.Code != http.StatusOK || rec.Body.String() != "<h1>docs</h1>" {
		t.Fatalf("directory index: code=%d body=%q, want 200 index body", rec.Code, rec.Body.String())
	}
}

func TestStaticDisposeRemovesHandler(t *testing.T) {
	dir := t.TempDir()
	if err := os.WriteFile(filepath.Join(dir, "a.txt"), []byte("A"), 0o644); err != nil {
		t.Fatalf("write fixture: %v", err)
	}
	root := "file://" + dir
	rt, svc, fsSvc := loadHarness(t)
	if err := fsSvc.Mount(root, fs.NewLocalProvider(dir)); err != nil {
		t.Fatalf("Mount: %v", err)
	}
	t.Cleanup(func() { _ = fsSvc.Unmount(root) })

	ctx := testCtx(t)
	comp, err := rt.Load(&Component{Route: "/files", Root: root})
	if err != nil {
		t.Fatalf("load static: %v", err)
	}
	if err := comp.Ready(ctx); err != nil {
		t.Fatalf("static Ready: %v", err)
	}
	if rec := serve(svc, "/files/a.txt"); rec.Code != http.StatusOK {
		t.Fatalf("before dispose: code=%d", rec.Code)
	}
	if err := comp.Dispose(); err != nil {
		t.Fatalf("Dispose: %v", err)
	}
	if err := comp.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}
	if rec := serve(svc, "/files/a.txt"); rec.Code != http.StatusNotFound {
		t.Fatalf("after dispose: code=%d, want 404", rec.Code)
	}
}

func TestComponentMetadata(t *testing.T) {
	c := &Component{Route: "/s", Root: "file:///tmp"}
	if c.Name() != "server-static" {
		t.Fatalf("Name = %q", c.Name())
	}
	if c.Provide() != nil {
		t.Fatalf("Provide = %v, want nil", c.Provide())
	}
	if len(c.Inject()) != 2 {
		t.Fatalf("Inject len = %d, want 2", len(c.Inject()))
	}
}
