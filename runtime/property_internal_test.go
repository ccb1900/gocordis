package runtime

// Property P2 (Recovery Exactness): Observable(S2) == Observable(S0) after an
// Apply followed by a full Unwind of the same activation, restricted to
// Runtime-managed reversible state.

import (
	"context"
	"sync"
	"testing"
	"time"
)

func TestPropertyRecoveryExactness(t *testing.T) {
	rt, err := New()
	if err != nil {
		t.Fatal(err)
	}
	key := NewKey[string]("recovery").Capability()

	var mu sync.Mutex
	opens := 0
	closes := 0

	comp := &wbProvideComp{
		name: "recovery",
		key:  key,
		apply: func(ctx *Context) error {
			if err := ctx.provideCap(key, "v"); err != nil {
				return err
			}
			return ctx.Effect(func() (func() error, error) {
				mu.Lock()
				opens++
				mu.Unlock()
				return func() error {
					mu.Lock()
					closes++
					mu.Unlock()
					return nil
				}, nil
			})
		},
	}

	f, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	deadline := time.Now().Add(10 * time.Second)
	for f.State() != StateActive && time.Now().Before(deadline) {
		time.Sleep(time.Millisecond)
	}
	if f.State() != StateActive {
		t.Fatalf("state = %v, want Active", f.State())
	}

	// S1: provider registered + resource open.
	if rec, ok := rt.rootRealm.lookupOwn(key); !ok || rec.value != "v" {
		t.Fatalf("S1 provider missing: %+v", rec)
	}

	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := f.Gone(ctx); err != nil {
		t.Fatalf("Gone: %v", err)
	}

	// S2: provider record gone and resource closed exactly once -> S0.
	if rec, ok := rt.rootRealm.lookupOwn(key); ok {
		t.Fatalf("S2 provider still present: %+v", rec)
	}
	mu.Lock()
	defer mu.Unlock()
	if opens != 1 || closes != 1 {
		t.Fatalf("opens=%d closes=%d, want 1/1 (S2 == S0)", opens, closes)
	}
}

type wbProvideComp struct {
	name  string
	key   CapabilityKey
	apply func(*Context) error
}

func (c *wbProvideComp) Name() string          { return c.name }
func (c *wbProvideComp) Inject() []Dependency  { return nil }
func (c *wbProvideComp) Provide() []Capability { return []Capability{c.key} }
func (c *wbProvideComp) Apply(ctx *Context) (Cleanup, error) {
	if err := c.apply(ctx); err != nil {
		return nil, err
	}
	return nil, nil
}
