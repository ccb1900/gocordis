// Package contract holds the provider-neutral types and structural interfaces
// shared by the gocordis FS Service and its concrete Local/Memory providers.
//
// It deliberately depends only on the standard library (plus doublestar for
// watch filters) so both providers and the Service can import it without an
// import cycle. The public extensions/fs package re-exports every type here as
// an alias, keeping the original external API source-compatible.
package contract

import (
	"context"
	"fmt"
	"io"
	"os"
	"path"
	"path/filepath"
	"strings"
	"sync"
	"time"

	"github.com/bmatcuk/doublestar/v4"
)

// FileInfo is the provider-neutral view of a file's metadata.
type FileInfo interface {
	Name() string
	Size() int64
	IsDir() bool
	ModTime() time.Time
}

// SymlinkInfo is an optional extension of FileInfo for providers that can
// distinguish symbolic links. Lstat results report link metadata; Stat results
// and all regular files report false/"".
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

// Watcher is the JS-FS-style watch handle: Change (Events), Ready, Error and
// Dispose. Ready is a closed channel once registration completes; Errors is
// advisory and never blocks the watcher; Dispose is idempotent and joins the
// underlying goroutine.
type Watcher interface {
	Events() <-chan WatchEvent
	Errors() <-chan error
	Ready() <-chan struct{}
	Dispose() error
}

// StreamWatcher is implemented by providers that expose the full Watcher
// contract. Providers that only implement Provider.Watch are adapted by the
// Service with NewCallbackWatcher; both paths behave identically to callers.
type StreamWatcher interface {
	WatchStream(ctx context.Context, name, filter string) (Watcher, error)
}

// Provider is a concrete filesystem implementation reachable through a URL
// scheme ("file" or "memory"). Close releases provider-owned resources; it is
// idempotent and is called only by the owner of the provider instance.
type Provider interface {
	Scheme() string
	Open(context.Context, string) (File, error)
	Stat(context.Context, string) (FileInfo, error)
	ReadDir(context.Context, string) ([]FileInfo, error)
	Write(context.Context, string, []byte) error
	Remove(context.Context, string) error
	io.Closer
	// Watch returns an inverse that stops the watcher and joins its goroutine.
	// The callback is invoked outside any provider/service lock.
	Watch(context.Context, string, func(WatchEvent) error) (func() error, error)
}

// StreamProvider is an optional capability for providers that can serve a byte
// range as a stream. offset must be >= 0; limit >= 0 caps the byte count and
// limit == -1 means "through EOF".
type StreamProvider interface {
	OpenStream(ctx context.Context, name string, offset, limit int64) (io.ReadCloser, error)
}

// ExtendedProvider is the original optional operation surface. Providers
// implement the option-aware interfaces below to also cover the JS Core
// {Recursive,Force}/{Recursive}/{Recursive,Overwrite} and symlink semantics.
type ExtendedProvider interface {
	Lstat(ctx context.Context, name string) (FileInfo, error)
	Mkdir(ctx context.Context, name string) error
	Rename(ctx context.Context, old, new string) error
	Copy(ctx context.Context, src, dst string) error
	Access(ctx context.Context, name string) error
}

// RemoveOptions mirrors the JS fs remove options. Recursive deletes a subtree;
// Force turns a missing target into success.
type RemoveOptions struct {
	Recursive bool
	Force     bool
}

// MkdirOptions mirrors the JS fs mkdir options. Recursive is the MkdirAll
// equivalent: create missing parents.
type MkdirOptions struct {
	Recursive bool
}

// CopyOptions mirrors the JS fs copy options. Recursive copies a directory
// tree; Overwrite permits replacing an existing destination.
type CopyOptions struct {
	Recursive bool
	Overwrite bool
}

// RemoveOptionsProvider, MkdirOptionsProvider, CopyOptionsProvider and
// SymlinkProvider are additive structural capabilities. A provider that does
// not implement one keeps working with the base operations; the Service maps a
// missing capability to *ErrNotSupported.
type RemoveOptionsProvider interface {
	RemoveWithOptions(ctx context.Context, name string, opts RemoveOptions) error
}

type MkdirOptionsProvider interface {
	MkdirWithOptions(ctx context.Context, name string, opts MkdirOptions) error
}

type CopyOptionsProvider interface {
	CopyWithOptions(ctx context.Context, src, dst string, opts CopyOptions) error
}

type SymlinkProvider interface {
	// Symlink creates the link named `link` pointing at `target`. Both names
	// are confined to the provider's namespace; an escaping target is rejected.
	Symlink(ctx context.Context, target, link string) error
	Readlink(ctx context.Context, name string) (string, error)
}

// ErrNotSupported is returned when the provider mounted for a name does not
// implement the optional interface behind a Service operation.
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

// ---------------------------------------------------------------------------
// metadata value
// ---------------------------------------------------------------------------

