package market_test

import (
	"context"
	"encoding/json"
	"net/url"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/hub"
	"dynamic-runtime/extensions/market"
	"dynamic-runtime/runtime"
)

type compStack struct {
	rt      *runtime.Runtime
	h       *host.Host
	hubReg  *hub.Registry
	console *host.UIComponent
	ctrl    *config.Controller
	fiber   *runtime.Fiber
	dir     string
	row     config.ComponentConfig
}

type hubProbe struct{ got chan *hub.Registry }

func (p *hubProbe) Name() string { return "market-hub-probe" }
func (p *hubProbe) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(host.HubKey)}
}
func (p *hubProbe) Provide() []runtime.Capability { return nil }
func (p *hubProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	h, err := runtime.Require(ctx, host.HubKey)
	if err != nil {
		return nil, err
	}
	p.got <- h
	return nil, nil
}

func newCompStack(t *testing.T) *compStack {
	t.Helper()
	dir := t.TempDir()
	rt, err := runtime.New()
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
	hostFiber, err := rt.Load(console)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, hostFiber)

	probe := &hubProbe{got: make(chan *hub.Registry, 1)}
	probeFiber, err := rt.Load(probe)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, probeFiber)
	hubReg := <-probe.got

	reg := config.NewFactoryRegistry()
	var ctrl *config.Controller
	mustRegister(t, reg, "industrial.camera", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return &markerComponent{name: "industrial.camera:" + cc.ID}, nil
	}))
	mustRegister(t, reg, "settings", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return &markerComponent{name: "settings:" + cc.ID}, nil
	}))
	mustRegister(t, reg, "market", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return market.NewComponent(cc, ctrl)
	}))
	ctrl = config.NewController(rt, reg)
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(ctx)
	})

	row := config.ComponentConfig{
		ID: "market", Type: "market",
		Config: map[string]any{
			"catalog_dirs":    []any{dir},
			"runtime_version": "1.2.0",
			"api_version":     "1.0.0",
			"trust":           "local",
		},
	}
	if err := ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{row}}); err != nil {
		t.Fatalf("Reconcile market: %v", err)
	}
	var fiber *runtime.Fiber
	for _, o := range ctrl.Owned() {
		if o.ID == "market" {
			fiber = o.Fiber
		}
	}
	if fiber == nil {
		t.Fatal("market component not owned")
	}
	waitActive(t, fiber)
	return &compStack{rt: rt, h: console.HostAdapter(), hubReg: hubReg, console: console, ctrl: ctrl, fiber: fiber, dir: dir, row: row}
}

func mustRegister(t *testing.T, reg config.FactoryRegistry, typ string, f config.Factory) {
	t.Helper()
	if err := reg.Register(typ, f); err != nil {
		t.Fatal(err)
	}
}

func waitActive(t *testing.T, f *runtime.Fiber) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == runtime.StateActive {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting for Active (state %v err %v)", f.State(), f.Err())
}

func TestComponentRegistersQueryPageAndTypedService(t *testing.T) {
	s := newCompStack(t)

	queries, commands := s.h.Names()
	if !contains(queries, market.CatalogQuery) {
		t.Fatalf("queries = %v, want %q", queries, market.CatalogQuery)
	}
	for _, cmd := range []string{market.InstallCommand, market.UpgradeCommand, market.UninstallCommand} {
		if !contains(commands, cmd) {
			t.Fatalf("commands = %v, want %q", commands, cmd)
		}
	}
	pages := s.console.Pages()
	var found bool
	for _, p := range pages {
		if p.ID == "market" && p.Route == "/market" && p.Renderer == "market-catalog" {
			found = true
		}
	}
	if !found {
		t.Fatalf("pages = %+v, want market page", pages)
	}

	res, ue := s.h.Query(market.CatalogQuery, nil)
	if ue != nil {
		t.Fatalf("catalog query: %+v", ue)
	}
	cat, ok := res.(market.Catalog)
	if !ok {
		t.Fatalf("query result type = %T, want market.Catalog", res)
	}
	if cat.Entries == nil {
		t.Fatal("catalog entries must be an initialized slice, not nil")
	}

	// The typed Service is a real capability apps can Require.
	probe := &serviceProbe{got: make(chan market.Service, 1)}
	pf, err := s.rt.Load(probe)
	if err != nil {
		t.Fatal(err)
	}
	waitActive(t, pf)
	svc := <-probe.got
	if svc == nil {
		t.Fatal("ServiceKey provided nil service")
	}
	if _, err := svc.Catalog(ctxT(t)); err != nil {
		t.Fatalf("provided service Catalog: %v", err)
	}
}

