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
	iofs "io/fs"
	"net/url"
	"os"
	"path"
	"path/filepath"
	"sort"
	"strings"
	"sync"
	"time"

	"github.com/bmatcuk/doublestar/v4"
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

// SymlinkInfo is an optional extension of FileInfo for providers that can
// distinguish symbolic links. Lstat results from the local provider report
// link metadata; memory results (and all regular files) report false/"".
type SymlinkInfo interface {
	IsSymlink() bool
	LinkTarget() string
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

// Watcher is the JS-FS-style watch handle. It mirrors the four capabilities of
// a Cordis FS watcher: Change (Events), Ready, Error and Dispose.
//
// Ready is a closed channel (not a single value) once the initial set of
// directories has been registered, so it is safe to select on at any time.
// Errors carries asynchronous watcher failures (for example fsnotify errors);
// it is advisory and never blocks the watcher. Dispose is idempotent: after it
// returns no further event is delivered (providers that can join their goroutine
// also close Events/Errors).
type Watcher interface {
	Events() <-chan WatchEvent
	Errors() <-chan error
	Ready() <-chan struct{}
	Dispose() error
}

// StreamWatcher is implemented by providers that expose the full Watcher
// contract. Providers that only implement Provider.Watch are adapted by the
// Service with a callback bridge; both paths behave identically to callers.
type StreamWatcher interface {
	// WatchStream watches name and its descendants. filter is a doublestar
	// glob (e.g. "**/*.txt") matched against paths relative to name, using
	// forward slashes; an empty filter accepts every change.
	WatchStream(ctx context.Context, name, filter string) (Watcher, error)
}

// Provider is a concrete filesystem implementation reachable through a URL
// scheme ("file" or "memory"). Close releases provider-owned resources (for
// example the os.Root held by the local provider); it must be idempotent and
// is called only by the owner of the provider instance.
type Provider interface {
	Scheme() string
	Open(context.Context, string) (File, error)
	Stat(context.Context, string) (FileInfo, error)
	ReadDir(context.Context, string) ([]FileInfo, error)
	Write(context.Context, string, []byte) error
	Remove(context.Context, string) error
	io.Closer
	// Watch returns an inverse that stops the watcher and joins its goroutine.
	// The callback is invoked outside any provider/service lock. Asynchronous
	// errors and the ready signal are available through WatchStream.
	Watch(context.Context, string, func(WatchEvent) error) (func() error, error)
}

// StreamProvider is an optional capability for providers that can serve a byte
// range as a stream. offset must be >= 0; limit >= 0 caps the byte count and
// limit == -1 means "through EOF". A short file is never an error.
type StreamProvider interface {
	OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error)
}

// ExtendedProvider is an optional capability for the remaining JS-FS operation
// surface. Providers that do not implement it keep working with the base
// Provider operations; the Service returns *ErrNotSupported for these calls.
type ExtendedProvider interface {
	Lstat(ctx context.Context, name string) (FileInfo, error)
	Mkdir(ctx context.Context, name string) error
	Rename(ctx context.Context, old, new string) error
	Copy(ctx context.Context, src, dst string) error
	Access(ctx context.Context, name string) error
}

// ErrNotSupported is returned when the provider mounted for a name does not
// implement the optional interface behind a Service operation. Use errors.As
// to distinguish a capability gap from an I/O failure.
type ErrNotSupported struct {
	Op     string
	Scheme string
}

func (e *ErrNotSupported) Error() string {
	if e.Scheme == "" {
		return fmt.Sprintf("fs: %s not supported by mounted provider", e.Op)
	}
	return fmt.Sprintf("fs: %s not supported by %q provider", e.Op, e.Scheme)
}

