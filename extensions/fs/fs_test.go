package fs_test

import (
	"bytes"
	"context"
	"io"
	"os"
	"path/filepath"
	"testing"
	"time"

	"dynamic-runtime/extensions/fs"
	"dynamic-runtime/runtime"
)

func tctx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)
	return ctx
}

type fsProbe struct{ ch chan fs.Service }

func (p *fsProbe) Name() string { return "fs-probe" }
func (p *fsProbe) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(fs.ServiceKey)}
}
func (p *fsProbe) Provide() []runtime.Capability { return nil }
func (p *fsProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	p.ch <- svc
	return nil, nil
}

func startFS(t *testing.T) (*runtime.Runtime, *runtime.Fiber, fs.Service) {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = rt.Close(context.Background()) })

	ff, err := rt.Load(&fs.Component{})
	if err != nil {
		t.Fatal(err)
	}
	if err := ff.Ready(tctx(t)); err != nil {
		t.Fatalf("fs component not ready: %v", err)
	}

	probe := &fsProbe{ch: make(chan fs.Service, 1)}
	pf, err := rt.Load(probe)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(tctx(t)); err != nil {
		t.Fatalf("fs probe not ready: %v", err)
	}
	return rt, ff, <-probe.ch
}

func readAll(p fs.Provider, ctx context.Context, name string) ([]byte, error) {
	f, err := p.Open(ctx, name)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	return io.ReadAll(f)
}

func TestLongestPrefixRouting(t *testing.T) {
	_, _, svc := startFS(t)
	ctx := tctx(t)

	general := fs.NewMemoryProvider()
	special := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///", general); err != nil {
		t.Fatal(err)
	}
	if err := svc.Mount("memory:///app/special", special); err != nil {
		t.Fatal(err)
	}
	if err := svc.Mount("memory:///", general); err == nil {
		t.Fatal("duplicate mount was accepted")
	}

	p, name, err := svc.Resolve("memory:///app/special/key")
	if err != nil {
		t.Fatal(err)
	}
	if p != special {
		t.Fatalf("longest-prefix route selected %q, want special", p.Scheme())
	}
	if name != "memory:///app/special/key" {
		t.Fatalf("Resolve returned name %q", name)
	}
	p, _, err = svc.Resolve("memory:///other/key")
	if err != nil || p != general {
		t.Fatalf("general route: p=%v err=%v", p, err)
	}
	// A prefix must not match a longer, different path segment.
	p, _, err = svc.Resolve("memory:///application/x")
	if err != nil || p != general {
		t.Fatalf("boundary route: p=%v err=%v", p, err)
	}

	if err := svc.WriteFile(ctx, "memory:///app/special/key", []byte("special")); err != nil {
		t.Fatal(err)
	}
	data, err := readAll(special, ctx, "memory:///app/special/key")
	if err != nil || string(data) != "special" {
		t.Fatalf("special provider data=%q err=%v", data, err)
	}
	data, err = readAll(general, ctx, "memory:///app/special/key")
	if err == nil {
		t.Fatalf("general provider unexpectedly has the key (data=%q)", data)
	}
	if err := svc.Unmount("memory:///app/special"); err != nil {
		t.Fatal(err)
	}
	// With the specific mount gone, the general mount takes over the route.
	p, _, err = svc.Resolve("memory:///app/special/key")
	if err != nil || p != general {
		t.Fatalf("route after Unmount: p=%v err=%v", p, err)
	}
	if err := svc.Unmount("memory:///"); err != nil {
		t.Fatal(err)
	}
	if _, _, err := svc.Resolve("memory:///app/special/key"); err == nil {
		t.Fatal("Resolve succeeded after all mounts were removed")
	}
}

