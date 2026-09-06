// Package configwatch provides the Config Watch Adapter: it turns external
// configuration changes observed by Watch into Config Controller Reconcile
// requests.
//
//	Watch -> Change -> Adapter -> Read -> Parse -> Validate -> Reconcile ->
//	Config Controller -> Runtime
//
// The Adapter owns nothing about Component lifecycle or Config Applied state:
// it only calls controller.Reconcile with the latest readable & valid external
// configuration. It never calls Runtime.Load / Fiber.Dispose, never runs HMR,
// never merges sources, and never holds its own Desired/Applied state.
//
// Semantics (v0.1):
//   - one Adapter binds one Source to one Config Controller (source deletion
//     reconciles an empty Config, removing only that controller's components);
//   - initial Sync happens AFTER subscribing to Watch (no lost-change window);
//   - a single processing loop serializes Reconciles; Watch changes are
//     coalesced into a "latest state is dirty" signal (no goroutine per change,
//     bounded memory);
//   - duplicate suppression by content hash of the latest read (adapter-level;
//     Config's own Applied equality is the second layer);
//   - invalid config / read failures leave the applied Runtime state unchanged.
package configwatch

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"errors"
	"fmt"
	"sync"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/watch"
)

// Errors.
var (
	ErrAdapterClosed     = errors.New("config watch adapter closed")
	ErrInvalidSource     = errors.New("invalid config source")
	ErrUnsupportedFormat = errors.New("unsupported config format")
	// ErrSourceNotFound is returned by a Reader when the source is absent. The
	// Adapter treats it as the documented delete semantics (empty Config).
	ErrSourceNotFound = errors.New("config source not found")
)

// Format names the external configuration format.
type Format string

const (
	// FormatTOML is the v0.1 supported format.
	FormatTOML Format = "toml"
)

// Source identifies one external configuration source bound to the adapter.
type Source struct {
	ID     string
	Path   string
	Format Format
}

func (s Source) valid() error {
	if s.ID == "" {
		return errWrap(ErrInvalidSource, "empty source id")
	}
	if s.Path == "" {
		return errWrap(ErrInvalidSource, "source %q has empty path", s.ID)
	}
	switch s.Format {
	case FormatTOML:
		return nil
	default:
		return errWrap(ErrUnsupportedFormat, "source %q format %q", s.ID, s.Format)
	}
}

// Reader reads the raw bytes of a Source. It never reconciles or touches the
// Runtime. A missing source should be reported as ErrSourceNotFound (wrapped).
type Reader interface {
	Read(ctx context.Context, source Source) ([]byte, error)
}

// Parser converts raw source bytes into a config.Config. It never touches the
// Runtime or the Config Controller.
type Parser interface {
	Parse(ctx context.Context, source Source, data []byte) (config.Config, error)
}

// Option configures an Adapter.
type Option func(*Adapter)

// WithReader overrides the default file Reader.
func WithReader(r Reader) Option { return func(a *Adapter) { a.reader = r } }

// WithParser overrides the default TOML Parser.
func WithParser(p Parser) Option { return func(a *Adapter) { a.parser = p } }

// Stats is a minimal, non-authoritative observability counter set.
type Stats struct {
	ChangesReceived    uint64
	ChangesSuppressed  uint64
	SyncSucceeded      uint64
	SyncFailed         uint64
	ReconcileSucceeded uint64
	ReconcileFailed    uint64
	ReadFailed         uint64
	ParseFailed        uint64
	LastError          error
}

// Adapter is the Config Watch Adapter.
type Adapter struct {
	source     Source
	controller *config.Controller
	reader     Reader
	parser     Parser
	sub        watch.Subscription

	base   context.Context
	cancel context.CancelFunc

	mu   sync.Mutex
	cond *sync.Cond
	// closed and pending are the ONLY internal lifecycle-ish flags allowed
	// (Running/Closing/Closed). There is deliberately no Applied/Desired state
	// here.
	closed  bool
	pending bool
	syncs   []*syncRequest

	// lastHash is the content hash of the last state successfully reconciled
	// (adapter-level duplicate suppression only).
	lastHash string

	workerDone   chan struct{}
	listenerDone chan struct{}
	listenerOn   bool

	stats Stats
}

type syncRequest struct {
	ctx context.Context
	res chan error
}

