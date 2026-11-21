// Package fs provides a small, mount-routed file-system capability for gocordis
// applications. It is deliberately an extension: the Service is a plain value
// provided through a typed key by a Component, and every reversible consequence
// (mounts, watchers) is installed as a runtime effect so the Runtime remains the
// single lifecycle authority.
package fs

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net/url"
	"os"
	"path"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/fsnotify/fsnotify"

	"dynamic-runtime/runtime"
)

// FileInfo is the provider-neutral view of a file's metadata.
type FileInfo interface {
	Name() string
	Size() int64
	IsDir() bool
	ModTime() time.Time
}

// File is an open, readable file. Close releases the underlying handle.
type File interface {
	io.Reader
	io.Closer
	Stat() (FileInfo, error)
}

// WatchEvent describes one filesystem change. Op is one of
// "create", "write", "remove", "rename".
type WatchEvent struct {
	Path string
	Op   string
}

// Provider is a concrete filesystem implementation reachable through a URL
// scheme ("file" or "memory").
type Provider interface {
	Scheme() string
	Open(context.Context, string) (File, error)
	Stat(context.Context, string) (FileInfo, error)
	ReadDir(context.Context, string) ([]FileInfo, error)
	Write(context.Context, string, []byte) error
	Remove(context.Context, string) error
	// Watch returns an inverse that stops the watcher and joins its goroutine.
	// The callback is invoked outside any provider/service lock.
	Watch(context.Context, string, func(WatchEvent) error) (func() error, error)
}

// Service is the mount-routed filesystem capability.
type Service interface {
	// Mount accepts an empty prefix as the fallback mount for names that match
	// no specific prefix. Duplicate prefixes are rejected.
	Mount(prefix string, p Provider) error
	Unmount(prefix string) error
	// Resolve returns the provider with the longest matching mount prefix (or
	// the empty-prefix fallback) and the original name, unchanged.
	Resolve(name string) (Provider, string, error)
	Open(context.Context, string) (File, error)
	ReadFile(context.Context, string) ([]byte, error)
	WriteFile(context.Context, string, []byte) error
	Stat(context.Context, string) (FileInfo, error)
	ReadDir(context.Context, string) ([]FileInfo, error)
	Remove(context.Context, string) error
	Watch(context.Context, string, func(WatchEvent) error) (func() error, error)
}

// ServiceKey is the typed capability key for the filesystem service.
var ServiceKey = runtime.NewKey[Service]("extension.fs.service")

// ---------------------------------------------------------------------------
// metadata
// ---------------------------------------------------------------------------

type fileInfo struct {
	name string
	size int64
	dir  bool
	mod  time.Time
}

func (f fileInfo) Name() string       { return f.name }
func (f fileInfo) Size() int64        { return f.size }
func (f fileInfo) IsDir() bool        { return f.dir }
func (f fileInfo) ModTime() time.Time { return f.mod }

func fromOSInfo(fi os.FileInfo) FileInfo {
	return fileInfo{name: fi.Name(), size: fi.Size(), dir: fi.IsDir(), mod: fi.ModTime()}
}

func notExist(name string) error {
	return fmt.Errorf("fs: %q: %w", name, os.ErrNotExist)
}

// ---------------------------------------------------------------------------
// service
// ---------------------------------------------------------------------------

type mountEntry struct {
	prefix   string
	provider Provider
}

// liveWatch is a service-level watch that can be re-routed when the mount table
// changes. The callback itself is always invoked by the provider outside locks.
type liveWatch struct {
	mu       sync.Mutex
	owner    *service
	name     string
	ctx      context.Context
	cb       func(WatchEvent) error
	provider Provider
	inv      func() error
	stopped  bool
}

