package loader_test

import (
	"context"
	"errors"
	"sync/atomic"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/loader"
	"dynamic-runtime/runtime"
)

// EXT-05: the Loader's Backend interface is Load-only, so a Backend that
// retains compiled resources (e.g. the WASM backend's CompiledModule) is never
// told when the Loader discards a module — the resources survive until
// Backend.Close. Loaders must notify a Backend implementing the optional
// unload interface when a module leaves the registry.
type unloadNotifyBackend struct {
	loader.Backend
	unloads atomic.Int32
}

func (b *unloadNotifyBackend) UnloadModule(id string) { b.unloads.Add(1) }

func (b *unloadNotifyBackend) Load(ctx context.Context, artifact loader.Artifact) (loader.Module, error) {
	return loader.Module{ID: artifact.ID, Type: "fake", Factory: config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return nil, errors.New("unused")
	})}, nil
}

func TestLoaderNotifiesBackendOnUnload(t *testing.T) {
	l := loader.NewBuiltinLoader()
	backend := &unloadNotifyBackend{}
	if err := l.RegisterBackend("fake", backend); err != nil {
		t.Fatal(err)
	}
	if _, err := l.Load(context.Background(), loader.Artifact{ID: "m1", BackendType: "fake", Type: "x", Version: "1", Source: "mem://fake"}); err != nil {
		t.Fatal(err)
	}
	if err := l.Unload(context.Background(), "m1"); err != nil {
		t.Fatal(err)
	}
	if n := backend.unloads.Load(); n != 1 {
		t.Fatalf("backend notified %d times on Unload, want 1 (EXT-05)", n)
	}
}