// New creates an Adapter for source bound to controller. It subscribes to the
// given Watch immediately (subscription first, sync second - no lost-change
// window).
func New(source Source, controller *config.Controller, w watch.Watch, opts ...Option) (*Adapter, error) {
	if err := source.valid(); err != nil {
		return nil, err
	}
	if controller == nil {
		return nil, errWrap(ErrInvalidSource, "nil config controller")
	}
	if w == nil {
		return nil, errWrap(ErrInvalidSource, "nil watch")
	}

	base, cancel := context.WithCancel(context.Background())
	a := &Adapter{
		source:     source,
		controller: controller,
		base:       base,
		cancel:     cancel,
	}
	a.cond = sync.NewCond(&a.mu)
	a.reader = newFileReader()
	a.parser = NewTOMLParser()
	for _, o := range opts {
		if o != nil {
			o(a)
		}
	}

	sub, err := w.Watch(base, watch.Source{
		ID:   source.ID,
		Kind: "file",
		URI:  "file://" + source.Path,
	})
	if err != nil {
		cancel()
		return nil, fmt.Errorf("configwatch: subscribe %q: %w", source.ID, err)
	}
	a.sub = sub

	a.workerDone = make(chan struct{})
	a.listenerDone = make(chan struct{})
	go a.worker()
	return a, nil
}

// Sync reads, parses, and reconciles the CURRENT external state once. It
// returns the outcome of that attempt. Sync never suppresses duplicates: the
// Config Controller's own equality decides whether a mutation is needed.
func (a *Adapter) Sync(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	req := &syncRequest{ctx: ctx, res: make(chan error, 1)}
	a.mu.Lock()
	if a.closed {
		a.mu.Unlock()
		return ErrAdapterClosed
	}
	a.syncs = append(a.syncs, req)
	a.cond.Signal()
	a.mu.Unlock()

	select {
	case err := <-req.res:
		if err == nil {
			a.bump(func(s *Stats) { s.SyncSucceeded++ })
		} else {
			a.bump(func(s *Stats) { s.SyncFailed++ })
			a.setLastError(err)
		}
		return err
	case <-ctx.Done():
		return ctx.Err()
	}
}

// Run consumes Watch changes until ctx is done, the subscription closes, or
// the Adapter is closed. All processing happens on the single worker loop; a
// change only marks "latest state is dirty" (bounded, coalesced - no goroutine
// per change).
func (a *Adapter) Run(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	a.mu.Lock()
	if a.closed {
		a.mu.Unlock()
		return ErrAdapterClosed
	}
	if a.listenerOn {
		a.mu.Unlock()
		return errors.New("configwatch: Run already started")
	}
	a.listenerOn = true
	a.mu.Unlock()

	go a.listener(ctx)

	select {
	case <-ctx.Done():
		return ctx.Err()
	case <-a.base.Done():
		return ErrAdapterClosed
	case <-a.listenerDone:
		return nil
	}
}

func (a *Adapter) listener(ctx context.Context) {
	defer close(a.listenerDone)
	for {
		select {
		case <-ctx.Done():
			return
		case <-a.base.Done():
			return
		case ch, ok := <-a.sub.Changes():
			if !ok {
				return
			}
			a.noteChange(ch)
		}
	}
}

func (a *Adapter) noteChange(ch watch.Change) {
	a.bump(func(s *Stats) { s.ChangesReceived++ })
	a.mu.Lock()
	if a.closed {
		a.mu.Unlock()
		return
	}
	// Intake duplicate suppression using the Watch-provided revision: if the
	// change's revision equals the state we already reconciled, ignore it.
	if ch.Current.ID != "" && ch.Current.ID == a.lastHash {
		a.stats.ChangesSuppressed++
		a.mu.Unlock()
		return
	}
	a.pending = true
	a.cond.Signal()
	a.mu.Unlock()
}

