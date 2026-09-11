package explorer_test

import (
	"context"
	"errors"
	"strings"
	"testing"

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

func switchTestService(t *testing.T, switchCalls *[]string) *explorer.Service {
	t.Helper()
	s := explorer.New(nil)
	s.SetOwned(func() []config.OwnedComponent {
		return []config.OwnedComponent{{
			ID:    "p1",
			Type:  "svc",
			Fiber: &runtime.Fiber{}, // non-nil: declared and owned
		}}
	})
	s.SetDesired(config.Config{Components: []config.ComponentConfig{{ID: "p1", Type: "svc"}}})
	s.SetDeclarationSwitch(func(_ context.Context, id string, enable bool) error {
		*switchCalls = append(*switchCalls, id+":"+map[bool]string{true: "on", false: "off"}[enable])
		return nil
	})
	return s
}

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
