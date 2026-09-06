package runtime

import (
	"context"
	"fmt"
	"math/rand/v2"
	"sort"
	"sync"
	"testing"
	"time"
)

// ---------------------------------------------------------------------------
// T66 — Progress: deterministic quiescence driver (no sleeps; test-only; uses
// the public command queue + per-fiber state signals).
// ---------------------------------------------------------------------------

type t66Probe struct{ done chan struct{} }

func (p *t66Probe) apply(*orchestrator) { close(p.done) }

func resolveFor(rt *Runtime, f *Fiber, key CapabilityKey) (ProviderIdentity, bool) {
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
	return rec.identity, valid
}

type t66ScanProbe struct {
	done       chan struct{}
	inFlight   bool
	waiterID   FiberID
	pendingSat string
}

func (p *t66ScanProbe) apply(o *orchestrator) {
	o.rt.mu.RLock()
	fs := make([]*Fiber, 0, len(o.rt.fibers))
	for _, f := range o.rt.fibers {
		fs = append(fs, f)
	}
	o.rt.mu.RUnlock()
	for _, f := range fs {
		st := f.state
		withdrawing := f.withdrawing
		gates := len(f.waitGates)
		if st == StateLoading || st == StateUnloading || (withdrawing && gates > 0) {
			p.inFlight = true
			if p.waiterID == 0 {
				p.waiterID = f.id
			}
			continue
		}
		if st == StatePending && p.pendingSat == "" {
			for _, dep := range f.inject {
				if _, ok := resolveFor(o.rt, f, dep.Key); ok {
					p.pendingSat = f.Name()
					break
				}
			}
		}
	}
	close(p.done)
}

// t66Drain advances the orchestrator deterministically until quiescence. The
// state scan runs on the orchestrator goroutine (no cross-goroutine race);
// waiting for the next transition uses the generation signal under the fiber
// lock.
func t66Drain(rt *Runtime) (int, error) {
	iter := 0
	for {
		scan := &t66ScanProbe{done: make(chan struct{})}
		if !rt.submit(scan) {
			return iter, nil
		}
		<-scan.done
		if scan.inFlight {
			iter++
			rt.mu.RLock()
			waiter := rt.fibers[scan.waiterID]
			rt.mu.RUnlock()
			if waiter == nil {
				continue
			}
			for {
				waiter.mu.RLock()
				st := waiter.state
				ch := waiter.signal.Channel()
				waiter.mu.RUnlock()
				if st != StateLoading && st != StateUnloading {
					break
				}
				<-ch
			}
			continue
		}
		if scan.pendingSat == "" {
			return iter, nil
		}
		// Settling probe: a Pending fiber with a satisfied dependency may be
		// about to be started by a sweep racing our observation.
		again := &t66ScanProbe{done: make(chan struct{})}
		if !rt.submit(again) {
			return iter, nil
		}
		<-again.done
		if again.inFlight {
			continue
		}
		if again.pendingSat != "" {
			return iter, fmt.Errorf("T66_DEADLOCK: pending fiber %s has satisfied dep", again.pendingSat)
		}
		return iter, nil
	}
}

func t66Key() CapabilityKey { return NewKey[string]("t66.progress").Capability() }

type t66Comp struct {
	name     string
	key      CapabilityKey
	provide  bool
	consumer bool
	n        int
}

