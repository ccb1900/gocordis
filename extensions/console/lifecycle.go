package console

import (
	"context"
	"errors"
	"fmt"
	"sort"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/webui"
)

// Reference PluginLifecycle implementation: a declaration-store-backed
// lifecycle for the console (paper §4.4 Configuration / §5.2.1). The console
// switch edits the DECLARATION (Enabled on the desired entry) and reconciles —
// the entry is the surviving identity, the fiber the identity of one
// enablement. Removed plugins keep their declaration with Enabled=false (the
// install-back action re-enables); persistence is the application's
// DesiredStore (manifest file, journal, database — see the persistence stance
// in docs/review §9.3).
//
// This is the wiring reference for applications exposing the console: the
// transport (webui.Server) only serves; THIS type decides what a switch means.

// DesiredStore is the application's declaration persistence: load returns the
// current desired set; save persists a new set (the implementation owns
// validation and rollback-on-failure).
type DesiredStore interface {
	Load(ctx context.Context) ([]config.ComponentConfig, error)
	Save(ctx context.Context, comps []config.ComponentConfig) error
}

// ControllerReconciler reconciles the runtime toward a desired set — in the
// standard assembly this is (*config.Controller).Reconcile.
type ControllerReconciler interface {
	Reconcile(ctx context.Context, desired config.Config) error
}

// ControllerLifecycle errors.
var (
	ErrComponentUnknown    = errors.New("console: component not in the desired set")
	ErrComponentPersistent = errors.New("console: component rejects the operation")
)

// ControllerLifecycle implements webui.PluginLifecycle over a DesiredStore +
// controller reconciler.
type ControllerLifecycle struct {
	store      DesiredStore
	reconciler ControllerReconciler
}

// NewControllerLifecycle builds the reference lifecycle.
func NewControllerLifecycle(store DesiredStore, reconciler ControllerReconciler) *ControllerLifecycle {
	return &ControllerLifecycle{store: store, reconciler: reconciler}
}

// load returns the desired set and the index of the component (ErrComponentUnknown).
func (l *ControllerLifecycle) load(ctx context.Context, id string) ([]config.ComponentConfig, int, error) {
	comps, err := l.store.Load(ctx)
	if err != nil {
		return nil, -1, err
	}
	for i := range comps {
		if comps[i].ID == id {
			return comps, i, nil
		}
	}
	return comps, -1, fmt.Errorf("%w: %q", ErrComponentUnknown, id)
}

// saveAndReconcile persists the new declaration and reconciles; on reconcile
// failure the PREVIOUS declaration is restored (both store and runtime), so a
// failed switch never lies about the declaration. prev must be the pre-change
// snapshot.
func (l *ControllerLifecycle) saveAndReconcile(ctx context.Context, prev, comps []config.ComponentConfig) error {
	if err := l.store.Save(ctx, comps); err != nil {
		return err
	}
	if err := l.reconciler.Reconcile(ctx, config.Config{Components: comps}); err != nil {
		if perr := l.store.Save(ctx, prev); perr != nil {
			return errors.Join(err, perr)
		}
		_ = l.reconciler.Reconcile(ctx, config.Config{Components: prev})
		return err
	}
	return nil
}

// Uninstall disables the component: Enabled=false in the declaration, then
// reconcile. The entry survives (install-back = re-enable).
func (l *ControllerLifecycle) Uninstall(ctx context.Context, id string) error {
	comps, i, err := l.load(ctx, id)
	if err != nil {
		return err
	}
	prev := append([]config.ComponentConfig(nil), comps...)
	off := false
	comps[i].Enabled = &off
	return l.saveAndReconcile(ctx, prev, comps)
}

// Install re-enables a previously disabled component: Enabled=true in the
// declaration, then reconcile.
func (l *ControllerLifecycle) Install(ctx context.Context, id string) error {
	comps, i, err := l.load(ctx, id)
	if err != nil {
		return err
	}
	prev := append([]config.ComponentConfig(nil), comps...)
	on := true
	comps[i].Enabled = &on
	return l.saveAndReconcile(ctx, prev, comps)
}

// Removed lists the disabled (uninstalled) declarations, sorted by ID.
func (l *ControllerLifecycle) Removed(_ context.Context) ([]webui.RemovedPlugin, error) {
	comps, err := l.store.Load(context.Background())
	if err != nil {
		return nil, err
	}
	var out []webui.RemovedPlugin
	for _, c := range comps {
		if c.Enabled != nil && !*c.Enabled {
			out = append(out, webui.RemovedPlugin{ID: c.ID, Name: c.ID})
		}
	}
	sort.Slice(out, func(i, j int) bool { return out[i].ID < out[j].ID })
	return out, nil
}

// Config returns the desired configuration of one component (from the
// declaration, not the running fiber).
func (l *ControllerLifecycle) Config(_ context.Context, id string) (map[string]any, error) {
	comps, i, err := l.load(context.Background(), id)
	if err != nil {
		return nil, err
	}
	if comps[i].Config == nil {
		return map[string]any{}, nil
	}
	return comps[i].Config, nil
}

// SetConfig replaces one component's declaration configuration and
// reconciles; a reconcile failure rolls the declaration back.
func (l *ControllerLifecycle) SetConfig(ctx context.Context, id string, cfg map[string]any) error {
	comps, i, err := l.load(ctx, id)
	if err != nil {
		return err
	}
	prev := append([]config.ComponentConfig(nil), comps...)
	comps[i].Config = cfg
	return l.saveAndReconcile(ctx, prev, comps)
}

// Compile-time interface check.
var _ webui.PluginLifecycle = (*ControllerLifecycle)(nil)
