package wasm

import "github.com/tetratelabs/wazero"

// moduleRef is the immutable, Backend-private descriptor of one validated WASM
// module: its wazero-compiled form plus identity. It is captured by the
// Module's Factory at Load time so every Component created from that Module
// instantiates the same artifact, while every activation still gets its own
// fresh wazero instance.
//
// moduleRef intentionally keeps no Fiber / Activation state: the Kernel owns
// the lifecycle; the Backend only knows how to materialize one module.
type moduleRef struct {
	id         string
	version    string
	moduleType string // Logical Module Type from the wasm manifest
	compiled   wazero.CompiledModule
}

// ID returns the module identity.
func (m *moduleRef) ID() string { return m.id }

// moduleGeneration is one resource GENERATION for a module id (NEW-10
// resource-identity protocol): the compiled ref plus the ownership state
// shared by all load attempts that deduped onto it. Loader handles are bound
// to a generation POINTER, so a superseded attempt (its generation replaced
// by Unload + a fresh load) can never release the newer generation's
// resources — closing a shared generation is decided by the holder count of
// THAT generation, not by the module id.
type moduleGeneration struct {
	ref       *moduleRef
	holders   int
	committed bool
}
