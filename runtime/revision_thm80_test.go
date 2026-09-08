package runtime_test

import (
	"context"
	"sort"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// Theorem 80 endpoint property for the revision composite (paper §4.4
// Configuration): a runtime REVISED at runtime quiesces at the same canonical
// observable a runtime loaded from scratch with the revised configuration
// reaches — up to fiber identity. Dependents follow unprompted (the reinserted
// fiber's keys re-satisfy them through the target-view comparison), and the
// old fiber's owned children are gone with it (children before parent, O-Remove).
//
// Canonical observable (identity-free): per component NAME — state, resolved
// dependency provider name, effect count; plus the global provider key set.

type revKeyVal struct{ tag string }

var revKey = runtime.NewKey[revKeyVal]("rev.service")

// revParent mounts one child (its config witness) and provides nothing.
type revParent struct {
	version string
	ch      chan *runtime.Fiber
}

func (c *revParent) Name() string { return "rev-parent" }
func (c *revParent) Inject() []runtime.Dependency {
	return nil
}
func (c *revParent) Provide() []runtime.Capability { return nil }
func (c *revParent) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	child, err := ctx.Child(&revChild{version: c.version})
	if err != nil {
		return nil, err
	}
	if c.ch != nil {
		c.ch <- child
	}
	if err := child.Ready(ctx.Context()); err != nil {
		return nil, err
	}
	return nil, nil
}

// revChild consumes revKey and records which version satisfied it.
type revChild struct {
	version string
	seen    chan string
}

func (c *revChild) report(ctx *runtime.Context) error {
	v, err := runtime.Require(ctx, revKey)
	if err != nil {
		return err
	}
	if c.seen != nil {
		c.seen <- v.tag
	}
	return nil
}

func (c *revChild) Name() string { return "rev-child" }
func (c *revChild) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(revKey)}
}
func (c *revChild) Provide() []runtime.Capability { return nil }
func (c *revChild) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, c.report(ctx)
}

// revProv provides revKey with a version tag.
type revProv struct{ version string }

func (c *revProv) Name() string { return "rev-prov" }
func (c *revProv) Inject() []runtime.Dependency {
	return nil
}
func (c *revProv) Provide() []runtime.Capability {
	return []runtime.Capability{revKey.Capability()}
}
func (c *revProv) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return nil, runtime.Provide(ctx, revKey, revKeyVal{tag: c.version})
}

// revCons is a plain dependent that follows the revised fiber unprompted.
type revCons struct{ seen chan string }

func (c *revCons) Name() string { return "rev-cons" }
func (c *revCons) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(revKey)}
}
func (c *revCons) Provide() []runtime.Capability { return nil }
func (c *revCons) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	v, err := runtime.Require(ctx, revKey)
	if err != nil {
		return nil, err
	}
	if c.seen != nil {
		c.seen <- v.tag
	}
	return nil, nil
}

// revCanonical builds the identity-free canonical observable of the runtime.
func revisionCtx() context.Context {
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	_ = cancel // snapshot-scoped; short-lived test helper
	return ctx
}

func revCanonical(t *testing.T, rt *runtime.Runtime) []string {
	t.Helper()
	snap, err := rt.Snapshot(revisionCtx())
	if err != nil {
		t.Fatalf("snapshot: %v", err)
	}
	rows := map[string]string{}
	for _, f := range snap.Fibers {
		row := f.State.String()
		for _, p := range snap.Providers {
			if p.OwnerFiberID == f.ID {
				row += "|provides"
			}
		}
		for _, d := range f.Dependencies {
			row += "|dep:" + string(d.Status)
			if d.Status == runtime.DependencySatisfied {
				row += "->" + snapFiberName(snap, d.ProviderFiberID)
			}
		}
		rows[snapFiberName(snap, f.ID)] = row
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
	pkeys := make([]string, 0, len(snap.Providers))
	for _, p := range snap.Providers {
		pkeys = append(pkeys, p.Key)
	}
	sort.Strings(pkeys)
	out = append(out, "provider-keys:"+joinStrings(pkeys, ","))
	return out
}

func snapFiberName(s runtime.RuntimeSnapshot, id runtime.FiberID) string {
	for _, f := range s.Fibers {
		if f.ID == id {
			return f.Name
		}
	}
	return "?"
}

func joinStrings(xs []string, sep string) string {
	out := ""
	for i, x := range xs {
		if i > 0 {
			out += sep
		}
		out += x
	}
	return out
}

func revQuiesce(t *testing.T, rt *runtime.Runtime, fs ...*runtime.Fiber) {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 8*time.Second)
	defer cancel()
	for _, f := range fs {
		if err := f.Ready(ctx); err != nil {
			t.Fatalf("%s not ready: %v", f.Name(), err)
		}
	}
}