func (w *liveWatch) stop() error {
	w.mu.Lock()
	if w.stopped {
		w.mu.Unlock()
		return nil
	}
	w.stopped = true
	inv := w.inv
	w.inv = nil
	w.mu.Unlock()

	w.owner.mu.Lock()
	delete(w.owner.watches, w)
	w.owner.mu.Unlock()
	if inv != nil {
		return inv()
	}
	return nil
}

// reroute moves a live watch to the provider that currently owns its name,
// following the JS fs/core behavior on mount/unmount. Provider and service
// locks are never held while user callbacks run.
func (w *liveWatch) reroute() {
	w.mu.Lock()
	if w.stopped {
		w.mu.Unlock()
		return
	}
	p, _, err := w.owner.Resolve(w.name)
	if err != nil || p == w.provider {
		w.mu.Unlock()
		return
	}
	inv, err := p.Watch(w.ctx, w.name, w.cb)
	if err != nil {
		w.mu.Unlock() // keep the old watch when re-registration fails
		return
	}
	old := w.inv
	w.inv, w.provider = inv, p
	w.mu.Unlock()
	if old != nil {
		_ = old()
	}
}

type service struct {
	mu      sync.RWMutex
	mounts  []mountEntry
	watches map[*liveWatch]struct{}
}

func newService() *service { return &service{watches: make(map[*liveWatch]struct{})} }

func (s *service) Mount(prefix string, p Provider) error {
	if p == nil {
		return errors.New("fs: nil provider")
	}
	s.mu.Lock()
	for _, m := range s.mounts {
		if m.prefix == prefix {
			s.mu.Unlock()
			return fmt.Errorf("fs: duplicate mount %q", prefix)
		}
	}
	s.mounts = append(s.mounts, mountEntry{prefix: prefix, provider: p})
	watches := s.watchListLocked()
	s.mu.Unlock()
	s.reroute(watches)
	return nil
}

func (s *service) Unmount(prefix string) error {
	s.mu.Lock()
	for i, m := range s.mounts {
		if m.prefix == prefix {
			s.mounts = append(s.mounts[:i], s.mounts[i+1:]...)
			watches := s.watchListLocked()
			s.mu.Unlock()
			s.reroute(watches)
			return nil
		}
	}
	s.mu.Unlock()
	return nil // idempotent
}

func (s *service) watchListLocked() []*liveWatch {
	out := make([]*liveWatch, 0, len(s.watches))
	for w := range s.watches {
		out = append(out, w)
	}
	return out
}

func (s *service) reroute(watches []*liveWatch) {
	for _, w := range watches {
		w.reroute()
	}
}

// Resolve picks the longest matching prefix; the empty prefix (if mounted) is
// the fallback when no specific prefix matches, matching the JS implementation.
func (s *service) Resolve(name string) (Provider, string, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	var best, fallback *mountEntry
	for i := range s.mounts {
		m := &s.mounts[i]
		if m.prefix == "" {
			fallback = m
			continue
		}
		if !strings.HasPrefix(name, m.prefix) {
			continue
		}
		rest := name[len(m.prefix):]
		if rest != "" && !strings.HasPrefix(rest, "/") && !strings.HasSuffix(m.prefix, "/") {
			continue // "app" must not match "application"
		}
		if best == nil || len(m.prefix) > len(best.prefix) {
			best = m
		}
	}
	if best == nil {
		best = fallback
	}
	if best == nil {
		return nil, "", fmt.Errorf("fs: no mount matches %q", name)
	}
	return best.provider, name, nil
}

func (s *service) Open(ctx context.Context, name string) (File, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return nil, err
	}
	return p.Open(ctx, res)
}

func (s *service) ReadFile(ctx context.Context, name string) ([]byte, error) {
	f, err := s.Open(ctx, name)
	if err != nil {
		return nil, err
	}
	defer f.Close()
	return io.ReadAll(f)
}

func (s *service) WriteFile(ctx context.Context, name string, data []byte) error {
	p, res, err := s.Resolve(name)
	if err != nil {
		return err
	}
	return p.Write(ctx, res, data)
}

