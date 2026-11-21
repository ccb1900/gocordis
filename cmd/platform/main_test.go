package main

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/insight"
	consoRuntime "dynamic-runtime/extensions/console/runtime"
	"dynamic-runtime/extensions/market"
	"dynamic-runtime/runtime"
)

// testCatalog writes a small local catalog: one installable WASM entry and one
// native (build-time) entry. No network or listener is involved.
func testCatalog(t *testing.T) string {
	t.Helper()
	dir := filepath.Join(t.TempDir(), "catalog")
	artifact := filepath.Join(dir, "alpha", "camera.wasm")
	write(t, artifact, "demo wasm bytes")
	sum := sha256.Sum256([]byte("demo wasm bytes"))
	write(t, filepath.Join(dir, "alpha", "plugin.toml"), fmt.Sprintf(`id = "alpha"
name = "Alpha camera"
version = "1.0.0"
description = "demo wasm plugin"
backend = "wasm"
module_type = "industrial.camera"
[compatibility]
runtime = ">=1.0.0"
api = ">=1.0.0"
[source]
kind = "wasm"
location = "camera.wasm"
checksum = %q
`, hex.EncodeToString(sum[:])))
	write(t, filepath.Join(dir, "builtin", "plugin.toml"), `id = "builtin-camera"
name = "Builtin camera"
version = "1.0.0"
backend = "native"
module_type = "industrial.camera"
[compatibility]
runtime = ">=1.0.0"
`)
	return dir
}

func write(t *testing.T, path, body string) {
	t.Helper()
	if err := os.MkdirAll(filepath.Dir(path), 0o755); err != nil {
		t.Fatal(err)
	}
	if err := os.WriteFile(path, []byte(body), 0o644); err != nil {
		t.Fatal(err)
	}
}

func newTestApp(t *testing.T) *app {
	t.Helper()
	t.Setenv("GOCORDIS_CATALOG_DIR", testCatalog(t))
	a, err := newApp("manifest.toml")
	if err != nil {
		t.Fatalf("newApp: %v", err)
	}
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = a.shutdown(ctx)
	})
	return a
}

func readyApp(t *testing.T) (*app, context.Context) {
	t.Helper()
	a := newTestApp(t)
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	t.Cleanup(cancel)
	if err := a.reconcileManifest(ctx); err != nil {
		t.Fatalf("reconcileManifest: %v", err)
	}
	return a, ctx
}

func ownedFiber(t *testing.T, a *app, id string) *runtime.Fiber {
	t.Helper()
	o, ok := a.owned(id)
	if !ok {
		t.Fatalf("component %q not owned", id)
	}
	return o.Fiber
}

func TestReconcileManifestActivatesAllComponents(t *testing.T) {
	a, ctx := readyApp(t)
	for _, id := range []string{"host", "plugins", "insight", "runtime-console", "market", "devtools", "demo"} {
		f := ownedFiber(t, a, id)
		if f == nil {
			t.Fatalf("component %q has no fiber", id)
		}
		if err := f.Ready(ctx); err != nil {
			t.Fatalf("component %q not Active: %v", id, err)
		}
		if got := f.State(); got != runtime.StateActive {
			t.Fatalf("component %q state = %s, want Active", id, got)
		}
	}
}

