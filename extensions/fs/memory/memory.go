// Package memory is the in-memory provider for the gocordis FS Service. Its
// namespace is a mutex-protected map; all names are normalized through memKey
// so no foreign scheme or parent traversal can escape the root.
package memory

import (
	"bytes"
	"context"
	"errors"
	"fmt"
	"io"
	"net/url"
	"os"
	"path"
	"sort"
	"strings"
	"sync"
	"time"

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

type memLink struct {
	target string
	mod    time.Time
}

type memWatch struct {
	key string
	cb  func(contract.WatchEvent) error
}

// Provider is an in-memory filesystem reachable as memory://....
type Provider struct {
	mu      sync.RWMutex
	files   map[string]memEntry
	dirs    map[string]struct{}
	links   map[string]memLink
	watches map[*memWatch]struct{}
}

// NewProvider returns an in-memory filesystem reachable as memory://....
func NewProvider() *Provider {
	return &Provider{
		files:   make(map[string]memEntry),
		dirs:    make(map[string]struct{}),
		links:   make(map[string]memLink),
		watches: make(map[*memWatch]struct{}),
	}
}

// New is a convenience alias returning the structural Provider interface.
func New() contract.Provider { return NewProvider() }

func (p *Provider) Scheme() string { return "memory" }

// Close is a no-op: the memory provider holds no operating-system resources.
func (p *Provider) Close() error { return nil }

// WatchStream reuses the generic callback watch and adapts it to Watcher.
func (p *Provider) WatchStream(ctx context.Context, name, filter string) (contract.Watcher, error) {
	return contract.NewCallbackWatcher(ctx, p, name, filter)
}

func (p *Provider) matchesLocked(key string) []*memWatch {
	var out []*memWatch
	for w := range p.watches {
		if w.key == key || strings.HasPrefix(key, w.key+"/") {
			out = append(out, w)
		}
	}
	return out
}

func (p *Provider) notify(watches []*memWatch, ev contract.WatchEvent) {
	for _, w := range watches {
		_ = w.cb(ev) // callbacks run outside the provider lock
	}
}

// resolveLocked follows symlink entries to their target key. A link cycle or
// foreign target resolves to the zero key (not found).
func (p *Provider) resolveLocked(key string) string {
	seen := make(map[string]struct{})
	for i := 0; i < 16; i++ {
		if _, ok := seen[key]; ok {
			return ""
		}
		seen[key] = struct{}{}
		l, ok := p.links[key]
		if !ok {
			return key
		}
		key = memKey(l.target)
		if key == "" {
			return ""
		}
	}
	return ""
}

type file struct {
	*bytes.Reader
	info contract.FileInfo
}

func (f *file) Close() error                     { return nil }
func (f *file) Stat() (contract.FileInfo, error) { return f.info, nil }

func (p *Provider) Open(ctx context.Context, name string) (contract.File, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	k := memKey(name)
	p.mu.RLock()
	resolved := p.resolveLocked(k)
	e, ok := p.files[resolved]
	p.mu.RUnlock()
	if !ok || resolved == "" {
		return nil, contract.NotExist(name)
	}
	info := contract.NewInfo(path.Base(resolved), int64(len(e.data)), false, e.mod)
	return &file{Reader: bytes.NewReader(e.data), info: info}, nil
}

func (p *Provider) Stat(ctx context.Context, name string) (contract.FileInfo, error) {
	k := memKey(name)
	p.mu.RLock()
	defer p.mu.RUnlock()
	resolved := p.resolveLocked(k)
	if e, ok := p.files[resolved]; ok && resolved != "" {
		return contract.NewInfo(path.Base(resolved), int64(len(e.data)), false, e.mod), nil
	}
	if _, ok := p.dirs[resolved]; ok && resolved != "" {
		return contract.NewInfo(path.Base(resolved), 0, true, time.Unix(0, 0)), nil
	}
	if resolved == "" { // root of the namespace
		return contract.NewInfo("", 0, true, time.Unix(0, 0)), nil
	}
	return nil, contract.NotExist(name)
}

func (p *Provider) ReadDir(ctx context.Context, name string) ([]contract.FileInfo, error) {
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
	set := make(map[string]contract.FileInfo)
	for f, e := range p.files {
		if !strings.HasPrefix(f, prefix) {
			continue
		}
		found = true
		rest := strings.TrimPrefix(f, prefix)
		if i := strings.Index(rest, "/"); i >= 0 {
			d := rest[:i]
			set[d] = contract.NewInfo(d, 0, true, time.Unix(0, 0))
		} else if rest != "" {
			set[rest] = contract.NewInfo(rest, int64(len(e.data)), false, e.mod)
		}
	}
	for d := range p.dirs {
		if !strings.HasPrefix(d, prefix) {
			continue
		}
		rest := strings.TrimPrefix(d, prefix)
		if i := strings.Index(rest, "/"); i < 0 && rest != "" {
			set[rest] = contract.NewInfo(rest, 0, true, time.Unix(0, 0))
		}
	}
	for l, lk := range p.links {
		if !strings.HasPrefix(l, prefix) {
			continue
		}
		rest := strings.TrimPrefix(l, prefix)
		if i := strings.Index(rest, "/"); i < 0 && rest != "" {
			found = true
			set[rest] = contract.NewInfo(rest, 0, false, lk.mod).AsSymlink(lk.target)
		}
	}
	if !found {
		return nil, contract.NotExist(name)
	}
	names := make([]string, 0, len(set))
	for n := range set {
		names = append(names, n)
	}
	sort.Strings(names)
	out := make([]contract.FileInfo, 0, len(names))
	for _, n := range names {
		out = append(out, set[n])
	}
	return out, nil
}

func (p *Provider) Write(ctx context.Context, name string, data []byte) error {
	k := memKey(name)
	if k == "" {
		return errors.New("fs: empty memory key")
	}
	if err := ctx.Err(); err != nil {
		return err
	}
	p.mu.Lock()
	_, existed := p.files[k]
	delete(p.links, k) // writing replaces a symlink with a regular file
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
	p.notify(watches, contract.WatchEvent{Path: name, Op: op})
	return nil
}

func (p *Provider) Remove(ctx context.Context, name string) error {
	return p.RemoveWithOptions(ctx, name, contract.RemoveOptions{})
}

// RemoveWithOptions implements {Recursive,Force} over the mutex-protected map.
func (p *Provider) RemoveWithOptions(ctx context.Context, name string, opts contract.RemoveOptions) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	k := memKey(name)
	if k == "" {
		return errors.New("fs: empty memory key")
	}
	p.mu.Lock()
	_, isFile := p.files[k]
	_, isDir := p.dirs[k]
	_, isLink := p.links[k]
	if !isFile && !isDir && !isLink {
		p.mu.Unlock()
		if opts.Force {
			return nil
		}
		return contract.NotExist(name)
	}
	if isDir && !opts.Recursive && p.dirNonEmptyLocked(k) {
		p.mu.Unlock()
		return fmt.Errorf("fs: remove %q: directory not empty", name)
	}
	delete(p.files, k)
	delete(p.dirs, k)
	delete(p.links, k)
	if isDir && opts.Recursive {
		prefix := k + "/"
		for f := range p.files {
			if strings.HasPrefix(f, prefix) {
				delete(p.files, f)
			}
		}
		for d := range p.dirs {
			if strings.HasPrefix(d, prefix) {
				delete(p.dirs, d)
			}
		}
		for l := range p.links {
			if strings.HasPrefix(l, prefix) {
				delete(p.links, l)
			}
		}
	}
	watches := p.matchesLocked(k)
	p.mu.Unlock()
	p.notify(watches, contract.WatchEvent{Path: name, Op: "remove"})
	return nil
}

