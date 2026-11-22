package transport

import (
	"context"
	"errors"
	"strings"
	"sync"
	"testing"
	"time"
)

// gateWriter blocks inside Write so tests can observe which lock is held while
// a pipe write is in flight. The production bug this pins: cancelShutdown in
// console/procplugin used to write under the pending-map mutex while ordinary
// RPC writes used writeMu, so the two could interleave on stdout.
type gateWriter struct {
	mu      sync.Mutex
	writes  []string
	entered chan struct{}
	release chan struct{}
}

func newGateWriter() *gateWriter {
	return &gateWriter{entered: make(chan struct{}, 8), release: make(chan struct{})}
}

func (g *gateWriter) Write(p []byte) (int, error) {
	g.mu.Lock()
	g.writes = append(g.writes, string(p))
	g.mu.Unlock()
	select {
	case g.entered <- struct{}{}:
	default:
	}
	<-g.release
	return len(p), nil
}

func (g *gateWriter) Close() error { return nil }

func (g *gateWriter) count() int {
	g.mu.Lock()
	defer g.mu.Unlock()
	return len(g.writes)
}

// TestShutdownWriteUsesSingleWriteLock proves every stdin write — including
// the graceful-shutdown request issued from exec's Cancel hook — passes through
// the same write lock, and that the pending/state lock is NOT held while a
// blocked pipe write is in flight.
func TestShutdownWriteUsesSingleWriteLock(t *testing.T) {
	w := newGateWriter()
	exited := make(chan struct{})
	c := newConn(nil, w, exited, options{shutdownWait: 10 * time.Second})

	callDone := make(chan error, 1)
	go func() {
		callDone <- c.Call(context.Background(), "echo", map[string]any{"n": 1}, nil)
	}()
	select {
	case <-w.entered:
	case <-time.After(2 * time.Second):
		t.Fatal("RPC write never reached stdin")
	}
	if got := w.count(); got != 1 {
		t.Fatalf("writes after RPC started = %d, want 1", got)
	}

	// The shutdown request must queue behind the in-flight write.
	shutdownDone := make(chan struct{})
	go func() {
		c.RequestShutdown()
		close(shutdownDone)
	}()
	select {
	case <-shutdownDone:
		t.Fatal("shutdown write completed while an RPC write was still in flight")
	case <-time.After(60 * time.Millisecond):
	}
	if got := w.count(); got != 1 {
		t.Fatalf("shutdown interleaved with RPC write: writes = %d, want 1", got)
	}

	close(exited)
	close(w.release)
	select {
	case <-callDone:
	case <-time.After(2 * time.Second):
		t.Fatal("call did not unblock after exit/write release")
	}
	select {
	case <-shutdownDone:
	case <-time.After(2 * time.Second):
		t.Fatal("shutdown did not return after write release")
	}
	if got := w.count(); got != 2 {
		t.Fatalf("writes after shutdown = %d, want 2 (one RPC + one shutdown)", got)
	}
}

// TestStateLockNotHeldDuringWrite proves a wedged pipe cannot wedge pending
// failure bookkeeping: markDead (state lock) must complete while a write is
// blocked on the write lock.
func TestStateLockNotHeldDuringWrite(t *testing.T) {
	w := newGateWriter()
	c := newConn(nil, w, make(chan struct{}), options{shutdownWait: time.Second})
	callDone := make(chan error, 1)
	go func() {
		callDone <- c.Call(context.Background(), "echo", nil, nil)
	}()
	select {
	case <-w.entered:
	case <-time.After(2 * time.Second):
		t.Fatal("RPC write never reached stdin")
	}
	deadDone := make(chan struct{})
	go func() {
		c.markDead(errors.New("simulated exit"))
		close(deadDone)
	}()
	select {
	case <-deadDone:
	case <-time.After(time.Second):
		t.Fatal("markDead blocked behind a blocking pipe write")
	}
	// Release the wedged pipe: a Call already inside write() only observes
	// the sticky failure once the write lock is available again.
	close(w.release)
	select {
	case err := <-callDone:
		if err == nil || !strings.Contains(err.Error(), "simulated exit") {
			t.Fatalf("call error = %v, want sticky dead error", err)
		}
	case <-time.After(2 * time.Second):
		t.Fatal("pending call was not failed by markDead")
	}
}
