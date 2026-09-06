// Package hmr provides the Runtime HMR Extension: safe, in-place replacement of
// a running Component implementation.
//
// HMR decides and coordinates replacement; the Runtime owns the Fiber lifecycle
// and Provider/Dependency semantics; the Loader provides the new Module.
//
//   - HMR never mutates Fiber state, the Provider registry, or Dependency state.
//   - HMR never runs Component/Factory code under its state lock.
//   - v0.1 strategy: WARM replacement - the new Module is loaded and the new
//     Fiber reaches Active BEFORE the old Fiber is unloaded (per spec §65/§66).
//     When the Kernel's exclusive-provider semantics reject warm overlap, HMR
//     falls back to the Kernel-supported withdraw-then-load path via public
//     Runtime API only (spec §67/§68). There is no zero-downtime guarantee and
//     no automatic rollback.
//   - Loader usage is explicit (Acquire/Release), never implicit.
package hmr

import (
	"context"
	"errors"
	"fmt"
	"sync"

	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// Errors. Compare with errors.Is.
var (
	ErrHMRClosed          = errors.New("hmr closed")
	ErrTargetNotFound     = errors.New("hmr target not found")
	ErrTargetExists       = errors.New("hmr target already exists")
	ErrInvalidTarget      = errors.New("invalid hmr target")
	ErrNotBound           = errors.New("hmr target not bound to an implementation")
	ErrIncompatibleModule = errors.New("incompatible module")
	ErrReplacementFailed  = errors.New("replacement failed")
	ErrReplacementRunning = errors.New("replacement already running")
	ErrHMRUnavailable     = errors.New("hmr unavailable")
)

// Target identifies a Component whose implementation can be replaced.
//
// IDs are distinct concepts: HMR Target ID != Component ID != Fiber ID !=
// Module ID/Version != Artifact ID.
type Target struct {
	// ID is the HMR logical identity of the target.
	ID string
	// ComponentID is the identity of the Runtime Component/Fiber this target
	// manages (used when constructing the new Component).
	ComponentID string
	// Artifact describes the DESIRED NEW implementation for Replace.
	Artifact loader.Artifact
}

// Result describes a committed replacement (immutable).
type Result struct {
	TargetID string

	OldModule loader.ModuleIdentity
	NewModule loader.ModuleIdentity

	OldFiberID runtime.FiberID
	NewFiberID runtime.FiberID
}

// ModuleLoader is the minimal Loader surface HMR needs. *loader.BuiltinLoader
// satisfies it.
type ModuleLoader interface {
	Load(ctx context.Context, artifact loader.Artifact) (*loader.Module, error)
	Unload(ctx context.Context, id string) error
}

// HMR replaces running Component implementations.
type HMR interface {
	Replace(context.Context, Target) error
	Close() error
	CloseContext(context.Context) error
}

// TargetRegistry is the read-only view of registered HMR targets.
type TargetRegistry interface {
	Get(id string) (Target, bool)
	Has(id string) bool
	Snapshot() []Target
}

// Binding is the current implementation bound to a target (read-only).
type Binding struct {
	TargetID    string
	ComponentID string
	Module      loader.ModuleIdentity
	ModuleType  string
	Fiber       *runtime.Fiber
}

func errWrap(base error, format string, args ...any) error {
	return fmt.Errorf("%w: %s", base, fmt.Sprintf(format, args...))
}

var _ = sync.Mutex{}
