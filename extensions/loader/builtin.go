package loader

import (
	"fmt"
	"sync"

	"dynamic-runtime/extensions/config"
)

// BuiltinFactory produces a Config-semantics Factory. BuiltinFactory is the
// in-process implementation source for the Builtin Loader.
type BuiltinFactory func() config.Factory

// BuiltinRegistry maps Source -> BuiltinFactory. It is concurrency-safe and
// never runs user Factory code under its lock.
type BuiltinRegistry interface {
	Register(source string, factory BuiltinFactory) error
	Lookup(source string) (BuiltinFactory, bool)
}

type builtinRegistry struct {
	mu       sync.RWMutex
	builtins map[string]BuiltinFactory
}

// NewBuiltinRegistry returns an empty BuiltinRegistry.
func NewBuiltinRegistry() BuiltinRegistry {
	return &builtinRegistry{builtins: make(map[string]BuiltinFactory)}
}

// Register registers a BuiltinFactory under source. Duplicate sources fail
// with ErrBuiltinExists and never replace the original.
func (r *builtinRegistry) Register(source string, factory BuiltinFactory) error {
	if source == "" || factory == nil {
		return ErrInvalidBuiltin
	}
	r.mu.Lock()
	defer r.mu.Unlock()
	if _, exists := r.builtins[source]; exists {
		return ErrBuiltinExists
	}
	r.builtins[source] = factory
	return nil
}

// Lookup returns the BuiltinFactory for source without executing it.
func (r *builtinRegistry) Lookup(source string) (BuiltinFactory, bool) {
	r.mu.RLock()
	defer r.mu.RUnlock()
	f, ok := r.builtins[source]
	return f, ok
}

var _ BuiltinRegistry = (*builtinRegistry)(nil)

// callBuiltinFactory invokes a BuiltinFactory and converts a panic into
// ErrModuleLoadPanic.
func callBuiltinFactory(f BuiltinFactory) (factory config.Factory, err error) {
	defer func() {
		if r := recover(); r != nil {
			factory = nil
			err = fmt.Errorf("%w: %v", ErrModuleLoadPanic, r)
		}
	}()
	return f(), nil
}

func errUnknownBuiltin(source string) error {
	return fmt.Errorf("%w: source %q", ErrLoadFailed, source)
}