func TestConfigSchemaQueryViaRealHub(t *testing.T) {
	a, ctx := readyApp(t)
	res, ue := a.hostUI().HostAdapter().Query(insight.SchemasQuery, nil)
	if ue != nil {
		t.Fatalf("config/schema query: %+v", ue)
	}
	list, ok := res.(insight.SchemaList)
	if !ok {
		t.Fatalf("config/schema result type = %T, want insight.SchemaList", res)
	}
	var found bool
	for _, e := range list.Components {
		if e.Type != demoSettingsType {
			continue
		}
		found = true
		var doc struct {
			Properties map[string]struct {
				Type  string `json:"type"`
				Enum  []any  `json:"enum"`
				Items *struct {
					Type string `json:"type"`
				} `json:"items"`
			} `json:"properties"`
			Required []string `json:"required"`
		}
		if err := json.Unmarshal(e.Schema, &doc); err != nil {
			t.Fatalf("schema JSON: %v (%s)", err, e.Schema)
		}
		if doc.Properties["mode"].Type != "string" || len(doc.Properties["mode"].Enum) != 3 {
			t.Fatalf("mode schema = %+v, want string enum", doc.Properties["mode"])
		}
		if doc.Properties["enabled"].Type != "boolean" {
			t.Fatalf("enabled schema = %+v, want boolean", doc.Properties["enabled"])
		}
		if doc.Properties["tags"].Type != "array" || doc.Properties["tags"].Items == nil ||
			doc.Properties["tags"].Items.Type != "string" {
			t.Fatalf("tags schema = %+v, want array of string", doc.Properties["tags"])
		}
		if len(doc.Required) == 0 || doc.Required[0] != "mode" {
			t.Fatalf("required = %v, want [mode]", doc.Required)
		}
	}
	if !found {
		t.Fatalf("config/schema missing %q: %+v", demoSettingsType, list.Components)
	}

	// A real SchemaFactory also validates at Reconcile time.
	bad := config.ComponentConfig{ID: "bad-demo", Type: demoSettingsType,
		Config: map[string]any{"mode": "not-a-mode"}}
	err := a.ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{bad}})
	if err == nil || !strings.Contains(err.Error(), config.ErrSchemaValidation.Error()) {
		t.Fatalf("invalid demo config error = %v, want schema validation failure", err)
	}
}

func TestInsightGraphQueryUsesDeclaredAndRuntimeGraphs(t *testing.T) {
	a, ctx := readyApp(t)
	res, ue := a.hostUI().HostAdapter().Query(insight.GraphQuery, nil)
	if ue != nil {
		t.Fatalf("insight/graph query: %+v", ue)
	}
	g, ok := res.(insight.Graph)
	if !ok {
		t.Fatalf("insight/graph result type = %T, want insight.Graph", res)
	}
	declared := map[string]insight.DeclaredNode{}
	for _, n := range g.Declared.Nodes {
		declared[n.ID] = n
	}
	for _, id := range []string{"host", "plugins", "insight", "runtime-console", "market", "devtools", "demo"} {
		if _, ok := declared[id]; !ok {
			t.Fatalf("declared graph missing %q: %+v", id, g.Declared.Nodes)
		}
	}
	hubKey := host.HubKey.Capability().String()
	var declaredEdge bool
	for _, e := range g.Declared.Edges {
		if e.From == "insight" && e.To == "host" && e.Key == hubKey {
			declaredEdge = true
		}
	}
	if !declaredEdge {
		t.Fatalf("declared graph missing insight->host on %s: %+v", hubKey, g.Declared.Edges)
	}

	if g.EventSequence == 0 {
		t.Fatal("graph event sequence must come from the real runtime")
	}
	if len(g.Runtime.Nodes) < 5 {
		t.Fatalf("runtime graph nodes = %+v, want at least five fibers", g.Runtime.Nodes)
	}
	var runtimeEdge bool
	for _, n := range g.Runtime.Nodes {
		if n.State != runtime.StateActive.String() {
			t.Fatalf("runtime node %s state = %s, want Active", n.Name, n.State)
		}
	}
	for _, e := range g.Runtime.Edges {
		if e.Key == hubKey && e.Status == string(runtime.DependencySatisfied) && e.ProviderFiberID != 0 {
			runtimeEdge = true
		}
	}
	if !runtimeEdge {
		t.Fatalf("runtime graph missing satisfied hub binding: %+v", g.Runtime.Edges)
	}
	_ = ctx
}

