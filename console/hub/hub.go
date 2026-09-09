// Package hub is the console's data and command junction. Applications
// register named queries and commands and publish observations; the console
// transports (HTTP/SSE, Wails) serve them. Registering is an Effect owned by
// the calling activation — the returned unregister is idempotent, so a
// late or stale cleanup can never remove a newer activation's entry.
//
// The hub is deliberately domain-free: it knows names and handlers, never
// what "collections" or "failures" mean. Domain vocabulary lives in the
// applications that register here.
package hub

import (
	"context"
	"encoding/json"
	"net/url"
	"sort"
	"sync"
)

// Error is the transport-neutral failure shape returned by handlers.
type Error struct {
	Code    string `json:"code"`
	Message string `json:"message"`
}

func (e *Error) Error() string { return e.Code + ": " + e.Message }

// QueryHandler answers a named console query. params carries the request's
// query parameters; the result is encoded by the transport as JSON "data".
type QueryHandler func(ctx context.Context, params url.Values) (any, *Error)

// CommandHandler executes a named console command with a JSON body. It
// returns after the command is accepted; completion travels as observation.
type CommandHandler func(ctx context.Context, body json.RawMessage) error

// Observation is the minimal invalidation message published to consoles.
// It never carries full state; consumers re-query.
type Observation struct {
	Type      string `json:"type"`
	SourceID  string `json:"sourceId,omitempty"`
	Timestamp string `json:"timestamp"`
}

type queryEntry struct {
	handler QueryHandler
	owner   string
}

type commandEntry struct {
	handler CommandHandler
	owner   string
}

// Registry is the mutable face of the hub.
type Registry struct {
	mu        sync.RWMutex
	queries   map[string]queryEntry
	commands  map[string]commandEntry
	observers map[int]chan Observation
	nextID    int
}

func New() *Registry {
	return &Registry{
		queries:   map[string]queryEntry{},
		commands:  map[string]commandEntry{},
		observers: map[int]chan Observation{},
	}
}

// RegisterQuery installs a named query handler. Registering the same name
// twice fails without disturbing the existing owner.
func (r *Registry) RegisterQuery(name string, owner string, h QueryHandler) (func() error, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, exists := r.queries[name]; exists {
		return nil, &Error{Code: "invalid_request", Message: "query " + name + " already registered"}
	}
	r.queries[name] = queryEntry{handler: h, owner: owner}
	removed := false
	return func() error {
		r.mu.Lock()
		defer r.mu.Unlock()
		if !removed {
			if cur, ok := r.queries[name]; ok && cur.owner == owner {
				delete(r.queries, name)
			}
			removed = true
		}
		return nil
	}, nil
}

// RegisterCommand installs a named command handler.
func (r *Registry) RegisterCommand(name string, owner string, h CommandHandler) (func() error, error) {
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, exists := r.commands[name]; exists {
		return nil, &Error{Code: "invalid_request", Message: "command " + name + " already registered"}
	}
	r.commands[name] = commandEntry{handler: h, owner: owner}
	removed := false
	return func() error {
		r.mu.Lock()
		defer r.mu.Unlock()
		if !removed {
			if cur, ok := r.commands[name]; ok && cur.owner == owner {
				delete(r.commands, name)
			}
			removed = true
		}
		return nil
	}, nil
}

// Query resolves a query handler.
func (r *Registry) Query(name string) (QueryHandler, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	e, ok := r.queries[name]
	return e.handler, ok
}

// Command resolves a command handler.
func (r *Registry) Command(name string) (CommandHandler, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	e, ok := r.commands[name]
	return e.handler, ok
}

// QueryNames returns the registered query names, sorted. Fleet/inspection
// surfaces use it to describe what a host's console serves.
func (r *Registry) QueryNames() []string {
	r.mu.RLock()
	defer r.mu.RUnlock()
	out := make([]string, 0, len(r.queries))
	for name := range r.queries {
		out = append(out, name)
	}
	sort.Strings(out)
	return out
}

// CommandNames returns the registered command names, sorted.
func (r *Registry) CommandNames() []string {
	r.mu.RLock()
	defer r.mu.RUnlock()
	out := make([]string, 0, len(r.commands))
	for name := range r.commands {
		out = append(out, name)
	}
	sort.Strings(out)
	return out
}

// OnObservation subscribes to the observation stream. The returned
// unregister stops the delivery; buffered messages are dropped, never
// replayed — consumers recover by re-querying.
func (r *Registry) OnObservation(buffer int, fn func(Observation)) (func() error, error) {
	if buffer <= 0 {
		buffer = 8
	}
	ch := make(chan Observation, buffer)
	r.mu.Lock()
	r.nextID++
	id := r.nextID
	r.observers[id] = ch
	r.mu.Unlock()
	go func() {
		for ev := range ch {
			fn(ev)
		}
	}()
	unsub := func() error {
		r.mu.Lock()
		defer r.mu.Unlock()
		if c, ok := r.observers[id]; ok {
			delete(r.observers, id)
			close(c)
		}
		return nil
	}
	return unsub, nil
}

// Publish fans one observation out to every subscriber. Delivery is
// non-blocking: a slow consumer drops the message (observation only
// invalidates; consumers compensate by re-querying).
func (r *Registry) Publish(ev Observation) {
	r.mu.RLock()
	chans := make([]chan Observation, 0, len(r.observers))
	for _, c := range r.observers {
		chans = append(chans, c)
	}
	r.mu.RUnlock()
	for _, c := range chans {
		select {
		case c <- ev:
		default:
		}
	}
}
