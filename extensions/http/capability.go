package http

import (
	"dynamic-runtime/runtime"
)

// Handle is the capability value an HTTP Provider exposes: a stable
// server-side handle identifying the serving resource of one Activation.
type Handle struct {
	// ID is an instance identity unique across every activation of every HTTP
	// component in this process. It is NOT a Kernel identity.
	ID uint64
	// Addr is the bound network address of the serving listener.
	Addr string
}

// ServerCapability is the typed capability key HTTP Providers provide and
// Consumers require. It is a typed runtime key — never a bare global string.
var ServerCapability = runtime.NewKey[Handle]("http.server")
