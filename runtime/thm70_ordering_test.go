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

// Thm70 — Ordering. A consumer may enter Apply only when every required
// dependency is satisfied (provider Active). We drive randomized provider
// up/down cycles and assert, from a user-level event log, that every consumer
// Apply sits between a provider-up and the next provider-down.

type thm70Rec struct {
	mu sync.Mutex
	ev []string
}

func (r *thm70Rec) add(e string) {
	r.mu.Lock()
	r.ev = append(r.ev, e)
	r.mu.Unlock()
}
func (r *thm70Rec) all() []string {
	r.mu.Lock()
	defer r.mu.Unlock()
	return append([]string(nil), r.ev...)
}

type thm70Provider struct {
	rec *thm70Rec
}

func (c *thm70Provider) Name() string          { return "P" }
func (c *thm70Provider) Inject() []Dependency  { return nil }
func (c *thm70Provider) Provide() []Capability { return []Capability{thm70Key.Capability()} }
func (c *thm70Provider) Apply(ctx *Context) (Cleanup, error) {
	if err := ctx.provideCap(thm70Key.Capability(), "v"); err != nil {
		return nil, err
	}
	c.rec.add("P:up")
	return func() error {
		c.rec.add("P:down")
		return nil
	}, nil
}

var thm70Key = NewKey[string]("thm70.order")

type thm70Consumer struct {
	name string
	rec  *thm70Rec
}

func (c *thm70Consumer) Name() string          { return c.name }
func (c *thm70Consumer) Inject() []Dependency  { return []Dependency{{Key: thm70Key.Capability()}} }
func (c *thm70Consumer) Provide() []Capability { return nil }
func (c *thm70Consumer) Apply(ctx *Context) (Cleanup, error) {
	if _, ok := ctx.realm.lookupOwn(thm70Key.Capability()); !ok {
		return nil, fmt.Errorf("consumer %s applied without provider", c.name)
	}
	c.rec.add("apply:" + c.name)
	return nil, nil
}

func thm70Validate(events []string) error {
	up := false
	for _, e := range events {
		switch {
		case e == "P:up":
			up = true
		case e == "P:down":
			up = false
		case strings.HasPrefix(e, "apply:"):
			if !up {
				return fmt.Errorf("THM70_ORDERING: %s occurred while provider inactive", e)
			}
		}
	}
	return nil
}

func TestThm70RandomizedOrdering(t *testing.T) {
	rec := &thm70Rec{}
	for _, seed := range []uint64{1, 2, 3, 4, 5} {
		t.Run(fmt.Sprintf("seed-%d", seed), func(t *testing.T) {
			rng := rand.New(rand.NewPCG(seed, seed^0x9e3779b97f4a7c15))
			ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
			defer cancel()
			rt, err := New()
			if err != nil {
				t.Fatal(err)
			}
			p := &thm70Provider{rec: rec}
			pf, err := rt.Load(p)
			if err != nil {
				t.Fatal(err)
			}
			if err := pf.Ready(ctx); err != nil {
				t.Fatal(err)
			}
			consumers := []*Fiber{}
			for i := 0; i < 3; i++ {
				cf, err := rt.Load(&thm70Consumer{name: fmt.Sprintf("C%d", i), rec: rec})
				if err != nil {
					t.Fatal(err)
				}
				if err := cf.Ready(ctx); err != nil {
					t.Fatal(err)
				}
				consumers = append(consumers, cf)
			}
			if err := thm70Validate(rec.all()); err != nil {
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
				if err := thm70Validate(rec.all()); err != nil {
					t.Fatalf("Thm70 seed=%d cycle=%d: %v", seed, cycle, err)
				}
			}
			_ = pf.Dispose()
			_ = pf.Gone(ctx)
			_ = rt.Close(context.Background())
		})
	}
}
