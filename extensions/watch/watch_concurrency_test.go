package watch_test

import (
	"context"
	"errors"
	"fmt"
	"os"
	"path/filepath"
	"runtime"
	"sync"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/extensions/watch"
)

// P4/P5 — subscription isolation + idempotent close are covered deterministically
// by the engine tests; here we verify at the FileWatcher level that closing one
// subscription leaves another (same source) fully functional.
func TestP4FileSubscriptionIsolation(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "f.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := newFileWatcher(t)
	s1 := mustWatchFile(t, w, "s", path)
	s2 := mustWatchFile(t, w, "s", path)
	time.Sleep(300 * time.Millisecond)

	// Both independent subscriptions observe modifications (a few sequential
	// writes tolerate occasional OS-event coalescing).
	saw1, saw2 := false, false
	for i := 0; i < 6 && (!saw1 || !saw2); i++ {
		if err := os.WriteFile(path, []byte(fmt.Sprintf("v%d", i)), 0o644); err != nil {
			t.Fatal(err)
		}
		if !saw1 {
			if _, ok := waitChange(t, s1, 3*time.Second, func(c watch.Change) bool { return c.Kind == watch.ChangeModified }); ok {
				saw1 = true
			}
		}
		if !saw2 {
			if _, ok := waitChange(t, s2, 3*time.Second, func(c watch.Change) bool { return c.Kind == watch.ChangeModified }); ok {
				saw2 = true
			}
		}
	}
	if !saw1 || !saw2 {
		t.Fatalf("subscriptions missed changes: s1=%v s2=%v", saw1, saw2)
	}

	if err := s1.Close(); err != nil {
		t.Fatal(err)
	}
	if err := s1.Close(); err != nil {
		t.Fatalf("second close: %v", err)
	}
	// After closing s1, s2 keeps observing modifications independently.
	saw2 = false
	for i := 0; i < 6 && !saw2; i++ {
		if err := os.WriteFile(path, []byte(fmt.Sprintf("after-%d", i)), 0o644); err != nil {
			t.Fatal(err)
		}
		if _, ok := waitChange(t, s2, 3*time.Second, func(c watch.Change) bool { return c.Kind == watch.ChangeModified }); ok {
			saw2 = true
		}
	}
	if !saw2 {
		t.Fatal("s2 was affected by s1 close")
	}
	_ = s2.Close()
}

