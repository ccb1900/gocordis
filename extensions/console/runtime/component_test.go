package runtime_test

import (
	"context"
	"encoding/json"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	consoRuntime "dynamic-runtime/extensions/console/runtime"
	"dynamic-runtime/extensions/devtools"
	"dynamic-runtime/extensions/pluginmeta"
	coreruntime "dynamic-runtime/runtime"
)

type valueComp struct {
	name    string
	key     coreruntime.Key[string]
	provide bool
	require bool
}

func (c *valueComp) Name() string { return c.name }
func (c *valueComp) Inject() []coreruntime.Dependency {
	if c.require {
		return []coreruntime.Dependency{coreruntime.Requires(c.key)}
	}
	return nil
}
func (c *valueComp) Provide() []coreruntime.Capability {
	if c.provide {
		return []coreruntime.Capability{c.key.Capability()}
	}
	return nil
}
func (c *valueComp) Apply(ctx *coreruntime.Context) (coreruntime.Cleanup, error) {
	if c.require {
		if _, err := coreruntime.Require(ctx, c.key); err != nil {
			return nil, err
		}
	}
	if c.provide {
		if err := coreruntime.Provide(ctx, c.key, "value"); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

type fakeDevtools struct{}

func (fakeDevtools) Diagnose(pluginmeta.Manifest) []devtools.Diagnostic {
	return []devtools.Diagnostic{{Severity: devtools.SeverityWarn, Code: "FAKE", Message: "diagnostic from devtools"}}
}

type devtoolsComp struct{}

func (*devtoolsComp) Name() string                     { return "fake-devtools" }
func (*devtoolsComp) Inject() []coreruntime.Dependency { return nil }
func (*devtoolsComp) Provide() []coreruntime.Capability {
	return []coreruntime.Capability{devtools.ServiceKey.Capability()}
}
func (*devtoolsComp) Apply(ctx *coreruntime.Context) (coreruntime.Cleanup, error) {
	if err := coreruntime.Provide[devtools.Service](ctx, devtools.ServiceKey, fakeDevtools{}); err != nil {
		return nil, err
	}
	return nil, nil
}

type stack struct {
	rt      *coreruntime.Runtime
	ring    *consoRuntime.Ring
	console *host.UIComponent
	h       *host.Host
	comp    *consoRuntime.Component
	fibers  map[string]*coreruntime.Fiber
	key     coreruntime.Key[string]
}

func newStack(t *testing.T, ringCapacity int, withDevtools bool) *stack {
	t.Helper()
	ring := consoRuntime.NewRing(ringCapacity)
	rt, err := coreruntime.New(coreruntime.WithEventSink(ring), coreruntime.WithRuntimeID("runtime:console-test"))
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = rt.Close(ctx)
	})

	console, err := host.NewConsole(config.ComponentConfig{ID: "console"})
	if err != nil {
		t.Fatal(err)
	}
	s := &stack{rt: rt, ring: ring, console: console, fibers: map[string]*coreruntime.Fiber{}}
	s.load(t, "console", console)
	s.h = console.HostAdapter() // adapter exists only after the console activation

	key := coreruntime.NewKey[string]("runtime-console.test.value")
	s.key = key
	s.load(t, "provider", &valueComp{name: "provider", key: key, provide: true})
	s.load(t, "consumer", &valueComp{name: "consumer", key: key, require: true})
	if withDevtools {
		s.load(t, "devtools", &devtoolsComp{})
	}

	opts := []consoRuntime.Option{}
	if withDevtools {
		opts = append(opts, consoRuntime.WithDevtoolsDiagnostics())
	}
	comp, err := consoRuntime.New(config.ComponentConfig{ID: "runtime-console"}, rt, ring, opts...)
	if err != nil {
		t.Fatal(err)
	}
	s.comp = comp
	s.load(t, "console-runtime", comp)
	return s
}

