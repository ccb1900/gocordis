package explorer_test

import (
	"context"
	"errors"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/explorer"
	"dynamic-runtime/runtime"
)

// Explorer Service conformance: inspection reads the controller's Owned view;
// control is DECLARATION-FIRST (ADR: paper §4.4 — disable = retire on the
// entry, the declaration is the surviving truth). The direct Fiber path is
// only the transient fallback when no declaration switch is wired.

type fakeOwned struct{ entries []config.OwnedComponent }

func (f *fakeOwned) owned() []config.OwnedComponent { return f.entries }

// switchTestService builds a fixture with a REAL runtime fiber: the
// declaration-first branch settles on the Runtime's terminal state (Ready /
// Gone), so a zero-value Fiber would neither compile a truthful result nor
// survive the wait. The switch simulates a declaration reconcile by
// loading/disposing the fiber — exactly what an application's switch does.
func switchTestService(t *testing.T, switchCalls *[]string) *explorer.Service {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})
	fiber, err := rt.Load(&svcComp{})
	if err != nil {
		t.Fatal(err)
	}
	if err := fiber.Ready(context.Background()); err != nil {
		t.Fatal(err)
	}

	s := explorer.New(nil)
	s.SetOwned(func() []config.OwnedComponent {
		return []config.OwnedComponent{{ID: "p1", Type: "svc", Fiber: fiber}}
	})
	s.SetDesired(config.Config{Components: []config.ComponentConfig{{ID: "p1", Type: "svc"}}})
	s.SetDeclarationSwitch(func(_ context.Context, id string, enable bool) error {
		*switchCalls = append(*switchCalls, id+":"+map[bool]string{true: "on", false: "off"}[enable])
		if id != "p1" {
			return nil
		}
		if enable {
			return fiber.Load()
		}
		return fiber.Dispose()
	})
	return s
}

type svcComp struct{}

func (c *svcComp) Name() string                                    { return "svc-p1" }
func (c *svcComp) Inject() []runtime.Dependency                    { return nil }
func (c *svcComp) Provide() []runtime.Capability                   { return nil }
func (c *svcComp) Apply(*runtime.Context) (runtime.Cleanup, error) { return nil, nil }

func TestControlRoutesThroughDeclarationSwitch(t *testing.T) {
	var calls []string
	s := switchTestService(t, &calls)

	res := s.Control(context.Background(), "p1", false)
	if !res.Accepted || res.Rejected {
		t.Fatalf("disable result = %+v, want accepted", res)
	}
	res = s.Control(context.Background(), "p1", true)
	if !res.Accepted {
		t.Fatalf("enable result = %+v, want accepted", res)
	}
	if got := strings.Join(calls, ","); got != "p1:off,p1:on" {
		t.Fatalf("declaration switch calls = %q, want p1:off,p1:on", got)
	}
}

func TestControlDeclarationSwitchErrorRejects(t *testing.T) {
	var calls []string
	s := switchTestService(t, &calls)
	s.SetDeclarationSwitch(func(_ context.Context, _ string, _ bool) error {
		return errors.New("manifest store unavailable")
	})
	res := s.Control(context.Background(), "p1", true)
	if res.Accepted || !res.Rejected {
		t.Fatalf("result = %+v, want rejected on switch error", res)
	}
}

func TestControlUnknownComponentRejected(t *testing.T) {
	var calls []string
	s := switchTestService(t, &calls)
	res := s.Control(context.Background(), "ghost", true)
	if !res.Rejected {
		t.Fatalf("result = %+v, want rejected for unknown id", res)
	}
	if len(calls) != 0 {
		t.Fatalf("declaration switch must not fire for unknown ids: %v", calls)
	}
}

// Fallback path: without a declaration switch, Control uses the direct fiber
// path (transient override semantics — next reconcile wins).
func TestControlFallbackDirectFiber(t *testing.T) {
	s := explorer.New(nil)
	s.SetOwned(func() []config.OwnedComponent { return []config.OwnedComponent{} })
	res := s.Control(context.Background(), "ghost-fiber", true)
	if !res.Rejected {
		t.Fatalf("fallback with no owned fiber = %+v, want rejected", res)
	}
}