func TestLocalProviderRoundTripAndPathEscape(t *testing.T) {
	root := t.TempDir()
	p := fs.NewLocalProvider(root)
	if p.Scheme() != "file" {
		t.Fatalf("scheme=%q", p.Scheme())
	}
	ctx := tctx(t)

	name := "file://" + filepath.Join(root, "hello.txt")
	if err := p.Write(ctx, name, []byte("hello")); err != nil {
		t.Fatal(err)
	}
	data, err := readAll(p, ctx, name)
	if err != nil || string(data) != "hello" {
		t.Fatalf("data=%q err=%v", data, err)
	}
	fi, err := p.Stat(ctx, name)
	if err != nil || fi.Name() != "hello.txt" || fi.Size() != 5 || fi.IsDir() {
		t.Fatalf("stat=%+v err=%v", fi, err)
	}
	entries, err := p.ReadDir(ctx, "file://"+root)
	if err != nil || len(entries) != 1 || entries[0].Name() != "hello.txt" {
		t.Fatalf("entries=%+v err=%v", entries, err)
	}
	if err := p.Remove(ctx, name); err != nil {
		t.Fatal(err)
	}
	if _, err := p.Stat(ctx, name); err == nil {
		t.Fatal("Stat succeeded after Remove")
	}

	for _, bad := range []string{
		"file:///../../etc/evil",
		"../../etc/evil",
		"file:///etc/passwd",
	} {
		if err := p.Write(ctx, bad, []byte("x")); err == nil {
			t.Fatalf("path escape %q was accepted", bad)
		}
	}

	// The same rejection must hold when routed through the Service.
	_, _, svc := startFS(t)
	if err := svc.Mount("file:///", fs.NewLocalProvider(root)); err != nil {
		t.Fatal(err)
	}
	if err := svc.WriteFile(ctx, "file:///etc/passwd", []byte("x")); err == nil {
		t.Fatal("service routed an escaping path")
	}
}

func TestMemoryProviderRoundTrip(t *testing.T) {
	p := fs.NewMemoryProvider()
	if p.Scheme() != "memory" {
		t.Fatalf("scheme=%q", p.Scheme())
	}
	ctx := tctx(t)

	if err := p.Write(ctx, "memory:///a/b/c.txt", []byte("abc")); err != nil {
		t.Fatal(err)
	}
	for _, name := range []string{"memory:///a/b/c.txt", "a/b/c.txt"} {
		data, err := readAll(p, ctx, name)
		if err != nil || string(data) != "abc" {
			t.Fatalf("%q: data=%q err=%v", name, data, err)
		}
	}
	fi, err := p.Stat(ctx, "memory:///a/b/c.txt")
	if err != nil || fi.Name() != "c.txt" || fi.Size() != 3 || fi.IsDir() || fi.ModTime().IsZero() {
		t.Fatalf("stat=%+v err=%v", fi, err)
	}
	entries, err := p.ReadDir(ctx, "memory:///a/b")
	if err != nil || len(entries) != 1 || entries[0].Name() != "c.txt" {
		t.Fatalf("entries=%+v err=%v", entries, err)
	}
	if err := p.Remove(ctx, "memory:///a/b/c.txt"); err != nil {
		t.Fatal(err)
	}
	if _, err := p.Stat(ctx, "memory:///a/b/c.txt"); err == nil {
		t.Fatal("Stat succeeded after Remove")
	}
}

