package configwatch_test

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/configwatch"
)

// A9 — WithReconcileTimeout: a post-reconcile hook that never resolves must
// fail the attempt with a deadline error instead of blocking the processing
// loop (and every queued Sync) forever.
func TestA9ReconcileTimeout(t *testing.T) {
	release := make(chan struct{})
	e := newEnv(t, tomlComp("cam", "camera", "night"),
		configwatch.WithReconcileTimeout(80*time.Millisecond),
		configwatch.WithPostReconcile(func(ctx context.Context, _ config.Config) error {
			select {
			case <-release:
				return nil
			case <-ctx.Done():
				return ctx.Err()
			}
		}),
	)

	err := e.adapter.Sync(ctxT(t))
	if err == nil {
		t.Fatal("Sync: want deadline error, got nil")
	}
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("want context.DeadlineExceeded, got %v", err)
	}
	if !strings.Contains(err.Error(), "timed out after") {
		t.Fatalf("error should name the configured limit: %v", err)
	}

	// The loop survives: with the hook released, a later Sync succeeds.
	close(release)
	err = e.adapter.Sync(ctxT(t))
	if err != nil {
		t.Fatalf("Sync after release: %v", err)
	}
	waitOwned(t, e, 1)
	waitActive(t, e)
}

// A10 — the default (no timeout option) stays unbounded: a blocked hook keeps
// Sync waiting, honouring the caller's own context cancellation.
func TestA10ReconcileUnboundedByDefault(t *testing.T) {
	release := make(chan struct{})
	defer close(release)
	e := newEnv(t, tomlComp("cam", "camera", "night"),
		configwatch.WithPostReconcile(func(ctx context.Context, _ config.Config) error {
			select {
			case <-release:
				return nil
			case <-ctx.Done():
				return ctx.Err()
			}
		}),
	)

	syncCtx, cancel := context.WithTimeout(context.Background(), 150*time.Millisecond)
	defer cancel()
	err := e.adapter.Sync(syncCtx)
	if !errors.Is(err, context.DeadlineExceeded) {
		t.Fatalf("want caller ctx deadline, got %v", err)
	}
}