func TestMarketCatalogQueryAndRowActions(t *testing.T) {
	a, ctx := readyApp(t)
	res, ue := a.hostUI().HostAdapter().Query(market.CatalogQuery, nil)
	if ue != nil {
		t.Fatalf("market/catalog query: %+v", ue)
	}
	cat, ok := res.(market.Catalog)
	if !ok {
		t.Fatalf("catalog result type = %T, want market.Catalog", res)
	}
	ids := map[string]bool{}
	for _, e := range cat.Entries {
		ids[e.ID] = true
	}
	if !ids["alpha"] || !ids["builtin-camera"] {
		t.Fatalf("catalog entries = %+v, want alpha + builtin-camera", cat.Entries)
	}

	var page *host.PageDefinition
	for i := range a.hostUI().Pages() {
		p := a.hostUI().Pages()[i]
		if p.ID == market.PageID {
			page = &p
		}
	}
	if page == nil {
		t.Fatal("market page not registered")
	}
	var blocks []struct {
		Kind       string `json:"kind"`
		Query      string `json:"query"`
		RowActions []struct {
			Label   string            `json:"label"`
			Command string            `json:"command"`
			Args    map[string]string `json:"args"`
		} `json:"rowActions"`
	}
	if err := json.Unmarshal(page.Views, &blocks); err != nil {
		t.Fatalf("market page views = %s: %v", page.Views, err)
	}
	actions := map[string]bool{}
	for _, b := range blocks {
		if b.Kind != "market-catalog" || b.Query != market.CatalogQuery {
			continue
		}
		for _, action := range b.RowActions {
			actions[action.Command] = true
			if got := action.Args["id"]; got != "$row.id" {
				t.Fatalf("action %s args = %v, want id=$row.id", action.Command, action.Args)
			}
		}
	}
	for _, cmd := range []string{market.InstallCommand, market.UpgradeCommand, market.UninstallCommand} {
		if !actions[cmd] {
			t.Fatalf("rowActions missing %q: %s", cmd, page.Views)
		}
	}
	_ = ctx
}

func TestMarketMutationsPreserveForeignDeclarations(t *testing.T) {
	a := newTestApp(t)
	// A real app registers loader-backed types; the demo root only needs this
	// stand-in so an installed catalog row can reach Active.
	if err := a.reg.Register("industrial.camera", config.FactoryFunc(
		func(cc config.ComponentConfig) (runtime.Component, error) {
			return &markerComponent{id: cc.ID}, nil
		})); err != nil {
		t.Fatal(err)
	}
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	if err := a.reconcileManifest(ctx); err != nil {
		t.Fatalf("reconcileManifest: %v", err)
	}
	before := map[string]*runtime.Fiber{}
	for _, id := range []string{"host", "plugins", "insight", "runtime-console", "market", "devtools", "demo"} {
		before[id] = ownedFiber(t, a, id)
	}

	h := a.hostUI().HostAdapter()
	if ue := h.Command(market.InstallCommand, json.RawMessage(`{"id":"alpha"}`)); ue != nil {
		t.Fatalf("market/install: %+v", ue)
	}
	if _, ok := a.owned("alpha"); !ok {
		t.Fatal("installed declaration missing")
	}
	if ue := h.Command(market.UninstallCommand, json.RawMessage(`{"id":"alpha"}`)); ue != nil {
		t.Fatalf("market/uninstall: %+v", ue)
	}
	if _, ok := a.owned("alpha"); ok {
		t.Fatal("uninstalled declaration survived")
	}
	for id, f := range before {
		now := ownedFiber(t, a, id)
		if now != f {
			t.Fatalf("foreign declaration %q fiber churned on market mutation", id)
		}
		if err := now.Ready(ctx); err != nil {
			t.Fatalf("foreign declaration %q not Active after mutation: %v", id, err)
		}
	}
}

