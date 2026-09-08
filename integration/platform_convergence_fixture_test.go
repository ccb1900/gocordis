package integration

import (
	"context"
	"fmt"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// ---------------------------------------------------------------------------
// Platform Convergence E2E Gate (spec docs/.../Platform Convergence E2E Gate
// Specification v0.1) — shared fixture.
//
// Everything here exercises the REAL public boundaries only: runtime.Component
// / runtime.Context / runtime.Runtime / Fiber and the public extension APIs.
// No kernel private state is touched.
// ---------------------------------------------------------------------------

var (
	// pcSvcKey is the single service capability of the reference composition
	// (PC-03/04/07/08/16).
	pcSvcKey = runtime.NewKey[string]("pc.svc")
	// pcEvKey is the reference-composition kernel event (PC-06/07/25).
	pcEvKey = runtime.NewEventKey[string]("pc.evt")
)

// pcRec is a concurrency-safe ordered recorder for effect/event/cleanup
// observation.
type pcRec struct {
	mu    sync.Mutex
	calls []string
}

func (r *pcRec) add(s string) {
	r.mu.Lock()
	defer r.mu.Unlock()
	r.calls = append(r.calls, s)
}

func (r *pcRec) got() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.calls...)
}

func (r *pcRec) join() string {
	return strings.Join(r.got(), ",")
}

func (r *pcRec) has(s string) bool {
	for _, c := range r.got() {
		if c == s {
			return true
		}
	}
	return false
}

// pcComp is one behavior-parameterized runtime.Component. A fresh instance is
// constructed for every Load so activations never share mutable state.
type pcComp struct {
	name    string
	inject  []runtime.Dependency
	provide []runtime.Capability
	apply   func(*runtime.Context) (runtime.Cleanup, error)
}

func (c *pcComp) Name() string                  { return c.name }
func (c *pcComp) Inject() []runtime.Dependency  { return c.inject }
func (c *pcComp) Provide() []runtime.Capability { return c.provide }
func (c *pcComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	return c.apply(ctx)
}

// pcProv is a root-scope provider of pcSvcKey with a distinct tag.
func pcProv(tag string) *pcComp {
	return &pcComp{
		name:    "pc-prov:" + tag,
		provide: []runtime.Capability{pcSvcKey.Capability()},
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if err := runtime.Provide(ctx, pcSvcKey, tag); err != nil {
				return nil, err
			}
			return nil, nil
		},
	}
}

// pcCons is a root-scope consumer of pcSvcKey. Every successful (re)binding
// writes the resolved tag to seen and bumps the kit counters.
func pcCons(k *kit, seen chan<- string) *pcComp {
	return &pcComp{
		name:   "pc-cons",
		inject: []runtime.Dependency{runtime.Requires(pcSvcKey)},
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if k != nil {
				k.rec()
			}
			v, err := runtime.Require(ctx, pcSvcKey)
			if err != nil {
				return nil, err
			}
			if seen != nil {
				select {
				case seen <- v:
				default:
				}
			}
			return func() error {
				if k != nil {
					k.done()
				}
				return nil
			}, nil
		},
	}
}

// pcFailApply always fails activation.
func pcFailApply(name string) *pcComp {
	return &pcComp{
		name: name,
		apply: func(*runtime.Context) (runtime.Cleanup, error) {
			return nil, fmt.Errorf("injected apply failure (%s)", name)
		},
	}
}

// pcGateApply blocks until release fires or the activation context is done.
// Used to hold a fiber deterministically in Loading.
func pcGateApply(name string, release <-chan struct{}) *pcComp {
	return &pcComp{
		name: name,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			select {
			case <-release:
				return nil, nil
			case <-ctx.Context().Done():
				return nil, ctx.Context().Err()
			}
		},
	}
}

