// Package registry provides the Runtime Registry Extension.
//
// A Registry expresses "one stable capability whose internal member set can
// change dynamically". Consumers depend on the Registry identity, never on
// individual members, so member Add/Remove/Replace never reactivates a
// consumer.
//
// Registry is an Extension, not part of the Kernel:
//   - this package does not import the runtime package;
//   - a Registry never creates Fibers, Activations, or Contexts;
//   - membership mutations are plain method calls with their own
//     linearization points and never touch Kernel dependency semantics;
//   - there is exactly one lifecycle: the lifecycle of the Fiber whose
//     activation owns the Registry object (no second lifecycle here).
//
// Exposing a Registry as a Runtime capability follows the stable-provider
// pattern: an application Component creates a *Registry[T] inside Apply,
// provides it under a typed capability key (runtime.Provide), and records
// runtime-managed membership mutations as reversible ctx.Effect operations:
//
//	reg := registry.New[Handler]()
//	if err := runtime.Provide(ctx, handlerKey, reg); err != nil { ... }
//	if err := ctx.Effect(func() (func() error, error) {
//	    if err := reg.Add("http", httpHandler); err != nil { return nil, err }
//	    return func() error { return reg.Remove("http") }, nil
//	}); err != nil { ... }
//
// Consumers then Require the registry capability and read it; member churn
// never invalidates their dependency.
package registry

import (
	"errors"
	"sync"
)

// Errors returned by membership mutations. Compare with errors.Is.
var (
	// ErrMemberExists is returned by Add when the MemberID already exists.
	// Add never overwrites.
	ErrMemberExists = errors.New("registry: member exists")
	// ErrMemberNotFound is returned by Remove/Replace when the MemberID is
	// absent. A failed mutation never changes the registry state.
	ErrMemberNotFound = errors.New("registry: member not found")
)

// MemberID uniquely identifies a member within one Registry.
type MemberID string

// Registry is a concurrency-safe dynamic member set with a stable identity
// (the identity of the object; as a Runtime capability its provider identity
// is owned by the Kernel).
//
// Every mutation (Add/Remove/Replace) and every Snapshot has a single
// linearization point. Lookup methods (Get/Has) linearize individually.
type Registry[T any] struct {
	mu      sync.RWMutex
	members map[MemberID]T

	subs      map[uint64]*Subscription[T]
	nextSubID uint64
}

// New creates an empty Registry.
func New[T any]() *Registry[T] {
	return &Registry[T]{
		members: make(map[MemberID]T),
		subs:    make(map[uint64]*Subscription[T]),
	}
}

// Add inserts a member. It fails with ErrMemberExists (without changing the
// registry) when the MemberID is already present.
func (r *Registry[T]) Add(id MemberID, v T) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, ok := r.members[id]; ok {
		return ErrMemberExists
	}
	r.members[id] = v
	r.notifyLocked(Change[T]{Kind: MemberAdded, ID: id, New: v})
	return nil
}

// Remove deletes a member. It fails with ErrMemberNotFound (without changing
// the registry) when the MemberID is absent. It never removes a different
// member.
func (r *Registry[T]) Remove(id MemberID) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	v, ok := r.members[id]
	if !ok {
		return ErrMemberNotFound
	}
	delete(r.members, id)
	r.notifyLocked(Change[T]{Kind: MemberRemoved, ID: id, Old: v})
	return nil
}

// Replace atomically swaps the value of an existing member (old -> new).
//
// The mutation never removes the member, so no observer can ever see an
// "absent" intermediate state: every Snapshot taken concurrently sees either
// the old or the new value. Replace fails with ErrMemberNotFound when the
// MemberID is absent.
func (r *Registry[T]) Replace(id MemberID, v T) error {
	r.mu.Lock()
	defer r.mu.Unlock()
	old, ok := r.members[id]
	if !ok {
		return ErrMemberNotFound
	}
	r.members[id] = v
	r.notifyLocked(Change[T]{Kind: MemberReplaced, ID: id, Old: old, New: v})
	return nil
}

// Get returns the value for id at a single linearization point.
func (r *Registry[T]) Get(id MemberID) (T, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	v, ok := r.members[id]
	return v, ok
}

// Has reports whether id is present at a single linearization point.
func (r *Registry[T]) Has(id MemberID) bool {
	r.mu.RLock()
	defer r.mu.RUnlock()
	_, ok := r.members[id]
	return ok
}

// Len returns the number of members at a single linearization point.
func (r *Registry[T]) Len() int {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return len(r.members)
}

