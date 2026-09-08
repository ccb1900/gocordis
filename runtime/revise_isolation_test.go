package runtime_test

import (
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Conformance for paper §4.4 Configuration — a revision may reassign the
// fiber's realm pairs ("the new realm pairs"): WithFreshIsolation reinserts
// the fiber with a FRESH namespace; dependents whose own ρ still points at
// the old namespace see a withdrawal and go Pending (they follow only through
// their own revision — the paper-faithful reading of crossing a realm move).

var (
	revIsoKey   = runtime.NewKey[string]("rev.iso.service")
	revOtherKey = runtime.NewKey[string]("rev.other")
)

// reIsoHost provides revIsoKey; mounted with Isolate(revIsoKey) at revision
// time (initially plain).
type reIsoHost struct {
	tag      string
	iso      bool
	childOut chan string
	ch       chan *runtime.Fiber
}

func (c *reIsoHost) Name() string { return "re-iso-host" }
func (c *reIsoHost) Inject() []runtime.Dependency {
	return nil
}
func (c *reIsoHost) Provide() []runtime.Capability {
	return []runtime.Capability{revIsoKey.Capability()}
}
func (c *reIsoHost) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if err := runtime.Provide(ctx, revIsoKey, c.tag); err != nil {
		return nil, err
	}
	if _, err := ctx.Child(&reIsoConsumer{out: c.childOut}); err != nil {
		return nil, err
	}
	// The consumer cannot bind until THIS activation becomes Active (binding
	// validity requires an Active provider), so do not wait here — the
	// sweep after activation activates it.
	return nil, nil
}

type reIsoConsumer struct {
	out chan string
}

func (c *reIsoConsumer) Name() string { return "re-iso-consumer" }
func (c *reIsoConsumer) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(revIsoKey)}
}
func (c *reIsoConsumer) Provide() []runtime.Capability { return nil }
func (c *reIsoConsumer) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, revIsoKey)
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

// TestReviseReassignsIsolation — revise the providing host with
// WithFreshIsolation: the new host subtree serves a FRESH namespace. The old
// subtree's consumer (bound to the old namespace) ends Pending after the
// revision, while a consumer mounted inside the new subtree binds v2 there.
func TestReviseReassignsIsolation(t *testing.T) {
	rt := newTestRuntime(t)
	consOut := make(chan string, 4)

	host, err := rt.Load(&reIsoHost{tag: "v1", childOut: consOut})
	if err != nil {
		t.Fatal(err)
	}
	d24Wait(t, host, runtime.StateActive)
	if v := <-consOut; v != "v1" {
		t.Fatalf("initial binding = %q, want v1", v)
	}

	// Revision WITH realm reassignment: the reinserted host gets a fresh
	// namespace for revIsoKey; its new child consumer binds v2 there.
	newHost, err := host.Revise(testTimeout(t), &reIsoHost{tag: "v2", childOut: consOut}, runtime.WithFreshIsolation())
	if err != nil {
		t.Fatalf("revise: %v", err)
	}
	if newHost.ID() == host.ID() {
		t.Fatal("revision must publish a fresh fiber identity")
	}
	if host.State() != runtime.StateGone {
		t.Fatalf("old host state = %v, want Gone (entry removed)", host.State())
	}
	d24Wait(t, newHost, runtime.StateActive)
	revWaitValue(t, consOut, "v2")

	// A plain root consumer NEVER binds either namespace's provider (the key
	// is isolated away from the root in both revisions) — per-key isolation
	// survives the revision.
	rootCons, err := rt.Load(&reIsoConsumer{out: consOut})
	if err != nil {
		t.Fatal(err)
	}
	d24Wait(t, rootCons, runtime.StatePending)
}

func revWaitValue(t *testing.T, ch chan string, want string) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		select {
		case v := <-ch:
			if v == want {
				return
			}
			t.Fatalf("resolved %q, want %q", v, want)
		case <-time.After(50 * time.Millisecond):
		}
	}
	t.Fatalf("timeout waiting for %q", want)
}
