package wasm

import (
	"fmt"

	"github.com/tetratelabs/wazero"
)

// manifestTypeSection is the wasm custom-section name carrying the Logical
// Module Type metadata (e.g. data "industrial.camera"). It is part of the WASM
// Backend-internal manifest; Loader/Config/Runtime never parse it.
const manifestTypeSection = "module_type"

// ABI contract for the minimal WASM Component interface (v0.1).
//
// A valid WASM module must export both entry points as functions:
//
//	runtime_component_create  — begin one Component activation
//	runtime_component_destroy — end one Component activation
//
// The concrete encoding/execution of this ABI is encapsulated inside this
// Backend (wazero drives it); Loader, Config and Runtime never see it. v0.1
// has no host API, no WASI and no capability system.
const (
	// ABICreate is the required "begin activation" export name.
	ABICreate = "runtime_component_create"
	// ABIDestroy is the required "end activation" export name.
	ABIDestroy = "runtime_component_destroy"
)

// abiContract is the immutable set of required function exports.
type abiContract struct {
	create  string
	destroy string
}

// defaultABI is the v0.1 minimal ABI.
var defaultABI = abiContract{create: ABICreate, destroy: ABIDestroy}

func errABI(format string, args ...any) error {
	return fmt.Errorf("%w: %s", ErrWASMABI, fmt.Sprintf(format, args...))
}

// validateABI checks that every required export exists in a wazero-compiled
// module and is a function export. wazero already guarantees the module is a
// valid WASM binary when it returns a CompiledModule, so only the ABI surface
// needs to be checked here.
func validateABI(compiled wazero.CompiledModule, abi abiContract) error {
	exports := compiled.ExportedFunctions()
	for _, required := range []string{abi.create, abi.destroy} {
		if _, ok := exports[required]; !ok {
			return errABI("required export %q is missing", required)
		}
	}
	return nil
}