func (p *Provider) dirNonEmptyLocked(k string) bool {
	prefix := k + "/"
	for f := range p.files {
		if strings.HasPrefix(f, prefix) {
			return true
		}
	}
	for d := range p.dirs {
		if strings.HasPrefix(d, prefix) {
			return true
		}
	}
	for l := range p.links {
		if strings.HasPrefix(l, prefix) {
			return true
		}
	}
	return false
}

func (p *Provider) OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error) {
	if err := contract.CheckRange(offset, limit); err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	k := memKey(name)
	if k == "" {
		return nil, contract.NotExist(name)
	}
	p.mu.RLock()
	resolved := p.resolveLocked(k)
	e, ok := p.files[resolved]
	p.mu.RUnlock() // entries are immutable: writers replace the slice, never mutate it
	if !ok || resolved == "" {
		return nil, contract.NotExist(name)
	}
	r := bytes.NewReader(e.data)
	if offset > 0 {
		if _, err := r.Seek(offset, io.SeekStart); err != nil {
			return nil, err
		}
	}
	var reader io.Reader = r
	if limit >= 0 {
		reader = io.LimitReader(r, limit)
	}
	return io.NopCloser(reader), nil
}

// Lstat reports raw link metadata: a symlink entry is IsSymlink=true with its
// stored target; ordinary files and directories report false/"".
func (p *Provider) Lstat(ctx context.Context, name string) (contract.FileInfo, error) {
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	k := memKey(name)
	if k == "" {
		return nil, contract.NotExist(name)
	}
	p.mu.RLock()
	defer p.mu.RUnlock()
	if l, ok := p.links[k]; ok {
		return contract.NewInfo(path.Base(k), 0, false, l.mod).AsSymlink(l.target), nil
	}
	if e, ok := p.files[k]; ok {
		return contract.NewInfo(path.Base(k), int64(len(e.data)), false, e.mod), nil
	}
	if _, ok := p.dirs[k]; ok {
		return contract.NewInfo(path.Base(k), 0, true, time.Unix(0, 0)), nil
	}
	if k == "" {
		return contract.NewInfo("", 0, true, time.Unix(0, 0)), nil
	}
	return nil, contract.NotExist(name)
}

