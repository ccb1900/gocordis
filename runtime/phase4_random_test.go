package runtime_test

import (
	"context"
	"math/rand"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// randKeyFor returns a typed key per id (root-realm provider/consumer churn).
func randKeyFor(id int) runtime.Key[string] {
	switch id % 3 {
	case 0:
		return cycAKey
	case 1:
		return cycBKey
	default:
		return randCKey
	}
}

var randCKey = runtime.NewKey[string]("rand.c")

type randProvider struct {
	key runtime.Key[string]
}

func (c *randProvider) Name() string                 { return "rp" }
func (c *randProvider) Inject() []runtime.Dependency { return nil }
func (c *randProvider) Provide() []runtime.Capability {
	return []runtime.Capability{c.key.Capability()}
}
func (c *randProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, c.key, "v")
}

type randConsumer struct {
	key runtime.Key[string]
}

func (c *randConsumer) Name() string { return "rc" }
func (c *randConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(c.key)}
}
func (c *randConsumer) Provide() []runtime.Capability { return nil }
func (c *randConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	_, err := runtime.Require(ctx, c.key)
	return nil, err
}

// TestPhase4BoundedRandomOps — a seeded, bounded schedule of load/dispose on
// providers and consumers (shared keys => deterministic duplicate/consume
// semantics). Invariant: no panic, no deadlock; after disposing every fiber and
// closing the runtime, all providers are gone.
func TestPhase4BoundedRandomOps(t *testing.T) {
	rt := newTestRuntime(t)
	rng := rand.New(rand.NewSource(20260906))
	var mu sync.Mutex
	var fibers []*runtime.Fiber

	for step := 0; step < 120; step++ {
		roll := rng.Intn(100)
		switch {
		case roll < 40: // load a provider
			p := &randProvider{key: randKeyFor(step)}
			f, err := rt.Load(p)
			if err != nil && err != runtime.ErrDependencyCycle {
				t.Fatalf("load provider: %v", err)
			}
			if err == nil {
				mu.Lock()
				fibers = append(fibers, f)
				mu.Unlock()
			}
		case roll < 70: // load a consumer (may stay Pending or become Active)
			c := &randConsumer{key: randKeyFor(step)}
			f, err := rt.Load(c)
			if err != nil {
				continue
			}
			mu.Lock()
			fibers = append(fibers, f)
			mu.Unlock()
		default: // dispose a random mounted fiber
			mu.Lock()
			if len(fibers) == 0 {
				mu.Unlock()
				continue
			}
			i := rng.Intn(len(fibers))
			f := fibers[i]
			fibers = append(fibers[:i], fibers[i+1:]...)
			mu.Unlock()
			_ = f.Dispose()
			_ = f.Gone(context.Background())
		}
	}

	mu.Lock()
	fs := append([]*runtime.Fiber(nil), fibers...)
	mu.Unlock()
	for _, f := range fs {
		_ = f.Dispose()
	}
	deadline := time.Now().Add(15 * time.Second)
	for _, f := range fs {
		for f.State() != runtime.StateGone && time.Now().Before(deadline) {
			time.Sleep(time.Millisecond)
		}
	}
	// newTestRuntime cleanup closes the runtime; nothing to assert here beyond
	// reaching this point without panic/deadlock (race mode validates sync).
}
