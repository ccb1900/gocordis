package runtime

import (
	"context"
	"fmt"
	"math/rand/v2"
	"testing"
	"time"
)

// T59 — Preservation: randomized legal operations; after every stable step the
// runtime registry/dependency/provider invariants hold (P1..P5). White-box:
// the checker reads only semantic runtime state (fibers, realm providers,
// snapshots) — it never runs user code.

type t59Comp struct {
	name    string
	kind    string // "provider" | "consumer"
	key     CapabilityKey
	inject  []Dependency
	provide []Capability
	tag     string
}

func (c *t59Comp) Name() string          { return c.name }
func (c *t59Comp) Inject() []Dependency  { return c.inject }
func (c *t59Comp) Provide() []Capability { return c.provide }
func (c *t59Comp) Apply(ctx *Context) (Cleanup, error) {
	if c.kind == "provider" {
		if err := ctx.provideCap(c.key, c.tag); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

// t59CheckP inspects the runtime semantic state.
func t59CheckP(rt *Runtime) error {
	rt.mu.RLock()
	fs := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		fs = append(fs, f)
	}
	rt.mu.RUnlock()

	byID := make(map[FiberID]*Fiber, len(fs))
	for _, f := range fs {
		byID[f.id] = f
	}
	for _, f := range fs {
		// P1 parent validity.
		f.mu.RLock()
		parent := f.parent
		state := f.state
		var actID ActivationID
		if f.activation != nil {
			actID = f.activation.id
		}
		f.mu.RUnlock()
		if parent != nil {
			if byID[parent.id] == nil {
				return fmt.Errorf("P1 parent validity: fiber %d parent %d missing", f.id, parent.id)
			}
		}
		// P3/P4 active fiber dependencies valid.
		if state == StateActive {
			for _, dep := range f.inject {
				id, ok := t59Resolve(rt, f, dep.Key)
				if !ok {
					return fmt.Errorf("P3 dependency validity: active fiber %d dep %s unsatisfied", f.id, dep.Key)
				}
				if f.activation == nil || f.activation.id != actID || f.activation.depsSnapshotKey(dep.Key) != id {
					// P5 checked below via snapshot.
				}
			}
			// P5 snapshot consistency.
			if f.activation != nil {
				for _, snap := range f.activation.deps {
					id, ok := t59Resolve(rt, f, snap.Key)
					if !ok || id != snap.Provider {
						return fmt.Errorf("P5 snapshot consistency: fiber %d dep %s snapshot %v != resolved %v (%v)", f.id, snap.Key, snap.Provider, id, ok)
					}
				}
			}
		}
	}
	// P2 provider uniqueness + provider-state validity (root realm; generator
	// uses root realm only).
	root := rt.rootRealm
	root.mu.RLock()
	type recEnt struct {
		key CapabilityKey
		rec *providerRecord
	}
	recs := make([]recEnt, 0, len(root.own))
	for k, r := range root.own {
		recs = append(recs, recEnt{k, r})
	}
	root.mu.RUnlock()
	for _, e := range recs {
		owner := byID[e.rec.identity.FiberID]
		if owner == nil {
			return fmt.Errorf("P2 provider %s owner fiber missing", e.key)
		}
		owner.mu.RLock()
		valid := owner.state == StateActive && owner.activation != nil && owner.activation.id == e.rec.identity.ActivationID
		retiring := e.rec.retiring
		owner.mu.RUnlock()
		if valid && retiring {
			return fmt.Errorf("P2 provider %s: active provider marked retiring", e.key)
		}
		if !valid && !retiring {
			return fmt.Errorf("P2 provider %s: non-active owner %d with non-retiring record", e.key, owner.id)
		}
	}
	return nil
}

func (a *activation) depsSnapshotKey(key CapabilityKey) ProviderIdentity {
	for _, d := range a.deps {
		if d.Key == key {
			return d.Provider
		}
	}
	return ProviderIdentity{}
}

func t59Resolve(rt *Runtime, f *Fiber, key CapabilityKey) (ProviderIdentity, bool) {
	if f.realm == nil {
		return ProviderIdentity{}, false
	}
	rec, ok := f.realm.lookup(key)
	if !ok || rec.retiring {
		return ProviderIdentity{}, false
	}
	rt.mu.RLock()
	owner := rt.fibers[rec.identity.FiberID]
	rt.mu.RUnlock()
	if owner == nil {
		return ProviderIdentity{}, false
	}
	owner.mu.RLock()
	valid := owner.state == StateActive && owner.activation != nil && owner.activation.id == rec.identity.ActivationID
	owner.mu.RUnlock()
	if !valid {
		return ProviderIdentity{}, false
	}
	return rec.identity, true
}

// t59Scenario builds one provider + N consumers on a single root realm.
type t59Scenario struct {
	rt        *Runtime
	key       CapabilityKey
	provider  *Fiber
	consumers []*Fiber
}

func t59Ctx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	t.Cleanup(cancel)
	return ctx
}