func (s *service) Stat(ctx context.Context, name string) (FileInfo, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return nil, err
	}
	return p.Stat(ctx, res)
}

func (s *service) ReadDir(ctx context.Context, name string) ([]FileInfo, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return nil, err
	}
	return p.ReadDir(ctx, res)
}

func (s *service) Remove(ctx context.Context, name string) error {
	p, res, err := s.Resolve(name)
	if err != nil {
		return err
	}
	return p.Remove(ctx, res)
}

func (s *service) Watch(ctx context.Context, name string, cb func(WatchEvent) error) (func() error, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return nil, err
	}
	inv, err := p.Watch(ctx, res, cb)
	if err != nil {
		return nil, err
	}
	w := &liveWatch{
		owner: s, name: name, ctx: ctx, cb: cb,
		provider: p, inv: inv,
	}
	s.mu.Lock()
	s.watches[w] = struct{}{}
	s.mu.Unlock()
	return w.stop, nil
}

// Close drops all mounts and stops all watches started through this service.
// It is installed as an effect by Component.Apply, so disposal leaves no residue.
func (s *service) Close() error {
	s.mu.Lock()
	watches := s.watchListLocked()
	s.mounts = nil
	s.mu.Unlock()
	var errs []error
	for _, w := range watches {
		if err := w.stop(); err != nil {
			errs = append(errs, err)
		}
	}
	return errors.Join(errs...)
}

// ---------------------------------------------------------------------------
// component
// ---------------------------------------------------------------------------

// Component provides the filesystem Service under ServiceKey.
type Component struct{}

func (c *Component) Name() string { return "extension.fs" }

func (c *Component) Inject() []runtime.Dependency { return nil }

func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc := newService()
	// Install the cleanup effect BEFORE the capability: LIFO unwind withdraws
	// ServiceKey first, then closes the service, so no consumer can observe a
	// closed service through a still-published binding.
	if err := ctx.Effect(func() (func() error, error) {
		return func() error { return svc.Close() }, nil
	}); err != nil {
		return nil, err
	}
	if err := runtime.Provide[Service](ctx, ServiceKey, svc); err != nil {
		return nil, err
	}
	return nil, nil
}

// Mount installs a mount through an effect: the inverse unmounts it when the
// calling activation unwinds. Duplicate prefixes are rejected by Service.Mount.
func Mount(c *runtime.Context, svc Service, prefix string, p Provider) error {
	return c.Effect(func() (func() error, error) {
		if err := svc.Mount(prefix, p); err != nil {
			return nil, err
		}
		return func() error { return svc.Unmount(prefix) }, nil
	})
}

// ---------------------------------------------------------------------------
// local (file://) provider
// ---------------------------------------------------------------------------

type localProvider struct {
	root     *os.Root
	rootPath string
	openErr  error
}

// NewLocalProvider returns a file:// provider confined to root. Both URL names
// (file:///abs/path) and plain paths (relative to root) are accepted. Every
// operation goes through os.Root, which resolves symlinks atomically relative
// to an open directory handle, so a swapped symlink cannot escape the root.
func NewLocalProvider(root string) Provider {
	abs, err := filepath.Abs(root)
	if err != nil {
		return &localProvider{openErr: err}
	}
	if err := os.MkdirAll(abs, 0o755); err != nil {
		return &localProvider{rootPath: abs, openErr: err}
	}
	r, err := os.OpenRoot(abs)
	if err != nil {
		return &localProvider{rootPath: abs, openErr: err}
	}
	return &localProvider{root: r, rootPath: abs}
}

func (p *localProvider) Scheme() string { return "file" }