// W12 — context cancellation terminates a running file subscription with the
// context error.
func TestW12FileContextCancellation(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "f.txt")
	if err := os.WriteFile(path, []byte("one"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := watch.NewFileWatcher()
	t.Cleanup(func() { _ = w.CloseContext(fileCtx(t)) })

	ctx, cancel := context.WithCancel(context.Background())
	src := watch.Source{ID: "s", Kind: "file", URI: "file://" + path}
	sub, err := w.Watch(ctx, src)
	if err != nil {
		if errors.Is(err, watch.ErrUnsupportedPlatform) {
			t.Skip("unsupported platform")
		}
		t.Fatalf("Watch: %v", err)
	}
	cancel()
	deadline := time.Now().Add(5 * time.Second)
	for sub.Err() == nil && time.Now().Before(deadline) {
		time.Sleep(10 * time.Millisecond)
	}
	if !errors.Is(sub.Err(), context.Canceled) {
		t.Fatalf("Err = %v, want context.Canceled", sub.Err())
	}
}

// P6 — Watch Close conserves every subscription (covered by W11 for one; here
// for many).
func TestP6WatchCloseClosesManySubscriptions(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "f.txt")
	if err := os.WriteFile(path, []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := watch.NewFileWatcher()
	var subs []watch.Subscription
	for i := 0; i < 8; i++ {
		sub := mustWatchFile(t, w, fmt.Sprintf("s%d", i), path)
		subs = append(subs, sub)
	}
	if err := w.Close(); err != nil {
		t.Fatalf("Close: %v", err)
	}
	for _, s := range subs {
		select {
		case _, open := <-s.Changes():
			if open {
				t.Fatal("subscription still open after Watch Close")
			}
		case <-time.After(3 * time.Second):
			t.Fatal("subscription did not close after Watch Close")
		}
	}
}

// P7 — no goroutine leak: repeated create/watch/close cycles drain all
// Watch-owned goroutines (watcher.Close waits for them).
func TestP7NoGoroutineLeak(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "f.txt")
	if err := os.WriteFile(path, []byte("x"), 0o644); err != nil {
		t.Fatal(err)
	}

	before := runtime.NumGoroutine()
	for i := 0; i < 20; i++ {
		w := watch.NewFileWatcher()
		sub := mustWatchFile(t, w, "s", path)
		time.Sleep(300 * time.Millisecond) // let the native watcher register
		_ = os.WriteFile(path, []byte(fmt.Sprintf("content-%d", i)), 0o644)
		// Drain at least one change to exercise the full pipeline.
		_, _ = waitChange(t, sub, 8*time.Second, func(c watch.Change) bool {
			return c.Kind == watch.ChangeAdded || c.Kind == watch.ChangeModified
		})
		_ = sub.Close()
		if err := w.Close(); err != nil {
			t.Fatalf("Close: %v", err)
		}
	}

	// Give goroutines a moment to fully unwind, then compare.
	deadline := time.Now().Add(5 * time.Second)
	after := runtime.NumGoroutine()
	for after > before+4 && time.Now().Before(deadline) {
		time.Sleep(20 * time.Millisecond)
		after = runtime.NumGoroutine()
	}
	if after > before+4 {
		t.Fatalf("goroutine count grew from %d to %d (leak?)", before, after)
	}
}

// P8 — concurrency safety: Watch / subscription Close / file modification /
// Watch Close all race without panic, deadlock, or race (validated by -race).
func TestP8ConcurrencySafety(t *testing.T) {
	dir := t.TempDir()
	path := filepath.Join(dir, "f.txt")
	if err := os.WriteFile(path, []byte("base"), 0o644); err != nil {
		t.Fatal(err)
	}
	w := watch.NewFileWatcher()

	stop := make(chan struct{})
	var wg sync.WaitGroup

	// Modifier.
	wg.Add(1)
	go func() {
		defer wg.Done()
		i := 0
		for {
			select {
			case <-stop:
				return
			default:
			}
			_ = os.WriteFile(path, []byte(fmt.Sprintf("v%d", i)), 0o644)
			i++
			time.Sleep(time.Millisecond)
		}
	}()

	// Subscriber churn (bounded: each goroutine creates a limited number of
	// subscriptions so the kqueue watcher sessions stay manageable).
	var closedSubs atomic.Int32
	for g := 0; g < 3; g++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for i := 0; i < 60; i++ {
				select {
				case <-stop:
					return
				default:
				}
				ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
				sub, err := w.Watch(ctx, watch.Source{ID: "s", Kind: "file", URI: "file://" + path})
				cancel()
				if err != nil {
					if errors.Is(err, watch.ErrWatchClosed) || errors.Is(err, watch.ErrUnsupportedPlatform) {
						return
					}
					continue
				}
				closedSubs.Add(1)
				_ = sub.Close()
			}
		}()
	}

	time.Sleep(100 * time.Millisecond)
	_ = w.Close() // closes all subscriptions + waits goroutines
	close(stop)
	wg.Wait()
	if closedSubs.Load() == 0 {
		t.Fatal("no subscription was created during the race")
	}
	// After Close, new Watch calls are deterministically rejected.
	if _, err := w.Watch(fileCtx(t), watch.Source{ID: "late", Kind: "file", URI: "file://" + path}); !errors.Is(err, watch.ErrWatchClosed) {
		t.Fatalf("Watch after Close = %v, want ErrWatchClosed", err)
	}
}
