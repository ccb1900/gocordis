package runtime

import (
	"context"
	"encoding/binary"
	"fmt"
	"math/rand/v2"
	"testing"
)

// FuzzInterleaving — random legal operation interleavings under deterministic
// seeds. Each generated case checks Thm64 (preservation), Thm70 (a consumer only
// applies with its provider active) and Thm73 (bounded deterministic quiescence).
// No sleeps, no global rand; failures reproduce from the decoded seed.
func FuzzInterleaving(f *testing.F) {
	for _, seed := range []uint64{1, 2, 3, 4, 5, 42, 99} {
		var b [8]byte
		binary.LittleEndian.PutUint64(b[:], seed)
		f.Add(b[:])
	}
	f.Fuzz(func(t *testing.T, data []byte) {
		// Deterministic decode for arbitrary-length corpus inputs.
		seed := uint64(0)
		for _, b := range data {
			seed = seed*31 + uint64(b)
		}
		if err := runFuzzScenario(seed); err != nil {
			t.Fatalf("Thm64/Thm70/Thm73 failure seed=%d: %v", seed, err)
		}
	})
}

func runFuzzScenario(seed uint64) error {
	rt, err := New()
	if err != nil {
		return err
	}
	rng := rand.New(rand.NewPCG(seed, seed^0x123456789abcdef0))
	ctx := context.Background()
	key := NewKey[string]("fuzz.interleave").Capability()

	mount := func(c Component) (*Fiber, error) {
		f, err := rt.Load(c)
		if err != nil {
			return nil, err
		}
		return f, nil
	}
	pf, err := mount(&thm73Comp{name: "P", key: key, provide: true})
	if err != nil {
		return err
	}
	var cons []*Fiber
	for i := 0; i < 2; i++ {
		c, err := mount(&thm73Comp{name: fmt.Sprintf("C%d", i), key: key, consumer: true})
		if err != nil {
			return err
		}
		cons = append(cons, c)
	}
	readyAll := func() error {
		if err := pf.Ready(ctx); err != nil {
			return err
		}
		for _, c := range cons {
			if err := c.Ready(ctx); err != nil {
				return err
			}
		}
		return thm64CheckOnOrchestrator(rt)
	}
	if err := readyAll(); err != nil {
		return err
	}

	for i := 0; i < 8; i++ {
		if rng.IntN(2) == 0 {
			if err := pf.Dispose(); err != nil {
				return err
			}
			if err := pf.Gone(ctx); err != nil {
				return err
			}
			for _, c := range cons {
				if err := c.WaitInactive(ctx); err != nil {
					return err
				}
				if c.State() != StatePending {
					return fmt.Errorf("consumer %s not Pending after provider dispose", c.Name())
				}
			}
			if err := thm64CheckOnOrchestrator(rt); err != nil {
				return err
			}
		} else {
			if pf.State() == StateGone {
				if err := pf.Load(); err != nil {
					return err
				}
			}
			if err := readyAll(); err != nil {
				return err
			}
		}
	}

	for _, c := range cons {
		_ = c.Dispose()
	}
	_ = pf.Dispose()
	for _, c := range cons {
		if err := c.Gone(ctx); err != nil {
			return err
		}
	}
	if err := pf.Gone(ctx); err != nil {
		return err
	}
	return rt.Close(ctx)
}