// relative converts a provider name into a root-relative path. os.Root (not a
// Lstat/EvalSymlinks pre-check) is the authority on symlink containment.
func (p *localProvider) relative(name string) (string, error) {
	if p.openErr != nil {
		return "", fmt.Errorf("fs: open root: %w", p.openErr)
	}
	if strings.HasPrefix(name, "file://") {
		u, err := url.Parse(name)
		if err != nil {
			return "", fmt.Errorf("fs: invalid file URL %q: %w", name, err)
		}
		if u.Host != "" && u.Host != "localhost" {
			return "", fmt.Errorf("fs: unsupported file host %q", u.Host)
		}
		name = u.Path
		if name == "" {
			name = "/"
		}
	} else if strings.Contains(name, "://") {
		return "", fmt.Errorf("fs: unsupported URL %q for file provider", name)
	}
	name = filepath.FromSlash(name)
	var rel string
	if filepath.IsAbs(name) {
		var err error
		rel, err = filepath.Rel(p.rootPath, filepath.Clean(name))
		if err != nil {
			return "", err
		}
	} else {
		rel = filepath.Clean(name)
	}
	if rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) || filepath.IsAbs(rel) {
		return "", fmt.Errorf("fs: path %q escapes root %q", name, p.rootPath)
	}
	return rel, nil
}

type localFile struct{ *os.File }

func (f *localFile) Stat() (FileInfo, error) {
	fi, err := f.File.Stat()
	if err != nil {
		return nil, err
	}
	return fromOSInfo(fi), nil
}

func (p *localProvider) Open(ctx context.Context, name string) (File, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	f, err := p.root.Open(rel)
	if err != nil {
		return nil, fmt.Errorf("fs: open: %w", err)
	}
	return &localFile{File: f}, nil
}

func (p *localProvider) Stat(ctx context.Context, name string) (FileInfo, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	fi, err := p.root.Stat(rel)
	if err != nil {
		return nil, fmt.Errorf("fs: stat: %w", err)
	}
	return fromOSInfo(fi), nil
}

func (p *localProvider) ReadDir(ctx context.Context, name string) ([]FileInfo, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	d, err := p.root.Open(rel)
	if err != nil {
		return nil, fmt.Errorf("fs: readdir: %w", err)
	}
	defer d.Close()
	entries, err := d.ReadDir(-1)
	if err != nil {
		return nil, fmt.Errorf("fs: readdir: %w", err)
	}
	out := make([]FileInfo, 0, len(entries))
	for _, e := range entries {
		fi, err := e.Info()
		if err != nil {
			return nil, err
		}
		out = append(out, fromOSInfo(fi))
	}
	return out, nil
}

// rootMkdirAll creates dir under a Root using only Go 1.24 APIs (Root.MkdirAll
// arrived in Go 1.25). Every step stays inside the Root's symlink-safe namespace.
func rootMkdirAll(r *os.Root, dir string, perm os.FileMode) error {
	if dir == "" || dir == "." || dir == string(filepath.Separator) {
		return nil
	}
	if parent := filepath.Dir(dir); parent != dir {
		if err := rootMkdirAll(r, parent, perm); err != nil {
			return err
		}
	}
	err := r.Mkdir(dir, perm)
	if err == nil {
		return nil
	}
	if os.IsExist(err) {
		if fi, statErr := r.Stat(dir); statErr == nil && fi.IsDir() {
			return nil
		}
	}
	return err
}

func (p *localProvider) Write(ctx context.Context, name string, data []byte) error {
	rel, err := p.relative(name)
	if err != nil {
		return err
	}
	if dir := filepath.Dir(rel); dir != "." {
		if err := rootMkdirAll(p.root, dir, 0o755); err != nil {
			return err
		}
	}
	f, err := p.root.OpenFile(rel, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0o644)
	if err != nil {
		return err
	}
	_, writeErr := f.Write(data)
	closeErr := f.Close()
	if writeErr != nil {
		return writeErr
	}
	return closeErr
}

func (p *localProvider) Remove(ctx context.Context, name string) error {
	rel, err := p.relative(name)
	if err != nil {
		return err
	}
	return p.root.Remove(rel)
}