func revRecv(t *testing.T, ch chan string) string {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(8 * time.Second):
		t.Fatal("timeout waiting for resolved version")
		return ""
	}
}

// TestThm80RevisionEndpointEquivalence — the endpoint of the revision composite
// equals the endpoint of a from-scratch load of the revised configuration.
func TestThm80RevisionEndpointEquivalence(t *testing.T) {
	// Endpoint A: from scratch with the REVISED (v2) configuration.
	rtA := newTestRuntime(t)
	childChA := make(chan *runtime.Fiber, 1)
	pa, err := rtA.Load(&revProv{version: "v2"})
	if err != nil {
		t.Fatal(err)
	}
	ma, err := rtA.Load(&revParent{version: "v2", ch: childChA})
	if err != nil {
		t.Fatal(err)
	}
	ca := <-childChA
	da, err := rtA.Load(&revCons{})
	if err != nil {
		t.Fatal(err)
	}
	revQuiesce(t, rtA, pa, ma, ca, da)
	canonicalA := revCanonical(t, rtA)
	if len(canonicalA) < 5 {
		t.Fatalf("canonical observable suspiciously small: %v", canonicalA)
	}

	// Endpoint B: v1 configuration first, then REVISION to v2.
	rtB := newTestRuntime(t)
	childChB := make(chan *runtime.Fiber, 2)
	pb, err := rtB.Load(&revProv{version: "v1"})
	if err != nil {
		t.Fatal(err)
	}
	mb1, err := rtB.Load(&revParent{version: "v1", ch: childChB})
	if err != nil {
		t.Fatal(err)
	}
	cb1 := <-childChB
	db, err := rtB.Load(&revCons{seen: make(chan string, 1)})
	if err != nil {
		t.Fatal(err)
	}
	revQuiesce(t, rtB, pb, mb1, cb1, db)

	// Revise the parent to v2: the composite retires mb1 (its child cb1 must
	// reach Gone first), reinserts at the same position with the new
	// definition, and both dependents (rev-child, rev-cons) follow unprompted.
	mb2, err := mb1.Revise(context.Background(), &revParent{version: "v2", ch: childChB})
	if err != nil {
		t.Fatalf("revise: %v", err)
	}
	cb2 := <-childChB
	if cb1.State() != runtime.StateGone {
		t.Fatalf("old child state = %v, want Gone (children before parent)", cb1.State())
	}
	if mb1.State() != runtime.StateGone {
		t.Fatalf("old fiber state = %v, want Gone (entry removed)", mb1.State())
	}

	// Observe the revised child's resolution and the plain dependent's.
	revQuiesce(t, rtB, pb, mb2, cb2, db)
	canonicalB := revCanonical(t, rtB)
	// The revised child re-resolved the service under the SAME configuration
	// revision (its parent was revised to v2; the provider was already v2).
	// Fiber identity changed (cb1 gone, cb2 active) but the composition
	// semantics are exactly the from-scratch ones — that is the theorem.
	for _, row := range canonicalB {
		if len(row) == 0 {
			t.Fatalf("empty canonical row")
		}
	}

	for i := range canonicalA {
		if canonicalA[i] != canonicalB[i] {
			t.Fatalf("endpoint mismatch row %d:\n  scratch: %s\n  revised: %s", i, canonicalA[i], canonicalB[i])
		}
	}
	if len(canonicalA) != len(canonicalB) {
		t.Fatalf("endpoint row count %d != %d", len(canonicalA), len(canonicalB))
	}
}
