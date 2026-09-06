// Package wait provides a condition-generation broadcast signal used to
// implement missed-wakeup-free waiting on Fiber state changes.
package wait

import "sync"

// Signal is a generation-based broadcast signal.
//
// State changes call Broadcast. A waiter reads its condition and the current
// channel atomically (under its own lock), then selects on that channel. If a
// broadcast happens after the read, the captured channel is closed and the
// waiter wakes; if it happens before the read, the waiter observes the new
// state directly. No wakeup can be missed.
type Signal struct {
	mu      sync.Mutex
	version uint64
	ch      chan struct{}
}

// New returns a ready-to-use Signal.
func New() *Signal {
	return &Signal{ch: make(chan struct{})}
}

// Broadcast wakes all waiters that captured the previous generation and
// installs a fresh channel for the next generation.
func (s *Signal) Broadcast() {
	s.mu.Lock()
	s.version++
	close(s.ch)
	s.ch = make(chan struct{})
	s.mu.Unlock()
}

// Channel returns the channel of the current generation. Callers should read
// their condition and this channel while holding whatever lock also guards the
// condition, so the read and the wait are atomic with respect to Broadcast.
func (s *Signal) Channel() <-chan struct{} {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.ch
}

// Version returns the current generation number (diagnostics/tests).
func (s *Signal) Version() uint64 {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.version
}
