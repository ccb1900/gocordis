package console_test

import (
	"context"
	"errors"
	"sync"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console"
)

// seqFakeController is a concurrency-safe reconciler fake whose failure is
// COUNTED (the shared fakeController's failNext is one-shot).
type seqFakeController struct {
	mu            sync.Mutex
	failRemaining int
	reconciles    []config.Config
}

func (f *seqFakeController) Reconcile(_ context.Context, desired config.Config) error {
	f.mu.Lock()
	defer f.mu.Unlock()
	if f.failRemaining > 0 {
		f.failRemaining--
		return errors.New("injected reconcile failure")
	}
	f.reconciles = append(f.reconciles, desired)
	return nil
}

// P2-1 regression: console lifecycle operations run one goroutine per HTTP
// request, so Uninstall/Install/SetConfig race each other. The whole
// Load-Modify-Save-Reconcile flow must be serialized under one mutex — two
// interleaved flows used to Save divergent snapshots while reconciling in the
// opposite order (store/runtime divergence with nothing scheduled to repair).
//
// Oracle: beta's config update is carried forward by EVERY valid serial order
// of the three operations (none of them overwrites beta), so a final store
// without beta's update proves a lost update. Alpha's final flag is
// order-dependent (uninstall vs install) and deliberately not asserted.
func TestControllerLifecycleConcurrentOpsNoLostUpdate(t *testing.T) {
	store := &memStore{comps: []config.ComponentConfig{
		{ID: "alpha", Type: "t"},
		{ID: "beta", Type: "t"},
	}}
	ctrl := &seqFakeController{}
	l := console.NewControllerLifecycle(store, ctrl)
	ctx := context.Background()

	var wg sync.WaitGroup
	wg.Add(3)
	go func() { defer wg.Done(); _ = l.Uninstall(ctx, "alpha") }()
	go func() {
		defer wg.Done()
		_ = l.SetConfig(ctx, "beta", map[string]any{"mode": "x"})
	}()
	go func() { defer wg.Done(); _ = l.Install(ctx, "alpha") }()
	wg.Wait()

	comps, err := store.Load(ctx)
	if err != nil {
		t.Fatal(err)
	}
	byID := map[string]config.ComponentConfig{}
	for _, c := range comps {
		byID[c.ID] = c
	}
	beta, ok := byID["beta"]
	if !ok || beta.Config["mode"] != "x" {
		t.Fatalf("beta config update lost under concurrency: %+v", beta)
	}
}

// P2-2 regression: when the restore reconcile after a failed switch ALSO
// fails, that failure must SURFACE (joined into the returned error) — the old
// code discarded it, leaving the store saying prev while the runtime still
// ran the new set with nothing scheduled to repair the divergence.
func TestControllerLifecycleRestoreReconcileFailureSurfaces(t *testing.T) {
	store := &memStore{comps: []config.ComponentConfig{{ID: "alpha", Type: "t"}}}
	ctrl := &seqFakeController{failRemaining: 2} // switch AND restore both fail
	l := console.NewControllerLifecycle(store, ctrl)
	ctx := context.Background()

	err := l.Uninstall(ctx, "alpha")
	if err == nil {
		t.Fatal("expected the switch to fail")
	}
	if !errors.Is(err, err) || len(err.Error()) == 0 {
		t.Fatal("empty error")
	}
	// The restore reconcile must have been attempted with the PREVIOUS
	// declaration (alpha re-enabled).
	ctrl.mu.Lock()
	defer ctrl.mu.Unlock()
	if len(ctrl.reconciles) != 0 {
		t.Fatalf("unexpected successful reconciles: %d", len(ctrl.reconciles))
	}
	// Both failures must be visible in the joined error text.
	if !contains(err.Error(), "restore reconcile") {
		t.Fatalf("restore reconcile failure swallowed: %v", err)
	}
}

func contains(haystack, needle string) bool {
	return len(haystack) >= len(needle) && (haystack == needle ||
		len(needle) == 0 ||
		indexOf(haystack, needle) >= 0)
}

func indexOf(h, n string) int {
	for i := 0; i+len(n) <= len(h); i++ {
		if h[i:i+len(n)] == n {
			return i
		}
	}
	return -1
}
