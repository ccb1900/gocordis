// Package local is the file:// provider for the gocordis FS Service. Every
// namespace operation goes through os.Root so symlinks cannot escape the root;
// the fsnotify-based Watcher implementation lives here too.
package local

import (
	"context"
	"errors"
	"fmt"
	"io"
	iofs "io/fs"
	"net/url"
	"os"
	"path/filepath"
	"strings"
	"sync"

	"github.com/fsnotify/fsnotify"

	"dynamic-runtime/extensions/fs/contract"
)

var (
	_ contract.Provider              = (*Provider)(nil)
	_ contract.StreamProvider        = (*Provider)(nil)
	_ contract.ExtendedProvider      = (*Provider)(nil)
	_ contract.StreamWatcher         = (*Provider)(nil)
	_ contract.RemoveOptionsProvider = (*Provider)(nil)
	_ contract.MkdirOptionsProvider  = (*Provider)(nil)
	_ contract.CopyOptionsProvider   = (*Provider)(nil)
	_ contract.SymlinkProvider       = (*Provider)(nil)
)

// Provider is a file:// provider confined to one root directory.
type Provider struct {
	root      *os.Root
	rootPath  string
	openErr   error
	closeOnce sync.Once
	closeErr  error
}

// NewProvider returns a file:// provider confined to root. Both URL names
// (file:///abs/path) and plain paths (relative to root) are accepted. Every
// operation goes through os.Root, which resolves symlinks atomically relative
// to an open directory handle, so a swapped symlink cannot escape the root.
func NewProvider(root string) *Provider {
	abs, err := filepath.Abs(root)
	if err != nil {
		return &Provider{openErr: err}
	}
	if err := os.MkdirAll(abs, 0o755); err != nil {
		return &Provider{rootPath: abs, openErr: err}
	}
	r, err := os.OpenRoot(abs)
	if err != nil {
		return &Provider{rootPath: abs, openErr: err}
	}
	return &Provider{root: r, rootPath: abs}
}

// New is a convenience alias returning the structural Provider interface.
func New(root string) contract.Provider { return NewProvider(root) }

func (p *Provider) Scheme() string { return "file" }

// Close releases the os.Root handle. It is idempotent so a provider shared by
// several mounts (or closed by both its owner and the service) stays safe.
func (p *Provider) Close() error {
	p.closeOnce.Do(func() {
		if p.root != nil {
			p.closeErr = p.root.Close()
		}
	})
	return p.closeErr
}