// pcPanicApply panics inside Apply (PC-23).
func pcPanicApply(name string) *pcComp {
	return &pcComp{
		name: name,
		apply: func(*runtime.Context) (runtime.Cleanup, error) {
			panic("pc: injected Apply panic")
		},
	}
}

// pcEffectHost installs effects in a caller-defined order and records both
// installs ("+X") and inverse runs ("-X") (PC-05 ordering).
func pcEffectHost(name string, rec *pcRec, labels ...string) *pcComp {
	return &pcComp{
		name: name,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			for _, l := range labels {
				l := l
				if err := ctx.Effect(func() (func() error, error) {
					rec.add("+" + l)
					return func() error {
						rec.add("-" + l)
						return nil
					}, nil
				}); err != nil {
					return nil, err
				}
			}
			return nil, nil
		},
	}
}

// pcEvRegistrar registers a kernel event handler for pcEvKey during its own
// Apply (PC-06/07). The handler records "name:payload".
func pcEvRegistrar(name string, rec *pcRec) *pcComp {
	return &pcComp{
		name: name,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			err := runtime.On(ctx, pcEvKey, func(c context.Context, payload string) error {
				rec.add(name + ":" + payload)
				return nil
			})
			return nil, err
		},
	}
}

// pcEvEmitter captures its activation Context so the test can dispatch through
// a live, owned emitter (public event.Emit API only).
func pcEvEmitter(dst **runtime.Context) *pcComp {
	return &pcComp{
		name: "pc-ev-emitter",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			*dst = ctx
			return nil, nil
		},
	}
}

// pcLeaf is a leaf component (no dependencies/capabilities); it records its
// apply/cleanup cycles through the shared kit.
func pcLeaf(name string, k *kit) *pcComp {
	return &pcComp{
		name: name,
		apply: func(*runtime.Context) (runtime.Cleanup, error) {
			if k != nil {
				k.rec()
			}
			return func() error {
				if k != nil {
					k.done()
				}
				return nil
			}, nil
		},
	}
}

// pcChildHost owns one child Fiber created during Apply (PC-09/18/25).
func pcChildHost(name string, child runtime.Component, got **runtime.Fiber) *pcComp {
	return &pcComp{
		name: name,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			cf, err := ctx.Child(child)
			if err != nil {
				return nil, err
			}
			*got = cf
			return nil, nil
		},
	}
}

// pcScopeHost mounts one provider and one consumer of pcSvcKey into the realm
// the host fiber itself was mounted into (children inherit the realm).
func pcScopeHost(tag string, ch chan<- *runtime.Fiber, out chan<- string) *pcComp {
	return &pcComp{
		name: "pc-scope-host:" + tag,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			pf, err := ctx.Child(pcProv(tag))
			if err != nil {
				return nil, err
			}
			if err := pf.Ready(ctx.Context()); err != nil {
				return nil, err
			}
			xf, err := ctx.Child(pcCons(nil, out))
			if err != nil {
				return nil, err
			}
			ch <- pf
			ch <- xf
			return nil, nil
		},
	}
}

// pcRealmActivator mounts two sibling explicit realms A and B, each hosting
// its own provider+consumer pair of the same logical key (PC-08).
func pcRealmActivator(chA, chB chan<- *runtime.Fiber, outA, outB chan<- string) *pcComp {
	return &pcComp{
		name: "pc-realm-activator",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if _, err := ctx.Child(pcScopeHost("A", chA, outA), runtime.WithScope()); err != nil {
				return nil, err
			}
			if _, err := ctx.Child(pcScopeHost("B", chB, outB), runtime.WithScope()); err != nil {
				return nil, err
			}
			return nil, nil
		},
	}
}

