package hmr_test

import (
	"reflect"
	"sort"
	"testing"
	"time"

	"dynamic-runtime/extensions/hmr"
	"dynamic-runtime/runtime"
)

// G-1 (paper §5.2.1 + Theorem 80): the HMR replacement is a SHORTER ROUTE to
// the same endpoint — after Replace, the runtime quiesces where a from-scratch
// load of the revised configuration would leave it. This pins the endpoint
// property on the warm path (candidate-first with the sanctioned
// withdraw-then-load fallback for capability providers), and asserts
// dependents follow unprompted.

var epKey = runtime.NewKey[string]("ep.service")

type epProvider struct{ tag string }

func (c *epProvider) Name() string { return "ep-provider" }
func (c *epProvider) Inject() []runtime.Dependency {
	return nil
}
func (c *epProvider) Provide() []runtime.Capability {
	return []runtime.Capability{epKey.Capability()}
}
func (c *epProvider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, epKey, c.tag)
}

type epConsumer struct{ seen chan string }

func (c *epConsumer) Name() string { return "ep-consumer" }
func (c *epConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(epKey)}
}
func (c *epConsumer) Provide() []runtime.Capability { return nil }
func (c *epConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, epKey)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v
	}
	return nil, nil
}

// epCanonical builds the identity-free canonical observable: per fiber NAME —
// state + resolved provider name — plus the sorted provider key set.
func epCanonical(t *testing.T, rt *runtime.Runtime) []string {
	t.Helper()
	snap, err := rt.Snapshot(ctxT(t))
	if err != nil {
		t.Fatalf("snapshot: %v", err)
	}
	nameOf := map[runtime.FiberID]string{}
	rows := map[string]string{}
	for _, f := range snap.Fibers {
		nameOf[f.ID] = f.Name
	}
	for _, f := range snap.Fibers {
		row := string(f.State)
		for _, d := range f.Dependencies {
			row += "|" + string(d.Status)
			if d.Status == "satisfied" {
				row += "->" + nameOf[d.ProviderFiberID]
			}
		}
		rows[f.Name] = row
	}
	keys := make([]string, 0, len(rows))
	for k := range rows {
		keys = append(keys, k)
	}
	sort.Strings(keys)
	out := make([]string, 0, len(keys)+1)
	for _, k := range keys {
		out = append(out, k+"="+rows[k])
	}
	pk := make([]string, 0, len(snap.Providers))
	for _, p := range snap.Providers {
		pk = append(pk, p.Key)
	}
	sort.Strings(pk)
	out = append(out, "providers:"+joinStrings0(pk, ","))
	return out
}

func joinStrings0(xs []string, sep string) string {
	out := ""
	for i, x := range xs {
		if i > 0 {
			out += sep
		}
		out += x
	}
	return out
}

func epWaitSeen(t *testing.T, ch chan string, want string) {
	t.Helper()
	deadline := time.After(10 * time.Second)
	for {
		select {
		case v := <-ch:
			if v == want {
				return
			}
		case <-deadline:
			t.Fatalf("timeout waiting for %q", want)
		}
	}
}

func epWaitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(10 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout %s -> %v (state %v err %v)", f.Name(), want, f.State(), f.Err())
}

// The v1/v2 provider factories for one env: each builtin source builds a
// provider with its own tag under the same capability.
func epRegisterFactories(t *testing.T, e *env) {
	e.regFactory(t, "builtin://ep-v1", "v1", &markerFactory{
		tag:     "v1",
		provide: []runtime.Capability{epKey.Capability()},
		applyFn: func(ctx *runtime.Context) error {
			return runtime.Provide(ctx, epKey, "v1")
		},
	})
	e.regFactory(t, "builtin://ep-v2", "v2", &markerFactory{
		tag:     "v2",
		provide: []runtime.Capability{epKey.Capability()},
		applyFn: func(ctx *runtime.Context) error {
			return runtime.Provide(ctx, epKey, "v2")
		},
	})
}

// TestHMREndpointMatchesFromScratch — the endpoint of the HMR replacement
// equals the endpoint of a from-scratch load of the revised configuration
// (identity-free canonical observable, row for row).
func TestHMREndpointMatchesFromScratch(t *testing.T) {
	// --- Endpoint A: v1 running, HMR-replaced to v2. ---
	e := newEnv(t)
	epRegisterFactories(t, e)

	mod1 := e.loadModule(t, artifact("ep-v1", "svc", "builtin://ep-v1", "1"))
	prov1Fiber, _ := e.install(t, "svc", "svc", mod1, artifact("ep-v1", "svc", "builtin://ep-v1", "1"))

	seen := make(chan string, 8)
	consumerFiber, err := e.rt.Load(&epConsumer{seen: seen})
	if err != nil {
		t.Fatal(err)
	}
	epWaitState(t, consumerFiber, runtime.StateActive)
	epWaitSeen(t, seen, "v1")

	// Replace to v2 through the warm path (candidate-first; the capability
	// duplicate forces the sanctioned withdraw-then-load fallback).
	if err := e.ctrl.Replace(ctxT(t), hmr.Target{
		ID:          "svc",
		ComponentID: "svc",
		Artifact:    artifact("ep-v2", "svc", "builtin://ep-v2", "2"),
	}); err != nil {
		t.Fatalf("Replace: %v", err)
	}
	b, ok := e.ctrl.CurrentBinding("svc")
	if !ok {
		t.Fatal("no binding after replace")
	}
	epWaitState(t, b.Fiber, runtime.StateActive)
	epWaitState(t, prov1Fiber, runtime.StateGone)
	epWaitSeen(t, seen, "v2")
	epWaitState(t, consumerFiber, runtime.StateActive)

	canonicalA := epCanonical(t, e.rt)

	// --- Endpoint B: from-scratch load of the v2 configuration. ---
	e2 := newEnv(t)
	epRegisterFactories(t, e2)
	mod2 := e2.loadModule(t, artifact("ep-v2", "svc", "builtin://ep-v2", "2"))
	prov2Fiber, _ := e2.install(t, "svc", "svc", mod2, artifact("ep-v2", "svc", "builtin://ep-v2", "2"))
	seen2 := make(chan string, 8)
	consumer2, err := e2.rt.Load(&epConsumer{seen: seen2})
	if err != nil {
		t.Fatal(err)
	}
	epWaitState(t, prov2Fiber, runtime.StateActive)
	epWaitState(t, consumer2, runtime.StateActive)
	epWaitSeen(t, seen2, "v2")

	canonicalB := epCanonical(t, e2.rt)

	if !reflect.DeepEqual(canonicalA, canonicalB) {
		t.Fatalf("endpoint mismatch:\n  hmr:     %v\n  scratch: %v", canonicalA, canonicalB)
	}
}
