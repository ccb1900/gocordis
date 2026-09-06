package runtime

import (
	"context"
	"fmt"
	"sync"
	"testing"
	"time"
)

// T61 — Recovery Exactness.
//
// (a) Effect recovery: each inverse executes exactly once and in strict LIFO
//     order (e3^-1, e2^-1, e1^-1); no late-effect leak.
// (b) Baseline equivalence: a Runtime that loaded + disposed F is observably
//     equivalent to a Runtime that never loaded F (same active set, no provider
//     residue, no effect residue).

// t61Rec is an ordered, user-level recorder for effect open/close events.
type t61Rec struct {
	mu sync.Mutex
	ev []string
}

func (r *t61Rec) add(e string) {
	r.mu.Lock()
	r.ev = append(r.ev, e)
	r.mu.Unlock()
}

func (r *t61Rec) snapshot() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.ev...)
}

// t61Comp: kind "provider" provides key (optionally) and runs an apply that
// opens n effects in order, each recording open:<id> and close:<id>.
type t61Comp struct {
	name    string
	key     CapabilityKey
	provide bool
	n       int
	rec     *t61Rec
}

func (c *t61Comp) Name() string         { return c.name }
func (c *t61Comp) Inject() []Dependency { return nil }
func (c *t61Comp) Provide() []Capability {
	if c.provide {
		return []Capability{c.key}
	}
	return nil
}
func (c *t61Comp) Apply(ctx *Context) (Cleanup, error) {
	if c.provide {
		if err := ctx.provideCap(c.key, c.name); err != nil {
			return nil, err
		}
	}
	for i := 1; i <= c.n; i++ {
		id := i
		if err := ctx.Effect(func() (func() error, error) {
			c.rec.add(fmt.Sprintf("open:%d", id))
			return func() error {
				c.rec.add(fmt.Sprintf("close:%d", id))
				return nil
			}, nil
		}); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

func t61Ctx(t *testing.T) context.Context {
	t.Helper()
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	t.Cleanup(cancel)
	return ctx
}

// TestT61LIFOExactlyOnceRecovery — three effects open in order; after Dispose
// they close in strict LIFO and exactly once; no residue.
func TestT61LIFOExactlyOnceRecovery(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	rec := &t61Rec{}
	comp := &t61Comp{name: "F", n: 3, rec: rec}
	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := f.Ready(t61Ctx(t)); err != nil {
		t.Fatal(err)
	}
	opens := rec.snapshot()
	if len(opens) != 3 {
		t.Fatalf("opens = %v", opens)
	}
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(t61Ctx(t)); err != nil {
		t.Fatal(err)
	}
	all := rec.snapshot()
	// Expect open:1, open:2, open:3, close:3, close:2, close:1 (LIFO).
	want := []string{"open:1", "open:2", "open:3", "close:3", "close:2", "close:1"}
	if len(all) != len(want) {
		t.Fatalf("trace length = %d, want %d: %v", len(all), len(want), all)
	}
	for i := range want {
		if all[i] != want[i] {
			t.Fatalf("T61_LIFO/T61_EXACTLY_ONCE: event %d = %q, want %q (full %v)", i, all[i], want[i], all)
		}
	}
}

// TestT61BaselineEquivalence — R1 loads a transient provider+effects and
// disposes it; afterwards it is observationally equivalent to R0 which never
// loaded F (both have only the stable provider G active, no residue).
func TestT61BaselineEquivalence(t *testing.T) {
	key := NewKey[string]("t61.baseline").Capability()
	run := func(withTransient bool) (*Runtime, *t61Rec, error) {
		rt, err := New()
		if err != nil {
			return nil, nil, err
		}
		// Stable provider G (present in both).
		g := &t61Comp{name: "G", key: key, provide: true, n: 1, rec: &t61Rec{}}
		gf, err := rt.Load(g)
		if err != nil {
			return nil, nil, err
		}
		if err := gf.Ready(t61Ctx(t)); err != nil {
			return nil, nil, err
		}
		if !withTransient {
			return rt, g.rec, nil
		}
		// Transient F: provides the SAME key would conflict, so F provides a
		// different key and only carries effects.
		frec := &t61Rec{}
		f := &t61Comp{name: "F", n: 2, rec: frec}
		ff, err := rt.Load(f)
		if err != nil {
			return nil, nil, err
		}
		if err := ff.Ready(t61Ctx(t)); err != nil {
			return nil, nil, err
		}
		if err := ff.Dispose(); err != nil {
			return nil, nil, err
		}
		if err := ff.Gone(t61Ctx(t)); err != nil {
			return nil, nil, err
		}
		return rt, frec, nil
	}

	rt0, _, err := run(false)
	if err != nil {
		t.Fatal(err)
	}
	rt1, frec, err := run(true)
	if err != nil {
		t.Fatal(err)
	}

	// Active-fiber observable sets must match: only G active in both.
	active0 := activeFiberNames(t, rt0)
	active1 := activeFiberNames(t, rt1)
	if len(active0) != 1 || len(active1) != 1 || active0[0] != "G" || active1[0] != "G" {
		t.Fatalf("T61_RECOVERY active sets diverged: R0=%v R1=%v", active0, active1)
	}

	// No effect residue from F (opens == closes, LIFO exact).
	ev := frec.snapshot()
	if len(ev) != 4 || ev[0] != "open:1" || ev[1] != "open:2" || ev[2] != "close:2" || ev[3] != "close:1" {
		t.Fatalf("T61_RECOVERY F effect residue/wrong order: %v", ev)
	}

	// Provider registry must contain only G (F provided nothing, but its fiber
	// must not linger as any provider): key present with identity G.
	rt0.rootRealm.mu.RLock()
	_, okG0 := rt0.rootRealm.own[key]
	rt0.rootRealm.mu.RUnlock()
	rt1.rootRealm.mu.RLock()
	rec1, okG1 := rt1.rootRealm.own[key]
	extra := len(rt1.rootRealm.own)
	rt1.rootRealm.mu.RUnlock()
	if !okG0 || !okG1 || rec1.identity.FiberID == 0 || extra != 1 {
		t.Fatalf("T61_RECOVERY provider residue: R0 ok=%v, R1 ok=%v extra=%d", okG0, okG1, extra)
	}

	_ = rt0.Close(context.Background())
	_ = rt1.Close(context.Background())
}

func activeFiberNames(t *testing.T, rt *Runtime) []string {
	t.Helper()
	rt.mu.RLock()
	fs := make([]*Fiber, 0, len(rt.fibers))
	for _, f := range rt.fibers {
		fs = append(fs, f)
	}
	rt.mu.RUnlock()
	var names []string
	for _, f := range fs {
		if f.State() == StateActive {
			names = append(names, f.Name())
		}
	}
	return names
}