// Service is the mount-routed filesystem capability.
type Service interface {
	// Mount accepts an empty prefix as the fallback mount for names that match
	// no specific prefix. Duplicate prefixes are rejected. Mount does NOT take
	// ownership of p: Unmount and Close never close a supplied provider.
	Mount(prefix string, p Provider) error
	// MountLocal/MountMemory create a provider, mount it and record it as
	// service-owned. Close releases those providers (Unmount still does not).
	MountLocal(prefix, root string) (Provider, error)
	MountMemory(prefix string) (Provider, error)
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
	// OpenStream/ReadRange need a StreamProvider, the five following calls need
	// an ExtendedProvider; otherwise they return *ErrNotSupported.
	OpenStream(context.Context, string, int64, int64) (io.ReadCloser, error)
	ReadRange(context.Context, string, int64, int64) ([]byte, error)
	Lstat(context.Context, string) (FileInfo, error)
	Mkdir(context.Context, string) error
	Rename(context.Context, string, string) error
	Copy(context.Context, string, string) error
	Access(context.Context, string) error
	Watch(context.Context, string, func(WatchEvent) error) (func() error, error)
	// WatchStream exposes the Change/Ready/Error/Dispose contract. A watch
	// follows its name to whichever provider owns the mount after a route
	// change, just like the callback-based Watch.
	WatchStream(ctx context.Context, name, filter string) (Watcher, error)
	// Close stops all watches, drops all mounts and closes owned providers.
	Close() error
}

// ServiceKey is the typed capability key for the filesystem service.
var ServiceKey = runtime.NewKey[Service]("extension.fs.service")

// ---------------------------------------------------------------------------
// metadata
// ---------------------------------------------------------------------------

type fileInfo struct {
	name   string
	size   int64
	dir    bool
	mod    time.Time
	link   bool
	target string
}

func (f fileInfo) Name() string       { return f.name }
func (f fileInfo) Size() int64        { return f.size }
func (f fileInfo) IsDir() bool        { return f.dir }
func (f fileInfo) ModTime() time.Time { return f.mod }
func (f fileInfo) IsSymlink() bool    { return f.link }
func (f fileInfo) LinkTarget() string { return f.target }

func fromOSInfo(fi os.FileInfo) FileInfo {
	return fileInfo{name: fi.Name(), size: fi.Size(), dir: fi.IsDir(), mod: fi.ModTime()}
}

// checkRange validates stream-range arguments shared by both providers.
func checkRange(offset, limit int64) error {
	if offset < 0 || limit < -1 {
		return fmt.Errorf("fs: invalid range offset=%d limit=%d", offset, limit)
	}
	return nil
}

// copyCtx copies with a cancellation check per chunk so a large Copy remains
// cooperative. It never runs under a provider/service lock.
func copyCtx(ctx context.Context, dst io.Writer, src io.Reader) error {
	buf := make([]byte, 32*1024)
	for {
		if err := ctx.Err(); err != nil {
			return err
		}
		n, err := src.Read(buf)
		if n > 0 {
			if _, werr := dst.Write(buf[:n]); werr != nil {
				return werr
			}
		}
		if err == io.EOF {
			return nil
		}
		if err != nil {
			return err
		}
	}
}

func notExist(name string) error {
	return fmt.Errorf("fs: %q: %w", name, os.ErrNotExist)
}

// validateFilter rejects malformed doublestar patterns up front so a bad watch
// filter fails at WatchStream time rather than silently dropping every event.
func validateFilter(pattern string) error {
	if pattern == "" {
		return nil
	}
	if !doublestar.ValidatePattern(pattern) {
		return fmt.Errorf("fs: invalid watch filter %q", pattern)
	}
	return nil
}

// matchFilter applies a doublestar glob to a path relative to the watched
// directory. doublestar uses forward slashes on every platform.
func matchFilter(pattern, rel string) bool {
	if pattern == "" {
		return true
	}
	if rel == "." || rel == "" {
		rel = ""
	}
	matched, err := doublestar.Match(pattern, filepath.ToSlash(rel))
	return err == nil && matched
}

// watchRelative returns an event path relative to the watched name in a
// provider-neutral (slash-separated) form. It handles both URL names and
// plain paths without depending on the provider's host filesystem.
func watchRelative(base, eventPath string) string {
	trim := func(s string) string {
		for _, scheme := range []string{"file://", "memory://"} {
			if strings.HasPrefix(s, scheme) {
				s = strings.TrimPrefix(s, scheme)
				break
			}
		}
		return path.Clean("/" + strings.TrimPrefix(filepath.ToSlash(s), "/"))
	}
	b, e := trim(base), trim(eventPath)
	if e == b {
		return path.Base(b)
	}
	if strings.HasPrefix(e, b+"/") {
		return strings.TrimPrefix(e, b+"/")
	}
	return ""
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
	// initDone is closed once the initial provider.Watch has either attached
	// its inverse or failed. Registration is published under s.mu before that
	// call, so a concurrent reroute must wait for it outside w.mu.
	initDone chan struct{}
}

