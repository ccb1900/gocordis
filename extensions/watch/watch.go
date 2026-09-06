// Package watch provides the Runtime Watch Extension: observing external
// resources and converting observed changes into normalized Change values.
//
// Watch only observes: it never reconciles Config, loads/unloads Modules, runs
// HMR, touches Fibers, uses the Event Bus, or schedules anything. Concrete
// v0.1 source: file (native event-driven watching; no polling as the main
// mechanism).
//
// Watch is an Extension, not part of the Kernel:
//   - this package does not import the runtime package;
//   - no Config/Loader/Event/Scheduler integration;
//   - Watch Change is NOT an Event Bus Event and NOT a Registry change.
//
// Subscription semantics (v0.1):
//   - future changes only (no initial fake "Added", no replay);
//   - per-subscription ordering equals the observation linearization order;
//   - logical deduplication (same revision -> no repeated change);
//   - bounded buffers with latest-state coalescing (slow consumers never block
//     the watcher and never lose the final revision);
//   - no user callbacks.
package watch

import (
	"context"
	"errors"
	"fmt"
	"time"
)

// Errors. Compare with errors.Is.
var (
	ErrWatchClosed        = errors.New("watch closed")
	ErrSubscriptionClosed = errors.New("subscription closed")
	ErrInvalidSource      = errors.New("invalid watch source")
	ErrInvalidChange      = errors.New("invalid watch change")
	ErrSourceNotFound     = errors.New("watch source not found")
	ErrUnsupportedKind    = errors.New("unsupported watch source kind")
	// ErrUnsupportedPlatform is retained for API compatibility. v0.1 watching
	// is implemented on the supported platforms via fsnotify
	// (ReadDirectoryChangesW on Windows, kqueue on macOS).
	ErrUnsupportedPlatform = errors.New("watch: native file watching unsupported on this platform")
)

// DefaultBufferSize is the bounded per-subscription change buffer.
const DefaultBufferSize = 64

// Source describes an external observation target.
type Source struct {
	// ID is the Watch-internal stable logical identity (stable even if the URI
	// changes; Source Identity is NOT a Module/Fiber/Subscription identity).
	ID string
	// Kind is the kind of the observed object (v0.1 concrete: "file").
	Kind string
	// URI is an opaque resource identifier (e.g. "file:///abs/path").
	URI string
}

func (s Source) valid() error {
	if s.ID == "" {
		return fmt.Errorf("%w: empty source id", ErrInvalidSource)
	}
	if s.Kind == "" {
		return fmt.Errorf("%w: source %q has empty kind", ErrInvalidSource, s.ID)
	}
	if s.URI == "" {
		return fmt.Errorf("%w: source %q has empty uri", ErrInvalidSource, s.ID)
	}
	return nil
}

// ChangeKind classifies a logical state transition.
type ChangeKind uint8

const (
	// ChangeAdded: Previous.Exists=false, Current.Exists=true.
	ChangeAdded ChangeKind = iota
	// ChangeModified: both exist and Previous.ID != Current.ID.
	ChangeModified
	// ChangeRemoved: Previous.Exists=true, Current.Exists=false.
	ChangeRemoved
)

func (k ChangeKind) String() string {
	switch k {
	case ChangeAdded:
		return "added"
	case ChangeModified:
		return "modified"
	case ChangeRemoved:
		return "removed"
	default:
		return "unknown"
	}
}

// Revision identifies one observed state of the target. The Watch core only
// requires that two different contents yield different Revision.ID values.
type Revision struct {
	Exists bool
	ID     string
}

// Change is an immutable value describing Previous -> Current.
type Change struct {
	SourceID string
	Kind     ChangeKind
	URI      string

	Previous Revision
	Current  Revision

	ObservedAt time.Time
}

// classify derives the ChangeKind from a legal Previous -> Current transition.
func classify(prev, cur Revision) (ChangeKind, error) {
	switch {
	case !prev.Exists && cur.Exists:
		return ChangeAdded, nil
	case prev.Exists && cur.Exists && prev.ID != cur.ID:
		return ChangeModified, nil
	case prev.Exists && !cur.Exists:
		return ChangeRemoved, nil
	default:
		return 0, fmt.Errorf("%w: invalid transition %+v -> %+v", ErrInvalidChange, prev, cur)
	}
}

// validateChange rejects illegal Change values.
func validateChange(c Change) error {
	if c.SourceID == "" {
		return fmt.Errorf("%w: empty source id", ErrInvalidChange)
	}
	if c.URI == "" {
		return fmt.Errorf("%w: empty uri", ErrInvalidChange)
	}
	switch c.Kind {
	case ChangeAdded:
		if c.Previous.Exists || !c.Current.Exists {
			return fmt.Errorf("%w: Added requires !Previous.Exists && Current.Exists", ErrInvalidChange)
		}
	case ChangeModified:
		if !c.Previous.Exists || !c.Current.Exists || c.Previous.ID == c.Current.ID {
			return fmt.Errorf("%w: Modified requires both exist and different IDs", ErrInvalidChange)
		}
	case ChangeRemoved:
		if !c.Previous.Exists || c.Current.Exists {
			return fmt.Errorf("%w: Removed requires Previous.Exists && !Current.Exists", ErrInvalidChange)
		}
	default:
		return fmt.Errorf("%w: unknown kind %d", ErrInvalidChange, c.Kind)
	}
	return nil
}

// Watch observes Sources. Duplicate Watch calls on the same Source are allowed
// and create independent Subscriptions.
type Watch interface {
	Watch(ctx context.Context, source Source) (Subscription, error)
	Close() error
	CloseContext(ctx context.Context) error
}

// Subscription delivers Changes for one observation.
type Subscription interface {
	// Changes delivers future Changes only (no initial fake Added, no replay).
	Changes() <-chan Change
	// Err returns the terminal error once the subscription has ended: nil for a
	// normal Close, the context error after context cancellation, or the
	// underlying watcher error on failure.
	Err() error
	Close() error
}
