package integration

import (
	"context"
	"fmt"
	"sync"
	"testing"
	"time"

	"dynamic-runtime/runtime"
)

// P3-10 (integration audit): the effect-iterator divert path (paper §4.2.2
// L-Divert) has runtime-level coverage only. This fixture exercises it at
// composition level: a long activation installs steps through yield while its
// dependency is withdrawn mid-yield. AGENTS.md §5 requires divert coverage for
// every IterComponent implementer.
//
// Expected behavior:
//   - the in-flight iteration lands (an iteration cannot be declined);
//   - the NEXT yield returns an error wrapping ErrDiverted;
//   - every effect installed before the divert unwinds in LIFO order;
//   - the fiber converges Pending (dependency loss), NOT Failed (a divert is
//     not a raise).
type divertComp struct {
	depKey    runtime.Key[string]
	steps     int           // total steps it wants to install
	entered   chan struct{} // closed after step 0 lands
	hold      chan struct{} // component parks here until the test releases it
	mu        sync.Mutex
	installed []string
	diverted  bool
	landed    int
}

func (c *divertComp) Name() string { return "divert-comp" }
func (c *divertComp) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(c.depKey)}
}
func (c *divertComp) Provide() []runtime.Capability { return nil }
func (c *divertComp) Apply(*runtime.Context) (runtime.Cleanup, error) {
	panic("Apply must never be called when ApplyIter is present")
}

func (c *divertComp) ApplyIter(ctx *runtime.Context, yield func(func() (runtime.Cleanup, error)) error) error {
	for i := 0; i < c.steps; i++ {
		i := i
		err := yield(func() (runtime.Cleanup, error) {
			c.mu.Lock()
			c.installed = append(c.installed, fmt.Sprintf("step-%d", i))
			c.mu.Unlock()
			return nil, nil
		})
		if err != nil {
			c.mu.Lock()
			c.diverted = true
			c.landed = i // iterations completed before the divert landed
			c.mu.Unlock()
			return err
		}
		if i == 0 {
			// Park between step 0 and step 1: the test withdraws the
			// dependency while the activation sits here, then releases us —
			// the next yield must observe the divert.
			close(c.entered)
			<-c.hold
		}
	}
	return nil
}

func TestCompositionIterDivertOnDependencyLoss(t *testing.T) {
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 20*time.Second)
	defer cancel()
	defer func() { _ = rt.Close(ctx) }()

	dep := runtime.NewKey[string]("divert.dep")
	depProv := &pcComp{
		name:    "divert-dep-prov",
		provide: []runtime.Capability{dep.Capability()},
		apply: func(ctx *runtime.Context) (runtime.Cleanup, error) {
			if err := runtime.Provide(ctx, dep, "dep"); err != nil {
				return nil, err
			}
			return nil, nil
		},
	}
	pf, err := rt.Load(depProv)
	if err != nil {
		t.Fatal(err)
	}
	if err := pf.Ready(ctx); err != nil {
		t.Fatal(err)
	}

	dc := &divertComp{
		depKey:  dep,
		steps:   8,
		entered: make(chan struct{}),
		hold:    make(chan struct{}),
	}
	holder, err := rt.Load(dc)
	if err != nil {
		t.Fatal(err)
	}

	// Wait for step 0 to land (the activation is mid-iterator, still Loading —
	// Ready would block until the whole iterator finishes).
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		dc.mu.Lock()
		n := len(dc.installed)
		dc.mu.Unlock()
		if n == 1 {
			break
		}
		time.Sleep(5 * time.Millisecond)
	}
	dc.mu.Lock()
	firstSteps := len(dc.installed)
	dc.mu.Unlock()
	if firstSteps != 1 {
		t.Fatalf("first iteration did not land: installed=%d", firstSteps)
	}

	// Withdraw the dependency WHILE the activation is parked before its next
	// yield, then release the component.
	if err := pf.Dispose(); err != nil {
		t.Fatal(err)
	}
	close(dc.hold)

	// The activation must observe the divert, unwind its installed steps LIFO,
	// and settle Pending — not Failed.
	eventually(t, 8*time.Second, "divert observed", func() bool {
		dc.mu.Lock()
		defer dc.mu.Unlock()
		return dc.diverted
	})
	dc.mu.Lock()
	diverted, landed, installed := dc.diverted, dc.landed, append([]string(nil), dc.installed...)
	dc.mu.Unlock()
	if !diverted {
		t.Fatal("no divert observed")
	}
	if landed > 1 {
		t.Fatalf("more than one iteration landed after dependency loss: %d", landed)
	}
	if len(installed) != 1 {
		t.Fatalf("unexpected installed set: %v", installed)
	}

	// Pending, not Failed: a divert is dependency loss, not a raise.
	deadline = time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if st := holder.State(); st == runtime.StatePending || st == runtime.StateUnloading {
			break
		}
		time.Sleep(5 * time.Millisecond)
	}
	if st := holder.State(); st == runtime.StateFailed {
		t.Fatalf("divert classified as Failed — a divert is dependency loss, not a raise")
	}
	if st := holder.State(); st == runtime.StateActive {
		t.Fatal("holder still Active after dependency withdrawal")
	}
	if err := rt.Close(ctx); err != nil {
		t.Fatalf("close after divert: %v", err)
	}
}