func TestWebUIAPIWithoutListener(t *testing.T) {
	a, _ := readyApp(t)
	srv := a.webUI(nil)
	for _, path := range []string{
		"/api/ui/pages",
		"/api/query/" + insight.SchemasQuery,
		"/api/query/" + insight.GraphQuery,
		"/api/query/" + market.CatalogQuery,
	} {
		rec := httptest.NewRecorder()
		srv.ServeHTTP(rec, httptest.NewRequest(http.MethodGet, path, nil))
		if rec.Code != http.StatusOK {
			t.Fatalf("GET %s = %d: %s", path, rec.Code, rec.Body.String())
		}
		if !strings.Contains(rec.Body.String(), `"data"`) {
			t.Fatalf("GET %s body = %s, want data envelope", path, rec.Body.String())
		}
	}
}

func TestShutdownLeavesAllFibersGone(t *testing.T) {
	a, ctx := readyApp(t)
	var fibers []*runtime.Fiber
	for _, id := range []string{"host", "plugins", "insight", "runtime-console", "market", "devtools", "demo"} {
		fibers = append(fibers, ownedFiber(t, a, id))
	}
	if err := a.shutdown(ctx); err != nil {
		t.Fatalf("shutdown: %v", err)
	}
	for i, f := range fibers {
		if err := f.Gone(ctx); err != nil {
			t.Fatalf("fiber %d not Gone after shutdown: %v", i, err)
		}
		if got := f.State(); got != runtime.StateGone {
			t.Fatalf("fiber %d state = %s, want Gone", i, got)
		}
	}
}

func TestDefaultManifestResolvesFromRepoRootAndPackageDir(t *testing.T) {
	path := defaultManifestPath()
	if _, err := os.Stat(path); err != nil {
		t.Fatalf("default manifest %q: %v", path, err)
	}
	if !strings.HasSuffix(filepath.ToSlash(path), "cmd/platform/manifest.toml") {
		t.Fatalf("default manifest = %q, want cmd/platform/manifest.toml", path)
	}
}

type markerComponent struct{ id string }

func (m *markerComponent) Name() string                  { return "industrial.camera:" + m.id }
func (m *markerComponent) Inject() []runtime.Dependency  { return nil }
func (m *markerComponent) Provide() []runtime.Capability { return nil }
func (m *markerComponent) Apply(*runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}

func TestRuntimeConsoleQueriesAndPagesViaRealPlatform(t *testing.T) {
	a, ctx := readyApp(t)
	hub := a.hostUI().HostAdapter()

	res, ue := hub.Query(consoRuntime.SnapshotQuery, nil)
	if ue != nil {
		t.Fatalf("runtime/snapshot query: %+v", ue)
	}
	snap, ok := res.(consoRuntime.SnapshotDTO)
	if !ok {
		t.Fatalf("runtime/snapshot result type = %T", res)
	}
	if snap.Sequence == 0 || len(snap.Fibers) == 0 || len(snap.Providers) == 0 {
		t.Fatalf("snapshot DTO incomplete: %+v", snap)
	}

	res, ue = hub.Query(consoRuntime.EventsQuery, nil)
	if ue != nil {
		t.Fatalf("runtime/events query: %+v", ue)
	}
	events, ok := res.(consoRuntime.EventsDTO)
	if !ok || len(events.Events) == 0 {
		t.Fatalf("runtime/events result = %T %+v", res, res)
	}

	res, ue = hub.Query(consoRuntime.DashboardQuery, nil)
	if ue != nil {
		t.Fatalf("framework/dashboard query: %+v", ue)
	}
	dashboard, ok := res.(consoRuntime.DashboardDTO)
	if !ok || dashboard.Counts.Fibers == 0 || len(dashboard.RecentEvents) == 0 {
		t.Fatalf("dashboard result = %T %+v", res, res)
	}
	_ = ctx

	pages := map[string]bool{}
	for _, p := range a.hostUI().Pages() {
		pages[p.ID] = true
	}
	for _, id := range []string{"plugins", "inspector", "dashboard", "insight", "market"} {
		if !pages[id] {
			t.Fatalf("page %q missing from platform composition: %v", id, pages)
		}
	}
}
