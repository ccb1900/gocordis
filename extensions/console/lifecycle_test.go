package console_test

import (
	"context"
	"errors"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console"
)

// Reference-lifecycle conformance (F-4): the declaration-store-backed
// PluginLifecycle. The console switch edits Enabled in the declaration and
// reconciles; a failed reconcile rolls the declaration back (the store never
// lies); persistence is the application's DesiredStore.

type memStore struct {
	comps   []config.ComponentConfig
	saveErr error
}

func (m *memStore) Load(_ context.Context) ([]config.ComponentConfig, error) {
	return append([]config.ComponentConfig(nil), m.comps...), nil
}

func (m *memStore) Save(_ context.Context, comps []config.ComponentConfig) error {
	if m.saveErr != nil {
		return m.saveErr
	}
	m.comps = append([]config.ComponentConfig(nil), comps...)
	return nil
}

type fakeController struct {
	reconciles []config.Config
	failNext   error
}

func (f *fakeController) Reconcile(_ context.Context, desired config.Config) error {
	if f.failNext != nil {
		err := f.failNext
		f.failNext = nil
		return err
	}
	f.reconciles = append(f.reconciles, desired)
	return nil
}

func enabledFlag(b bool) *bool { return &b }

func newLifecycle(t *testing.T) (*console.ControllerLifecycle, *memStore, *fakeController) {
	t.Helper()
	store := &memStore{comps: []config.ComponentConfig{
		{ID: "alpha", Type: "alpha", Enabled: enabledFlag(true)},
		{ID: "beta", Type: "beta", Enabled: enabledFlag(false)},
	}}
	ctrl := &fakeController{}
	return console.NewControllerLifecycle(store, ctrl), store, ctrl
}

func TestLifecycleUninstallDisablesDeclaration(t *testing.T) {
	lc, store, ctrl := newLifecycle(t)
	if err := lc.Uninstall(context.Background(), "alpha"); err != nil {
		t.Fatal(err)
	}
	if *store.comps[0].Enabled != false {
		t.Fatal("uninstall must set Enabled=false in the declaration")
	}
	if len(ctrl.reconciles) != 1 {
		t.Fatalf("reconciles = %d, want 1", len(ctrl.reconciles))
	}
}

func TestLifecycleInstallReEnables(t *testing.T) {
	lc, store, _ := newLifecycle(t)
	if err := lc.Install(context.Background(), "beta"); err != nil {
		t.Fatal(err)
	}
	if *store.comps[1].Enabled != true {
		t.Fatal("install must set Enabled=true in the declaration")
	}
}

func TestLifecycleRemovedListsDisabled(t *testing.T) {
	lc, _, _ := newLifecycle(t)
	removed, err := lc.Removed(context.Background())
	if err != nil {
		t.Fatal(err)
	}
	if len(removed) != 1 || removed[0].ID != "beta" {
		t.Fatalf("removed = %+v, want [beta]", removed)
	}
}

func TestLifecycleConfigRoundTripViaDeclaration(t *testing.T) {
	lc, store, _ := newLifecycle(t)
	store.comps[0].Config = map[string]any{"batch": 8}

	got, err := lc.Config(context.Background(), "alpha")
	if err != nil {
		t.Fatal(err)
	}
	if got["batch"] != 8 {
		t.Fatalf("config = %v", got)
	}

	if err := lc.SetConfig(context.Background(), "alpha", map[string]any{"batch": 16}); err != nil {
		t.Fatal(err)
	}
	if store.comps[0].Config["batch"] != 16 {
		t.Fatalf("declaration config = %v, want batch=16", store.comps[0].Config)
	}
}

// The declaration never lies: a failed reconcile rolls the store back.
func TestLifecycleFailedReconcileRollsBackDeclaration(t *testing.T) {
	lc, store, ctrl := newLifecycle(t)
	wantErr := errors.New("reconcile failed")
	ctrl.failNext = wantErr

	if err := lc.Uninstall(context.Background(), "alpha"); !errors.Is(err, wantErr) {
		t.Fatalf("err = %v, want reconcile failure", err)
	}
	if *store.comps[0].Enabled != true {
		t.Fatal("declaration must roll back after a failed reconcile")
	}

	cfg := map[string]any{"batch": 99}
	ctrl.failNext = wantErr
	if err := lc.SetConfig(context.Background(), "alpha", cfg); !errors.Is(err, wantErr) {
		t.Fatalf("err = %v, want reconcile failure", err)
	}
	if _, changed := store.comps[0].Config["batch"]; changed {
		t.Fatal("config declaration must roll back after a failed reconcile")
	}
}

func TestLifecycleUnknownComponent(t *testing.T) {
	lc, _, _ := newLifecycle(t)
	if err := lc.Uninstall(context.Background(), "ghost"); !errors.Is(err, console.ErrComponentUnknown) {
		t.Fatalf("err = %v, want ErrComponentUnknown", err)
	}
}

func TestLifecycleStoreSaveFailurePropagates(t *testing.T) {
	lc, store, _ := newLifecycle(t)
	store.saveErr = errors.New("disk full")
	if err := lc.Uninstall(context.Background(), "alpha"); !errors.Is(err, store.saveErr) {
		t.Fatalf("err = %v, want store failure", err)
	}
}