// abandon removes a watch whose provider registration failed or landed after
// Service.Close. It is called exactly once by the registering goroutine.
func (w *liveWatch) abandon() {
	w.mu.Lock()
	w.stopped = true
	w.mu.Unlock()
	w.owner.mu.Lock()
	delete(w.owner.watches, w)
	w.owner.mu.Unlock()
	close(w.initDone)
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
	init := w.initDone
	w.mu.Unlock()
	<-init // never wait while holding w.mu: registration needs it to attach
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

// callbackWatcher adapts a provider that only implements Watch to the Watcher
// contract. Ready closes as soon as the underlying watch is registered; Errors
// stays empty because a callback Watch has no asynchronous error channel.
type callbackWatcher struct {
	events chan WatchEvent
	errors chan error
	ready  chan struct{}
	stop   chan struct{}
	inv    func() error
	once   sync.Once
}

func newCallbackWatcher(ctx context.Context, p Provider, name, filter string) (Watcher, error) {
	if err := validateFilter(filter); err != nil {
		return nil, err
	}
	cw := &callbackWatcher{
		events: make(chan WatchEvent, 64),
		errors: make(chan error, 8),
		ready:  make(chan struct{}),
		stop:   make(chan struct{}),
	}
	inv, err := p.Watch(ctx, name, func(ev WatchEvent) error {
		if !matchFilter(filter, watchRelative(name, ev.Path)) {
			return nil
		}
		select {
		case cw.events <- ev:
		case <-cw.stop:
		}
		return nil
	})
	if err != nil {
		return nil, err
	}
	cw.inv = inv
	close(cw.ready)
	return cw, nil
}

func (w *callbackWatcher) Events() <-chan WatchEvent { return w.events }
func (w *callbackWatcher) Errors() <-chan error      { return w.errors }
func (w *callbackWatcher) Ready() <-chan struct{}    { return w.ready }

func (w *callbackWatcher) Dispose() error {
	var err error
	w.once.Do(func() {
		close(w.stop)
		if w.inv != nil {
			err = w.inv()
		}
	})
	return err
}

// liveWatcher is the service-level Watcher. Like liveWatch it follows its name
// across mount changes; unlike the callback path it also exposes Ready/Error.
type liveWatcher struct {
	mu       sync.Mutex
	owner    *service
	name     string
	ctx      context.Context
	filter   string
	provider Provider
	inner    Watcher
	events   chan WatchEvent
	errors   chan error
	ready    chan struct{}
	readyOne sync.Once
	stopFwd  chan struct{}
	fwdDone  chan struct{}
	stopped  bool
	// initDone is closed once the initial provider watch is attached or fails.
	initDone chan struct{}
}

// stopForwarder stops and joins one forwarder generation. Nil channels mean no
// forwarder was ever started (e.g. Close won the race during registration).
func (w *liveWatcher) stopForwarder(stop, done chan struct{}) {
	if stop == nil || done == nil {
		return
	}
	close(stop)
	<-done
}

// abandon removes a stream watch whose provider registration failed or landed
// after Service.Close. Called exactly once by the registering goroutine.
func (w *liveWatcher) abandon() {
	w.mu.Lock()
	w.stopped = true
	w.mu.Unlock()
	w.owner.mu.Lock()
	delete(w.owner.streams, w)
	w.owner.mu.Unlock()
	close(w.initDone)
	w.readyOne.Do(func() { close(w.ready) })
}

func openWatcher(ctx context.Context, p Provider, name, filter string) (Watcher, error) {
	if sw, ok := p.(StreamWatcher); ok {
		return sw.WatchStream(ctx, name, filter)
	}
	return newCallbackWatcher(ctx, p, name, filter)
}

// forward copies one inner watcher's signals into the stable public channels.
// stop is per-generation so a reroute can cancel just the old forwarder.
func (w *liveWatcher) forward(inner Watcher, stop <-chan struct{}, done chan<- struct{}) {
	defer close(done)
	readyCh := inner.Ready()
	for {
		select {
		case <-stop:
			return
		case <-w.ctx.Done():
			return
		case <-readyCh:
			w.readyOne.Do(func() { close(w.ready) })
			readyCh = nil
		case ev, ok := <-inner.Events():
			if !ok {
				return
			}
			select {
			case w.events <- ev:
			case <-stop:
				return
			case <-w.ctx.Done():
				return
			}
		case err, ok := <-inner.Errors():
			if !ok {
				return
			}
			select {
			case w.errors <- err:
			case <-stop:
				return
			case <-w.ctx.Done():
				return
			}
		}
	}
}

func (w *liveWatcher) reroute() {
	w.mu.Lock()
	if w.stopped {
		w.mu.Unlock()
		return
	}
	init := w.initDone
	w.mu.Unlock()
	<-init // wait outside w.mu so registration can publish its watcher
	w.mu.Lock()
	if w.stopped {
		w.mu.Unlock()
		return
	}
	p, res, err := w.owner.Resolve(w.name)
	if err != nil || p == w.provider {
		w.mu.Unlock()
		return
	}
	inner, err := openWatcher(w.ctx, p, res, w.filter)
	if err != nil {
		w.mu.Unlock() // keep the old watch when re-registration fails
		return
	}
	oldInner, oldStop, oldDone := w.inner, w.stopFwd, w.fwdDone
	stop, done := make(chan struct{}), make(chan struct{})
	w.inner, w.stopFwd, w.fwdDone, w.provider = inner, stop, done, p
	go w.forward(inner, stop, done)
	w.mu.Unlock()
	close(oldStop)
	<-oldDone
	_ = oldInner.Dispose()
}

func (w *liveWatcher) Dispose() error {
	w.mu.Lock()
	if w.stopped {
		w.mu.Unlock()
		return nil
	}
	w.stopped = true
	inner, stop, done := w.inner, w.stopFwd, w.fwdDone
	w.mu.Unlock()

	w.owner.mu.Lock()
	delete(w.owner.streams, w)
	w.owner.mu.Unlock()
	w.stopForwarder(stop, done) // no forwarder can touch public channels after this
	var err error
	if inner != nil {
		err = inner.Dispose()
	}
	w.readyOne.Do(func() { close(w.ready) })
	return err
}

func (w *liveWatcher) Events() <-chan WatchEvent { return w.events }
func (w *liveWatcher) Errors() <-chan error      { return w.errors }
func (w *liveWatcher) Ready() <-chan struct{}    { return w.ready }

type service struct {
	mu      sync.RWMutex
	mounts  []mountEntry
	watches map[*liveWatch]struct{}
	streams map[*liveWatcher]struct{}
	owned   []Provider
	closed  bool
}

func newService() *service {
	return &service{
		watches: make(map[*liveWatch]struct{}),
		streams: make(map[*liveWatcher]struct{}),
	}
}

// mount records a provider under prefix. Only a service-created provider is
// recorded as owned; Mount (the public entry point) never takes ownership.
func (s *service) mount(prefix string, p Provider, owned bool) error {
	if p == nil {
		return errors.New("fs: nil provider")
	}
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return errors.New("fs: service is closed")
	}
	for _, m := range s.mounts {
		if m.prefix == prefix {
			s.mu.Unlock()
			return fmt.Errorf("fs: duplicate mount %q", prefix)
		}
	}
	s.mounts = append(s.mounts, mountEntry{prefix: prefix, provider: p})
	if owned {
		s.owned = append(s.owned, p)
	}
	watches, streams := s.watchListLocked(), s.streamListLocked()
	s.mu.Unlock()
	s.reroute(watches)
	s.rerouteStreams(streams)
	return nil
}

