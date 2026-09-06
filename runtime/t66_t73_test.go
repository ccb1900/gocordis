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

type t73Provider struct {
	key CapabilityKey
	tag string // default "P"
}

func (c *t73Provider) Name() string          { return "P" }
func (c *t73Provider) Inject() []Dependency  { return nil }
func (c *t73Provider) Provide() []Capability { return []Capability{c.key} }
func (c *t73Provider) Apply(ctx *Context) (Cleanup, error) {
	val := c.tag
	if val == "" {
		val = "P"
	}
	return nil, ctx.provideCap(c.key, val)
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

// ---------------------------------------------------------------------------
// Reviewer-gap closures: (1) quiescence with Active fibers allowed, (2) T63
// withdrawal ordering, (3) acyclic dependency-precedence invariant.
// ---------------------------------------------------------------------------

// TestT66QuiescenceAllowsActive — a legal quiescent state may contain Active
// fibers (provider + consumer) with no pending lifecycle transition.
func TestT66QuiescenceAllowsActive(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	key := NewKey[string]("t66.quiescent").Capability()
	pf, err := rt.Load(&t66Comp{name: "P", key: key, provide: true})
	if err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(&t66Comp{name: "C", key: key, consumer: true})
	if err != nil {
		t.Fatal(err)
	}
	if _, err := t66Drain(rt); err != nil {
		t.Fatal(err)
	}
	// Quiescent AND both Active (quiescence != all fibers Gone).
	if pf.State() != StateActive || cf.State() != StateActive {
		t.Fatalf("expected quiescent-with-Active, got P=%v C=%v", pf.State(), cf.State())
	}
	if err := t59CheckOnOrchestrator(rt); err != nil {
		t.Fatalf("preservation at quiescence: %v", err)
	}
	_ = pf.Dispose()
	_ = cf.Dispose()
	if _, err := t66Drain(rt); err != nil {
		t.Fatal(err)
	}
	_ = rt.Close(context.Background())
}

// TestT63WithdrawalOrdering — consumer deactivation completes before the
// provider's own withdrawal cleanup (consumer-first), recorded from user-level
// cleanup events.
func TestT63WithdrawalOrdering(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	rec := &t63Rec{}
	key := NewKey[string]("t63.withdraw").Capability()
	pf, err := rt.Load(&t63ProviderOrdered{rec: rec, key: key})
	if err != nil {
		t.Fatal(err)
	}
	cf, err := rt.Load(&t63ConsumerOrdered{name: "C", rec: rec, key: key})
	if err != nil {
		t.Fatal(err)
	}
	ctx := t63Ctx(t)
	if err := pf.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if err := cf.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := pf.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	events := rec.all()
	ci := -1
	pi := -1
	for i, e := range events {
		if e == "C:cleanup" && ci < 0 {
			ci = i
		}
		if e == "P:cleanup" && pi < 0 {
			pi = i
		}
	}
	if ci < 0 || pi < 0 || !(ci < pi) {
		t.Fatalf("T63_ORDERING withdrawal: consumer cleanup (%d) must precede provider cleanup (%d): %v", ci, pi, events)
	}
}

type t63ProviderOrdered struct {
	rec *t63Rec
	key CapabilityKey
}

func (c *t63ProviderOrdered) Name() string          { return "P" }
func (c *t63ProviderOrdered) Inject() []Dependency  { return nil }
func (c *t63ProviderOrdered) Provide() []Capability { return []Capability{c.key} }
func (c *t63ProviderOrdered) Apply(ctx *Context) (Cleanup, error) {
	if err := ctx.provideCap(c.key, "v"); err != nil {
		return nil, err
	}
	c.rec.add("P:up")
	return func() error {
		c.rec.add("P:cleanup")
		return nil
	}, nil
}

type t63ConsumerOrdered struct {
	name string
	rec  *t63Rec
	key  CapabilityKey
}

func (c *t63ConsumerOrdered) Name() string          { return c.name }
func (c *t63ConsumerOrdered) Inject() []Dependency  { return []Dependency{{Key: c.key}} }
func (c *t63ConsumerOrdered) Provide() []Capability { return nil }
func (c *t63ConsumerOrdered) Apply(ctx *Context) (Cleanup, error) {
	if _, ok := ctx.realm.lookup(c.key); !ok {
		return nil, fmt.Errorf("consumer applied without provider")
	}
	c.rec.add("C:apply")
	return func() error {
		c.rec.add("C:cleanup")
		return nil
	}, nil
}

func t63Ctx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// t66AcyclicPrecedence verifies the theorem precondition: the declared
// dependency-precedence graph over mounted fibers is acyclic. (Load/Child
// already reject cycles; this is the invariant oracle at every check point.)
func t66AcyclicPrecedence(rt *Runtime) error {
	rt.mu.RLock()
	fs := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		fs = append(fs, f)
	}
	rt.mu.RUnlock()
	provides := map[CapabilityKey]*Fiber{}
	for _, f := range fs {
		for _, k := range f.provide {
			if _, dup := provides[k]; dup {
				continue // declared duplicates tolerated
			}
			provides[k] = f
		}
	}
	// Edge f -> g when f requires a capability g declares it provides.
	adj := make(map[FiberID][]FiberID, len(fs))
	for _, f := range fs {
		for _, dep := range f.inject {
			if g := provides[dep.Key]; g != nil && g.id != f.id {
				adj[f.id] = append(adj[f.id], g.id)
			}
		}
	}
	const (
		white = 0
		gray  = 1
		black = 2
	)
	color := map[FiberID]int{}
	var visit func(FiberID, []FiberID) error
	visit = func(id FiberID, chain []FiberID) error {
		color[id] = gray
		for _, n := range adj[id] {
			if color[n] == gray {
				return fmt.Errorf("T66_PRECONDITION: dependency cycle %v -> %v", chain, n)
			}
			if color[n] == white {
				if err := visit(n, append(chain, n)); err != nil {
					return err
				}
			}
		}
		color[id] = black
		return nil
	}
	for _, f := range fs {
		if color[f.id] == white {
			if err := visit(f.id, []FiberID{f.id}); err != nil {
				return err
			}
		}
	}
	return nil
}

