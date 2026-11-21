// Package fs provides a mount-routed file-system capability for gocordis
// applications. It is deliberately an extension: the Service is a plain value
// provided through a typed key by a Component, and every reversible consequence
// (mounts, watchers) is installed as a runtime effect so the Runtime remains the
// single lifecycle authority.
//
// Provider-neutral types live in extensions/fs/contract; the concrete file://
// and memory:// providers live in extensions/fs/local and extensions/fs/memory.
// This package re-exports their types (see aliases.go) to preserve the original
// external API.
package fs

import (
	"context"
	"errors"
	"fmt"
	"io"
	"net/url"
	"path"
	"strings"
	"sync"

	"dynamic-runtime/extensions/fs/contract"
	"dynamic-runtime/extensions/fs/local"
	"dynamic-runtime/extensions/fs/memory"
	"dynamic-runtime/runtime"
)

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
	// Legacy operations delegate to their option-aware counterparts with zero
	// options, keeping every pre-existing caller source-compatible.
	Remove(context.Context, string) error
	RemoveWithOptions(context.Context, string, RemoveOptions) error
	OpenStream(context.Context, string, int64, int64) (io.ReadCloser, error)
	ReadRange(context.Context, string, int64, int64) ([]byte, error)
	Lstat(context.Context, string) (FileInfo, error)
	Mkdir(context.Context, string) error
	MkdirWithOptions(context.Context, string, MkdirOptions) error
	Rename(context.Context, string, string) error
	Copy(context.Context, string, string) error
	CopyWithOptions(context.Context, string, string, CopyOptions) error
	Access(context.Context, string) error
	// Symlink/Readlink need a SymlinkProvider; otherwise ErrNotSupported.
	Symlink(ctx context.Context, target, link string) error
	Readlink(ctx context.Context, name string) (string, error)
	// ResolveWithBase resolves a relative URL against a mounted base, rejecting
	// paths that escape the base or land on a different mount.
	ResolveWithBase(ctx context.Context, base, name string) (string, error)
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
// service state and watch routing
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
// contract. It is now provided by the shared contract package.
type callbackWatcher = contract.CallbackWatcher

func newCallbackWatcher(ctx context.Context, p Provider, name, filter string) (Watcher, error) {
	return contract.NewCallbackWatcher(ctx, p, name, filter)
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
	// fwdWG tracks every forwarder generation, including ones being retired by
	// a reroute, so Dispose can wait for all senders before closing channels.
	fwdWG   sync.WaitGroup
	stopped bool
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
	defer w.fwdWG.Done()
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
	w.fwdWG.Add(1)
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
	w.stopForwarder(stop, done) // stop and join the current forwarder generation
	var err error
	if inner != nil {
		err = inner.Dispose()
	}
	// A reroute may still be joining a retiring forwarder. Wait for every
	// generation before closing the public channels so no sender can race a
	// close (and no range over Events/Errors blocks forever).
	w.fwdWG.Wait()
	close(w.events)
	close(w.errors)
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
	p := local.New(root)
	if err := s.mount(prefix, p, true); err != nil {
		_ = p.Close()
		return nil, err
	}
	return p, nil
}

func (s *service) MountMemory(prefix string) (Provider, error) {
	p := memory.New()
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
	return s.RemoveWithOptions(ctx, name, RemoveOptions{})
}