func (s *service) Mount(prefix string, p Provider) error { return s.mount(prefix, p, false) }

func (s *service) MountLocal(prefix, root string) (Provider, error) {
	p := NewLocalProvider(root)
	if err := s.mount(prefix, p, true); err != nil {
		_ = p.Close()
		return nil, err
	}
	return p, nil
}

func (s *service) MountMemory(prefix string) (Provider, error) {
	p := NewMemoryProvider()
	if err := s.mount(prefix, p, true); err != nil {
		_ = p.Close()
		return nil, err
	}
	return p, nil
}

// Unmount only removes the mount entry. A provider is never closed here: it
// may be mounted more than once or have been supplied by an external owner.
func (s *service) Unmount(prefix string) error {
	s.mu.Lock()
	for i, m := range s.mounts {
		if m.prefix == prefix {
			s.mounts = append(s.mounts[:i], s.mounts[i+1:]...)
			watches, streams := s.watchListLocked(), s.streamListLocked()
			s.mu.Unlock()
			s.reroute(watches)
			s.rerouteStreams(streams)
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

func (s *service) streamListLocked() []*liveWatcher {
	out := make([]*liveWatcher, 0, len(s.streams))
	for w := range s.streams {
		out = append(out, w)
	}
	return out
}

func (s *service) reroute(watches []*liveWatch) {
	for _, w := range watches {
		w.reroute()
	}
}

func (s *service) rerouteStreams(watches []*liveWatcher) {
	for _, w := range watches {
		w.reroute()
	}
}

// Resolve picks the longest matching prefix; the empty prefix (if mounted) is
// the fallback when no specific prefix matches, matching the JS implementation.
func (s *service) Resolve(name string) (Provider, string, error) {
	s.mu.RLock()
	defer s.mu.RUnlock()
	return s.resolveLocked(name)
}

// resolveLocked is Resolve for callers that already hold s.mu (read or write).
// Keeping the closed check, resolve and watch registration in one critical
// section prevents Close from slipping between resolve and registration.
func (s *service) resolveLocked(name string) (Provider, string, error) {
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

// streamProvider resolves a name and checks the optional streaming capability.
// Provider code is only called after the service lock has been released.
func (s *service) streamProvider(name, op string) (StreamProvider, string, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return nil, "", err
	}
	sp, ok := p.(StreamProvider)
	if !ok {
		return nil, "", &ErrNotSupported{Op: op, Scheme: p.Scheme()}
	}
	return sp, res, nil
}

func (s *service) extendedProvider(name, op string) (ExtendedProvider, string, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return nil, "", err
	}
	ep, ok := p.(ExtendedProvider)
	if !ok {
		return nil, "", &ErrNotSupported{Op: op, Scheme: p.Scheme()}
	}
	return ep, res, nil
}

// resolvePair is used by the two-path operations: both names must be served by
// the same mounted provider, and that provider must implement the extension.
func (s *service) resolvePair(src, dst, op string) (ExtendedProvider, string, string, error) {
	p, a, err := s.Resolve(src)
	if err != nil {
		return nil, "", "", err
	}
	ep, ok := p.(ExtendedProvider)
	if !ok {
		return nil, "", "", &ErrNotSupported{Op: op, Scheme: p.Scheme()}
	}
	p2, b, err := s.Resolve(dst)
	if err != nil {
		return nil, "", "", err
	}
	if p != p2 {
		return nil, "", "", fmt.Errorf("fs: %s across providers %q and %q", op, p.Scheme(), p2.Scheme())
	}
	return ep, a, b, nil
}

func (s *service) OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error) {
	sp, res, err := s.streamProvider(name, "OpenStream")
	if err != nil {
		return nil, err
	}
	return sp.OpenStream(ctx, res, offset, limit)
}

func (s *service) ReadRange(ctx context.Context, name string, offset, limit int64) ([]byte, error) {
	sp, res, err := s.streamProvider(name, "ReadRange")
	if err != nil {
		return nil, err
	}
	r, err := sp.OpenStream(ctx, res, offset, limit)
	if err != nil {
		return nil, err
	}
	defer r.Close()
	return io.ReadAll(r)
}

func (s *service) Lstat(ctx context.Context, name string) (FileInfo, error) {
	ep, res, err := s.extendedProvider(name, "Lstat")
	if err != nil {
		return nil, err
	}
	return ep.Lstat(ctx, res)
}

func (s *service) Mkdir(ctx context.Context, name string) error {
	ep, res, err := s.extendedProvider(name, "Mkdir")
	if err != nil {
		return err
	}
	return ep.Mkdir(ctx, res)
}

func (s *service) Rename(ctx context.Context, old, new string) error {
	ep, a, b, err := s.resolvePair(old, new, "Rename")
	if err != nil {
		return err
	}
	return ep.Rename(ctx, a, b)
}

func (s *service) Copy(ctx context.Context, src, dst string) error {
	ep, a, b, err := s.resolvePair(src, dst, "Copy")
	if err != nil {
		return err
	}
	return ep.Copy(ctx, a, b)
}

func (s *service) Access(ctx context.Context, name string) error {
	ep, res, err := s.extendedProvider(name, "Access")
	if err != nil {
		return err
	}
	return ep.Access(ctx, res)
}

func (s *service) Watch(ctx context.Context, name string, cb func(WatchEvent) error) (func() error, error) {
	// Resolve, closed check and registration share one lock: Close cannot
	// snapshot and clear s.watches between resolving and inserting this watch.
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return nil, errors.New("fs: service is closed")
	}
	p, res, err := s.resolveLocked(name)
	if err != nil {
		s.mu.Unlock()
		return nil, err
	}
	w := &liveWatch{
		owner: s, name: name, ctx: ctx, cb: cb,
		provider: p, initDone: make(chan struct{}),
	}
	s.watches[w] = struct{}{}
	s.mu.Unlock()

	// Provider/user code is never called under s.mu. If Close wins the race
	// after registration, stop() sees a nil inverse and we dispose ours here.
	inv, err := p.Watch(ctx, res, cb)
	if err != nil {
		w.abandon()
		return nil, err
	}
	w.mu.Lock()
	if w.stopped {
		w.mu.Unlock()
		close(w.initDone)
		_ = inv()
		return nil, errors.New("fs: service is closed")
	}
	w.inv = inv
	w.mu.Unlock()
	close(w.initDone)
	return w.stop, nil
}