// relative converts a provider name into a root-relative path. os.Root (not a
// Lstat/EvalSymlinks pre-check) is the authority on symlink containment.
func (p *Provider) relative(name string) (string, error) {
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

type file struct{ *os.File }

func (f *file) Stat() (contract.FileInfo, error) {
	fi, err := f.File.Stat()
	if err != nil {
		return nil, err
	}
	return contract.FromOSInfo(fi), nil
}

func (p *Provider) Open(ctx context.Context, name string) (contract.File, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	f, err := p.root.Open(rel)
	if err != nil {
		return nil, fmt.Errorf("fs: open: %w", err)
	}
	return &file{File: f}, nil
}

func (p *Provider) Stat(ctx context.Context, name string) (contract.FileInfo, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	fi, err := p.root.Stat(rel)
	if err != nil {
		return nil, fmt.Errorf("fs: stat: %w", err)
	}
	return contract.FromOSInfo(fi), nil
}

func (p *Provider) ReadDir(ctx context.Context, name string) ([]contract.FileInfo, error) {
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
	out := make([]contract.FileInfo, 0, len(entries))
	for _, e := range entries {
		fi, err := e.Info()
		if err != nil {
			return nil, err
		}
		out = append(out, contract.FromOSInfo(fi))
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

func (p *Provider) Write(ctx context.Context, name string, data []byte) error {
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

func (p *Provider) Remove(ctx context.Context, name string) error {
	return p.RemoveWithOptions(ctx, name, contract.RemoveOptions{})
}

// RemoveWithOptions implements {Recursive,Force}. Force turns a missing target
// into success; Recursive removes a directory tree through os.Root only.
func (p *Provider) RemoveWithOptions(ctx context.Context, name string, opts contract.RemoveOptions) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	rel, err := p.relative(name)
	if err != nil {
		return err
	}
	if !opts.Recursive {
		err := p.root.Remove(rel)
		if opts.Force && os.IsNotExist(err) {
			return nil
		}
		return err
	}
	// Node semantics: Recursive does NOT imply Force. Probe the path first so
	// a missing target still reports NotExist unless Force was requested.
	if _, err := p.root.Lstat(rel); err != nil {
		if os.IsNotExist(err) {
			if opts.Force {
				return nil
			}
			return contract.NotExist(name)
		}
		return err
	}
	return rootRemoveAll(ctx, p.root, rel)
}

// rootRemoveAll recursively removes name via os.Root primitives available in
// Go 1.24. Symlink entries are removed as links, never followed.
func rootRemoveAll(ctx context.Context, r *os.Root, name string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	fi, err := r.Lstat(name)
	if err != nil {
		if os.IsNotExist(err) {
			return nil // a child may vanish during the walk; top-level Force is checked by the caller
		}
		return err
	}
	if !fi.IsDir() {
		return r.Remove(name)
	}
	d, err := r.Open(name)
	if err != nil {
		return err
	}
	entries, err := d.ReadDir(-1)
	_ = d.Close()
	if err != nil {
		return err
	}
	for _, e := range entries {
		if err := ctx.Err(); err != nil {
			return err
		}
		child := filepath.Join(name, e.Name())
		cfi, err := r.Lstat(child)
		if err != nil {
			return err
		}
		if cfi.IsDir() {
			if err := rootRemoveAll(ctx, r, child); err != nil {
				return err
			}
			continue
		}
		if err := r.Remove(child); err != nil {
			return err
		}
	}
	return r.Remove(name)
}

// limitedReadCloser couples a bounded reader with the handle it reads from.
type limitedReadCloser struct {
	io.Reader
	io.Closer
}

func (p *Provider) OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error) {
	if err := contract.CheckRange(offset, limit); err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	f, err := p.root.OpenFile(rel, os.O_RDONLY, 0)
	if err != nil {
		return nil, fmt.Errorf("fs: open stream: %w", err)
	}
	if offset > 0 {
		if _, err := f.Seek(offset, io.SeekStart); err != nil {
			_ = f.Close()
			return nil, fmt.Errorf("fs: seek: %w", err)
		}
	}
	var r io.Reader = f
	if limit >= 0 {
		r = io.LimitReader(f, limit)
	}
	return &limitedReadCloser{Reader: r, Closer: f}, nil
}

func (p *Provider) Lstat(ctx context.Context, name string) (contract.FileInfo, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	fi, err := p.root.Lstat(rel)
	if err != nil {
		return nil, fmt.Errorf("fs: lstat: %w", err)
	}
	out := contract.NewInfo(fi.Name(), fi.Size(), fi.IsDir(), fi.ModTime())
	if fi.Mode()&os.ModeSymlink != 0 {
		// Root.Readlink postdates this module's go directive. Root.Lstat has
		// already confined every path component, and readlink only reads the
		// link text (it does not follow the final link), so no outside file is
		// touched by this metadata call.
		target, err := os.Readlink(filepath.Join(p.rootPath, rel))
		if err != nil {
			return nil, fmt.Errorf("fs: readlink: %w", err)
		}
		out = out.AsSymlink(target)
	}
	return out, nil
}

func (p *Provider) Mkdir(ctx context.Context, name string) error {
	return p.MkdirWithOptions(ctx, name, contract.MkdirOptions{})
}

func (p *Provider) MkdirWithOptions(ctx context.Context, name string, opts contract.MkdirOptions) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	rel, err := p.relative(name)
	if err != nil {
		return err
	}
	if opts.Recursive {
		return rootMkdirAll(p.root, rel, 0o755)
	}
	return p.root.Mkdir(rel, 0o755)
}

// Symlink accepts only targets that resolve to an absolute path inside the
// root; both names are confined through os.Root first. os.Root deliberately
// rejects absolute symlink *text* (it would escape when followed), so the link
// stores a target relative to the link's own directory. That keeps every
// follow confined to the root while still honoring an absolute target argument.
func (p *Provider) Symlink(ctx context.Context, target, link string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	targetRel, err := p.relative(target)
	if err != nil {
		return fmt.Errorf("fs: symlink target escapes root: %w", err)
	}
	linkRel, err := p.relative(link)
	if err != nil {
		return err
	}
	if targetRel == "." || linkRel == "." {
		return fmt.Errorf("fs: symlink needs a non-root name")
	}
	stored, err := filepath.Rel(filepath.Dir(linkRel), targetRel)
	if err != nil || stored == ".." || strings.HasPrefix(stored, ".."+string(filepath.Separator)) {
		return fmt.Errorf("fs: symlink target escapes root: %q", target)
	}
	// The parent must be a real directory *inside* the root. Root.Open follows
	// intermediate symlinks and refuses any path that escapes, so a parent that
	// is itself a symlink to an outside directory is rejected here instead of
	// letting the raw symlink call escape.
	parent := filepath.Dir(linkRel)
	d, err := p.root.Open(parent)
	if err != nil {
		return fmt.Errorf("fs: symlink parent: %w", err)
	}
	fi, statErr := d.Stat()
	_ = d.Close()
	if statErr != nil {
		return fmt.Errorf("fs: symlink parent: %w", statErr)
	}
	if !fi.IsDir() {
		return fmt.Errorf("fs: symlink parent %q is not a directory", parent)
	}
	return p.symlink(stored, linkRel)
}

// Readlink returns the stored link text. The name is confined with Root.Lstat
// before os.Readlink reads the link itself (which never follows it).
func (p *Provider) Readlink(ctx context.Context, name string) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	rel, err := p.relative(name)
	if err != nil {
		return "", err
	}
	fi, err := p.root.Lstat(rel)
	if err != nil {
		return "", fmt.Errorf("fs: readlink: %w", err)
	}
	if fi.Mode()&os.ModeSymlink == 0 {
		return "", fmt.Errorf("fs: readlink: %q is not a symlink", name)
	}
	target, err := os.Readlink(filepath.Join(p.rootPath, rel))
	if err != nil {
		return "", fmt.Errorf("fs: readlink: %w", err)
	}
	return target, nil
}