// worker is the single processing loop: serializes Sync requests and coalesced
// change processing, and never runs Read/Parse/Reconcile under a lock.
func (a *Adapter) worker() {
	defer close(a.workerDone)
	for {
		a.mu.Lock()
		for !a.closed && !a.pending && len(a.syncs) == 0 {
			a.cond.Wait()
		}
		if a.closed {
			for _, s := range a.syncs {
				s.res <- ErrAdapterClosed
			}
			a.syncs = nil
			a.mu.Unlock()
			return
		}
		var req *syncRequest
		if len(a.syncs) > 0 {
			req = a.syncs[0]
			a.syncs = a.syncs[1:]
		} else {
			a.pending = false
		}
		a.mu.Unlock()

		if req != nil {
			// Explicit sync: never suppressed by hash (Config equality decides).
			err := a.process(req.ctx, false)
			req.res <- err
			continue
		}
		// Coalesced change: process latest state.
		_ = a.process(a.base, true)
	}
}

// process reads the latest source state and reconciles it. fromChange enables
// adapter-level duplicate suppression by content hash.
func (a *Adapter) process(ctx context.Context, fromChange bool) error {
	data, err := a.reader.Read(ctx, a.source)
	if err != nil {
		if errors.Is(err, ErrSourceNotFound) {
			return a.applyEmpty(ctx, fromChange)
		}
		a.bump(func(s *Stats) { s.ReadFailed++ })
		a.setLastError(fmt.Errorf("read config source %q: %w", a.source.ID, err))
		return err
	}

	hash := contentHash(data)
	if fromChange {
		a.mu.Lock()
		same := hash == a.lastHash
		a.mu.Unlock()
		if same {
			return nil // duplicate suppression (no unnecessary reconcile)
		}
	}

	cfg, err := a.parser.Parse(ctx, a.source, data)
	if err != nil {
		a.bump(func(s *Stats) { s.ParseFailed++ })
		a.setLastError(fmt.Errorf("parse config source %q: %w", a.source.ID, err))
		return err
	}

	if err := a.reconcile(ctx, cfg); err != nil {
		return err
	}
	a.mu.Lock()
	a.lastHash = hash
	a.mu.Unlock()
	return nil
}

func (a *Adapter) applyEmpty(ctx context.Context, fromChange bool) error {
	const emptyMarker = "\x00<empty>"
	if fromChange {
		a.mu.Lock()
		same := a.lastHash == emptyMarker
		a.mu.Unlock()
		if same {
			return nil
		}
	}
	if err := a.reconcile(ctx, config.Config{}); err != nil {
		return err
	}
	a.mu.Lock()
	a.lastHash = emptyMarker
	a.mu.Unlock()
	return nil
}

func (a *Adapter) reconcile(ctx context.Context, cfg config.Config) error {
	if err := a.controller.Reconcile(ctx, cfg); err != nil {
		a.bump(func(s *Stats) { s.ReconcileFailed++ })
		a.setLastError(fmt.Errorf("reconcile config source %q: %w", a.source.ID, err))
		return err
	}
	a.bump(func(s *Stats) { s.ReconcileSucceeded++ })
	return nil
}

// Close stops the Adapter: no new work is accepted, the current processing may
// finish, and the Watch subscription is closed. Close is idempotent. The Config
// Controller is NOT closed (it belongs to the application).
func (a *Adapter) Close() error {
	return a.CloseContext(context.Background())
}

// CloseContext is Close with a bounded wait. On timeout it returns ctx.Err()
// while the Adapter remains in its closing state.
func (a *Adapter) CloseContext(ctx context.Context) error {
	if ctx == nil {
		ctx = context.Background()
	}
	a.mu.Lock()
	if !a.closed {
		a.closed = true
		a.cancel()
		a.cond.Broadcast()
	}
	a.mu.Unlock()

	_ = a.sub.Close()

	select {
	case <-a.workerDone:
	case <-ctx.Done():
		return ctx.Err()
	}
	return nil
}

// Stats returns a copy of the observability counters.
func (a *Adapter) Stats() Stats {
	a.mu.Lock()
	defer a.mu.Unlock()
	return a.stats
}

func (a *Adapter) bump(fn func(*Stats)) {
	a.mu.Lock()
	fn(&a.stats)
	a.mu.Unlock()
}

func (a *Adapter) setLastError(err error) {
	a.mu.Lock()
	a.stats.LastError = err
	a.mu.Unlock()
}

func contentHash(data []byte) string {
	sum := sha256.Sum256(data)
	return hex.EncodeToString(sum[:])
}

func errWrap(base error, format string, args ...any) error {
	return fmt.Errorf("%w: %s", base, fmt.Sprintf(format, args...))
}