func (p *Provider) Mkdir(ctx context.Context, name string) error {
	return p.MkdirWithOptions(ctx, name, contract.MkdirOptions{})
}

func (p *Provider) MkdirWithOptions(ctx context.Context, name string, opts contract.MkdirOptions) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	k := memKey(name)
	if k == "" {
		return errors.New("fs: empty memory key")
	}
	p.mu.Lock()
	_, isDir := p.dirs[k]
	_, isFile := p.files[k]
	_, isLink := p.links[k]
	if isDir || isFile || isLink {
		p.mu.Unlock()
		return fmt.Errorf("fs: mkdir %q: %w", name, os.ErrExist)
	}
	if !opts.Recursive {
		parent := memParent(k)
		if parent != "" {
			if _, ok := p.dirs[parent]; !ok {
				p.mu.Unlock()
				return fmt.Errorf("fs: mkdir %q: parent does not exist", name)
			}
		}
		p.dirs[k] = struct{}{}
	} else {
		for d := k; d != ""; d = memParent(d) {
			p.dirs[d] = struct{}{}
		}
	}
	watches := p.matchesLocked(k)
	p.mu.Unlock()
	p.notify(watches, contract.WatchEvent{Path: name, Op: "create"})
	return nil
}

// Symlink stores a link entry with metadata. Both names are normalized; a
// foreign-scheme target yields an empty key and is rejected as an escape.
func (p *Provider) Symlink(ctx context.Context, target, link string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	k, tk := memKey(link), memKey(target)
	if k == "" || tk == "" {
		return fmt.Errorf("fs: symlink escapes memory namespace: target=%q link=%q", target, link)
	}
	p.mu.Lock()
	if _, exists := p.files[k]; exists {
		p.mu.Unlock()
		return fmt.Errorf("fs: symlink %q: %w", link, os.ErrExist)
	}
	if _, exists := p.dirs[k]; exists {
		p.mu.Unlock()
		return fmt.Errorf("fs: symlink %q: %w", link, os.ErrExist)
	}
	if _, exists := p.links[k]; exists {
		p.mu.Unlock()
		return fmt.Errorf("fs: symlink %q: %w", link, os.ErrExist)
	}
	p.links[k] = memLink{target: target, mod: time.Now()}
	for d := memParent(k); d != ""; d = memParent(d) {
		p.dirs[d] = struct{}{}
	}
	watches := p.matchesLocked(k)
	p.mu.Unlock()
	p.notify(watches, contract.WatchEvent{Path: link, Op: "create"})
	return nil
}

func (p *Provider) Readlink(ctx context.Context, name string) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	k := memKey(name)
	if k == "" {
		return "", contract.NotExist(name)
	}
	p.mu.RLock()
	l, ok := p.links[k]
	p.mu.RUnlock()
	if !ok {
		return "", contract.NotExist(name)
	}
	return l.target, nil
}