func TestWatchEventDeliveredThenInverseStops(t *testing.T) {
	p := fs.NewMemoryProvider()
	ctx := tctx(t)
	events := make(chan fs.WatchEvent, 8)
	inv, err := p.Watch(ctx, "memory:///watched.txt", func(ev fs.WatchEvent) error {
		events <- ev
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}

	if err := p.Write(ctx, "memory:///watched.txt", []byte("v1")); err != nil {
		t.Fatal(err)
	}
	select {
	case ev := <-events:
		if ev.Op != "create" {
			t.Fatalf("first op=%q, want create", ev.Op)
		}
	case <-ctx.Done():
		t.Fatal("no create event delivered")
	}
	if err := p.Write(ctx, "memory:///watched.txt", []byte("v2")); err != nil {
		t.Fatal(err)
	}
	select {
	case ev := <-events:
		if ev.Op != "write" {
			t.Fatalf("second op=%q, want write", ev.Op)
		}
	case <-ctx.Done():
		t.Fatal("no write event delivered")
	}

	if err := inv(); err != nil {
		t.Fatal(err)
	}
	if err := inv(); err != nil {
		t.Fatalf("inverse not idempotent: %v", err)
	}
	if err := p.Write(ctx, "memory:///watched.txt", []byte("v3")); err != nil {
		t.Fatal(err)
	}
	select {
	case ev := <-events:
		t.Fatalf("event delivered after inverse: %+v", ev)
	default:
	}
}

func TestLocalWatchDeliversAndJoins(t *testing.T) {
	root := t.TempDir()
	p := fs.NewLocalProvider(root)
	ctx := tctx(t)
	name := "file://" + filepath.Join(root, "w.txt")

	started := make(chan fs.WatchEvent, 4)
	release := make(chan struct{})
	inv, err := p.Watch(ctx, name, func(ev fs.WatchEvent) error {
		if ev.Op != "create" && ev.Op != "write" {
			return nil
		}
		started <- ev
		<-release // hold the callback so the inverse cannot finish early
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}

	if err := os.WriteFile(filepath.Join(root, "w.txt"), []byte("v1"), 0o644); err != nil {
		t.Fatal(err)
	}
	select {
	case <-started: // event delivered, callback now running
	case <-ctx.Done():
		t.Fatal("no local watch event delivered")
	}

	joined := make(chan error, 1)
	go func() { joined <- inv() }()
	select {
	case <-joined:
		t.Fatal("inverse returned while a callback was still running")
	case <-time.After(20 * time.Millisecond):
	}
	close(release)
	select {
	case err := <-joined:
		if err != nil {
			t.Fatal(err)
		}
	case <-ctx.Done():
		t.Fatal("inverse did not join the watcher goroutine")
	}
	if err := inv(); err != nil {
		t.Fatalf("inverse not idempotent: %v", err)
	}
}

type mountProbe struct {
	svc fs.Service
	p   fs.Provider
	ch  chan error
}

func (c *mountProbe) Name() string { return "mount-probe" }
func (c *mountProbe) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(fs.ServiceKey)}
}
func (c *mountProbe) Provide() []runtime.Capability { return nil }
func (c *mountProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, fs.ServiceKey)
	if err != nil {
		return nil, err
	}
	if err := fs.Mount(ctx, svc, "memory:///app", c.p); err != nil {
		return nil, err
	}
	if err := svc.WriteFile(ctx.Context(), "memory:///app/x", []byte("x")); err != nil {
		return nil, err
	}
	c.ch <- nil
	return nil, nil
}

func TestMountEffectAndComponentDisposeRemovesResidue(t *testing.T) {
	rt, ff, svc := startFS(t)
	ctx := tctx(t)

	mp := &mountProbe{p: fs.NewMemoryProvider(), ch: make(chan error, 1)}
	mf, err := rt.Load(mp)
	if err != nil {
		t.Fatal(err)
	}
	if err := mf.Ready(ctx); err != nil {
		t.Fatalf("mount probe not ready: %v", err)
	}
	select {
	case err := <-mp.ch:
		if err != nil {
			t.Fatal(err)
		}
	default:
	}
	data, err := svc.ReadFile(ctx, "memory:///app/x")
	if err != nil || !bytes.Equal(data, []byte("x")) {
		t.Fatalf("data=%q err=%v", data, err)
	}

	// Disposing the mount owner unmounts through the ctx.Effect inverse.
	if err := mf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := mf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if _, _, err := svc.Resolve("memory:///app/x"); err == nil {
		t.Fatal("mount survived owner disposal")
	}

	// A direct mount (no owning fiber) is cleaned up by the fs component itself.
	if err := svc.Mount("memory:///direct", fs.NewMemoryProvider()); err != nil {
		t.Fatal(err)
	}
	if err := ff.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := ff.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	if _, _, err := svc.Resolve("memory:///direct/x"); err == nil {
		t.Fatal("service residue survived component disposal")
	}
}

func TestLocalProviderRejectsSymlinkSwapEscape(t *testing.T) {
	root := t.TempDir()
	outside := t.TempDir()
	secretPath := filepath.Join(outside, "secret.txt")
	if err := os.WriteFile(secretPath, []byte("secret"), 0o644); err != nil {
		t.Fatal(err)
	}
	switchDir := filepath.Join(root, "switch")
	if err := os.Mkdir(switchDir, 0o755); err != nil {
		t.Fatal(err)
	}
	p := fs.NewLocalProvider(root)
	ctx := tctx(t)
	okName := "file://" + filepath.Join(root, "switch", "ok.txt")
	if err := p.Write(ctx, okName, []byte("ok")); err != nil {
		t.Fatal(err)
	}

	// Replace a real directory inside the root with a symlink to the outside.
	if err := os.RemoveAll(switchDir); err != nil {
		t.Fatal(err)
	}
	if err := os.Symlink(outside, switchDir); err != nil {
		t.Fatal(err)
	}
	name := "file://" + filepath.Join(root, "switch", "secret.txt")
	if err := p.Write(ctx, name, []byte("overwritten")); err == nil {
		t.Fatal("Write escaped through the swapped symlink")
	}
	if f, err := p.Open(ctx, name); err == nil {
		f.Close()
		t.Fatal("Open escaped through the swapped symlink")
	}
	if _, err := p.Stat(ctx, name); err == nil {
		t.Fatal("Stat escaped through the swapped symlink")
	}
	if err := p.Remove(ctx, name); err == nil {
		t.Fatal("Remove escaped through the swapped symlink")
	}
	data, err := os.ReadFile(secretPath)
	if err != nil || string(data) != "secret" {
		t.Fatalf("outside file changed: data=%q err=%v", data, err)
	}
}