// RemoveWithOptions uses the provider's option-aware capability when present,
// otherwise maps the zero option set to the legacy Provider.Remove.
func (s *service) RemoveWithOptions(ctx context.Context, name string, opts RemoveOptions) error {
	p, res, err := s.Resolve(name)
	if err != nil {
		return err
	}
	if rop, ok := p.(RemoveOptionsProvider); ok {
		return rop.RemoveWithOptions(ctx, res, opts)
	}
	if opts == (RemoveOptions{}) {
		return p.Remove(ctx, res)
	}
	return &ErrNotSupported{Op: "RemoveWithOptions", Scheme: p.Scheme()}
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

// resolvePairAs is resolvePair without the ExtendedProvider requirement: it
// only enforces that both paths belong to the same mounted provider.
func (s *service) resolvePairAs(src, dst, op string) (Provider, string, string, error) {
	p, a, err := s.Resolve(src)
	if err != nil {
		return nil, "", "", err
	}
	p2, b, err := s.Resolve(dst)
	if err != nil {
		return nil, "", "", err
	}
	if p != p2 {
		return nil, "", "", fmt.Errorf("fs: %s across providers %q and %q", op, p.Scheme(), p2.Scheme())
	}
	return p, a, b, nil
}

// resolvePair is used by the two-path operations: both names must be served by
// the same mounted provider, and that provider must implement the extension.
func (s *service) resolvePair(src, dst, op string) (ExtendedProvider, string, string, error) {
	p, a, b, err := s.resolvePairAs(src, dst, op)
	if err != nil {
		return nil, "", "", err
	}
	ep, ok := p.(ExtendedProvider)
	if !ok {
		return nil, "", "", &ErrNotSupported{Op: op, Scheme: p.Scheme()}
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
	return s.MkdirWithOptions(ctx, name, MkdirOptions{})
}

func (s *service) MkdirWithOptions(ctx context.Context, name string, opts MkdirOptions) error {
	p, res, err := s.Resolve(name)
	if err != nil {
		return err
	}
	if mop, ok := p.(MkdirOptionsProvider); ok {
		return mop.MkdirWithOptions(ctx, res, opts)
	}
	if opts == (MkdirOptions{}) {
		if ep, ok := p.(ExtendedProvider); ok {
			return ep.Mkdir(ctx, res)
		}
	}
	return &ErrNotSupported{Op: "Mkdir", Scheme: p.Scheme()}
}

func (s *service) Rename(ctx context.Context, old, new string) error {
	ep, a, b, err := s.resolvePair(old, new, "Rename")
	if err != nil {
		return err
	}
	return ep.Rename(ctx, a, b)
}

func (s *service) Copy(ctx context.Context, src, dst string) error {
	return s.CopyWithOptions(ctx, src, dst, CopyOptions{})
}

func (s *service) CopyWithOptions(ctx context.Context, src, dst string, opts CopyOptions) error {
	p, a, b, err := s.resolvePairAs(src, dst, "Copy")
	if err != nil {
		return err
	}
	if cop, ok := p.(CopyOptionsProvider); ok {
		return cop.CopyWithOptions(ctx, a, b, opts)
	}
	if opts == (CopyOptions{}) {
		if ep, ok := p.(ExtendedProvider); ok {
			return ep.Copy(ctx, a, b)
		}
	}
	return &ErrNotSupported{Op: "Copy", Scheme: p.Scheme()}
}

func (s *service) Access(ctx context.Context, name string) error {
	ep, res, err := s.extendedProvider(name, "Access")
	if err != nil {
		return err
	}
	return ep.Access(ctx, res)
}

func (s *service) Symlink(ctx context.Context, target, link string) error {
	p, a, b, err := s.resolvePairAs(target, link, "Symlink")
	if err != nil {
		return err
	}
	sp, ok := p.(SymlinkProvider)
	if !ok {
		return &ErrNotSupported{Op: "Symlink", Scheme: p.Scheme()}
	}
	return sp.Symlink(ctx, a, b)
}

func (s *service) Readlink(ctx context.Context, name string) (string, error) {
	p, res, err := s.Resolve(name)
	if err != nil {
		return "", err
	}
	sp, ok := p.(SymlinkProvider)
	if !ok {
		return "", &ErrNotSupported{Op: "Readlink", Scheme: p.Scheme()}
	}
	return sp.Readlink(ctx, res)
}

// ResolveWithBase resolves a relative URL against a mounted base URL using RFC
// 3986 reference resolution, then rejects a result that escapes the base path,
// changes scheme/host, or lands on a different mounted provider.
func (s *service) ResolveWithBase(ctx context.Context, base, name string) (string, error) {
	if err := ctx.Err(); err != nil {
		return "", err
	}
	bu, err := url.Parse(base)
	if err != nil || bu.Scheme == "" {
		return "", fmt.Errorf("fs: invalid base URL %q: %v", base, err)
	}
	nu, err := url.Parse(name)
	if err != nil {
		return "", fmt.Errorf("fs: invalid relative URL %q: %w", name, err)
	}
	if nu.Scheme != "" && nu.Scheme != bu.Scheme {
		return "", fmt.Errorf("fs: cross-scheme URL %q", name)
	}
	if nu.Host != "" && nu.Host != bu.Host {
		return "", fmt.Errorf("fs: cross-host URL %q", name)
	}
	resolved := bu.ResolveReference(nu)
	if resolved.Scheme != bu.Scheme || resolved.Host != bu.Host {
		return "", fmt.Errorf("fs: URL %q escapes base %q", name, base)
	}
	basePath := path.Clean("/" + strings.TrimPrefix(bu.Path, "/"))
	if !strings.HasSuffix(bu.Path, "/") {
		basePath = path.Dir(basePath)
	}
	resPath := path.Clean("/" + strings.TrimPrefix(resolved.Path, "/"))
	if basePath != "/" && resPath != basePath && !strings.HasPrefix(resPath, basePath+"/") {
		return "", fmt.Errorf("fs: URL %q escapes base %q", name, base)
	}
	out := resolved.String()
	pb, _, err := s.Resolve(base)
	if err != nil {
		return "", err
	}
	pr, _, err := s.Resolve(out)
	if err != nil {
		return "", err
	}
	if pb != pr {
		return "", fmt.Errorf("fs: URL %q crosses a mount boundary", name)
	}
	return out, nil
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
	if err := contract.ValidateFilter(filter); err != nil {
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
	w.fwdWG.Add(1)
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
