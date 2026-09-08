package runtime_test

import (
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Conformance for paper Definition 24/25 (isolation as a per-key realm table
// fixed at insertion) — the per-key granularity:
//
//	ρ assigns ONE realm per key; get/set resolve and provide against exactly
//	(k, ρ(k)). A component may isolate ONE key into its own namespace while
//	SHARING the rest of its context with the parent — the isolated key neither
//	sees nor is seen by the shared namespace; the shared keys continue
//	binding normally (per-key isolation with sharing).

var (
	d24Shared    = runtime.NewKey[string]("d24.shared")
	d24IsoShared = runtime.NewKey[string]("d24.isoShared") // the sibling isolates this one
)

// d24Provider provides every key in keys with the same tag.
type d24Provider struct {
	tkeys []runtime.Key[string]
	tag   string
}

func (c *d24Provider) Name() string { return "d24-provider" }
func (c *d24Provider) Inject() []runtime.Dependency {
	return nil
}
func (c *d24Provider) Provide() []runtime.Capability {
	caps := make([]runtime.Capability, 0, len(c.tkeys))
	for _, k := range c.tkeys {
		caps = append(caps, k.Capability())
	}
	return caps
}
func (c *d24Provider) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	for _, k := range c.tkeys {
		if err := runtime.Provide(ctx, k, c.tag); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

type d24Consumer struct {
	key runtime.Key[string]
	out chan string
}

func (c *d24Consumer) Name() string { return "d24-consumer" }
func (c *d24Consumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(c.key)}
}
func (c *d24Consumer) Provide() []runtime.Capability { return nil }
func (c *d24Consumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, c.key)
	if err != nil {
		return nil, err
	}
	if c.out != nil {
		select {
		case c.out <- v:
		default:
		}
	}
	return nil, nil
}

// d24IsoHost is mounted WITH Isolate(isoShared): it receives a per-key table
// {isoShared -> fresh namespace}; its plain children inherit that table, so
// the provider and consumer below share exactly the isolated namespace while
// remaining in the host's scope realm for every other key.
type d24IsoHost struct {
	provKey runtime.Key[string]
	consKey runtime.Key[string]
	tag     string
	out     chan string
	ch      chan *runtime.Fiber
}

func (c *d24IsoHost) Name() string { return "d24-iso-host" }
func (c *d24IsoHost) Inject() []runtime.Dependency {
	return nil
}
func (c *d24IsoHost) Provide() []runtime.Capability { return nil }
func (c *d24IsoHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	p, err := ctx.Child(&d24Provider{tkeys: []runtime.Key[string]{c.provKey}, tag: c.tag})
	if err != nil {
		return nil, err
	}
	if err := p.Ready(ctx.Context()); err != nil {
		return nil, err
	}
	x, err := ctx.Child(&d24Consumer{key: c.consKey, out: c.out})
	if err != nil {
		return nil, err
	}
	c.ch <- p
	c.ch <- x
	return nil, nil
}

// d24Activator mounts the isolated subtree.
type d24Activator struct {
	host    *d24IsoHost
	hostCh  chan *runtime.Fiber
	isoKeys []runtime.Capability
}

func (c *d24Activator) Name() string { return "d24-activator" }
func (c *d24Activator) Inject() []runtime.Dependency {
	return nil
}
func (c *d24Activator) Provide() []runtime.Capability { return nil }
func (c *d24Activator) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	opts := []runtime.ScopeOption{runtime.Isolate(c.isoKeys...)}
	h, err := ctx.Child(c.host, opts...)
	if err != nil {
		return nil, err
	}
	c.hostCh <- h
	return nil, nil
}

func d24Wait(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout %s -> %v (state %v err %v)", f.Name(), want, f.State(), f.Err())
}

// TestDef24PerKeyIsolationWithSharing — one component isolates ONE key while
// sharing another with the root namespace; each (key, ρ(key)) pair resolves
// independently, and disposal of the isolated subtree leaves the shared
// binding untouched.
func TestDef24PerKeyIsolationWithSharing(t *testing.T) {
	rt := newTestRuntime(t)
	outShared := make(chan string, 4)
	outIso := make(chan string, 4)
	hostCh := make(chan *runtime.Fiber, 2)

	// Root: provides the SHARED key only.
	root, err := rt.Load(&d24Provider{tkeys: []runtime.Key[string]{d24Shared}, tag: "root"})
	if err != nil {
		t.Fatal(err)
	}
	d24Wait(t, root, runtime.StateActive)

	// Host: isolates ONLY d24IsoShared; d24Shared keeps resolving/providing in
	// the shared (root) namespace.
	act, err := rt.Load(&d24Activator{
		host: &d24IsoHost{
			provKey: d24IsoShared,
			consKey: d24IsoShared,
			tag:     "iso",
			out:     outIso,
			ch:      hostCh,
		},
		hostCh:  hostCh,
		isoKeys: []runtime.Capability{d24IsoShared.Capability()},
	})
	if err != nil {
		t.Fatal(err)
	}
	<-hostCh
	<-hostCh
	d24Wait(t, act, runtime.StateActive)

	// The isolated consumer binds the isolated namespace's provider.
	if v := <-outIso; v != "iso" {
		t.Fatalf("isolated consumer resolved %q, want iso (own namespace)", v)
	}

	// A shared-namespace consumer still binds the ROOT provider of d24Shared.
	sharedCons, err := rt.Load(&d24Consumer{key: d24Shared, out: outShared})
	if err != nil {
		t.Fatal(err)
	}
	d24Wait(t, sharedCons, runtime.StateActive)
	if v := <-outShared; v != "root" {
		t.Fatalf("shared consumer resolved %q, want root (sharing unaffected)", v)
	}

	// Disposal of the isolated subtree drops its namespace; the shared binding
	// is untouched (nothing in the subtree ever provided into the shared
	// namespace for an isolated key).
	_ = act.Dispose()
	if err := act.Gone(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	if sharedCons.State() != runtime.StateActive {
		t.Fatalf("shared consumer disturbed by isolated namespace teardown: %v", sharedCons.State())
	}

	// Root provider still bound; withdraw it — the shared consumer goes
	// Pending (its namespace lost the provider), proving it was bound to the
	// root namespace all along.
	if err := root.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := sharedCons.WaitInactive(testTimeout(t)); err != nil {
		t.Fatal(err)
	}
	d24Wait(t, sharedCons, runtime.StatePending)
}
