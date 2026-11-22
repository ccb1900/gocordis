package webui

import (
	"context"
	"net/http"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/explorer"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/runtime"
)

// activeExplorer builds one genuinely active Explorer transport through the
// real Runtime (the transport's activate() is internal to the explorer package).
func activeExplorer(t *testing.T) *explorer.ExplorerTransport {
	t.Helper()
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	t.Cleanup(func() {
		closeCtx, closeCancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer closeCancel()
		_ = rt.Close(closeCtx)
	})

	hostComp, err := host.NewConsole(config.ComponentConfig{ID: "host"})
	if err != nil {
		t.Fatal(err)
	}
	hf, err := rt.Load(hostComp)
	if err != nil {
		t.Fatal(err)
	}
	if err := hf.Ready(ctx); err != nil {
		t.Fatal(err)
	}

	svc := explorer.New(nil)
	svc.SetDesired(config.Config{Components: []config.ComponentConfig{{ID: "probe", Type: "probe"}}})
	comp, err := explorer.NewPlugin(config.ComponentConfig{ID: "plugins"}, svc)
	if err != nil {
		t.Fatal(err)
	}
	ef, err := rt.Load(comp)
	if err != nil {
		t.Fatal(err)
	}
	if err := ef.Ready(ctx); err != nil {
		t.Fatal(err)
	}
	adapter := comp.HostAdapter()
	if adapter == nil {
		t.Fatal("explorer component has no active host adapter")
	}
	return adapter
}

// TestExplorerResolvedOncePerRequest is the regression for a dynamic provider:
// two currentExplorer() calls in one request can resolve two DIFFERENT
// activations (disable→enable churn). The old routes resolved once for the nil
// check and again for the call — a stale/deactivated transport could answer —
// so every Explorer route must resolve once and reuse the local reference.
func TestExplorerResolvedOncePerRequest(t *testing.T) {
	active := activeExplorer(t)
	// A fresh (inactive) transport stands in for the next activation. If the
	// route resolves twice, the second resolution answers "unavailable".
	other := explorer.NewTransport(context.Background(), nil)

	s := New(nil, nil)
	calls := 0
	s.SetExplorerProvider(func() *explorer.ExplorerTransport {
		calls++
		if calls%2 == 1 {
			return active
		}
		return other
	})

	// GET /api/plugins
	calls = 0
	rec := lcGet(t, s, "/api/plugins")
	if rec.Code != http.StatusOK {
		t.Fatalf("GET /api/plugins = %d (%s), want 200 from the resolved activation", rec.Code, rec.Body.String())
	}
	if !strings.Contains(rec.Body.String(), `"probe"`) {
		t.Fatalf("GET /api/plugins body = %s, want active activation rows", rec.Body.String())
	}
	if calls != 1 {
		t.Fatalf("GET /api/plugins resolved the provider %d times, want exactly 1", calls)
	}

	// POST /api/plugins/control
	calls = 0
	rec = lcPost(t, s, "/api/plugins/control", `{"pluginId":"probe","enable":true}`)
	if rec.Code != http.StatusOK {
		t.Fatalf("POST /api/plugins/control = %d (%s), want 200 from the resolved activation", rec.Code, rec.Body.String())
	}
	if calls != 1 {
		t.Fatalf("POST /api/plugins/control resolved the provider %d times, want exactly 1", calls)
	}
}