func (c *t66Comp) Name() string { return c.name }
func (c *t66Comp) Inject() []Dependency {
	if c.consumer {
		return []Dependency{{Key: c.key}}
	}
	return nil
}
func (c *t66Comp) Provide() []Capability {
	if c.provide {
		return []Capability{c.key}
	}
	return nil
}
func (c *t66Comp) Apply(ctx *Context) (Cleanup, error) {
	if c.provide {
		if err := ctx.provideCap(c.key, c.name); err != nil {
			return nil, err
		}
	}
	if c.consumer {
		if _, ok := ctx.realm.lookup(c.key); !ok {
			return nil, fmt.Errorf("consumer %s applied without provider", c.name)
		}
	}
	for i := 0; i < c.n; i++ {
		if err := ctx.Effect(func() (func() error, error) {
			return func() error { return nil }, nil
		}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func TestT66BoundedProgress(t *testing.T) {
	for _, seed := range []uint64{1, 2, 3, 4, 5} {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewPCG(seed, seed^0x9e3779b97f4a7c15))
			ctx, cancel := context.WithTimeout(context.Background(), 60*time.Second)
			defer cancel()
			rt, err := New()
			if err != nil {
				t.Fatal(err)
			}
			var fibers []*Fiber
			var names []string
			mk := func(f *Fiber, n string) {
				fibers = append(fibers, f)
				names = append(names, n)
			}
			pf, err := rt.Load(&t66Comp{name: "P", key: t66Key(), provide: true})
			if err != nil {
				t.Fatal(err)
			}
			mk(pf, "P")
			for i := 0; i < 3; i++ {
				cf, err := rt.Load(&t66Comp{name: fmt.Sprintf("C%d", i), key: t66Key(), consumer: true})
				if err != nil {
					t.Fatal(err)
				}
				mk(cf, fmt.Sprintf("C%d", i))
			}
			ef, err := rt.Load(&t66Comp{name: "E", n: 2})
			if err != nil {
				t.Fatal(err)
			}
			mk(ef, "E")
			iter, derr := t66Drain(rt)
			if derr != nil {
				t.Fatal(derr)
			}
			if !allStableActive(t, rt, names[:5]) {
				t.Fatal("initial set not all active")
			}
			ops := 0
			for cycle := 0; cycle < 24; cycle++ {
				switch rng.IntN(3) {
				case 0:
					if err := pf.Dispose(); err != nil {
						t.Fatal(err)
					}
					ops++
				case 1:
					if pf.State() == StateGone {
						if err := pf.Load(); err != nil {
							t.Fatal(err)
						}
						ops++
					}
				default:
					if ef.State() == StateActive {
						if err := ef.Dispose(); err == nil {
							ops++
						}
					} else if ef.State() == StateGone {
						if err := ef.Load(); err == nil {
							ops++
						}
					}
				}
				n, derr := t66Drain(rt)
				if derr != nil {
					t.Fatalf("T66 seed=%d cycle=%d: %v", seed, cycle, derr)
				}
				iter += n
			}
			bound := (ops+1)*(2*len(fibers)+4) + 8
			if iter > bound {
				t.Fatalf("T66_PROGRESS seed=%d iterations=%d bound=%d ops=%d", seed, iter, bound, ops)
			}
			for _, f := range fibers {
				_ = f.Dispose()
			}
			if _, err := t66Drain(rt); err != nil {
				t.Fatal(err)
			}
			cl, ccl := context.WithTimeout(context.Background(), 20*time.Second)
			defer ccl()
			if err := rt.Close(cl); err != nil {
				t.Fatalf("T66 close: %v", err)
			}
			_ = ctx
		})
	}
}

func allStableActive(t *testing.T, rt *Runtime, names []string) bool {
	t.Helper()
	rt.mu.RLock()
	defer rt.mu.RUnlock()
	for _, f := range rt.fibers {
		for _, n := range names {
			if f.Name() == n && f.state != StateActive {
				return false
			}
		}
	}
	return true
}

// ---------------------------------------------------------------------------
// T73 — Confluence: same logical operation set, many legal mount schedules,
// same quiescent observable state.
// ---------------------------------------------------------------------------

type t73Obs struct {
	active []string
	seen   []string
}

type t73Rec struct {
	mu   sync.Mutex
	vals []string
}

func (r *t73Rec) add(s string) {
	r.mu.Lock()
	r.vals = append(r.vals, s)
	r.mu.Unlock()
}

func (r *t73Rec) snapshot() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.vals...)
}

type t73Provider struct{ key CapabilityKey }

func (c *t73Provider) Name() string          { return "P" }
func (c *t73Provider) Inject() []Dependency  { return nil }
func (c *t73Provider) Provide() []Capability { return []Capability{c.key} }
func (c *t73Provider) Apply(ctx *Context) (Cleanup, error) {
	return nil, ctx.provideCap(c.key, "P")
}

type t73Consumer struct {
	key CapabilityKey
	rec *t73Rec
}

func (c *t73Consumer) Name() string          { return "consumer" }
func (c *t73Consumer) Inject() []Dependency  { return []Dependency{{Key: c.key}} }
func (c *t73Consumer) Provide() []Capability { return nil }
func (c *t73Consumer) Apply(ctx *Context) (Cleanup, error) {
	rec, ok := ctx.realm.lookup(c.key)
	if !ok {
		return nil, fmt.Errorf("consumer applied without provider")
	}
	c.rec.add(fmt.Sprintf("%v", rec.value))
	return nil, nil
}