func TestComponentHubCommandsMutateDeclarationsOnly(t *testing.T) {
	s := newCompStack(t)
	wasm := filepath.Join(s.dir, "alpha", "camera.wasm")
	writeFile(t, wasm, "bytes")
	sum := sha256File(t, wasm)
	writeFile(t, filepath.Join(s.dir, "alpha", "plugin.toml"), wasmManifest("alpha", "1.0.0", sum, ">=1.0.0"))

	body := json.RawMessage(`{"id":"alpha"}`)
	if ue := s.h.Command(market.InstallCommand, body); ue != nil {
		t.Fatalf("install command: %+v", ue)
	}
	var row config.OwnedComponent
	for _, o := range s.ctrl.Owned() {
		if o.ID == "alpha" {
			row = o
		}
	}
	if row.Type != "industrial.camera" || row.Config["version"] != "1.0.0" {
		t.Fatalf("installed row = %+v", row)
	}
	// The market component's own declaration survived the service's reconcile.
	if !hasOwned(t, s.ctrl, "market") {
		t.Fatal("market declaration was wiped by its own service reconcile")
	}
	if ue := s.h.Command(market.UninstallCommand, body); ue != nil {
		t.Fatalf("uninstall command: %+v", ue)
	}
	if hasOwned(t, s.ctrl, "alpha") {
		t.Fatal("alpha row survived uninstall command")
	}
}

func TestComponentDisposeLeavesNoResidue(t *testing.T) {
	s := newCompStack(t)
	if err := s.ctrl.Reconcile(ctxT(t), config.Config{}); err != nil {
		t.Fatalf("Reconcile empty: %v", err)
	}
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if _, ok := s.hubReg.Query(market.CatalogQuery); !ok {
			break
		}
		time.Sleep(2 * time.Millisecond)
	}
	if _, ok := s.hubReg.Query(market.CatalogQuery); ok {
		t.Fatal("catalog query residue after dispose")
	}
	for _, cmd := range []string{market.InstallCommand, market.UpgradeCommand, market.UninstallCommand} {
		if _, ok := s.hubReg.Command(cmd); ok {
			t.Fatalf("command %q residue after dispose", cmd)
		}
	}
	for _, p := range s.console.Pages() {
		if p.ID == "market" {
			t.Fatal("market page residue after dispose")
		}
	}
	// Hub unregister is idempotent: a fresh owner can claim the freed name.
	un, err := s.hubReg.RegisterQuery(market.CatalogQuery, "test", func(context.Context, url.Values) (any, *hub.Error) {
		return "ok", nil
	})
	if err != nil {
		t.Fatalf("query name still owned after dispose: %v", err)
	}
	if err := un(); err != nil { // idempotent cleanup
		t.Fatal(err)
	}
	if err := un(); err != nil {
		t.Fatal(err)
	}
}

