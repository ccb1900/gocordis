package watch

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"fmt"
	"os"
	"path/filepath"
	"strings"
	"sync"
)

// FileWatcher is the v0.1 concrete Watch: it observes file Sources
// (URI "file:///abs/path") and emits normalized Changes. Detection is
// event-driven (native OS notifications); there is no polling of file state.
type FileWatcher struct {
	mu     sync.Mutex
	closed bool
	buffer int

	subs   map[uint64]*fileSub
	nextID uint64
	wg     sync.WaitGroup
}

// NewFileWatcher creates a FileWatcher with the default subscription buffer.
func NewFileWatcher() *FileWatcher {
	return NewFileWatcherWithBuffer(DefaultBufferSize)
}

// NewFileWatcherWithBuffer creates a FileWatcher with a custom (positive,
// bounded) per-subscription buffer.
func NewFileWatcherWithBuffer(buffer int) *FileWatcher {
	if buffer <= 0 {
		buffer = DefaultBufferSize
	}
	return &FileWatcher{
		buffer: buffer,
		subs:   make(map[uint64]*fileSub),
	}
}

type fileSub struct {
	id     uint64
	source Source
	path   string
	sub    *subscription
}

// Watch starts observing source and returns a new Subscription. Duplicate
// Watch calls are allowed and independent. No initial fake "Added" change is
// emitted; only future changes are delivered.
func (f *FileWatcher) Watch(ctx context.Context, source Source) (Subscription, error) {
	if ctx == nil {
		ctx = context.Background()
	}
	if err := source.valid(); err != nil {
		return nil, err
	}
	if source.Kind != "file" {
		return nil, fmt.Errorf("%w: kind %q", ErrUnsupportedKind, source.Kind)
	}
	path, err := fileURItoPath(source.URI)
	if err != nil {
		return nil, err
	}
	if err := ctx.Err(); err != nil {
		return nil, err
	}

	f.mu.Lock()
	if f.closed {
		f.mu.Unlock()
		return nil, ErrWatchClosed
	}
	f.nextID++
	fs := &fileSub{
		id:     f.nextID,
		source: source,
		path:   path,
		sub:    newSubscription(f.buffer),
	}
	f.subs[fs.id] = fs
	f.wg.Add(1) // counted while holding mu so Close cannot miss it
	f.mu.Unlock()

	go f.runFileSession(ctx, fs)
	return fs.sub, nil
}

func (f *FileWatcher) runFileSession(ctx context.Context, fs *fileSub) {
	defer f.wg.Done()
	runFileSession(ctx, fs)
}

// Close shuts the watcher down: it rejects new Watch calls, closes every
// subscription, stops every underlying watcher, and waits for the internal
// goroutines to exit. Close is idempotent.
func (f *FileWatcher) Close() error {
	return f.CloseContext(context.Background())
}

// CloseContext is Close with a bounded wait. On timeout it returns ctx.Err()
// while the watcher remains in its closing state.
func (f *FileWatcher) CloseContext(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	f.mu.Lock()
	if !f.closed {
		f.closed = true
	}
	subs := make([]*fileSub, 0, len(f.subs))
	for _, fs := range f.subs {
		subs = append(subs, fs)
	}
	f.mu.Unlock()

	for _, fs := range subs {
		fs.sub.Close()
	}

	idle := make(chan struct{})
	go func() {
		f.wg.Wait()
		close(idle)
	}()
	select {
	case <-idle:
		return nil
	case <-ctx.Done():
		return ctx.Err()
	}
}

// fileURItoPath validates "file:///abs/path" and returns the absolute path.
func fileURItoPath(uri string) (string, error) {
	if !strings.HasPrefix(uri, "file://") {
		return "", fmt.Errorf("%w: uri %q is not a file:// uri", ErrInvalidSource, uri)
	}
	p := strings.TrimPrefix(uri, "file://")
	if p == "" || !strings.HasPrefix(p, "/") {
		return "", fmt.Errorf("%w: uri %q has no absolute path", ErrInvalidSource, uri)
	}
	clean := filepath.Clean(p)
	if !filepath.IsAbs(clean) {
		return "", fmt.Errorf("%w: uri %q is not absolute", ErrInvalidSource, uri)
	}
	return clean, nil
}

// fileRevision computes a Revision for path. When the file is readable its ID
// is the SHA-256 of the content (reliable equality). Consistency boundary
// (documented): if the file exists but cannot be read, the ID degrades to
// mtime+size, which cannot distinguish same-size writes within the same
// timestamp granularity.
func fileRevision(path string) Revision {
	fi, err := os.Stat(path)
	if err != nil {
		// Not-exists (and, conservatively, other stat failures) -> absent.
		return Revision{}
	}
	data, err := os.ReadFile(path)
	if err == nil {
		sum := sha256.Sum256(data)
		return Revision{Exists: true, ID: hex.EncodeToString(sum[:])}
	}
	return Revision{
		Exists: true,
		ID:     fmt.Sprintf("stat:%d:%d", fi.ModTime().UnixNano(), fi.Size()),
	}
}

var _ Watch = (*FileWatcher)(nil)
