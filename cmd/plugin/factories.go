package main

import (
	"fmt"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"
)

// builtinFactories is deliberately tiny: the plugin CLI cannot know how a host
// builds a real plugin component from metadata. Hosts that ship plugins embed
// sandboxlib.Sandbox with their own factory registry (see cmd/plugin README
// and docs/guide/user-manual.md). "demo" exists so the --factory path is
// exercisable without any host code and proves the factory-backed sandbox
// really reconciles an Active fiber and provider.
func builtinFactories() map[string]config.Factory {
	return map[string]config.Factory{
		"demo": config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
			return &demoSandboxComponent{}, nil
		}),
	}
}

func lookupBuiltinFactory(name string) (config.Factory, error) {
	factory, ok := builtinFactories()[name]
	if !ok || factory == nil {
		return nil, fmt.Errorf("plugin: unknown factory %q (known factories: demo); hosts embed sandboxlib.Sandbox with their own registry", name)
	}
	return factory, nil
}

// demoSandboxComponent is the built-in factory's component. It provides one
// capability so `sandbox --factory demo` produces a non-empty snapshot.
type demoSandboxComponent struct{}

func (*demoSandboxComponent) Name() string                 { return "plugin-demo" }
func (*demoSandboxComponent) Inject() []runtime.Dependency { return nil }
func (*demoSandboxComponent) Provide() []runtime.Capability {
	return []runtime.Capability{runtime.NewKey[string]("plugin.demo").Capability()}
}
func (*demoSandboxComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, runtime.NewKey[string]("plugin.demo"), "ready")
}