// An unrelated, already-applied application declaration must survive market
// Install/Uninstall: the convenience Component cannot treat its Base as the
// complete desired set (review issue 1, HIGH).
func TestUnrelatedDeclarationSurvivesMarketMutations(t *testing.T) {
	s := newCompStack(t)
	unrelated := config.ComponentConfig{
		ID: "unrelated", Type: "settings", Config: map[string]any{"level": 1},
	}
	if err := s.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{s.row, unrelated}}); err != nil {
		t.Fatalf("Reconcile with unrelated row: %v", err)
	}
	before := ownedRowByID(t, s.ctrl, "unrelated")
	waitActive(t, before.Fiber)

	wasm := filepath.Join(s.dir, "alpha", "camera.wasm")
	writeFile(t, wasm, "bytes")
	sum := sha256File(t, wasm)
	writeFile(t, filepath.Join(s.dir, "alpha", "plugin.toml"), wasmManifest("alpha", "1.0.0", sum, ">=1.0.0"))

	body := json.RawMessage(`{"id":"alpha"}`)
	if ue := s.h.Command(market.InstallCommand, body); ue != nil {
		t.Fatalf("install: %+v", ue)
	}
	afterInstall := ownedRowByID(t, s.ctrl, "unrelated")
	if afterInstall.Fiber != before.Fiber || afterInstall.Type != "settings" {
		t.Fatalf("unrelated row churned or lost on install: %+v", afterInstall)
	}
	if !hasOwned(t, s.ctrl, "alpha") {
		t.Fatal("alpha was not installed")
	}
	if ue := s.h.Command(market.UninstallCommand, body); ue != nil {
		t.Fatalf("uninstall: %+v", ue)
	}
	afterUninstall := ownedRowByID(t, s.ctrl, "unrelated")
	if afterUninstall.Fiber != before.Fiber {
		t.Fatalf("unrelated row churned or lost on uninstall: %+v", afterUninstall)
	}
	if !hasOwned(t, s.ctrl, "market") {
		t.Fatal("market's own declaration was wiped")
	}
}

// One malformed manifest must not blank the Market page: the partial catalog
// and its reasons travel as an additive, backward-compatible DTO field.
func TestCatalogQueryShowsValidEntriesWithBadManifest(t *testing.T) {
	s := newCompStack(t)
	wasm := filepath.Join(s.dir, "alpha", "camera.wasm")
	writeFile(t, wasm, "bytes")
	sum := sha256File(t, wasm)
	writeFile(t, filepath.Join(s.dir, "alpha", "plugin.toml"), wasmManifest("alpha", "1.0.0", sum, ">=1.0.0"))
	writeFile(t, filepath.Join(s.dir, "bad", "plugin.json"), `{"id":"bad","surprise":true}`)

	res, ue := s.h.Query(market.CatalogQuery, nil)
	if ue != nil {
		t.Fatalf("catalog query must be non-fatal for bad entries: %+v", ue)
	}
	cat, ok := res.(market.Catalog)
	if !ok {
		t.Fatalf("query result type = %T, want market.Catalog", res)
	}
	if len(cat.Entries) != 1 || cat.Entries[0].ID != "alpha" {
		t.Fatalf("valid entries not shown: %+v", cat.Entries)
	}
	if len(cat.Issues) == 0 || !strings.Contains(cat.Issues[0].Reason, "unknown field") {
		t.Fatalf("issue reason missing: %+v", cat.Issues)
	}
	raw, err := json.Marshal(cat)
	if err != nil {
		t.Fatal(err)
	}
	var top map[string]json.RawMessage
	if err := json.Unmarshal(raw, &top); err != nil {
		t.Fatal(err)
	}
	if _, ok := top["entries"]; !ok {
		t.Fatalf("DTO lost entries key: %s", raw)
	}
}

type serviceProbe struct {
	got chan market.Service
}

func (p *serviceProbe) Name() string { return "market-service-probe" }
func (p *serviceProbe) Inject() []runtime.Dependency {
	return []runtime.Dependency{runtime.Requires(market.ServiceKey)}
}
func (p *serviceProbe) Provide() []runtime.Capability { return nil }
func (p *serviceProbe) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	svc, err := runtime.Require(ctx, market.ServiceKey)
	if err != nil {
		return nil, err
	}
	p.got <- svc
	return nil, nil
}

func ownedRowByID(t *testing.T, ctrl *config.Controller, id string) config.OwnedComponent {
	t.Helper()
	for _, o := range ctrl.Owned() {
		if o.ID == id {
			return o
		}
	}
	t.Fatalf("row %q not owned", id)
	return config.OwnedComponent{}
}

func hasOwned(t *testing.T, ctrl *config.Controller, id string) bool {
	t.Helper()
	for _, o := range ctrl.Owned() {
		if o.ID == id {
			return true
		}
	}
	return false
}

func contains(list []string, want string) bool {
	for _, s := range list {
		if s == want {
			return true
		}
	}
	return false
}