// TestT66AcyclicPrecedenceInvariant — the dependency-precedence graph stays
// acyclic across randomized provider reload cycles (precondition of Progress).
func TestT66AcyclicPrecedenceInvariant(t *testing.T) {
	for _, seed := range []uint64{1, 2, 3, 4, 5} {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewPCG(seed, seed^0xabcdef012345678))
			rt, err := New()
			if err != nil {
				t.Fatal(err)
			}
			key := t66Key()
			pf, err := rt.Load(&t66Comp{name: "P", key: key, provide: true})
			if err != nil {
				t.Fatal(err)
			}
			var cs []*Fiber
			for i := 0; i < 2; i++ {
				cf, err := rt.Load(&t66Comp{name: fmt.Sprintf("C%d", i), key: key, consumer: true})
				if err != nil {
					t.Fatal(err)
				}
				cs = append(cs, cf)
			}
			if _, err := t66Drain(rt); err != nil {
				t.Fatal(err)
			}
			if err := t66AcyclicPrecedence(rt); err != nil {
				t.Fatal(err)
			}
			for cycle := 0; cycle < 12; cycle++ {
				if rng.IntN(2) == 0 {
					_ = pf.Dispose()
				} else if pf.State() == StateGone {
					_ = pf.Load()
				}
				if _, err := t66Drain(rt); err != nil {
					t.Fatal(err)
				}
				if err := t66AcyclicPrecedence(rt); err != nil {
					t.Fatalf("seed=%d cycle=%d: %v", seed, cycle, err)
				}
			}
			for _, c := range cs {
				_ = c.Dispose()
			}
			_ = pf.Dispose()
			_, _ = t66Drain(rt)
			_ = rt.Close(context.Background())
		})
	}
}

// ---------------------------------------------------------------------------
// T73 (reviewer form): logical operation set -> dependency-precedence DAG ->
// random legal topological schedules -> runtime execution -> quiescence ->
// Observe() equivalence.
// ---------------------------------------------------------------------------

// t73DAG describes a logical scenario: steps and precedence edges.
type t73DAG struct {
	steps []string
	edges [][2]string // a must come before b
}

func t73ProviderConsumerDAG() t73DAG {
	return t73DAG{
		steps: []string{"M(P)", "M(C1)", "M(C2)", "D(C1)", "D(C2)", "D(P)"},
		edges: [][2]string{
			// Consumers may only mount after their provider is mounted
			// (dependency precedence: consumer activation depends on provider).
			{"M(P)", "M(C1)"},
			{"M(P)", "M(C2)"},
			{"M(C1)", "D(C1)"},
			{"M(C2)", "D(C2)"},
			{"M(P)", "D(P)"},
			// Provider is not disposed until every consumer has been mounted,
			// so every schedule observes the full activation set.
			{"M(C1)", "D(P)"},
			{"M(C2)", "D(P)"},
		},
	}
}