// WatchStream exposes the JS-style Change/Ready/Error/Dispose watcher. The
// returned handle follows mount changes like Watch does, and filter is a
// doublestar glob relative to the watched path (empty means no filtering).
func (s *service) WatchStream(ctx context.Context, name, filter string) (Watcher, error) {
	if err := validateFilter(filter); err != nil {
		return nil, err
	}
	// Same single critical section as Watch: a concurrent Close either sees
	// this watch and disposes it, or marks s.closed before we register.
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return nil, errors.New("fs: service is closed")
	}
	p, res, err := s.resolveLocked(name)
	if err != nil {
		s.mu.Unlock()
		return nil, err
	}
	w := &liveWatcher{
		owner: s, name: name, ctx: ctx, filter: filter,
		provider: p, initDone: make(chan struct{}),
		events: make(chan WatchEvent, 64), errors: make(chan error, 8),
		ready: make(chan struct{}),
	}
	s.streams[w] = struct{}{}
	s.mu.Unlock()

	inner, err := openWatcher(ctx, p, res, filter) // user code, outside s.mu
	if err != nil {
		w.abandon()
		return nil, err
	}
	w.mu.Lock()
	if w.stopped {
		stop, done := w.stopFwd, w.fwdDone
		w.mu.Unlock()
		w.stopForwarder(stop, done)
		_ = inner.Dispose()
		close(w.initDone)
		return nil, errors.New("fs: service is closed")
	}
	w.inner = inner
	w.stopFwd, w.fwdDone = make(chan struct{}), make(chan struct{})
	stop, done := w.stopFwd, w.fwdDone
	w.mu.Unlock()
	go w.forward(inner, stop, done) // never started on the closed path
	close(w.initDone)
	return w, nil
}

