package runtime

import (
	"context"
	"sort"
	"testing"
	"time"
)

// Theorem 80 confluence across ISOLATION NAMESPACES (paper §4.4 Isolation:
// the rules apply at K×R as they stand — one namespace per (key, ρ) binding).
// Two independent subsystems, each mounted inside its own WithScope namespace,
// interleave their mount/dispose orderings; every legal interleaving reaches
// the same canonical observable. Complements TestThm80TwoIndependentSubsystems
// (root-realm dimension) with the namespace dimension promised by ADR-0001.

// scHost mounts one subsystem (provider + chained consumer) inside its own
// WithScope namespace.
type scHost struct {
	key  CapabilityKey
	tag  string
	rec  *thm80Rec
	done chan struct{}
}

func (c *scHost) Name() string          { return "sc-host:" + c.tag }
func (c *scHost) Inject() []Dependency  { return nil }
func (c *scHost) Provide() []Capability { return nil }
func (c *scHost) Apply(ctx *Context) (Cleanup, error) {
	p, err := ctx.Child(&thm80Provider{key: c.key, tag: c.tag})
	if err != nil {
		return nil, err
	}
	if err := p.Ready(ctx.Context()); err != nil {
		return nil, err
	}
	if _, err := ctx.Child(&thm80Consumer{key: c.key, rec: c.rec}); err != nil {
		return nil, err
	}
	return nil, nil
}

func TestThm80ScopedNamespaceConfluence(t *testing.T) {
	keyA := NewKey[string]("sc.sysA").Capability()
	keyB := NewKey[string]("sc.sysB").Capability()

	run := func(order []string) thm80Obs {
		t.Helper()
		ctx, cancel := context.WithTimeout(context.Background(), 40*time.Second)
		defer cancel()
		rt, err := New()
		if err != nil {
			t.Fatal(err)
		}
		rec := &thm80Rec{}
		loaded := map[string]*Fiber{}
		comps := map[string]Component{
			"A": &scHost{key: keyA, tag: "PA", rec: rec},
			"B": &scHost{key: keyB, tag: "PB", rec: rec},
		}
		for _, step := range order {
			name := step[2 : len(step)-1]
			switch step[0] {
			case 'M':
				f, err := rt.Load(comps[name])
				if err != nil {
					t.Fatal(err)
				}
				loaded[name] = f
				if err := f.Ready(ctx); err != nil {
					t.Fatal(err)
				}
			case 'D':
				if err := loaded[name].Dispose(); err != nil {
					t.Fatal(err)
				}
				if err := loaded[name].Gone(ctx); err != nil {
					t.Fatal(err)
				}
			}
		}
		if _, err := thm73Drain(rt); err != nil {
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
		return thm80Obs{active: active, seen: seen}
	}

	// Legal interleavings of M(A), M(B), D(A): dispose follows its mount; the
	// two subsystems interleave freely. Final state: only subsystem B active.
	orders := [][]string{
		{"M(A)", "M(B)", "D(A)"},
		{"M(A)", "D(A)", "M(B)"},
		{"M(B)", "M(A)", "D(A)"},
	}
	want := run(orders[0])
	sortedSeen := append([]string(nil), want.seen...)
	sort.Strings(sortedSeen)
	if len(sortedSeen) != 2 {
		t.Fatalf("seen = %v, want both subsystem tags", sortedSeen)
	}
	for _, order := range orders[1:] {
		got := run(order)
		if joinStrings0(got.active, ",") != joinStrings0(want.active, ",") ||
			joinStrings0(got.seen, ",") != joinStrings0(want.seen, ",") {
			t.Fatalf("interleaving %v diverged: active=%v seen=%v, want active=%v seen=%v",
				order, got.active, got.seen, want.active, want.seen)
		}
	}
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
