package runtime

import (
	"context"
	"fmt"
	"math/rand/v2"
	"strings"
	"sync"
	"testing"
	"time"
)

// T63 — Ordering. A consumer may enter Apply only when every required
// dependency is satisfied (provider Active). We drive randomized provider
// up/down cycles and assert, from a user-level event log, that every consumer
// Apply sits between a provider-up and the next provider-down.

type t63Rec struct {
	mu sync.Mutex
	ev []string
}

func (r *t63Rec) add(e string) {
	r.mu.Lock()
	r.ev = append(r.ev, e)
	r.mu.Unlock()
}
func (r *t63Rec) all() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.ev...)
}

type t63Provider struct {
	rec *t63Rec
}

func (c *t63Provider) Name() string          { return "P" }
func (c *t63Provider) Inject() []Dependency  { return nil }
func (c *t63Provider) Provide() []Capability { return []Capability{t63Key.Capability()} }
func (c *t63Provider) Apply(ctx *Context) (Cleanup, error) {
	if err := ctx.provideCap(t63Key.Capability(), "v"); err != nil {
		return nil, err
	}
	c.rec.add("P:up")
	return func() error {
		c.rec.add("P:down")
		return nil
	}, nil
}

var t63Key = NewKey[string]("t63.order")

type t63Consumer struct {
	name string
	rec  *t63Rec
}

func (c *t63Consumer) Name() string          { return c.name }
func (c *t63Consumer) Inject() []Dependency  { return []Dependency{{Key: t63Key.Capability()}} }
func (c *t63Consumer) Provide() []Capability { return nil }
func (c *t63Consumer) Apply(ctx *Context) (Cleanup, error) {
	if _, ok := ctx.realm.lookup(t63Key.Capability()); !ok {
		return nil, fmt.Errorf("consumer %s applied without provider", c.name)
	}
	c.rec.add("apply:" + c.name)
	return nil, nil
}

func t63Validate(events []string) error {
	up := false
	for _, e := range events {
		switch {
		case e == "P:up":
			up = true
		case e == "P:down":
			up = false
		case strings.HasPrefix(e, "apply:"):
			if !up {
				return fmt.Errorf("T63_ORDERING: %s occurred while provider inactive", e)
			}
		}
	}
	return nil
}

func TestT63RandomizedOrdering(t *testing.T) {
	rec := &t63Rec{}
	for _, seed := range []uint64{1, 2, 3, 4, 5} {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewPCG(seed, seed^0x9e3779b97f4a7c15))
			ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
			defer cancel()
			rt, err := New()
			if err != nil {
				t.Fatal(err)
			}
			p := &t63Provider{rec: rec}
			pf, err := rt.Load(p)
			if err != nil {
				t.Fatal(err)
			}
			if err := pf.Ready(ctx); err != nil {
				t.Fatal(err)
			}
			consumers := []*Fiber{}
			for i := 0; i < 3; i++ {
				cf, err := rt.Load(&t63Consumer{name: fmt.Sprintf("C%d", i), rec: rec})
				if err != nil {
					t.Fatal(err)
				}
				if err := cf.Ready(ctx); err != nil {
					t.Fatal(err)
				}
				consumers = append(consumers, cf)
			}
			if err := t63Validate(rec.all()); err != nil {
				t.Fatal(err)
			}

			for cycle := 0; cycle < 40; cycle++ {
				switch rng.IntN(2) {
				case 0: // provider down -> consumers to Pending
					if err := pf.Dispose(); err != nil {
						t.Fatal(err)
					}
					if err := pf.Gone(ctx); err != nil {
						t.Fatal(err)
					}
					for _, cf := range consumers {
						if err := cf.WaitInactive(ctx); err != nil {
							t.Fatal(err)
						}
						if cf.State() != StatePending {
							t.Fatalf("consumer %s state=%v, want Pending", cf.Name(), cf.State())
						}
					}
				case 1: // provider up (reload same fiber) -> consumers re-apply
					if err := pf.Load(); err != nil {
						t.Fatal(err)
					}
					if err := pf.Ready(ctx); err != nil {
						t.Fatal(err)
					}
					for _, cf := range consumers {
						if err := cf.Ready(ctx); err != nil {
							t.Fatalf("consumer %s not reactivated: %v", cf.Name(), err)
						}
					}
				}
				if err := t63Validate(rec.all()); err != nil {
					t.Fatalf("T63 seed=%d cycle=%d: %v", seed, cycle, err)
				}
			}
			_ = pf.Dispose()
			_ = pf.Gone(ctx)
			_ = rt.Close(context.Background())
		})
	}
}