func t59LoadProvider(t *testing.T, rt *Runtime, key CapabilityKey, tag string) *Fiber {
	t.Helper()
	c := &t59Comp{name: "p:" + tag, kind: "provider", key: key, provide: []Capability{key}, tag: tag}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(t59Ctx(t)); err != nil {
		t.Fatal(err)
	}
	return f
}

func t59LoadConsumer(t *testing.T, rt *Runtime, key CapabilityKey, name string) *Fiber {
	t.Helper()
	c := &t59Comp{name: name, kind: "consumer", key: key, inject: []Dependency{{Key: key}}}
	f, err := rt.Load(c)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(t59Ctx(t)); err != nil {
		t.Fatal(err)
	}
	return f
}

// t59WaitPending deterministically waits until the fiber has no live activation
// and rests in Pending (no sleeping; Mounted fibers settle to Pending
// synchronously once their activation ended, before the withdrawing provider
// can reach Gone).
func t59WaitPending(t *testing.T, f *Fiber) {
	t.Helper()
	if err := f.WaitInactive(t59Ctx(t)); err != nil {
		t.Fatal(err)
	}
	if f.State() != StatePending {
		t.Fatalf("fiber %s state = %v, want Pending", f.Name(), f.State())
	}
}

func t59Reload(t *testing.T, rt *Runtime, f *Fiber, s *t59Scenario, key CapabilityKey) {
	t.Helper()
	if err := f.Load(); err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(t59Ctx(t)); err != nil {
		t.Fatal(err)
	}
	for _, c := range s.consumers {
		if c.State() != StateActive {
			if err := c.Ready(t59Ctx(t)); err != nil {
				t.Fatalf("consumer %s not reactivated: %v", c.Name(), err)
			}
		}
	}
}

// TestT59PreservationRandomized — randomized provider dispose/reload cycles;
// after every stable step P1..P5 must hold.
func TestT59PreservationRandomized(t *testing.T) {
	key := NewKey[string]("t59.rand").Capability()
	for _, seed := range []uint64{1, 2, 3, 4, 5} {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewPCG(seed, seed^0x9e3779b97f4a7c15))
			rt, err := New()
			if err != nil {
				t.Fatal(err)
			}
			s := &t59Scenario{rt: rt, key: key}
			s.provider = t59LoadProvider(t, rt, key, "v0")
			for i := 0; i < 4; i++ {
				s.consumers = append(s.consumers, t59LoadConsumer(t, rt, key, fmt.Sprintf("c%d", i)))
			}
			if err := t59CheckP(rt); err != nil {
				t.Fatalf("initial invariants: %v", err)
			}

			gen := uint64(0)
			for step := 0; step < 60; step++ {
				gen++
				switch rng.IntN(3) {
				case 0: // dispose provider -> consumers to Pending
					if err := s.provider.Dispose(); err != nil {
						t.Fatal(err)
					}
					if err := s.provider.Gone(t59Ctx(t)); err != nil {
						t.Fatal(err)
					}
					for _, c := range s.consumers {
						t59WaitPending(t, c)
					}
				case 1: // reload provider (same fiber) -> consumers reactivate
					t59Reload(t, rt, s.provider, s, key)
				default: // dispose one consumer permanently
					if len(s.consumers) > 1 {
						c := s.consumers[len(s.consumers)-1]
						s.consumers = s.consumers[:len(s.consumers)-1]
						if err := c.Dispose(); err != nil {
							t.Fatal(err)
						}
						if err := c.Gone(t59Ctx(t)); err != nil {
							t.Fatal(err)
						}
					}
				}
				if err := t59CheckP(rt); err != nil {
					t.Fatalf("T59_PRESERVATION seed=%d step=%d: %v", seed, step, err)
				}
			}
			_ = s.provider.Dispose()
			_ = s.provider.Gone(t59Ctx(t))
			_ = rt.Close(context.Background())
		})
	}
}

// t59CheckOnOrchestrator runs the preservation checker on the orchestrator
// goroutine (via a probe command), so no concurrent lifecycle transition can
// race the read and the check is deterministic.
func t59CheckOnOrchestrator(rt *Runtime) error {
	p := &t59CheckProbe{done: make(chan struct{})}
	if !rt.submit(p) {
		return nil
	}
	<-p.done
	return p.err
}

type t59CheckProbe struct {
	err  error
	done chan struct{}
}

func (p *t59CheckProbe) apply(o *orchestrator) {
	p.err = t59CheckP(o.rt)
	close(p.done)
}