func (s *stack) load(t *testing.T, name string, c coreruntime.Component) *coreruntime.Fiber {
	t.Helper()
	f, err := s.rt.Load(c)
	if err != nil {
		t.Fatalf("load %s: %v", name, err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	if err := f.Ready(ctx); err != nil {
		t.Fatalf("%s not Active: %v", name, err)
	}
	s.fibers[name] = f
	return f
}

func (s *stack) query(t *testing.T, name string) any {
	t.Helper()
	res, ue := s.h.Query(name, nil)
	if ue != nil {
		t.Fatalf("query %s: %v", name, ue)
	}
	return res
}

func snapshotJSON(t *testing.T, v any) string {
	t.Helper()
	raw, err := json.Marshal(v)
	if err != nil {
		t.Fatal(err)
	}
	return string(raw)
}

func TestComponentInjectDeclarations(t *testing.T) {
	base, err := consoRuntime.New(config.ComponentConfig{ID: "console-runtime"}, &coreruntime.Runtime{}, consoRuntime.NewRing(1))
	if err != nil {
		t.Fatal(err)
	}
	keys := map[string]bool{}
	for _, d := range base.Inject() {
		keys[d.Key.String()] = true
	}
	if !keys[host.HubKey.Capability().String()] || !keys[host.UIHostKey.Capability().String()] {
		t.Fatalf("Inject missing host capabilities: %v", keys)
	}
	if keys[devtools.ServiceKey.Capability().String()] {
		t.Fatalf("default Inject must not require devtools: %v", keys)
	}

	full, err := consoRuntime.New(config.ComponentConfig{ID: "console-runtime"}, &coreruntime.Runtime{}, consoRuntime.NewRing(1), consoRuntime.WithDevtoolsDiagnostics())
	if err != nil {
		t.Fatal(err)
	}
	keys = map[string]bool{}
	for _, d := range full.Inject() {
		keys[d.Key.String()] = true
	}
	if !keys[devtools.ServiceKey.Capability().String()] {
		t.Fatalf("WithDevtoolsDiagnostics must declare devtools.ServiceKey: %v", keys)
	}
}

func TestSnapshotQueryReflectsActiveFactsAndMatchesRuntimeSnapshot(t *testing.T) {
	s := newStack(t, 64, false)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	res := s.query(t, consoRuntime.SnapshotQuery)
	dto, ok := res.(consoRuntime.SnapshotDTO)
	if !ok {
		t.Fatalf("snapshot result type = %T, want SnapshotDTO", res)
	}
	if dto.Sequence == 0 || dto.RuntimeID != "runtime:console-test" || dto.State != coreruntime.RuntimeRunning.String() {
		t.Fatalf("snapshot envelope = %+v", dto)
	}
	if len(dto.Fibers) < 4 {
		t.Fatalf("fibers = %d, want >= 4: %+v", len(dto.Fibers), dto.Fibers)
	}
	for _, f := range dto.Fibers {
		if f.State != coreruntime.StateActive.String() || f.ActivationID == 0 || f.ChildFiberIDs == nil || f.ParentFiberIDs == nil {
			t.Fatalf("fiber dto incomplete: %+v", f)
		}
	}

	providerID := s.fibers["provider"].ID()
	consumerID := s.fibers["consumer"].ID()
	var providerRow consoRuntime.ProviderDTO
	for _, p := range dto.Providers {
		if p.Key == s.key.Capability().String() {
			providerRow = p
		}
	}
	if providerRow.OwnerFiberID != uint64(providerID) || providerRow.State != coreruntime.StateActive.String() || providerRow.Retiring {
		t.Fatalf("provider row = %+v, want live provider on fiber %d", providerRow, providerID)
	}
	var dep consoRuntime.DependencyDTO
	for _, d := range dto.Dependencies {
		if d.Key == s.key.Capability().String() && d.ConsumerFiberID == uint64(consumerID) {
			dep = d
		}
	}
	if dep.ProviderFiberID != uint64(providerID) || dep.Status != string(coreruntime.DependencySatisfied) {
		t.Fatalf("dependency row = %+v, want satisfied %d -> %d", dep, consumerID, providerID)
	}
	var effect consoRuntime.EffectDTO
	for _, e := range dto.Effects {
		if e.OwnerFiberID == uint64(providerID) && e.Kind == coreruntime.EffectKindProvider.String() {
			effect = e
		}
	}
	if effect.Key != s.key.Capability().String() || effect.State != "Committed" || effect.OwnerFiberID == 0 {
		t.Fatalf("effect row = %+v, want committed provider effect", effect)
	}
	if len(dto.Scopes) == 0 || dto.Scopes[0].ProviderKeys == nil || dto.Scopes[0].FiberIDs == nil {
		t.Fatalf("scopes = %+v", dto.Scopes)
	}

	// The DTO must be a pure projection of Runtime.Snapshot: no recomputation.
	snap, err := s.rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if got, want := snapshotJSON(t, dto), snapshotJSON(t, consoRuntime.SnapshotFrom(snap)); got != want {
		t.Fatalf("query DTO differs from Snapshot projection:\n got %s\nwant %s", got, want)
	}
}

func TestEventsQueryComesFromBoundedRing(t *testing.T) {
	s := newStack(t, 8, false)
	res := s.query(t, consoRuntime.EventsQuery)
	events, ok := res.(consoRuntime.EventsDTO)
	if !ok {
		t.Fatalf("events result type = %T, want EventsDTO", res)
	}
	if len(events.Events) == 0 || len(events.Events) > 8 {
		t.Fatalf("events = %d, want 1..8 (ring bound)", len(events.Events))
	}
	if events.Sequence == 0 || events.Sequence != events.Events[len(events.Events)-1].Sequence {
		t.Fatalf("events envelope sequence = %d, last = %d", events.Sequence, events.Events[len(events.Events)-1].Sequence)
	}
	var last uint64
	for i, e := range events.Events {
		if i > 0 && e.Sequence <= last {
			t.Fatalf("events out of order: %d then %d", last, e.Sequence)
		}
		last = e.Sequence
		if e.RuntimeID != "runtime:console-test" || e.Type == "" {
			t.Fatalf("event dto = %+v", e)
		}
	}
}

func TestDashboardQueryCounts(t *testing.T) {
	s := newStack(t, 32, false)
	res := s.query(t, consoRuntime.DashboardQuery)
	dashboard, ok := res.(consoRuntime.DashboardDTO)
	if !ok {
		t.Fatalf("dashboard result type = %T, want DashboardDTO", res)
	}
	if dashboard.State != coreruntime.RuntimeRunning.String() || dashboard.Sequence == 0 {
		t.Fatalf("dashboard envelope = %+v", dashboard)
	}
	if dashboard.Counts.Fibers < 4 || dashboard.Counts.Active != dashboard.Counts.Fibers || dashboard.Counts.Providers == 0 || dashboard.Counts.Dependencies == 0 || dashboard.Counts.Effects == 0 {
		t.Fatalf("dashboard counts = %+v", dashboard.Counts)
	}
	if dashboard.Counts.Failed != 0 || dashboard.Counts.Pending != 0 {
		t.Fatalf("failed/pending counts = %d/%d, want 0/0", dashboard.Counts.Failed, dashboard.Counts.Pending)
	}
	if len(dashboard.RecentEvents) == 0 || len(dashboard.RecentEvents) > 32 {
		t.Fatalf("recentEvents = %d", len(dashboard.RecentEvents))
	}
	if len(dashboard.Diagnostics) != 0 {
		t.Fatalf("diagnostics without devtools = %+v, want empty", dashboard.Diagnostics)
	}
}

func TestDashboardDiagnosticsFromDevtoolsService(t *testing.T) {
	s := newStack(t, 32, true)
	res := s.query(t, consoRuntime.DashboardQuery)
	dashboard := res.(consoRuntime.DashboardDTO)
	if len(dashboard.Diagnostics) != 1 {
		t.Fatalf("diagnostics = %+v, want one from devtools.Service", dashboard.Diagnostics)
	}
	d := dashboard.Diagnostics[0]
	if d.Severity != string(devtools.SeverityWarn) || d.Code != "FAKE" || !strings.Contains(d.Message, "devtools") {
		t.Fatalf("diagnostic = %+v", d)
	}
}

func TestPagesRegisteredAsEffectsAndLeaveNoResidue(t *testing.T) {
	s := newStack(t, 32, false)

	assertPages := func(want bool) {
		t.Helper()
		pages := map[string]host.PageDefinition{}
		for _, p := range s.console.Pages() {
			pages[p.ID] = p
		}
		for _, spec := range []struct{ id, route, renderer string }{
			{consoRuntime.InspectorPageID, consoRuntime.InspectorRoute, consoRuntime.InspectorRenderer},
			{consoRuntime.DashboardPageID, consoRuntime.DashboardRoute, consoRuntime.DashboardRenderer},
		} {
			p, ok := pages[spec.id]
			if !want {
				if ok {
					t.Fatalf("page %s survived dispose: %+v", spec.id, p)
				}
				continue
			}
			if !ok || p.Route != spec.route || p.Renderer != spec.renderer {
				t.Fatalf("page %s = %+v (present=%v), want route %s renderer %s", spec.id, p, ok, spec.route, spec.renderer)
			}
		}
	}
	assertQueries := func(want bool) {
		t.Helper()
		names, _ := s.h.Names()
		present := map[string]bool{}
		for _, n := range names {
			present[n] = true
		}
		for _, name := range []string{consoRuntime.SnapshotQuery, consoRuntime.EventsQuery, consoRuntime.DashboardQuery} {
			if present[name] != want {
				t.Fatalf("query %s present = %v, want %v (all: %v)", name, present[name], want, names)
			}
		}
	}
	assertPages(true)
	assertQueries(true)

	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	f := s.fibers["console-runtime"]
	if err := f.Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := f.Gone(ctx); err != nil {
		t.Fatal(err)
	}
	assertPages(false)
	assertQueries(false)

	// Re-activation registers a fresh generation; the previous unregister is
	// owner-scoped and cannot remove the new contribution (no stale residue).
	s.load(t, "console-runtime-2", s.comp)
	assertPages(true)
	assertQueries(true)
	if err := s.fibers["console-runtime-2"].Dispose(); err != nil {
		t.Fatal(err)
	}
	if err := s.fibers["console-runtime-2"].Gone(ctx); err != nil {
		t.Fatal(err)
	}
	assertPages(false)
	assertQueries(false)
}