func (p *Provider) Rename(ctx context.Context, old, new string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	a, err := p.relative(old)
	if err != nil {
		return err
	}
	b, err := p.relative(new)
	if err != nil {
		return err
	}
	return p.rename(ctx, a, b)
}

func (p *Provider) Copy(ctx context.Context, src, dst string) error {
	return p.CopyWithOptions(ctx, src, dst, contract.CopyOptions{})
}

// CopyWithOptions implements {Recursive,Overwrite}. Non-recursive copies a
// single file; Recursive walks a directory tree through os.Root. Overwrite=false
// rejects an existing destination, Overwrite=true truncates and replaces it.
func (p *Provider) CopyWithOptions(ctx context.Context, src, dst string, opts contract.CopyOptions) error {
	a, err := p.relative(src)
	if err != nil {
		return err
	}
	b, err := p.relative(dst)
	if err != nil {
		return err
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	if !opts.Recursive {
		return rootCopyFile(ctx, p.root, a, b, opts.Overwrite)
	}
	return rootCopyTree(ctx, p.root, a, b, opts.Overwrite)
}

func rootCopyFile(ctx context.Context, r *os.Root, a, b string, overwrite bool) error {
	if !overwrite {
		if _, err := r.Lstat(b); err == nil {
			return fmt.Errorf("fs: copy destination exists: %q", b)
		} else if !os.IsNotExist(err) {
			return err
		}
	}
	if dir := filepath.Dir(b); dir != "." {
		if err := rootMkdirAll(r, dir, 0o755); err != nil {
			return err
		}
	}
	in, err := r.OpenFile(a, os.O_RDONLY, 0)
	if err != nil {
		return fmt.Errorf("fs: copy source: %w", err)
	}
	defer in.Close()
	out, err := r.OpenFile(b, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0o644)
	if err != nil {
		return fmt.Errorf("fs: copy destination: %w", err)
	}
	copyErr := contract.CopyCtx(ctx, out, in)
	closeErr := out.Close()
	if copyErr != nil {
		return copyErr
	}
	return closeErr
}

func rootCopyTree(ctx context.Context, r *os.Root, a, b string, overwrite bool) error {
	fi, err := r.Lstat(a)
	if err != nil {
		return fmt.Errorf("fs: copy source: %w", err)
	}
	if !fi.IsDir() {
		return rootCopyFile(ctx, r, a, b, overwrite)
	}
	if _, err := r.Lstat(b); err == nil {
		bfi, err := r.Stat(b)
		if err != nil || !bfi.IsDir() {
			return fmt.Errorf("fs: copy destination %q exists and is not a directory", b)
		}
	} else if !os.IsNotExist(err) {
		return err
	}
	if err := rootMkdirAll(r, b, 0o755); err != nil {
		return err
	}
	d, err := r.Open(a)
	if err != nil {
		return err
	}
	entries, err := d.ReadDir(-1)
	_ = d.Close()
	if err != nil {
		return err
	}
	for _, e := range entries {
		if err := ctx.Err(); err != nil {
			return err
		}
		if err := rootCopyTree(ctx, r, filepath.Join(a, e.Name()), filepath.Join(b, e.Name()), overwrite); err != nil {
			return err
		}
	}
	return nil
}

func (p *Provider) Access(ctx context.Context, name string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	rel, err := p.relative(name)
	if err != nil {
		return err
	}
	f, err := p.root.OpenFile(rel, os.O_RDONLY, 0)
	if err != nil {
		return fmt.Errorf("fs: access: %w", err)
	}
	return f.Close()
}

// ---------------------------------------------------------------------------
// fsnotify watcher adapter
// ---------------------------------------------------------------------------

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

// watcher implements Watcher over one fsnotify watcher. fsnotify has no
// recursion: existing subdirectories are registered by an initial WalkDir and
// new ones are registered when their Create event arrives. Events for any
// descendant of the requested path are reported; cb, when non-nil, receives
// them instead of the Events channel (the legacy callback Watch API).
type watcher struct {
	watcher  *fsnotify.Watcher
	ctx      context.Context
	name     string
	prefix   string
	rootPath string
	filter   string
	cb       func(contract.WatchEvent) error
	events   chan contract.WatchEvent
	errors   chan error
	ready    chan struct{}
	stop     chan struct{}
	done     chan struct{}
	once     sync.Once
}

func (w *watcher) Events() <-chan contract.WatchEvent { return w.events }
func (w *watcher) Errors() <-chan error               { return w.errors }
func (w *watcher) Ready() <-chan struct{}             { return w.ready }

// Dispose is idempotent and joins the watcher goroutine: after it returns no
// callback or channel send can still be in flight.
func (w *watcher) Dispose() error {
	w.once.Do(func() { close(w.stop) })
	<-w.done
	return nil
}

func (w *watcher) sendErr(err error) {
	select {
	case w.errors <- err:
	default: // errors are advisory: never stall the event loop
	}
}

// eventPath keeps the public path in the same namespace as the watched name.
func (w *watcher) eventPath(p, rel string) string {
	if p == w.prefix {
		return w.name
	}
	if strings.HasPrefix(w.name, "file://") {
		return "file://" + filepath.ToSlash(p)
	}
	if filepath.IsAbs(w.name) {
		return p
	}
	return filepath.Join(w.name, rel)
}

// insideRoot reports whether p is contained in rootPath. It is used to keep
// fsnotify's recursive walk and dynamic subdirectory registration inside the
// provider root even if a symlink appears mid-watch.
func insideRoot(rootPath, p string) bool {
	rel, err := filepath.Rel(rootPath, p)
	if err != nil {
		return false
	}
	return rel != ".." && !strings.HasPrefix(rel, ".."+string(filepath.Separator)) && !filepath.IsAbs(rel)
}

// confinedWatchDir validates the watch target through os.Root and returns the
// resolved host directory for fsnotify. Root.Open follows intermediate symlinks
// and refuses paths that escape, so a watched directory reached via a symlink
// to outside the root is rejected before any raw watcher is created.
func (p *Provider) confinedWatchDir(ctx context.Context, rel string) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	dirRel := rel
	fi, err := p.root.Stat(rel)
	if err != nil {
		if !errors.Is(err, os.ErrNotExist) {
			return "", fmt.Errorf("fs: watch target: %w", err)
		}
		dirRel = filepath.Dir(rel) // file not created yet: watch its parent
	} else if !fi.IsDir() {
		dirRel = filepath.Dir(rel)
	}
	if dirRel == "" {
		dirRel = "."
	}
	d, err := p.root.Open(dirRel)
	if err != nil {
		return "", fmt.Errorf("fs: watch directory: %w", err)
	}
	dfi, statErr := d.Stat()
	_ = d.Close()
	if statErr != nil {
		return "", fmt.Errorf("fs: watch directory: %w", statErr)
	}
	if !dfi.IsDir() {
		return "", fmt.Errorf("fs: watch directory %q is not a directory", dirRel)
	}
	rp := filepath.Join(p.rootPath, dirRel)
	resolved, err := filepath.EvalSymlinks(rp)
	if err != nil {
		return "", fmt.Errorf("fs: watch directory: %w", err)
	}
	if !insideRoot(p.rootPath, resolved) {
		return "", fmt.Errorf("fs: watch directory %q escapes root", dirRel)
	}
	return resolved, nil
}