// pcScopedConsHost mounts one consumer (no provider) into the host's realm.
func pcScopedConsHost(ch chan<- *runtime.Fiber, out chan<- string) *pcComp {
	return &pcComp{
		name: "pc-scoped-consumer-host",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			// Paper model (ADR-0001): the scope namespace must contain its own
			// provider for the consumer to bind — no ancestor fallback.
			pf, err := ctx.Child(pcProv("v1"))
			if err != nil {
				return nil, err
			}
			if err := pf.Ready(ctx.Context()); err != nil {
				return nil, err
			}
			xf, err := ctx.Child(pcCons(nil, out))
			if err != nil {
				return nil, err
			}
			ch <- xf
			return nil, nil
		},
	}
}

// pcScopedConsActivator mounts a single explicit scope realm containing a
// scope-local provider and consumer (PC-08 scope composition).
func pcScopedConsActivator(ch chan<- *runtime.Fiber, out chan<- string) *pcComp {
	return &pcComp{
		name: "pc-scoped-consumer-activator",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if _, err := ctx.Child(pcScopedConsHost(ch, out), runtime.WithScope()); err != nil {
				return nil, err
			}
			return nil, nil
		},
	}
}

// pcEmptyScopeConsActivator mounts an explicit scope whose ONLY content is a
// consumer — its namespace has no provider for the key (PC-08 no-fallback).
func pcEmptyScopeConsActivator(ch chan<- *runtime.Fiber, out chan<- string) *pcComp {
	return &pcComp{
		name: "pc-empty-scope-cons-activator",
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			xf, err := ctx.Child(pcCons(nil, out), runtime.WithScope())
			if err != nil {
				return nil, err
			}
			ch <- xf
			return nil, nil
		},
	}
}

// pcReentrantRoot creates a child composition and loads a nested root fiber
// from inside Apply through the runtime handle captured at construction
// (PC-25).
func pcReentrantRoot(name string, rt *runtime.Runtime, child runtime.Component, rec *pcRec) *pcComp {
	return &pcComp{
		name: name,
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if _, err := ctx.Child(child); err != nil {
				return nil, err
			}
			if f, err := rt.Load(pcLeaf("pc-reentrant-leaf", nil)); err != nil {
				return nil, err
			} else if err := f.Ready(ctx.Context()); err != nil {
				return nil, err
			}
			rec.add("root-loaded")
			return nil, nil
		},
	}
}

// ---------------------------------------------------------------------------
// Deterministic lifecycle + snapshot helpers (all public API).
// ---------------------------------------------------------------------------

func pcNewRT(t *testing.T) *runtime.Runtime {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatalf("runtime.New: %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
		defer cancel()
		if err := rt.Close(ctx); err != nil {
			t.Logf("cleanup runtime close: %v", err)
		}
	})
	return rt
}

func pcTimeout(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// pcLoadActive loads a component and waits for its first activation.
func pcLoadActive(t *testing.T, rt *runtime.Runtime, comp runtime.Component) *runtime.Fiber {
	t.Helper()
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatalf("rt.Load(%s): %v", comp.Name(), err)
	}
	if err := f.Ready(pcTimeout(t)); err != nil {
		t.Fatalf("fiber %s not active: %v", f.Name(), err)
	}
	return f
}

// pcDisposeGone disposes f and waits until it reaches Gone.
func pcDisposeGone(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	if err := f.Dispose(); err != nil {
		t.Fatalf("Dispose(%s): %v", f.Name(), err)
	}
	if err := f.Gone(pcTimeout(t)); err != nil {
		t.Fatalf("fiber %s not gone: %v", f.Name(), err)
	}
}

func pcSnap(t *testing.T, rt *runtime.Runtime) runtime.RuntimeSnapshot {
	t.Helper()
	snap, err := rt.Snapshot(pcTimeout(t))
	if err != nil {
		t.Fatalf("Snapshot: %v", err)
	}
	return snap
}

func pcLiveFiberCount(snap runtime.RuntimeSnapshot) int { return len(snap.Fibers) }
func pcProviderCount(snap runtime.RuntimeSnapshot) int  { return len(snap.Providers) }
func pcEffectCount(snap runtime.RuntimeSnapshot) int    { return len(snap.Effects) }
func pcScopeCount(snap runtime.RuntimeSnapshot) int     { return len(snap.Scopes) }