func mapOp(op fsnotify.Op) string {
	switch {
	case op.Has(fsnotify.Create):
		return "create"
	case op.Has(fsnotify.Remove):
		return "remove"
	case op.Has(fsnotify.Rename):
		return "rename"
	default:
		return "write" // Write or Chmod
	}
}

func (p *localProvider) Watch(ctx context.Context, name string, cb func(WatchEvent) error) (func() error, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	rp := filepath.Join(p.rootPath, rel)
	watcher, err := fsnotify.NewWatcher()
	if err != nil {
		return nil, err
	}
	// Watching the parent directory also catches create/remove/rename of a
	// target that does not exist yet.
	if err := watcher.Add(filepath.Dir(rp)); err != nil {
		_ = watcher.Close()
		return nil, err
	}
	stop := make(chan struct{})
	done := make(chan struct{})
	var once sync.Once
	go func() {
		defer close(done)
		defer watcher.Close()
		for {
			select {
			case <-stop:
				return
			case <-ctx.Done():
				return
			case ev, ok := <-watcher.Events:
				if !ok {
					return
				}
				if filepath.Clean(ev.Name) != rp {
					continue
				}
				// No lock is held while invoking user code.
				_ = cb(WatchEvent{Path: name, Op: mapOp(ev.Op)})
			case _, ok := <-watcher.Errors:
				if !ok {
					return
				}
			}
		}
	}()
	return func() error {
		once.Do(func() { close(stop) })
		<-done // join: after the inverse returns, no callback can run
		return nil
	}, nil
}

// ---------------------------------------------------------------------------
// memory provider
// ---------------------------------------------------------------------------

func memKey(name string) string {
	if strings.HasPrefix(name, "memory://") {
		if u, err := url.Parse(name); err == nil {
			name = u.Path
		} else {
			name = strings.TrimPrefix(name, "memory://")
		}
	} else if strings.Contains(name, "://") {
		return "" // foreign scheme
	}
	name = strings.ReplaceAll(name, "\\", "/")
	cleaned := path.Clean("/" + strings.TrimPrefix(name, "/"))
	return strings.TrimPrefix(cleaned, "/")
}

func memParent(key string) string {
	i := strings.LastIndex(key, "/")
	if i < 0 {
		return ""
	}
	return key[:i]
}

type memEntry struct {
	data []byte
	mod  time.Time
}

type memWatch struct {
	key string
	cb  func(WatchEvent) error
}

type memoryProvider struct {
	mu      sync.RWMutex
	files   map[string]memEntry
	dirs    map[string]struct{}
	watches map[*memWatch]struct{}
}

// NewMemoryProvider returns an in-memory filesystem reachable as memory://....
func NewMemoryProvider() Provider {
	return &memoryProvider{
		files:   make(map[string]memEntry),
		dirs:    make(map[string]struct{}),
		watches: make(map[*memWatch]struct{}),
	}
}

func (p *memoryProvider) Scheme() string { return "memory" }

func (p *memoryProvider) matchesLocked(key string) []*memWatch {
	var out []*memWatch
	for w := range p.watches {
		if w.key == key || strings.HasPrefix(key, w.key+"/") {
			out = append(out, w)
		}
	}
	return out
}

func (p *memoryProvider) notify(watches []*memWatch, ev WatchEvent) {
	for _, w := range watches {
		_ = w.cb(ev) // callbacks run outside the provider lock
	}
}

type memFile struct {
	*bytes.Reader
	info FileInfo
}

func (f *memFile) Close() error            { return nil }
func (f *memFile) Stat() (FileInfo, error) { return f.info, nil }

