# WASM Module Backend v0.1

`extensions/loader/wasm` implements the **WASM Module Backend v0.1** of the
Dynamic Composable Runtime Loader Extension, on top of a real WASM runtime
([`github.com/tetratelabs/wazero`](https://github.com/tetratelabs/wazero)
v1.11, interpreter engine, pure Go / no cgo).

Proven chain:

```text
.wasm
  ↓  WASM Backend (this package; wazero validates + compiles)
loader.Module { Type: "industrial.camera" /* logical, from wasm manifest */, Factory: ... }
  ↓  config.Factory
runtime.Component
  ↓  runtime.Load  (Kernel)
Fiber → Active
```

Architectural facts proven by this phase:

- **Module ≠ Component ≠ Fiber.** The Backend only turns an Artifact into a
  `loader.Module`. It never writes the Module Registry (the Loader commits), it
  never starts/disposes a Fiber, and it never touches Provider/Dependency/
  Config state.
- **One lifecycle only.** A WASM instance is an *Activation-owned resource*:
  each activation materializes a fresh wazero module instance and runs the
  minimal ABI (`runtime_component_create` at begin,
  `runtime_component_destroy` at teardown) via `runtime.Context.Effect`. The
  Kernel unwinds the inverse exactly once. There is no second lifecycle system.
- **Real execution.** wazero fully validates the binary at Load and actually
  executes module code. The test fixtures export a mutable `runtime_marker`
  global that `create` sets to 42, so `backend_test.go` proves instructions
  really run; a trap-in-`destroy` fixture proves teardown really calls the
  export.
- **Per-activation freshness.** Instances are never reused across activations;
  every instance gets a unique module name.
- **Explicit usage.** `loader.ModuleUsage` remains the only mechanism that
  blocks `Unload`; the Loader never infers usage by scanning Fibers.
- **Closed world.** v0.1 provides no host API and no WASI: modules importing
  host functions compile but cannot be instantiated
  (`ErrWASMInstantiate`). Guest code cannot reach filesystem/network/stdin/
  clock/random.

## Dependency

`go.mod` requires `github.com/tetratelabs/wazero v1.11.0` (Go 1.24 floor) and
`golang.org/x/sys v0.38.0`. After the initial `go mod download`, all builds and
tests run offline from the module cache.

## Tests

- `backend_test.go` — wazero validation/execution edge cases (V-01…V-09).
- `wasm_test.go` — contract tests W-01…W-14 through the real Loader + Runtime.
- `integration/wasm_e2e_test.go` — E2E-WASM-01…10 and P-WASM-01…06.

Gates: `go test ./...`, `go test -race ./...`, `go vet ./...`.