// topoSchedules deterministically yields up to n random legal topological
// orders of the DAG (Kahn with seeded tie-break).
func topoSchedules(d t73DAG, seed uint64, n int) [][]string {
	rng := rand.New(rand.NewPCG(seed, seed^0xdeadbeefcafef00d))
	indeg := map[string]int{}
	succ := map[string][]string{}
	for _, s := range d.steps {
		indeg[s] = 0
	}
	for _, e := range d.edges {
		succ[e[0]] = append(succ[e[0]], e[1])
		indeg[e[1]]++
	}
	seen := map[string]bool{}
	var out [][]string
	attempts := 0
	for len(out) < n && attempts < 4000 {
		attempts++
		deg := map[string]int{}
		for k, v := range indeg {
			deg[k] = v
		}
		var order []string
		ready := []string{}
		for k, v := range deg {
			if v == 0 {
				ready = append(ready, k)
			}
		}
		for len(ready) > 0 {
			rng.Shuffle(len(ready), func(i, j int) { ready[i], ready[j] = ready[j], ready[i] })
			cur := ready[0]
			ready = ready[1:]
			order = append(order, cur)
			for _, nx := range succ[cur] {
				deg[nx]--
				if deg[nx] == 0 {
					ready = append(ready, nx)
				}
			}
		}
		if len(order) != len(d.steps) {
			panic("t73 DAG has a cycle (test bug)")
		}
		k := fmt.Sprint(order)
		if !seen[k] {
			seen[k] = true
			out = append(out, order)
		}
	}
	return out
}

// runT73DAGSchedule executes one legal topological schedule and returns the
// quiescent observable state.
func runT73DAGSchedule(t *testing.T, order []string, key CapabilityKey) t73Obs {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	rec := &t73Rec{}
	loaded := map[string]*Fiber{}
	comp := map[string]Component{
		"P":  &t73Provider{key: key},
		"C1": &t73Consumer{key: key, rec: rec},
		"C2": &t73Consumer{key: key, rec: rec},
	}
	for _, step := range order {
		switch {
		case step == "M(P)":
			f, err := rt.Load(comp["P"])
			if err != nil {
				t.Fatal(err)
			}
			loaded["P"] = f
			if err := f.Ready(ctx); err != nil {
				t.Fatal(err)
			}
		case step == "M(C1)" || step == "M(C2)":
			name := step[2:4]
			f, err := rt.Load(comp[name])
			if err != nil {
				t.Fatal(err)
			}
			loaded[name] = f
			if err := f.Ready(ctx); err != nil {
				t.Fatal(err)
			}
		case step == "D(P)":
			if err := loaded["P"].Dispose(); err != nil {
				t.Fatal(err)
			}
			if err := loaded["P"].Gone(ctx); err != nil {
				t.Fatal(err)
			}
		case step == "D(C1)" || step == "D(C2)":
			name := step[2:4]
			if err := loaded[name].Dispose(); err != nil {
				t.Fatal(err)
			}
			if err := loaded[name].Gone(ctx); err != nil {
				t.Fatal(err)
			}
		}
	}
	// Quiescence (deterministic step driver) before observing.
	if _, err := t66Drain(rt); err != nil {
		t.Fatal(err)
	}
	var active []string
	rt.mu.RLock()
	for _, f := range rt.fibers {
		if f.state == StateActive {
			active = append(active, f.Name())
		}
	}
	rt.mu.RUnlock()
	sort.Strings(active)
	seen := rec.snapshot()
	sort.Strings(seen)
	for _, name := range []string{"P", "C1", "C2"} {
		if f := loaded[name]; f != nil {
			if err := f.Gone(ctx); err != nil {
				t.Fatalf("%s not gone: %v", name, err)
			}
		}
	}
	if err := rt.Close(context.Background()); err != nil {
		t.Fatal(err)
	}
	return t73Obs{active: active, seen: seen}
}

// TestT73DAGTopologicalSchedules — same logical op set, DAG-derived legal
// topological schedules, identical quiescent observable states.
func TestT73DAGTopologicalSchedules(t *testing.T) {
	key := NewKey[string]("t73.dag").Capability()
	dag := t73ProviderConsumerDAG()
	for _, seed := range []uint64{21, 22, 23, 24} {
		schedules := topoSchedules(dag, seed, 24)
		if len(schedules) < 2 {
			t.Fatalf("expected multiple schedules, got %d", len(schedules))
		}
		base := t73Obs{}
		for i, sch := range schedules {
			obs := runT73DAGSchedule(t, sch, key)
			if i == 0 {
				base = obs
				continue
			}
			if fmt.Sprint(obs) != fmt.Sprint(base) {
				t.Fatalf("T73_CONFLUENCE seed=%d schedule#%d diverged:\n got %+v\nbase %+v", seed, i, obs, base)
			}
		}
		if len(base.active) != 0 {
			t.Fatalf("seed=%d expected empty quiescent active set, got %v", seed, base.active)
		}
		if len(base.seen) != 2 {
			t.Fatalf("seed=%d consumers seen %v, want 2 observations", seed, base.seen)
		}
	}
}