func (p *Provider) Rename(ctx context.Context, old, new string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	a, b := memKey(old), memKey(new)
	if a == "" || b == "" {
		return errors.New("fs: empty memory key")
	}
	p.mu.Lock()
	if a == b {
		p.mu.Unlock()
		return nil
	}
	e, isFile := p.files[a]
	_, isDir := p.dirs[a]
	l, isLink := p.links[a]
	if !isFile && !isDir && !isLink {
		p.mu.Unlock()
		return contract.NotExist(old)
	}
	if p.existsLocked(b) {
		p.mu.Unlock()
		return fmt.Errorf("fs: rename target %q exists", new)
	}
	if isDir && strings.HasPrefix(b, a+"/") {
		p.mu.Unlock()
		return fmt.Errorf("fs: cannot rename directory %q into itself", old)
	}
	if isFile {
		delete(p.files, a)
		p.files[b] = e
	}
	if isDir {
		delete(p.dirs, a)
		p.dirs[b] = struct{}{}
		prefix := a + "/"
		for k, v := range p.files {
			if strings.HasPrefix(k, prefix) {
				p.files[b+"/"+strings.TrimPrefix(k, prefix)] = v
				delete(p.files, k)
			}
		}
		for d := range p.dirs {
			if strings.HasPrefix(d, prefix) {
				p.dirs[b+"/"+strings.TrimPrefix(d, prefix)] = struct{}{}
				delete(p.dirs, d)
			}
		}
		for lk, lv := range p.links {
			if strings.HasPrefix(lk, prefix) {
				p.links[b+"/"+strings.TrimPrefix(lk, prefix)] = lv
				delete(p.links, lk)
			}
		}
	}
	if isLink {
		delete(p.links, a)
		p.links[b] = l
	}
	for d := memParent(b); d != ""; d = memParent(d) {
		p.dirs[d] = struct{}{}
	}
	watches := append(p.matchesLocked(a), p.matchesLocked(b)...)
	p.mu.Unlock()
	p.notify(watches, contract.WatchEvent{Path: new, Op: "rename"})
	return nil
}

func (p *Provider) existsLocked(k string) bool {
	if _, ok := p.files[k]; ok {
		return true
	}
	if _, ok := p.dirs[k]; ok {
		return true
	}
	if _, ok := p.links[k]; ok {
		return true
	}
	return false
}

func (p *Provider) Copy(ctx context.Context, src, dst string) error {
	return p.CopyWithOptions(ctx, src, dst, contract.CopyOptions{})
}

// CopyWithOptions implements {Recursive,Overwrite} over the map. Source links
// are followed for reading; Recursive copies whole subtrees independently.
func (p *Provider) CopyWithOptions(ctx context.Context, src, dst string, opts contract.CopyOptions) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	a, b := memKey(src), memKey(dst)
	if a == "" || b == "" {
		return errors.New("fs: empty memory key")
	}
	if a == b {
		return fmt.Errorf("fs: copy %q onto itself", src)
	}
	p.mu.Lock()
	resolved := p.resolveLocked(a)
	e, isFile := p.files[resolved]
	_, isDir := p.dirs[resolved]
	if !isFile && !isDir {
		p.mu.Unlock()
		return contract.NotExist(src)
	}
	if p.existsLocked(b) && !opts.Overwrite {
		p.mu.Unlock()
		return fmt.Errorf("fs: copy destination %q exists", dst)
	}
	if isDir && !opts.Recursive {
		p.mu.Unlock()
		return fmt.Errorf("fs: copy: %q is a directory", src)
	}
	// Snapshot the source subtree before mutating the map.
	type srcFile struct {
		key  string
		data []byte
		mod  time.Time
	}
	var files []srcFile
	var dirs []string
	if !isDir {
		files = append(files, srcFile{key: b, data: append([]byte(nil), e.data...), mod: time.Now()})
	} else {
		prefix := resolved + "/"
		for k, v := range p.files {
			if strings.HasPrefix(k, prefix) {
				files = append(files, srcFile{
					key:  b + "/" + strings.TrimPrefix(k, prefix),
					data: append([]byte(nil), v.data...), mod: time.Now(),
				})
			}
		}
		for d := range p.dirs {
			if strings.HasPrefix(d, prefix) {
				dirs = append(dirs, b+"/"+strings.TrimPrefix(d, prefix))
			}
		}
	}
	// Replace an overwritten destination.
	delete(p.files, b)
	delete(p.dirs, b)
	delete(p.links, b)
	for _, f := range files {
		p.files[f.key] = memEntry{data: f.data, mod: f.mod}
	}
	for _, d := range dirs {
		p.dirs[d] = struct{}{}
	}
	if isDir {
		p.dirs[b] = struct{}{}
	}
	for d := memParent(b); d != ""; d = memParent(d) {
		p.dirs[d] = struct{}{}
	}
	watches := p.matchesLocked(b)
	p.mu.Unlock()
	p.notify(watches, contract.WatchEvent{Path: dst, Op: "create"})
	return nil
}

func (p *Provider) Access(ctx context.Context, name string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	k := memKey(name)
	if k == "" {
		return contract.NotExist(name)
	}
	p.mu.RLock()
	defer p.mu.RUnlock()
	resolved := p.resolveLocked(k)
	if resolved == "" {
		return contract.NotExist(name)
	}
	if _, ok := p.files[resolved]; ok {
		return nil
	}
	if _, ok := p.dirs[resolved]; ok {
		return nil
	}
	return contract.NotExist(name)
}

func (p *Provider) Watch(ctx context.Context, name string, cb func(contract.WatchEvent) error) (func() error, error) {
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