func newWatcher(ctx context.Context, name, rp, filter string, cb func(contract.WatchEvent) error, rootPath string) (*watcher, error) {
	// If the target is not an existing directory (a file, or not created yet),
	// watch its parent so creation/removal of the target is still observed.
	watchDir := rp
	if fi, err := os.Stat(rp); err != nil || !fi.IsDir() {
		watchDir = filepath.Dir(rp)
	}
	if !insideRoot(rootPath, watchDir) {
		return nil, fmt.Errorf("fs: watch directory %q escapes root", watchDir)
	}
	fsw, err := fsnotify.NewWatcher()
	if err != nil {
		return nil, err
	}
	w := &watcher{
		watcher: fsw, ctx: ctx, name: name, prefix: filepath.Clean(rp),
		filter: filter, cb: cb, rootPath: rootPath,
		events: make(chan contract.WatchEvent, 64), errors: make(chan error, 16),
		ready: make(chan struct{}), stop: make(chan struct{}), done: make(chan struct{}),
	}
	if err := fsw.Add(watchDir); err != nil {
		_ = fsw.Close()
		return nil, err
	}
	// WalkDir does not follow symlinks, so an attacker cannot make us register
	// a directory outside the root via a symlinked subdirectory.
	if err := filepath.WalkDir(watchDir, func(p string, d iofs.DirEntry, err error) error {
		if err != nil {
			w.sendErr(err)
			return nil
		}
		if !d.IsDir() || p == watchDir {
			return nil
		}
		if !insideRoot(rootPath, p) {
			return nil // never register a directory outside the root
		}
		if err := fsw.Add(p); err != nil {
			w.sendErr(err)
		}
		return nil
	}); err != nil {
		_ = fsw.Close()
		return nil, err
	}
	close(w.ready) // ready means: all initial directories are registered
	go w.run()
	return w, nil
}