// pcQuiesced polls until the runtime projects zero live fibers / providers /
// effects on the orchestrator-linearized Snapshot. No sleeps for the happy
// path: Ready/Gone and snapshot linearization provide the synchronization.
func pcQuiesced(t *testing.T, rt *runtime.Runtime, what string) {
	t.Helper()
	deadline := time.Now().Add(15 * time.Second)
	for time.Now().Before(deadline) {
		snap := pcSnap(t, rt)
		if len(snap.Fibers) == 0 && len(snap.Providers) == 0 && len(snap.Effects) == 0 {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	snap := pcSnap(t, rt)
	t.Fatalf("runtime not quiesced: %s (fibers=%d providers=%d effects=%d)", what,
		len(snap.Fibers), len(snap.Providers), len(snap.Effects))
}

// pcLiveFiberNames returns the sorted names of live fibers (Gone fibers are
// excluded by the kernel's live projection).
func pcLiveFiberNames(snap runtime.RuntimeSnapshot) []string {
	names := make([]string, 0, len(snap.Fibers))
	for _, f := range snap.Fibers {
		if f.State == runtime.StateGone {
			continue
		}
		names = append(names, f.Name)
	}
	sort.Strings(names)
	return names
}

func pcNamesEq(got []string, want ...string) bool {
	if len(got) != len(want) {
		return false
	}
	for i := range want {
		if got[i] != want[i] {
			return false
		}
	}
	return true
}

func pcNamesStr(names []string) string { return strings.Join(names, ",") }

// pcRecv reads one value from ch with a timeout (deterministic waiting).
func pcRecv[T any](t *testing.T, ch <-chan T, what string) T {
	t.Helper()
	select {
	case v := <-ch:
		return v
	case <-time.After(10 * time.Second):
		t.Fatalf("timeout waiting for %s", what)
		var zero T
		return zero
	}
}

// pcCanonical reduces a Snapshot to a deterministic, runtime-ID-independent
// summary for cross-runtime equivalence checks (Thm80): the sorted live fiber
// set, provider bindings, and dependency status.
func pcCanonical(snap runtime.RuntimeSnapshot) string {
	var b strings.Builder
	fibers := append([]runtime.FiberSnapshot(nil), snap.Fibers...)
	sort.Slice(fibers, func(i, j int) bool { return fibers[i].Name < fibers[j].Name })
	for _, f := range fibers {
		if f.State == runtime.StateGone {
			continue
		}
		live := 0
		if f.ActivationID != 0 {
			live = 1
		}
		fmt.Fprintf(&b, "fiber:%s:%s:%d;", f.Name, f.State, live)
		if f.ParentFiberID != 0 {
			for _, g := range fibers {
				if g.ID == f.ParentFiberID {
					fmt.Fprintf(&b, "child-of:%s;", g.Name)
					break
				}
			}
		}
	}
	provs := append([]runtime.ProviderView(nil), snap.Providers...)
	sort.Slice(provs, func(i, j int) bool {
		if provs[i].Key != provs[j].Key {
			return provs[i].Key < provs[j].Key
		}
		return provs[i].OwnerFiberID < provs[j].OwnerFiberID
	})
	for _, p := range provs {
		owner := ""
		for _, f := range fibers {
			if f.ID == p.OwnerFiberID {
				owner = f.Name
				break
			}
		}
		fmt.Fprintf(&b, "prov:%s:%s;", p.Key, owner)
	}
	deps := append([]runtime.DependencyView(nil), snap.Dependencies...)
	sort.Slice(deps, func(i, j int) bool { return deps[i].Key < deps[j].Key })
	for _, d := range deps {
		fmt.Fprintf(&b, "dep:%s:%s;", d.Key, d.Status)
	}
	return b.String()
}
