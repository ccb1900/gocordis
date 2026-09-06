package watch

import (
	"sync"
	"time"
)

// subscription delivers coalesced, ordered Changes to one consumer through a
// bounded channel.
//
// Architecture: the watcher session publishes the LATEST undelivered Change
// into sub.pending (never blocking, coalescing older ones away). A dedicated
// delivery goroutine moves pending changes into the bounded Changes channel in
// order. A slow consumer therefore only stalls its own delivery goroutine -
// never the watcher - and the newest revision is never lost.
type subscription struct {
	changes chan Change

	mu      sync.Mutex
	pending *Change
	err     error
	closed  bool

	stop chan struct{} // closed by Close -> stops session + delivery
	wake chan struct{} // capacity 1: delivery has a pending change
	done chan struct{} // delivery goroutine exited
}

func newSubscription(buffer int) *subscription {
	s := &subscription{
		changes: make(chan Change, buffer),
		stop:    make(chan struct{}),
		wake:    make(chan struct{}, 1),
		done:    make(chan struct{}),
	}
	go s.deliverLoop()
	return s
}

// publish records the latest change for delivery. It never blocks and never
// touches the Changes channel directly (so Close can never race a send).
func (s *subscription) publish(c Change) {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return
	}
	s.pending = &c
	s.mu.Unlock()
	select {
	case s.wake <- struct{}{}:
	default:
	}
}

func (s *subscription) deliverLoop() {
	defer close(s.done)
	for {
		s.mu.Lock()
		c := s.pending
		s.mu.Unlock()

		if c == nil {
			select {
			case <-s.wake:
				continue
			case <-s.stop:
				return
			}
		}

		select {
		case s.changes <- *c:
			s.mu.Lock()
			if s.pending == c {
				s.pending = nil // clear only if not already superseded
			}
			s.mu.Unlock()
		case <-s.stop:
			return
		}
	}
}

// finish records the terminal error (if any) and closes the subscription. It is
// idempotent and may be called by the session (normal stop / error / context)
// or by the consumer via Close.
func (s *subscription) finish(err error) {
	s.mu.Lock()
	if s.closed {
		s.mu.Unlock()
		return
	}
	s.closed = true
	if err != nil && s.err == nil {
		s.err = err
	}
	close(s.stop)
	s.mu.Unlock()

	<-s.done
	close(s.changes)
}

// Changes returns the delivery channel. It is closed after Close.
func (s *subscription) Changes() <-chan Change { return s.changes }

// Close closes the subscription. After Close returns, no further Change can be
// observed and the Changes channel is closed. Close is idempotent.
func (s *subscription) Close() error {
	s.finish(nil)
	return nil
}

// Err returns the terminal error once the subscription has ended.
func (s *subscription) Err() error {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.err
}

// stopped exposes the stop signal.
func (s *subscription) stopped() <-chan struct{} { return s.stop }

func (s *subscription) deliverDone() <-chan struct{} { return s.done }

var _ = time.Now
