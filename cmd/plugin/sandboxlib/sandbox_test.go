package sandboxlib_test

import (
	"bytes"
	"context"
	"errors"
	"strings"
	"sync/atomic"
	"testing"
	"time"

	"dynamic-runtime/cmd/plugin/sandboxlib"
	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/pluginmeta"
	"dynamic-runtime/runtime"
)

// integrationComponent is a real runtime.Component: it declares and provides a
// capability and registers an effect whose inverse records that Sandbox closed
// cleanly.
type integrationComponent struct {
	key       runtime.Key[string]
	cleanups  *atomic.Int64
	failApply bool
}

func (c *integrationComponent) Name() string                 { return "integration-plugin" }
func (c *integrationComponent) Inject() []runtime.Dependency { return nil }
func (c *integrationComponent) Provide() []runtime.Capability {
	return []runtime.Capability{c.key.Capability()}
}
func (c *integrationComponent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.failApply {
		return nil, errors.New("apply refused")
	}
	if err := ctx.Effect(func() (func() error, error) {
		return func() error {
			c.cleanups.Add(1)
			return nil
		}, nil
	}); err != nil {
		return nil, err
	}
	return nil, runtime.Provide(ctx, c.key, "ready")
}

func testManifest(id string) pluginmeta.Manifest {
	return pluginmeta.Manifest{
		ID:      id,
		Name:    "Integration Plugin",
		Version: "1.0.0",
		Backend: pluginmeta.BackendNative,
		Source:  pluginmeta.Source{Kind: "builtin", Location: "builtin://" + id},
	}
}

func TestSandboxPublicAPIReconcilesAndClosesCleanly(t *testing.T) {
	key := runtime.NewKey[string]("plugin.integration")
	var cleanups atomic.Int64
	factory := config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &integrationComponent{key: key, cleanups: &cleanups}, nil
	})

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()
	var out bytes.Buffer
	if err := sandboxlib.Sandbox(ctx, testManifest("integration-plugin"), factory, &out); err != nil {
		t.Fatalf("Sandbox returned error: %v\n%s", err, out.String())
	}

	text := out.String()
	for _, want := range []string{
		"runtime ",
		"fibers: 1",
		"providers: 1",
		"dependencies: 0",
		"fiber ",
		"integration-plugin",
		"state=" + runtime.StateActive.String(),
		"provider string(\"plugin.integration\")",
	} {
		if !strings.Contains(text, want) {
			t.Errorf("snapshot summary missing %q:\n%s", want, text)
		}
	}
	if got := cleanups.Load(); got != 1 {
		t.Fatalf("effect inverse ran %d times, want exactly 1 (clean close)", got)
	}
}

func TestSandboxPublicAPIErrorPaths(t *testing.T) {
	ctx := context.Background()
	var out bytes.Buffer

	if err := sandboxlib.Sandbox(ctx, testManifest("x"), nil, &out); err == nil {
		t.Error("nil factory was accepted")
	}
	bad := testManifest("")
	if err := sandboxlib.Sandbox(ctx, bad, config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &integrationComponent{}, nil
	}), &out); err == nil {
		t.Error("invalid manifest was accepted")
	}

	key := runtime.NewKey[string]("plugin.fail")
	failing := config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &integrationComponent{key: key, cleanups: &atomic.Int64{}, failApply: true}, nil
	})
	if err := sandboxlib.Sandbox(ctx, testManifest("fail-plugin"), failing, &out); err == nil {
		t.Error("Apply failure did not surface as an error")
	}
	if !strings.Contains(out.String(), "fibers:") {
		t.Error("snapshot summary was not printed on the Apply-failure path")
	}
}
