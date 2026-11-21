package fs

import (
	"context"
	"sync"
	"testing"
	"time"
)

// testWatcher is a controllable inner watcher whose Dispose is observable.
type testWatcher struct {
	events   chan WatchEvent
	errs     chan error
	ready    chan struct{}
	disposed chan struct{}
	once     sync.Once
}

func newTestWatcher() *testWatcher {
	return &testWatcher{
		events: make(chan WatchEvent), errs: make(chan error),
		ready: make(chan struct{}), disposed: make(chan struct{}, 2),
	}
}

func (w *testWatcher) Events() <-chan WatchEvent { return w.events }
func (w *testWatcher) Errors() <-chan error      { return w.errs }
func (w *testWatcher) Ready() <-chan struct{}    { return w.ready }
func (w *testWatcher) Dispose() error {
	w.once.Do(func() { w.disposed <- struct{}{} })
	return nil
}

// blockingWatchProvider publishes entered as soon as Watch is called, then
// waits for release before returning the inverse. The watch is registered in
// the service map before Watch is called, so Close can be forced to interleave.
type blockingWatchProvider struct {
	Provider
	entered  chan struct{}
	release  chan struct{}
	inverses chan struct{}
}

func (p *blockingWatchProvider) Watch(_ context.Context, _ string, _ func(WatchEvent) error) (func() error, error) {
	close(p.entered)
	<-p.release
	return func() error { p.inverses <- struct{}{}; return nil }, nil
}

// blockingStreamProvider does the same for the StreamWatcher path.
type blockingStreamProvider struct {
	Provider
	entered chan struct{}
	release chan struct{}
	inner   *testWatcher
}

func (p *blockingStreamProvider) WatchStream(_ context.Context, _ string, _ string) (Watcher, error) {
	close(p.entered)
	<-p.release
	return p.inner, nil
}

func testContext(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 2*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// TestCloseDuringWatchRegistration reproduces the window where Close
// snapshots and clears s.watches after Resolve but before registration.
func TestCloseDuringWatchRegistration(t *testing.T) {
	ctx := testContext(t)
	s := newService()
	p := &blockingWatchProvider{
		Provider: NewMemoryProvider(),
		entered:  make(chan struct{}), release: make(chan struct{}),
		inverses: make(chan struct{}, 2),
	}
	if err := s.Mount("memory:///", p); err != nil {
		t.Fatal(err)
	}
	result := make(chan error, 1)
	go func() {
		inv, err := s.Watch(ctx, "memory:///x", func(WatchEvent) error { return nil })
		if err == nil {
			err = inv()
		}
		result <- err
	}()

	<-p.entered // registration completed under s.mu; provider Watch is mid-flight
	if err := s.Close(); err != nil {
		t.Fatal(err)
	}
	close(p.release)
	select {
	case err := <-result:
		if err == nil {
			t.Fatal("Watch succeeded after Close")
		}
	case <-ctx.Done():
		t.Fatal("watch registration never finished")
	}

	s.mu.RLock()
	n := len(s.watches)
	s.mu.RUnlock()
	if n != 0 {
		t.Fatalf("%d watches survived Close", n)
	}
	select {
	case <-p.inverses: // the registering goroutine disposed the late inverse
	default:
		t.Fatal("late provider watch was not disposed")
	}
}

// TestCloseDuringWatchStreamRegistration covers the same window for
// WatchStream and asserts no forwarder goroutine was left behind.
func TestCloseDuringWatchStreamRegistration(t *testing.T) {
	ctx := testContext(t)
	s := newService()
	inner := newTestWatcher()
	p := &blockingStreamProvider{
		Provider: NewMemoryProvider(),
		entered:  make(chan struct{}), release: make(chan struct{}),
		inner: inner,
	}
	if err := s.Mount("memory:///", p); err != nil {
		t.Fatal(err)
	}
	result := make(chan error, 1)
	go func() {
		w, err := s.WatchStream(ctx, "memory:///x", "")
		if err == nil {
			_ = w.Dispose()
		}
		result <- err
	}()

	<-p.entered // registration completed under s.mu
	s.mu.RLock()
	var captured *liveWatcher
	for w := range s.streams {
		captured = w
	}
	s.mu.RUnlock()
	if captured == nil {
		t.Fatal("stream watch was not registered before the provider returned")
	}
	if err := s.Close(); err != nil {
		t.Fatal(err)
	}
	close(p.release)
	select {
	case err := <-result:
		if err == nil {
			t.Fatal("WatchStream succeeded after Close")
		}
	case <-ctx.Done():
		t.Fatal("stream registration never finished")
	}

	s.mu.RLock()
	n := len(s.streams)
	s.mu.RUnlock()
	if n != 0 {
		t.Fatalf("%d stream watches survived Close", n)
	}
	if captured.fwdDone != nil {
		select {
		case <-captured.fwdDone:
		default:
			t.Fatal("forwarder goroutine survived Close")
		}
	}
	select {
	case <-inner.disposed:
	default:
		t.Fatal("late inner watcher was not disposed")
	}
}

// TestCloseJoinsStartedForwarder checks the other ordering: the forwarder is
// running when Close arrives, so Close must close stopFwd and join fwdDone.
func TestCloseJoinsStartedForwarder(t *testing.T) {
	ctx := testContext(t)
	s := newService()
	inner := newTestWatcher()
	p := &blockingStreamProvider{
		Provider: NewMemoryProvider(),
		entered:  make(chan struct{}), release: make(chan struct{}),
		inner: inner,
	}
	close(p.release) // provider returns immediately
	if err := s.Mount("memory:///", p); err != nil {
		t.Fatal(err)
	}
	w, err := s.WatchStream(ctx, "memory:///x", "")
	if err != nil {
		t.Fatal(err)
	}
	captured := w.(*liveWatcher)
	if err := s.Close(); err != nil {
		t.Fatal(err)
	}
	select {
	case <-captured.fwdDone:
	default:
		t.Fatal("Close did not join the running forwarder")
	}
	select {
	case <-inner.disposed:
	default:
		t.Fatal("inner watcher was not disposed")
	}
}