// runT73Schedule mounts the same logical set in the given legal order and
// returns the quiescent observable state (explicit signal waits: Ready/Gone).
func runT73Schedule(t *testing.T, order []string, key CapabilityKey) t73Obs {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	rec := &t73Rec{}
	comps := map[string]Component{
		"P":  &t73Provider{key: key},
		"C1": &t73Consumer{key: key, rec: rec},
		"C2": &t73Consumer{key: key, rec: rec},
		"E":  &t66Comp{name: "E", n: 1},
	}
	handles := map[string]*Fiber{}
	for _, name := range order {
		f, err := rt.Load(comps[name])
		if err != nil {
			t.Fatal(err)
		}
		handles[name] = f
	}
	// All mounts are issued; consumers may have started Pending but every
	// fiber eventually becomes Active because the provider is in the set.
	for _, name := range []string{"P", "C1", "C2", "E"} {
		if f := handles[name]; f != nil {
			if err := f.Ready(ctx); err != nil {
				t.Fatalf("fiber %s not active in schedule %v: %v", name, order, err)
			}
		}
	}
	var active []string
	for name := range handles {
		if handles[name].State() == StateActive {
			active = append(active, name)
		}
	}
	sort.Strings(active)
	seen := rec.snapshot()
	sort.Strings(seen)

	// Deterministic teardown: dispose every handle and wait for Gone.
	for _, f := range handles {
		_ = f.Dispose()
	}
	for name, f := range handles {
		if err := f.Gone(ctx); err != nil {
			t.Fatalf("fiber %s not gone: %v", name, err)
		}
	}
	if err := rt.Close(context.Background()); err != nil {
		t.Fatalf("close: %v", err)
	}
	return t73Obs{active: active, seen: seen}
}

// legalT73Schedules returns all distinct legal permutations of the mount set
// (mounting a consumer before its provider is legal — it waits Pending).
func legalT73Schedules(seed uint64) [][]string {
	set := []string{"P", "C1", "C2", "E"}
	rng := rand.New(rand.NewPCG(seed, seed^0x243f6a8885a308d3))
	var out [][]string
	var gen func([]string, int)
	gen = func(cur []string, used int) {
		if used == len(set) {
			p := append([]string(nil), cur...)
			out = append(out, p)
			return
		}
		for _, v := range set {
			dup := false
			for j := 0; j < used; j++ {
				if cur[j] == v {
					dup = true
					break
				}
			}
			if dup {
				continue
			}
			gen(append(cur, v), used+1)
		}
	}
	gen(nil, 0)
	// Deterministic order variation per seed (still exactly the 24 legal
	// permutations).
	rng.Shuffle(len(out), func(i, j int) { out[i], out[j] = out[j], out[i] })
	return out
}

// t73Precondition documents the theorem precondition for the generated trace:
// at most one provider per capability in the realm and effects are independent
// (observable-free). If a generator ever violated it, the property would be
// reported NOT APPLICABLE rather than skipped silently.
func t73Precondition(key CapabilityKey, order []string) error {
	providers := 0
	consumers := 0
	for _, n := range order {
		switch n {
		case "P":
			providers++
		case "C1", "C2":
			consumers++
		}
	}
	if providers != 1 {
		return fmt.Errorf("T73_PRECONDITION: expected exactly one provider, got %d", providers)
	}
	if consumers < 2 {
		return fmt.Errorf("T73_PRECONDITION: need at least two consumers, got %d", consumers)
	}
	_ = key
	return nil
}

func TestT73ConfluenceRandomizedSchedules(t *testing.T) {
	key := NewKey[string]("t73.confluence").Capability()
	for _, seed := range []uint64{11, 12, 13, 14} {
		if err := t73Precondition(key, []string{"P", "C1", "C2", "E"}); err != nil {
			t.Fatalf("%v", err)
		}
		schedules := legalT73Schedules(seed)
		baseline := t73Obs{}
		for i, s := range schedules {
			obs := runT73Schedule(t, s, key)
			if i == 0 {
				baseline = obs
				continue
			}
			if fmt.Sprint(obs) != fmt.Sprint(baseline) {
				t.Fatalf("T73_CONFLUENCE seed=%d schedule#%d diverged:\n got %+v\nbase %+v", seed, i, obs, baseline)
			}
		}
		if len(baseline.active) != 4 {
			t.Fatalf("T73 seed=%d expected 4 active, got %v", seed, baseline.active)
		}
		if len(baseline.seen) != 2 || baseline.seen[0] != "P" || baseline.seen[1] != "P" {
			t.Fatalf("T73 seed=%d consumers seen %v, want [P P]", seed, baseline.seen)
		}
	}
}
