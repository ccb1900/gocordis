package devtools

import (
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/runtime"
)

// Service is the read-only diagnostic capability exposed to other components.
type Service interface {
	Diagnose(pluginmeta.Manifest) []Diagnostic
}

// ServiceKey is the typed capability key for the diagnostics service.
var ServiceKey = runtime.NewKey[Service]("extension.devtools.service")

type service struct {
	runtimeVersion string
	apiVersion     string
}

func (s *service) Diagnose(m pluginmeta.Manifest) []Diagnostic {
	return DiagnoseAgainst(m, s.runtimeVersion, s.apiVersion)
}

// Component publishes a Service through runtime.Provide.
//
// runtime.Provide is effect-owned registration: the provider is committed as a
// Provider effect of this activation and the Runtime installs its inverse on
// unwind (LIFO), so disposing the component removes the capability. The
// component starts no goroutines and performs no out-of-band mutation.
type Component struct {
	// RuntimeVersion and APIVersion, when set, are the host versions the
	// provided service evaluates compatibility constraints against.
	RuntimeVersion string
	APIVersion     string
}

func (c *Component) Name() string { return "extension.devtools" }

func (c *Component) Inject() []runtime.Dependency { return nil }

func (c *Component) Provide() []runtime.Capability {
	return []runtime.Capability{ServiceKey.Capability()}
}

func (c *Component) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc := &service{runtimeVersion: c.RuntimeVersion, apiVersion: c.APIVersion}
	if err := runtime.Provide[Service](ctx, ServiceKey, svc); err != nil {
		return nil, err
	}
	return nil, nil
}

var (
	_ Service           = (*service)(nil)
	_ runtime.Component = (*Component)(nil)
)