// Snapshot returns an immutable view of the registry at a single linearization
// point. Later mutations never modify an already-returned Snapshot.
func (r *Registry[T]) Snapshot() Snapshot[T] {
	r.mu.RLock()
	defer r.mu.RUnlock()
	return Snapshot[T]{members: cloneMembers(r.members)}
}

// Subscribe registers a change subscription and returns the exact registry
// snapshot at the subscription's linearization point.
//
// Semantics (v0.1, non-durable):
//   - The returned Snapshot reflects the members present at the subscription
//     point; the event stream delivers exactly the mutations that linearize
//     after that point (no gap, no duplication).
//   - Events are delivered per-subscription in mutation order.
//   - Delivery is best-effort with a bounded buffer: a slow consumer that does
//     not drain may miss events (Registry mutations never block on watchers).
//   - Unsubscribe is idempotent; after it returns, no further events are
//     delivered and the Changes channel is closed (buffered events remain
//     readable until drained).
func (r *Registry[T]) Subscribe() (*Subscription[T], Snapshot[T]) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.nextSubID++
	s := &Subscription[T]{
		reg: r,
		id:  r.nextSubID,
		ch:  make(chan Change[T], subscriptionBuffer),
	}
	r.subs[s.id] = s
	return s, Snapshot[T]{members: cloneMembers(r.members)}
}

// notifyLocked fans a change out to every subscription. It must be called with
// r.mu held. Sends are non-blocking so a slow watcher can never block (or
// deadlock) a registry mutation.
func (r *Registry[T]) notifyLocked(c Change[T]) {
	for _, s := range r.subs {
		select {
		case s.ch <- c:
		default:
			// slow consumer: drop this event (documented, non-durable watch)
		}
	}
}

// subscriptionBuffer is the per-subscription event buffer size.
const subscriptionBuffer = 64

// ChangeKind discriminates the kind of a membership change.
type ChangeKind uint8

const (
	// MemberAdded indicates a member was inserted. Change.New is set.
	MemberAdded ChangeKind = iota
	// MemberRemoved indicates a member was deleted. Change.Old is set.
	MemberRemoved
	// MemberReplaced indicates an existing member's value changed atomically.
	// Both Change.Old and Change.New are set.
	MemberReplaced
)

func (k ChangeKind) String() string {
	switch k {
	case MemberAdded:
		return "added"
	case MemberRemoved:
		return "removed"
	case MemberReplaced:
		return "replaced"
	default:
		return "unknown"
	}
}

// Change describes one membership mutation.
type Change[T any] struct {
	Kind ChangeKind
	ID   MemberID
	Old  T
	New  T
}

// Subscription delivers membership changes for one subscriber.
type Subscription[T any] struct {
	reg       *Registry[T]
	id        uint64
	ch        chan Change[T]
	closeOnce sync.Once
}

// Changes returns the event channel. See Registry.Subscribe for the exact
// delivery semantics.
func (s *Subscription[T]) Changes() <-chan Change[T] { return s.ch }

// Unsubscribe stops future event delivery and closes the Changes channel.
// It is idempotent.
func (s *Subscription[T]) Unsubscribe() {
	s.closeOnce.Do(func() {
		s.reg.mu.Lock()
		if s.reg.subs[s.id] == s {
			delete(s.reg.subs, s.id)
		}
		close(s.ch)
		s.reg.mu.Unlock()
	})
}

// Snapshot is an immutable view of a Registry at one linearization point.
// The zero value is an empty snapshot.
type Snapshot[T any] struct {
	members map[MemberID]T
}

// Get returns the value for id in this snapshot.
func (s Snapshot[T]) Get(id MemberID) (T, bool) {
	v, ok := s.members[id]
	return v, ok
}

// Has reports whether id is present in this snapshot.
func (s Snapshot[T]) Has(id MemberID) bool {
	_, ok := s.members[id]
	return ok
}

// Len returns the number of members in this snapshot.
func (s Snapshot[T]) Len() int { return len(s.members) }

// Range iterates the snapshot. The iteration order is unspecified and MUST NOT
// be relied upon (the registry provides no implicit ordering guarantee in
// v0.1). Range stops early when fn returns false.
func (s Snapshot[T]) Range(fn func(MemberID, T) bool) {
	for id, v := range s.members {
		if !fn(id, v) {
			return
		}
	}
}

func cloneMembers[T any](m map[MemberID]T) map[MemberID]T {
	out := make(map[MemberID]T, len(m))
	for id, v := range m {
		out[id] = v
	}
	return out
}