func (p *memoryProvider) Open(ctx context.Context, name string) (File, error) {
	k := memKey(name)
	p.mu.RLock()
	e, ok := p.files[k]
	p.mu.RUnlock()
	if !ok {
		return nil, notExist(name)
	}
	info := fileInfo{name: path.Base(k), size: int64(len(e.data)), mod: e.mod}
	return &memFile{Reader: bytes.NewReader(e.data), info: info}, nil
}

func (p *memoryProvider) Stat(ctx context.Context, name string) (FileInfo, error) {
	k := memKey(name)
	p.mu.RLock()
	defer p.mu.RUnlock()
	if e, ok := p.files[k]; ok {
		return fileInfo{name: path.Base(k), size: int64(len(e.data)), mod: e.mod}, nil
	}
	if _, ok := p.dirs[k]; ok {
		return fileInfo{name: path.Base(k), dir: true, mod: time.Unix(0, 0)}, nil
	}
	return nil, notExist(name)
}

func (p *memoryProvider) ReadDir(ctx context.Context, name string) ([]FileInfo, error) {
	k := memKey(name)
	prefix := k
	if prefix != "" {
		prefix += "/"
	}
	p.mu.RLock()
	defer p.mu.RUnlock()
	found := k == ""
	if _, ok := p.dirs[k]; ok {
		found = true
	}
	set := make(map[string]FileInfo)
	for f, e := range p.files {
		if !strings.HasPrefix(f, prefix) {
			continue
		}
		found = true
		rest := strings.TrimPrefix(f, prefix)
		if i := strings.Index(rest, "/"); i >= 0 {
			d := rest[:i]
			set[d] = fileInfo{name: d, dir: true, mod: time.Unix(0, 0)}
		} else if rest != "" {
			set[rest] = fileInfo{name: rest, size: int64(len(e.data)), mod: e.mod}
		}
	}
	for d := range p.dirs {
		if !strings.HasPrefix(d, prefix) {
			continue
		}
		rest := strings.TrimPrefix(d, prefix)
		if i := strings.Index(rest, "/"); i < 0 && rest != "" {
			set[rest] = fileInfo{name: rest, dir: true, mod: time.Unix(0, 0)}
		}
	}
	if !found {
		return nil, notExist(name)
	}
	names := make([]string, 0, len(set))
	for n := range set {
		names = append(names, n)
	}
	sort.Strings(names)
	out := make([]FileInfo, 0, len(names))
	for _, n := range names {
		out = append(out, set[n])
	}
	return out, nil
}

func (p *memoryProvider) Write(ctx context.Context, name string, data []byte) error {
	k := memKey(name)
	if k == "" {
		return errors.New("fs: empty memory key")
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	p.mu.Lock()
	_, existed := p.files[k]
	mod := time.Now()
	p.files[k] = memEntry{data: append([]byte(nil), data...), mod: mod}
	for d := memParent(k); d != ""; d = memParent(d) {
		p.dirs[d] = struct{}{}
	}
	op := "create"
	if existed {
		op = "write"
	}
	watches := p.matchesLocked(k)
	p.mu.Unlock()
	p.notify(watches, WatchEvent{Path: name, Op: op})
	return nil
}

func (p *memoryProvider) Remove(ctx context.Context, name string) error {
	k := memKey(name)
	p.mu.Lock()
	if _, ok := p.files[k]; !ok {
		p.mu.Unlock()
		return notExist(name)
	}
	delete(p.files, k)
	watches := p.matchesLocked(k)
	p.mu.Unlock()
	p.notify(watches, WatchEvent{Path: name, Op: "remove"})
	return nil
}

func (p *memoryProvider) Watch(ctx context.Context, name string, cb func(WatchEvent) error) (func() error, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	w := &memWatch{key: memKey(name), cb: cb}
	p.mu.Lock()
	p.watches[w] = struct{}{}
	p.mu.Unlock()
	var once sync.Once
	return func() error {
		once.Do(func() {
			p.mu.Lock()
			delete(p.watches, w)
			p.mu.Unlock()
		})
		return nil
	}, nil
}