// Info is a ready-made FileInfo + SymlinkInfo implementation used by both
// providers. Build it with NewInfo and optionally decorate with AsSymlink.
type Info struct {
	name   string
	size   int64
	dir    bool
	mod    time.Time
	link   bool
	target string
}

func NewInfo(name string, size int64, dir bool, mod time.Time) Info {
	return Info{name: name, size: size, dir: dir, mod: mod}
}

func (i Info) AsSymlink(target string) Info {
	i.link, i.target = true, target
	return i
}

func (i Info) Name() string       { return i.name }
func (i Info) Size() int64        { return i.size }
func (i Info) ModTime() time.Time { return i.mod }
func (i Info) IsDir() bool        { return i.dir }
func (i Info) IsSymlink() bool    { return i.link }
func (i Info) LinkTarget() string { return i.target }

// FromOSInfo converts an os.FileInfo into a provider-neutral FileInfo.
func FromOSInfo(fi os.FileInfo) FileInfo {
	return NewInfo(fi.Name(), fi.Size(), fi.IsDir(), fi.ModTime())
}

// ---------------------------------------------------------------------------
// shared helpers
// ---------------------------------------------------------------------------

// CheckRange validates stream-range arguments shared by both providers.
func CheckRange(offset, limit int64) error {
	if offset < 0 || limit < -1 {
		return fmt.Errorf("fs: invalid range offset=%d limit=%d", offset, limit)
	}
	return nil
}

// CopyCtx copies with a cancellation check per chunk so a large Copy remains
// cooperative. It never runs under a provider/service lock.
func CopyCtx(ctx context.Context, dst io.Writer, src io.Reader) error {
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

// NotExist builds a wrapped os.ErrNotExist error for name.
func NotExist(name string) error {
	return fmt.Errorf("fs: %q: %w", name, os.ErrNotExist)
}

// ValidateFilter rejects malformed doublestar patterns up front.
func ValidateFilter(pattern string) error {
	if pattern == "" {
		return nil
	}
	if !doublestar.ValidatePattern(pattern) {
		return fmt.Errorf("fs: invalid watch filter %q", pattern)
	}
	return nil
}

// MatchFilter applies a doublestar glob to a path relative to the watched dir.
// doublestar uses forward slashes on every platform.
func MatchFilter(pattern, rel string) bool {
	if pattern == "" {
		return true
	}
	if rel == "." || rel == "" {
		rel = ""
	}
	matched, err := doublestar.Match(pattern, filepath.ToSlash(rel))
	return err == nil && matched
}

// WatchRelative returns an event path relative to the watched name in a
// provider-neutral (slash-separated) form. It handles both URL names and plain
// paths without depending on the provider's host filesystem.
func WatchRelative(base, eventPath string) string {
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
// generic callback-watcher adapter
// ---------------------------------------------------------------------------

// CallbackWatcher adapts a provider that only implements Watch to the full
// Watcher contract. Ready closes as soon as the underlying watch is attached;
// Errors stays empty because a callback Watch has no asynchronous error
// channel.
type CallbackWatcher struct {
	events chan WatchEvent
	errors chan error
	ready  chan struct{}
	stop   chan struct{}
	inv    func() error
	once   sync.Once

	// A provider may have collected this watch for a notify just before the
	// inverse removed it, so a callback can still be mid-send after inv()
	// returns. Guard Add against Dispose closing the channels.
	cbMu   sync.Mutex
	closed bool
	cbWG   sync.WaitGroup
}

// NewCallbackWatcher registers a callback watch and adapts it to Watcher.
func NewCallbackWatcher(ctx context.Context, p Provider, name, filter string) (Watcher, error) {
	if err := ValidateFilter(filter); err != nil {
		return nil, err
	}
	cw := &CallbackWatcher{
		events: make(chan WatchEvent, 64),
		errors: make(chan error, 8),
		ready:  make(chan struct{}),
		stop:   make(chan struct{}),
	}
	inv, err := p.Watch(ctx, name, func(ev WatchEvent) error {
		cw.cbMu.Lock()
		if cw.closed {
			cw.cbMu.Unlock()
			return nil
		}
		cw.cbWG.Add(1)
		cw.cbMu.Unlock()
		defer cw.cbWG.Done()
		if !MatchFilter(filter, WatchRelative(name, ev.Path)) {
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

func (w *CallbackWatcher) Events() <-chan WatchEvent { return w.events }
func (w *CallbackWatcher) Errors() <-chan error      { return w.errors }
func (w *CallbackWatcher) Ready() <-chan struct{}    { return w.ready }

func (w *CallbackWatcher) Dispose() error {
	var err error
	w.once.Do(func() {
		w.cbMu.Lock()
		w.closed = true
		w.cbMu.Unlock()
		close(w.stop)
		if w.inv != nil {
			err = w.inv() // joins the provider watch when it owns a goroutine
		}
		w.cbWG.Wait() // no callback can still be sending
		close(w.events)
		close(w.errors)
	})
	return err
}
