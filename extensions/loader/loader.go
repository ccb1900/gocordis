// Package loader provides the Runtime Loader Extension: turning an external
// implementation (an Artifact) into a Module with a clear identity, usage,
// ownership, and lifecycle, whose Factory can be explicitly registered into a
// Config FactoryRegistry - without touching the Kernel lifecycle.
//
// Loader owns:
//   - Artifact -> Module loading,
//   - the Loaded Module registry,
//   - Module usage/ownership tracking,
//   - explicit (never automatic) Config factory registration.
//
// Loader does NOT own: Component/Fiber lifecycle, Provider lifecycle,
// Dependency resolution, Config Desired/Reconcile state, Watch, HMR,
// Discovery, automatic reload, or any Kernel state.
//
// v0.1 ships the Builtin (in-process) Loader, which materializes Modules from
// pre-registered Go factories (Source like "builtin://camera") and additionally
// dispatches Artifacts to registered Backends by Artifact.BackendType (for
// example a WASM Backend materializing Modules from .wasm files). Backend Type
// ("how to load") is fully separated from Logical Module Type ("what the Module
// represents"); the Loader keeps sole ownership of the Module Registry, usage
// and lifecycle regardless of which Backend produced a Module.
//
// Module lifecycle (Absent -> Loaded -> Absent) is fully independent of the
// Kernel Fiber lifecycle; Module Unload never disposes a Fiber, and Module Load
// never starts one.
package loader

import (
	"context"
	"errors"
	"fmt"

	"dynamic-runtime/extensions/config"
)

// Errors. Compare with errors.Is.
var (
	ErrLoaderClosed         = errors.New("loader closed")
	ErrModuleExists         = errors.New("module already exists")
	ErrModuleNotFound       = errors.New("module not found")
	ErrModuleInUse          = errors.New("module in use")
	ErrInvalidArtifact      = errors.New("invalid artifact")
	ErrInvalidModule        = errors.New("invalid module")
	ErrLoadFailed           = errors.New("module load failed")
	ErrModuleLoadPanic      = errors.New("module load panic")
	ErrModuleUseExists      = errors.New("module use already exists")
	ErrModuleUseNotFound    = errors.New("module use not found")
	ErrBuiltinExists        = errors.New("builtin already exists")
	ErrBuiltinNotFound      = errors.New("builtin not found")
	ErrInvalidBuiltin       = errors.New("invalid builtin")
	ErrBackendExists        = errors.New("backend already exists")
	ErrInvalidBackend       = errors.New("invalid backend")
	ErrArtifactTypeConflict = errors.New("artifact type conflict")
	ErrInvalidBackendType   = errors.New("invalid backend type")
	ErrInvalidModuleType    = errors.New("invalid module type")
)

// BackendType identifies HOW an Artifact is loaded (which Loader Backend
// interprets it), e.g. "builtin" or "wasm". It is deliberately distinct from
// the Logical Module Type: BackendType decides the loading path, never what a
// Module represents.
type BackendType string

// Well-known backend types.
const (
	BackendBuiltin BackendType = "builtin"
	BackendWASM    BackendType = "wasm"
)

// Artifact is the description of an implementation to load.
type Artifact struct {
	// ID is the Loader-internal stable logical identity.
	ID string
	// Type is a DEPRECATED compatibility alias.
	//
	// - When BackendType is empty and Type names a backend type ("builtin" /
	//   "wasm"), Type selects the Backend (legacy).
	// - For the builtin backend, Type carries the Logical Module Type
	//   (e.g. "industrial.camera").
	// - For the wasm backend, the Logical Module Type comes from the module's
	//   own manifest; Type must NOT be copied into Module.Type.
	//
	// BackendType is the canonical field; Type and BackendType must never name
	// different backend types at the same time (ErrArtifactTypeConflict).
	Type string
	// BackendType selects the Loader Backend that interprets this Artifact
	// (canonical; see BackendType). Empty means legacy resolution.
	BackendType BackendType
	// Source is the implementation source; its format is not interpreted by
	// the Loader core (e.g. "builtin://camera").
	Source string
	// Version is optional and never implies automatic upgrade semantics.
	Version string
}

// knownBackendTypes are the backend names accepted by legacy Type resolution.
var knownBackendTypes = map[BackendType]struct{}{
	BackendBuiltin: {},
	BackendWASM:    {},
}

// Module is a loaded implementation: an immutable descriptor binding an
// identity to a Config-semantics Factory.
//
// Module.Type is the Logical Module Type (e.g. "industrial.camera"): what the
// Module represents in the application. It is NOT the Backend Type that loaded
// it — BackendType and ModuleType live in separate namespaces and must never be
// conflated.
type Module struct {
	ID      string
	Type    string
	Version string
	Factory config.Factory
}

// ModuleIdentity identifies one loaded implementation instance.
type ModuleIdentity struct {
	ID      string
	Version string
}

// Identity returns the module identity.
func (m Module) Identity() ModuleIdentity {
	return ModuleIdentity{ID: m.ID, Version: m.Version}
}

// Loader loads and unloads Modules.
type Loader interface {
	Load(ctx context.Context, artifact Artifact) (*Module, error)
	Unload(ctx context.Context, id string) error
	Close() error
	CloseContext(ctx context.Context) error
}

// Backend materializes one Artifact into a Module.
//
// A Backend is deliberately narrower than a Loader:
//   - it NEVER owns or writes the Module Registry (the Loader commits),
//   - it NEVER starts or disposes a Fiber,
//   - it NEVER touches Provider / Dependency / Config state,
//   - it only turns an Artifact of one Type into a Module implementation.
//
// Backend.Load must be safe for concurrent use, must respect ctx, must not run
// user code under the Loader's lock, and must not mutate any Loader state.
type Backend interface {
	Load(ctx context.Context, artifact Artifact) (Module, error)
}

// ModuleRegistry is the read-only view of loaded Modules.
type ModuleRegistry interface {
	Get(id string) (Module, bool)
	Has(id string) bool
	Snapshot() []Module
}

// ModuleUsage tracks explicit Module ownership (ModuleID -> set of owners).
//
// Semantics:
//   - Acquire(moduleID, ownerID) linearizes; a duplicate (moduleID, ownerID)
//     fails with ErrModuleUseExists (no hidden reference counting);
//   - Release(moduleID, ownerID) linearizes; releasing a non-existent
//     ownership fails with ErrModuleUseNotFound;
//   - InUse(moduleID) reports whether at least one owner holds the module.
//
// Usage is explicit: the Loader never infers usage by scanning Fibers or by
// reading Config.Applied state.
type ModuleUsage interface {
	Acquire(moduleID, ownerID string) error
	Release(moduleID, ownerID string) error
	InUse(moduleID string) bool
}

func invalidArtifactf(format string, args ...any) error {
	return fmt.Errorf("%w: %s", ErrInvalidArtifact, fmt.Sprintf(format, args...))
}
