package wasm

import "errors"

// Errors. Compare with errors.Is.
var (
	// ErrInvalidWASM is returned when the artifact bytes are not a structurally
	// valid WASM module (bad magic/version, broken section framing, malformed
	// required section).
	ErrInvalidWASM = errors.New("invalid wasm module")
	// ErrWASMSourceNotFound is returned when the artifact Source cannot be
	// resolved to a readable file (missing file or unsupported scheme).
	ErrWASMSourceNotFound = errors.New("wasm source not found")
	// ErrWASMABI is returned when a structurally valid module violates the
	// minimal WASM ABI contract (a required export is missing or not a
	// function).
	ErrWASMABI = errors.New("wasm abi contract violation")
	// ErrWASMInstantiate is returned when a module cannot be instantiated under
	// the v0.1 closed world (host imports, start function, ...).
	ErrWASMInstantiate = errors.New("wasm instantiate failed")
	// ErrWASMFactory is returned when a WASM-backed Factory cannot create a
	// Component (invalid ComponentConfig).
	ErrWASMFactory = errors.New("wasm factory failed")
	// ErrWASMBackendClosed is returned by Load/Instantiate after Backend.Close.
	ErrWASMBackendClosed = errors.New("wasm backend closed")
	// ErrWASMBackendPanic is returned when a panic is recovered inside the
	// Backend so it can never crash the Loader or corrupt the Module Registry.
	ErrWASMBackendPanic = errors.New("wasm backend panic")
)