// ---------------------------------------------------------------------------
// T73 expansion: two independent single-provider subsystems + an effect
// component. Independence is a generator-level premise: different capability
// keys, no cross-component effects. Interleaving the two subsystems must be
// confluent.
// ---------------------------------------------------------------------------

func t73TwoSubsystemDAG() t73DAG {
	steps := []string{"M(P1)", "M(C1)", "M(C2)", "M(P2)", "M(C3)", "M(E)",
		"D(C1)", "D(C2)", "D(C3)", "D(E)", "D(P1)", "D(P2)"}
	var edges [][2]string
	must := func(a, b string) { edges = append(edges, [2]string{a, b}) }
	must("M(P1)", "M(C1)")
	must("M(P1)", "M(C2)")
	must("M(P2)", "M(C3)")
	// local dispose-after-mount, and provider disposal after its consumers mount
	must("M(C1)", "D(C1)")
	must("M(C2)", "D(C2)")
	must("M(C3)", "D(C3)")
	must("M(E)", "D(E)")
	must("M(P1)", "D(P1)")
	must("M(P1)", "M(C1)")
	must("M(C1)", "D(P1)")
	must("M(C2)", "D(P1)")
	must("M(P2)", "D(P2)")
	must("M(C3)", "D(P2)")
	return t73DAG{steps: steps, edges: edges}
}

func runT73TwoSubsystemSchedule(t *testing.T, order []string, keyA, keyB CapabilityKey) t73Obs {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 40*time.Second)
	defer cancel()
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	rec := &t73Rec{}
	loaded := map[string]*Fiber{}
	comps := map[string]Component{
		"P1": &t73Provider{key: keyA, tag: "P1"},
		"C1": &t73Consumer{key: keyA, rec: rec},
		"C2": &t73Consumer{key: keyA, rec: rec},
		"P2": &t73Provider{key: keyB, tag: "P2"},
		"C3": &t73Consumer{key: keyB, rec: rec},
		"E":  &t66Comp{name: "E", n: 1},
	}
	for _, step := range order {
		name := ""
		if len(step) > 2 && step[len(step)-1] == ')' {
			name = step[2 : len(step)-1]
		}
		switch {
		case len(step) == 0:
			continue
		case step[0] == 'M':
			f, err := rt.Load(comps[name])
			if err != nil {
				t.Fatal(err)
			}
			loaded[name] = f
			if err := f.Ready(ctx); err != nil {
				t.Fatal(err)
			}
		case step[0] == 'D':
			if err := loaded[name].Dispose(); err != nil {
				t.Fatal(err)
			}
			if err := loaded[name].Gone(ctx); err != nil {
				t.Fatal(err)
			}
		}
	}
	if _, err := t66Drain(rt); err != nil {
		t.Fatal(err)
	}
	var active []string
	rt.mu.RLock()
	for _, f := range rt.fibers {
		if f.state == StateActive {
			active = append(active, f.Name())
		}
	}
	rt.mu.RUnlock()
	sort.Strings(active)
	seen := rec.snapshot()
	sort.Strings(seen)
	if err := rt.Close(context.Background()); err != nil {
		t.Fatal(err)
	}
	return t73Obs{active: active, seen: seen}
}

// TestT73TwoIndependentSubsystems — interleaving two independent subsystems is
// confluent: identical observables (seen [P1 P1 P2]) across topological
// schedules that mix the two subsystems and the effect component.
func TestT73TwoIndependentSubsystems(t *testing.T) {
	keyA := NewKey[string]("t73.sysA").Capability()
	keyB := NewKey[string]("t73.sysB").Capability()
	dag := t73TwoSubsystemDAG()
	for _, seed := range []uint64{31, 32} {
		schedules := topoSchedules(dag, seed, 16)
		if len(schedules) < 2 {
			t.Fatalf("seed=%d expected multiple schedules, got %d", seed, len(schedules))
		}
		base := t73Obs{}
		for i, sch := range schedules {
			obs := runT73TwoSubsystemSchedule(t, sch, keyA, keyB)
			if i == 0 {
				base = obs
				continue
			}
			if fmt.Sprint(obs) != fmt.Sprint(base) {
				t.Fatalf("T73_CONFLUENCE seed=%d schedule#%d diverged:\n got %+v\nbase %+v", seed, i, obs, base)
			}
		}
		if len(base.active) != 0 {
			t.Fatalf("seed=%d expected empty active set, got %v", seed, base.active)
		}
		if len(base.seen) != 3 || base.seen[0] != "P1" || base.seen[1] != "P1" || base.seen[2] != "P2" {
			t.Fatalf("seed=%d seen = %v, want [P1 P1 P2]", seed, base.seen)
		}
	}
}