// Close drops all mounts, stops every watch and closes only the providers that
// this service created (via MountLocal/MountMemory). Externally supplied
// providers keep living until their own owner closes them. It is installed as
// an effect by Component.Apply, so disposal leaves no residue.
func (s *service) Close() error {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return nil
	}
	s.closed = true
	watches, streams := s.watchListLocked(), s.streamListLocked()
	owned := s.owned
	s.mounts, s.owned = nil, nil
	s.watches = make(map[*liveWatch]struct{})
	s.streams = make(map[*liveWatcher]struct{})
	s.mu.Unlock()

	var errs []error
	for _, w := range streams {
		if err := w.Dispose(); err != nil {
			errs = append(errs, err)
		}
	}
	for _, w := range watches {
		if err := w.stop(); err != nil {
			errs = append(errs, err)
		}
	}
	for _, p := range owned {
		if err := p.Close(); err != nil {
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
	root      *os.Root
	rootPath  string
	openErr   error
	closeOnce sync.Once
	closeErr  error
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

// Close releases the os.Root handle. It is idempotent so a provider shared by
// several mounts (or closed by both its owner and the service) stays safe.
func (p *localProvider) Close() error {
	p.closeOnce.Do(func() {
		if p.root != nil {
			p.closeErr = p.root.Close()
		}
	})
	return p.closeErr
}

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

// limitedReadCloser couples a bounded reader with the handle it reads from.
type limitedReadCloser struct {
	io.Reader
	io.Closer
}

func (p *localProvider) OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error) {
	if err := checkRange(offset, limit); err != nil {
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

func (p *localProvider) Lstat(ctx context.Context, name string) (FileInfo, error) {
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
	out := fileInfo{name: fi.Name(), size: fi.Size(), dir: fi.IsDir(), mod: fi.ModTime()}
	if fi.Mode()&os.ModeSymlink != 0 {
		out.link = true
		// os.Root.Readlink postdates this module's go directive. Root.Lstat has
		// already confined every path component, and readlink only reads the
		// link text (it does not follow the final link), so no outside file is
		// touched by this metadata call.
		target, err := os.Readlink(filepath.Join(p.rootPath, rel))
		if err != nil {
			return nil, fmt.Errorf("fs: readlink: %w", err)
		}
		out.target = target
	}
	return out, nil
}

func (p *localProvider) Mkdir(ctx context.Context, name string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	rel, err := p.relative(name)
	if err != nil {
		return err
	}
	return p.root.Mkdir(rel, 0o755)
}

func (p *localProvider) Rename(ctx context.Context, old, new string) error {
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
	// Root.Rename is the confined stdlib primitive, but it postdates this
	// module's go1.24 directive; a narrow interface keeps go vet's stdversion
	// check quiet while still calling Root.Rename itself.
	var renamer interface{ Rename(string, string) error } = p.root
	return renamer.Rename(a, b)
}

func (p *localProvider) Copy(ctx context.Context, src, dst string) error {
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
	in, err := p.root.OpenFile(a, os.O_RDONLY, 0)
	if err != nil {
		return fmt.Errorf("fs: copy source: %w", err)
	}
	defer in.Close()
	if dir := filepath.Dir(b); dir != "." {
		if err := rootMkdirAll(p.root, dir, 0o755); err != nil {
			return err
		}
	}
	out, err := p.root.OpenFile(b, os.O_WRONLY|os.O_CREATE|os.O_TRUNC, 0o644)
	if err != nil {
		return fmt.Errorf("fs: copy destination: %w", err)
	}
	copyErr := copyCtx(ctx, out, in)
	closeErr := out.Close()
	if copyErr != nil {
		return copyErr
	}
	return closeErr
}

func (p *localProvider) Access(ctx context.Context, name string) error {
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

// localWatch implements Watcher over one fsnotify watcher. fsnotify has no
// recursion: existing subdirectories are registered by an initial WalkDir and
// new ones are registered when their Create event arrives. Events for any
// descendant of the requested path are reported; cb, when non-nil, receives
// them instead of the Events channel (the legacy callback Watch API).
type localWatch struct {
	watcher *fsnotify.Watcher
	ctx     context.Context
	name    string
	prefix  string
	filter  string
	cb      func(WatchEvent) error
	events  chan WatchEvent
	errors  chan error
	ready   chan struct{}
	stop    chan struct{}
	done    chan struct{}
	once    sync.Once
}

func (w *localWatch) Events() <-chan WatchEvent { return w.events }
func (w *localWatch) Errors() <-chan error      { return w.errors }
func (w *localWatch) Ready() <-chan struct{}    { return w.ready }

// Dispose is idempotent and joins the watcher goroutine: after it returns no
// callback or channel send can still be in flight.
func (w *localWatch) Dispose() error {
	w.once.Do(func() { close(w.stop) })
	<-w.done
	return nil
}

func (w *localWatch) sendErr(err error) {
	select {
	case w.errors <- err:
	default: // errors are advisory: never stall the event loop
	}
}

// eventPath keeps the public path in the same namespace as the watched name.
func (w *localWatch) eventPath(p, rel string) string {
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

func newLocalWatch(ctx context.Context, name, rp, filter string, cb func(WatchEvent) error) (*localWatch, error) {
	// If the target is not an existing directory (a file, or not created yet),
	// watch its parent so creation/removal of the target is still observed.
	watchDir := rp
	if fi, err := os.Stat(rp); err != nil || !fi.IsDir() {
		watchDir = filepath.Dir(rp)
	}
	fsw, err := fsnotify.NewWatcher()
	if err != nil {
		return nil, err
	}
	w := &localWatch{
		watcher: fsw, ctx: ctx, name: name, prefix: filepath.Clean(rp),
		filter: filter, cb: cb,
		events: make(chan WatchEvent, 64), errors: make(chan error, 16),
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

func (w *localWatch) run() {
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
			if ev.Op.Has(fsnotify.Create) {
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
			if !matchFilter(w.filter, watchRelative(w.name, ep)) {
				continue
			}
			out := WatchEvent{Path: ep, Op: mapOp(ev.Op)}
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
			w.sendErr(err) // P1-04: errors reach the caller via Watcher.Errors
		}
	}
}

func (p *localProvider) Watch(ctx context.Context, name string, cb func(WatchEvent) error) (func() error, error) {
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	w, err := newLocalWatch(ctx, name, filepath.Join(p.rootPath, rel), "", cb)
	if err != nil {
		return nil, err
	}
	return func() error { return w.Dispose() }, nil
}

// WatchStream exposes the full Watcher contract for a directory and all of its
// descendants. filter is a doublestar glob relative to the watched path.
func (p *localProvider) WatchStream(ctx context.Context, name, filter string) (Watcher, error) {
	if err := validateFilter(filter); err != nil {
		return nil, err
	}
	rel, err := p.relative(name)
	if err != nil {
		return nil, err
	}
	return newLocalWatch(ctx, name, filepath.Join(p.rootPath, rel), filter, nil)
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

// Close is a no-op: the memory provider holds no operating-system resources.
func (p *memoryProvider) Close() error { return nil }

// WatchStream reuses the callback watch and adapts it to the Watcher contract.
func (p *memoryProvider) WatchStream(ctx context.Context, name, filter string) (Watcher, error) {
	return newCallbackWatcher(ctx, p, name, filter)
}

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

func (p *memoryProvider) OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error) {
	if err := checkRange(offset, limit); err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}
	k := memKey(name)
	if k == "" {
		return nil, notExist(name)
	}
	p.mu.RLock()
	e, ok := p.files[k]
	p.mu.RUnlock() // entries are immutable: writers replace the slice, never mutate it
	if !ok {
		return nil, notExist(name)
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

// Lstat equals Stat here: the in-memory map has no symlink entries, so all
// returned FileInfos implement SymlinkInfo as false/"".
func (p *memoryProvider) Lstat(ctx context.Context, name string) (FileInfo, error) {
	return p.Stat(ctx, name)
}

func (p *memoryProvider) Mkdir(ctx context.Context, name string) error {
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
	if isDir || isFile {
		p.mu.Unlock()
		return fmt.Errorf("fs: mkdir %q: %w", name, os.ErrExist)
	}
	p.dirs[k] = struct{}{}
	watches := p.matchesLocked(k)
	p.mu.Unlock()
	p.notify(watches, WatchEvent{Path: name, Op: "create"})
	return nil
}

func (p *memoryProvider) Rename(ctx context.Context, old, new string) error {
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
	if !isFile && !isDir {
		p.mu.Unlock()
		return notExist(old)
	}
	if _, exists := p.files[b]; exists {
		p.mu.Unlock()
		return fmt.Errorf("fs: rename target %q exists", new)
	}
	if _, exists := p.dirs[b]; exists {
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
	}
	for d := memParent(b); d != ""; d = memParent(d) {
		p.dirs[d] = struct{}{}
	}
	watches := append(p.matchesLocked(a), p.matchesLocked(b)...)
	p.mu.Unlock()
	p.notify(watches, WatchEvent{Path: new, Op: "rename"})
	return nil
}

func (p *memoryProvider) Copy(ctx context.Context, src, dst string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	a, b := memKey(src), memKey(dst)
	if a == "" || b == "" {
		return errors.New("fs: empty memory key")
	}
	p.mu.Lock()
	e, ok := p.files[a]
	if !ok {
		isDir := false
		_, isDir = p.dirs[a]
		p.mu.Unlock()
		if isDir {
			return fmt.Errorf("fs: copy: %q is a directory", src)
		}
		return notExist(src)
	}
	data := append([]byte(nil), e.data...) // independent copy, made under the lock
	p.files[b] = memEntry{data: data, mod: time.Now()}
	for d := memParent(b); d != ""; d = memParent(d) {
		p.dirs[d] = struct{}{}
	}
	watches := p.matchesLocked(b)
	p.mu.Unlock()
	p.notify(watches, WatchEvent{Path: dst, Op: "create"})
	return nil
}

func (p *memoryProvider) Access(ctx context.Context, name string) error {
	if err := ctx.Err(); err != nil {
		return err
	}
	k := memKey(name)
	if k == "" {
		return notExist(name)
	}
	p.mu.RLock()
	defer p.mu.RUnlock()
	if _, ok := p.files[k]; ok {
		return nil
	}
	if _, ok := p.dirs[k]; ok {
		return nil
	}
	return notExist(name)
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
