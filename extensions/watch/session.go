package watch

import (
	"context"
	"time"
)

// sessionAction describes why a session wait returned.
type sessionAction int

const (
	actionEvent sessionAction = iota // observed something; re-probe
	actionStop                       // subscription closed
	actionCtx                        // context canceled
	actionFatal                      // terminal watcher error (err set)
)

// sessionWait blocks until an observation event, subscription stop, context
// cancellation, or a fatal error occurs. Implementations must return promptly
// after stop/ctx so Close has bounded latency.
type sessionWait func(ctx context.Context, stopped <-chan struct{}) (sessionAction, error)

// runSession is the shared observation loop:
//
//	baseline revision (no initial change) -> wait -> re-probe -> dedup ->
//	classify Previous->Current -> publish (coalescing) -> repeat.
func runSession(ctx context.Context, sub *subscription, source Source, probe func() Revision, wait sessionWait) {
	// Baseline: capture the current revision without emitting a fake change.
	runSessionFrom(ctx, sub, source, probe(), probe, wait)
}

// runSessionFrom is runSession with an externally captured baseline revision.
// Concrete watchers that register native notifications before starting should
// capture the baseline first so no event can fall between baseline and
// registration.
func runSessionFrom(ctx context.Context, sub *subscription, source Source, last Revision, probe func() Revision, wait sessionWait) {
	for {
		action, fatalErr := wait(ctx, sub.stopped())
		switch action {
		case actionCtx:
			sub.finish(ctx.Err())
			return
		case actionFatal:
			if fatalErr == nil {
				fatalErr = ErrSourceNotFound
			}
			sub.finish(fatalErr)
			return
		case actionStop:
			sub.finish(nil)
			return
		}

		// actionEvent: something may have changed; recompute and dedup.
		cur := probe()
		if cur == last {
			continue
		}
		kind, err := classify(last, cur)
		if err != nil {
			sub.finish(err)
			return
		}
		prev := last
		last = cur
		sub.publish(Change{
			SourceID:   source.ID,
			Kind:       kind,
			URI:        source.URI,
			Previous:   prev,
			Current:    cur,
			ObservedAt: time.Now(),
		})
	}
}