func (w *watcher) run() {
	defer close(w.done)
	defer w.watcher.Close()
	defer close(w.events)
	defer close(w.errors)
	for {
		select {
		case <-w.stop:
			return
		case <-w.ctx.Done():
			return
		case ev, ok := <-w.watcher.Events:
			if !ok {
				return
			}
			if ev.Op.Has(fsnotify.Create) && insideRoot(w.rootPath, ev.Name) {
				if fi, err := os.Stat(ev.Name); err == nil && fi.IsDir() {
					if err := w.watcher.Add(ev.Name); err != nil {
						w.sendErr(err)
					}
				}
			}
			p := filepath.Clean(ev.Name)
			rel, err := filepath.Rel(w.prefix, p)
			if err != nil || (rel == ".." || strings.HasPrefix(rel, ".."+string(filepath.Separator)) || filepath.IsAbs(rel)) {
				continue // not the target or one of its descendants
			}
			ep := w.eventPath(p, rel)
			if !contract.MatchFilter(w.filter, contract.WatchRelative(w.name, ep)) {
				continue
			}
			out := contract.WatchEvent{Path: ep, Op: mapOp(ev.Op)}
			if w.cb != nil {
				_ = w.cb(out) // invoked outside any provider/service lock
			} else {
				select {
				case w.events <- out:
				case <-w.stop:
					return
				case <-w.ctx.Done():
					return
				}
			}
		case err, ok := <-w.watcher.Errors:
			if !ok {
				return
			}
			w.sendErr(err) // errors reach the caller via Watcher.Errors
		}
	}
}

func (p *Provider) Watch(ctx context.Context, name string, cb func(contract.WatchEvent) error) (func() error, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	rp, err := p.confinedWatchDir(ctx, rel)
	if err != nil {
		return nil, err
	}
	w, err := newWatcher(ctx, name, rp, "", cb, p.rootPath)
	if err != nil {
		return nil, err
	}
	return func() error { return w.Dispose() }, nil
}

// WatchStream exposes the full Watcher contract for a directory and all of its
// descendants. filter is a doublestar glob relative to the watched path.
func (p *Provider) WatchStream(ctx context.Context, name, filter string) (contract.Watcher, error) {
	if err := contract.ValidateFilter(filter); err != nil {
		return nil, err
	}
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	rp, err := p.confinedWatchDir(ctx, rel)
	if err != nil {
		return nil, err
	}
	return newWatcher(ctx, name, rp, filter, nil, p.rootPath)
}