func TestEmptyPrefixFallbackMount(t *testing.T) {
	_, _, svc := startFS(t)
	ctx := tctx(t)
	fallback := fs.NewMemoryProvider()
	if err := svc.Mount("", fallback); err != nil {
		t.Fatalf("empty-prefix mount rejected: %v", err)
	}
	if err := svc.Mount("", fallback); err == nil {
		t.Fatal("duplicate empty-prefix mount accepted")
	}
	p, _, err := svc.Resolve("memory:///fallback.txt")
	if err != nil || p != fallback {
		t.Fatalf("fallback route: p=%v err=%v", p, err)
	}
	if err := svc.WriteFile(ctx, "memory:///fallback.txt", []byte("fb")); err != nil {
		t.Fatal(err)
	}
	data, err := readAll(fallback, ctx, "memory:///fallback.txt")
	if err != nil || string(data) != "fb" {
		t.Fatalf("fallback data=%q err=%v", data, err)
	}

	specific := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///app", specific); err != nil {
		t.Fatal(err)
	}
	p, _, err = svc.Resolve("memory:///app/x")
	if err != nil || p != specific {
		t.Fatalf("specific route: p=%v err=%v", p, err)
	}
	p, _, err = svc.Resolve("memory:///other/x")
	if err != nil || p != fallback {
		t.Fatalf("fallback after specific mount: p=%v err=%v", p, err)
	}
}

func TestWatchReroutesOnMountChange(t *testing.T) {
	_, _, svc := startFS(t)
	ctx := tctx(t)
	general := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///", general); err != nil {
		t.Fatal(err)
	}
	events := make(chan fs.WatchEvent, 8)
	inv, err := svc.Watch(ctx, "memory:///app/f", func(ev fs.WatchEvent) error {
		events <- ev
		return nil
	})
	if err != nil {
		t.Fatal(err)
	}
	defer inv()

	expect := func(op string) {
		t.Helper()
		select {
		case ev := <-events:
			if ev.Op != op {
				t.Fatalf("op=%q, want %q", ev.Op, op)
			}
		case <-ctx.Done():
			t.Fatalf("no %s event delivered", op)
		}
	}
	noEvent := func() {
		t.Helper()
		select {
		case ev := <-events:
			t.Fatalf("stale provider still watched: %+v", ev)
		default: // memory callbacks are synchronous, so this is deterministic
		}
	}

	if err := general.Write(ctx, "memory:///app/f", []byte("g1")); err != nil {
		t.Fatal(err)
	}
	expect("create")
	noEvent()

	// A longer prefix steals the route; the live watch must follow it.
	specific := fs.NewMemoryProvider()
	if err := svc.Mount("memory:///app", specific); err != nil {
		t.Fatal(err)
	}
	if err := specific.Write(ctx, "memory:///app/f", []byte("s1")); err != nil {
		t.Fatal(err)
	}
	expect("create")
	noEvent()
	if err := general.Write(ctx, "memory:///app/f", []byte("g2")); err != nil {
		t.Fatal(err)
	}
	noEvent()

	// Unmounting the specific provider returns the watch to the fallback.
	if err := svc.Unmount("memory:///app"); err != nil {
		t.Fatal(err)
	}
	if err := general.Write(ctx, "memory:///app/f", []byte("g3")); err != nil {
		t.Fatal(err)
	}
	expect("write")
	noEvent()
	if err := specific.Write(ctx, "memory:///app/f", []byte("s2")); err != nil {
		t.Fatal(err)
	}
	noEvent()
}
