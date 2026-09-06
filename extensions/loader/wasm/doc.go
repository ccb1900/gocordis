// Package wasm implements the WASM Module Backend v0.1: turning a .wasm
// Artifact into a Loader Module without touching the Kernel lifecycle.
//
// Scope (see docs/Dynamic Composable Runtime — WASM Module Backend
// Specification v0.1.md):
//
//	.wasm -> WASM Backend -> loader.Module -> config.Factory ->
//	runtime.Component -> runtime.Load -> Fiber -> Active
//
// The WASM Backend owns ONLY Artifact -> Module. It never writes the Module
// Registry, never starts/disposes a Fiber, never touches Provider/Dependency/
// Config state, and implements no second lifecycle system. A WASM instance is
// an Activation-owned resource: each activation materializes a fresh wazero
// instance and drives the minimal runtime_component_create /
// runtime_component_destroy ABI on it, with destruction bound through
// runtime.Context.Effect — the Kernel remains the sole lifecycle authority.
//
// # Runtime
//
// Validation and execution are real: the Backend runs on
// github.com/tetratelabs/wazero v1.11 (interpreter engine, pure Go, no cgo).
// Load compiles the module with wazero (full binary validation), reads the
// Logical Module Type from the module's own manifest (the "module_type" custom
// section) and checks the minimal ABI surface; each activation instantiates a
// fresh module instance and actually executes the exported create/destroy
// functions. The Backend is selected by Artifact.BackendType ("wasm"), which
// is never copied into Module.Type.
//
// v0.1 intentionally provides no host API and no WASI: modules that import
// host functions compile but cannot be instantiated (ErrWASMInstantiate), and
// guest code cannot reach the host (no filesystem/network/stdin/clock/...).
// Guest-visible side effects of create/destroy are therefore not observable
// through host calls; execution itself is observable through the exported
// "runtime_marker" global in the test fixtures (see backend_test.go).
package wasm
