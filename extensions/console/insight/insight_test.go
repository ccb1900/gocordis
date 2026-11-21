package insight_test

import (
	"context"
	"encoding/json"
	"testing"
	"time"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/extensions/console/host"
	"dynamic-runtime/extensions/console/insight"
	"dynamic-runtime/runtime"

	jsonschema "github.com/santhosh-tekuri/jsonschema/v5"
)

type valueComp struct {
	name    string
	key     runtime.Key[string]
	provide bool
	require bool
}

func (c *valueComp) Name() string { return c.name }
func (c *valueComp) Inject() []runtime.Dependency {
	if c.require {
		return []runtime.Dependency{runtime.Requires(c.key)}
	}
	return nil
}
func (c *valueComp) Provide() []runtime.Capability {
	if c.provide {
		return []runtime.Capability{c.key.Capability()}
	}
	return nil
}
func (c *valueComp) Apply(ctx *runtime.Context) (runtime.Cleanup, error) {
	if c.require {
		if _, err := runtime.Require(ctx, c.key); err != nil {
			return nil, err
		}
	}
	if c.provide {
		if err := runtime.Provide(ctx, c.key, "value"); err != nil {
			return nil, err
		}
	}
	return nil, nil
}

type settingComp struct{}

func (settingComp) Name() string                  { return "settings" }
func (settingComp) Inject() []runtime.Dependency  { return nil }
func (settingComp) Provide() []runtime.Capability { return nil }
func (settingComp) Apply(*runtime.Context) (runtime.Cleanup, error) {
	return nil, nil
}

type schemaFactory struct {
	schema *config.Schema
	build  func(config.ComponentConfig) (runtime.Component, error)
}

func (f *schemaFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	return f.build(cc)
}
func (f *schemaFactory) ConfigSchema() *config.Schema { return f.schema }

type stack struct {
	rt      *runtime.Runtime
	console *host.UIComponent
	h       *host.Host
	ctrl    *config.Controller
	service *insight.Service

	valueKey   runtime.Key[string]
	missingKey runtime.Key[string]
}

func newStack(t *testing.T) *stack {
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

	console, err := host.NewConsole(config.ComponentConfig{ID: "console"})
	if err != nil {
		t.Fatal(err)
	}
	hostFiber, err := rt.Load(console)
	if err != nil {
		t.Fatal(err)
	}
	waitState(t, hostFiber, runtime.StateActive)

	reg := config.NewFactoryRegistry()
	ctrl := config.NewController(rt, reg)
	t.Cleanup(func() {
		ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
		defer cancel()
		_ = ctrl.CloseContext(ctx)
	})
	service := insight.New(ctrl, rt)

	s := &stack{
		rt: rt, console: console, h: console.HostAdapter(), ctrl: ctrl, service: service,
		valueKey:   runtime.NewKey[string]("insight.value"),
		missingKey: runtime.NewKey[string]("insight.missing"),
	}

	mustRegister(t, reg, "provider", config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &valueComp{name: "provider", key: s.valueKey, provide: true}, nil
	}))
	mustRegister(t, reg, "consumer", config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &valueComp{name: "consumer", key: s.valueKey, require: true}, nil
	}))
	mustRegister(t, reg, "waiting", config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &valueComp{name: "waiting", key: s.missingKey, require: true}, nil
	}))
	mustRegister(t, reg, "settings", &schemaFactory{
		schema: &config.Schema{Type: "object", Required: []string{"level"}, Properties: map[string]*config.Schema{
			"level": {Type: "integer"},
		}},
		build: func(config.ComponentConfig) (runtime.Component, error) { return settingComp{}, nil },
	})
	mustRegister(t, reg, "insight", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		return insight.NewComponent(cc, service)
	}))

	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	if err := ctrl.Reconcile(ctx, config.Config{Components: []config.ComponentConfig{
		{ID: "p", Type: "provider"},
		{ID: "c", Type: "consumer"},
		{ID: "w", Type: "waiting"},
		{ID: "settings", Type: "settings", Config: map[string]any{"level": 3}},
		{ID: "insight", Type: "insight"},
	}}); err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	for _, id := range []string{"p", "c", "settings", "insight"} {
		f := ownedFiber(t, ctrl, id)
		waitState(t, f, runtime.StateActive)
	}
	waitState(t, ownedFiber(t, ctrl, "w"), runtime.StatePending)
	return s
}

func mustRegister(t *testing.T, reg config.FactoryRegistry, typ string, f config.Factory) {
	t.Helper()
	if err := reg.Register(typ, f); err != nil {
		t.Fatalf("Register(%q): %v", typ, err)
	}
}

func ownedFiber(t *testing.T, ctrl *config.Controller, id string) *runtime.Fiber {
	t.Helper()
	for _, o := range ctrl.Owned() {
		if o.ID == id {
			if o.Fiber == nil {
				t.Fatalf("component %q has no fiber", id)
			}
			return o.Fiber
		}
	}
	t.Fatalf("component %q not owned", id)
	return nil
}

func waitState(t *testing.T, f *runtime.Fiber, want runtime.FiberState) {
	t.Helper()
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if f.State() == want {
			return
		}
		time.Sleep(2 * time.Millisecond)
	}
	t.Fatalf("timeout waiting for %v (state %v err %v)", want, f.State(), f.Err())
}

func queryGraph(t *testing.T, s *stack) insight.Graph {
	t.Helper()
	res, ue := s.h.Query(insight.GraphQuery, nil)
	if ue != nil {
		t.Fatalf("query %s: %+v", insight.GraphQuery, ue)
	}
	g, ok := res.(insight.Graph)
	if !ok {
		t.Fatalf("query result type = %T, want insight.Graph", res)
	}
	return g
}

// ---------------------------------------------------------------------------
// Declared vs runtime shapes, provider binding and Pending status.
// ---------------------------------------------------------------------------

func TestInsightGraphQuery(t *testing.T) {
	s := newStack(t)
	g := queryGraph(t, s)
	if g.EventSequence == 0 {
		t.Fatal("EventSequence = 0, want a snapshot version anchor")
	}

	// Declared side comes from the Config Controller only.
	declNodes := map[string]insight.DeclaredNode{}
	for _, n := range g.Declared.Nodes {
		declNodes[n.ID] = n
	}
	if len(declNodes) != 5 {
		t.Fatalf("declared nodes = %d, want 5: %+v", len(declNodes), g.Declared.Nodes)
	}
	key := s.valueKey.Capability().String()
	if n := declNodes["p"]; n.Name != "provider" || n.Type != "provider" || len(n.Provides) != 1 || n.Provides[0] != key {
		t.Fatalf("declared provider = %+v", n)
	}
	if n := declNodes["c"]; n.Name != "consumer" || len(n.Requires) != 1 || n.Requires[0] != key {
		t.Fatalf("declared consumer = %+v", n)
	}
	if n := declNodes["w"]; len(n.Requires) != 1 || n.Requires[0] != s.missingKey.Capability().String() {
		t.Fatalf("declared waiting = %+v", n)
	}
	var declaredEdge bool
	for _, e := range g.Declared.Edges {
		if e.From == "c" && e.To == "p" && e.Key == key {
			declaredEdge = true
		}
	}
	if !declaredEdge {
		t.Fatalf("missing declared edge c->p on %q: %+v", key, g.Declared.Edges)
	}

	// Runtime side comes from Snapshot only: ProviderView, DependencyView,
	// FiberSnapshot with its ScopeID/ActivationID.
	pID, cID, wID := uint64(ownedFiber(t, s.ctrl, "p").ID()), uint64(ownedFiber(t, s.ctrl, "c").ID()), uint64(ownedFiber(t, s.ctrl, "w").ID())
	runNodes := map[uint64]insight.RuntimeNode{}
	for _, n := range g.Runtime.Nodes {
		runNodes[n.FiberID] = n
	}
	if len(runNodes) != 6 { // console host + five configured components
		t.Fatalf("runtime nodes = %d, want 6: %+v", len(runNodes), g.Runtime.Nodes)
	}
	if n := runNodes[pID]; n.Name != "provider" || n.State != "Active" || n.ActivationID == 0 || len(n.Provides) != 1 {
		t.Fatalf("runtime provider node = %+v", n)
	}
	if n := runNodes[cID]; n.Name != "consumer" || n.State != "Active" || len(n.Requires) != 1 {
		t.Fatalf("runtime consumer node = %+v", n)
	}
	if n := runNodes[wID]; n.Name != "waiting" || n.State != "Pending" {
		t.Fatalf("runtime waiting node = %+v", n)
	}
	if _, ok := runNodes[pID]; ok {
		if runNodes[pID].ScopeID != 0 {
			t.Fatalf("root fiber scope = %d, want 0", runNodes[pID].ScopeID)
		}
	}

	var satisfied, waiting bool
	for _, e := range g.Runtime.Edges {
		switch {
		case e.ConsumerFiberID == cID && e.ProviderFiberID == pID && e.Key == key && e.Status == "Satisfied":
			satisfied = true
		case e.ConsumerFiberID == wID && e.ProviderFiberID == 0 && e.Status == "Waiting":
			waiting = true
		}
	}
	if !satisfied || !waiting {
		t.Fatalf("runtime edges missing binding/waiting (satisfied=%v waiting=%v): %+v", satisfied, waiting, g.Runtime.Edges)
	}
}

// ---------------------------------------------------------------------------
// Read-only: a query must not advance the Runtime or mutate any Fiber.
// ---------------------------------------------------------------------------

func TestInsightGraphReadOnly(t *testing.T) {
	s := newStack(t)
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()
	before, err := s.rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	_ = queryGraph(t, s)
	after, err := s.rt.Snapshot(ctx)
	if err != nil {
		t.Fatal(err)
	}
	if before.EventSequence != after.EventSequence {
		t.Fatalf("query advanced event sequence %d -> %d", before.EventSequence, after.EventSequence)
	}
	if len(before.Fibers) != len(after.Fibers) {
		t.Fatalf("query changed fiber count %d -> %d", len(before.Fibers), len(after.Fibers))
	}
	for i := range before.Fibers {
		if before.Fibers[i].State != after.Fibers[i].State || before.Fibers[i].ActivationID != after.Fibers[i].ActivationID {
			t.Fatalf("query mutated fiber %d: %+v -> %+v", before.Fibers[i].ID, before.Fibers[i], after.Fibers[i])
		}
	}
}

// ---------------------------------------------------------------------------
// config/schema query + Console page + no residue on dispose.
// ---------------------------------------------------------------------------

func TestConfigSchemaQuery(t *testing.T) {
	s := newStack(t)
	res, ue := s.h.Query(insight.SchemasQuery, nil)
	if ue != nil {
		t.Fatalf("query %s: %+v", insight.SchemasQuery, ue)
	}
	list, ok := res.(insight.SchemaList)
	if !ok {
		t.Fatalf("result type = %T, want insight.SchemaList", res)
	}
	if len(list.Components) != 1 || list.Components[0].Type != "settings" {
		t.Fatalf("schema entries = %+v, want only settings", list.Components)
	}
	if _, err := jsonschema.CompileString("settings.json", string(list.Components[0].Schema)); err != nil {
		t.Fatalf("returned schema does not compile: %v", err)
	}

	pages, ue := s.h.ListPages()
	if ue != nil {
		t.Fatalf("ListPages: %+v", ue)
	}
	var found bool
	for _, p := range pages.Pages {
		if p.ID == "insight" {
			found = true
			if p.Route != "/insight" || p.Renderer != "insight-graph" {
				t.Fatalf("insight page = %+v", p)
			}
		}
	}
	if !found {
		t.Fatalf("insight page missing: %+v", pages.Pages)
	}
}

func TestInsightDisposeRemovesResidue(t *testing.T) {
	s := newStack(t)
	ctx, cancel := context.WithTimeout(context.Background(), 15*time.Second)
	defer cancel()
	if err := s.ctrl.Reconcile(ctx, config.Config{}); err != nil {
		t.Fatalf("Reconcile(empty): %v", err)
	}
	deadline := time.Now().Add(8 * time.Second)
	for time.Now().Before(deadline) {
		if _, ue := s.h.Query(insight.GraphQuery, nil); ue != nil && ue.Code == "not_found" {
			break
		}
		time.Sleep(2 * time.Millisecond)
	}
	for _, name := range []string{insight.GraphQuery, insight.SchemasQuery} {
		if _, ue := s.h.Query(name, nil); ue == nil || ue.Code != "not_found" {
			t.Fatalf("query %q residue after dispose: %+v", name, ue)
		}
	}
	pages, ue := s.h.ListPages()
	if ue != nil {
		t.Fatalf("ListPages: %+v", ue)
	}
	for _, p := range pages.Pages {
		if p.ID == "insight" {
			t.Fatalf("insight page residue after dispose: %+v", p)
		}
	}
}

// ---------------------------------------------------------------------------
// Exact JSON DTO shape (wire contract for the console renderer).
// ---------------------------------------------------------------------------

func TestGraphJSONShape(t *testing.T) {
	g := insight.Graph{
		EventSequence: 7,
		Declared: insight.DeclaredGraph{
			Nodes: []insight.DeclaredNode{{ID: "p", Name: "provider", Type: "provider", Requires: []string{}, Provides: []string{"key"}}},
			Edges: []insight.DeclaredEdge{{From: "c", To: "p", Key: "key"}},
		},
		Runtime: insight.RuntimeGraph{
			Nodes: []insight.RuntimeNode{{FiberID: 1, Name: "provider", State: "Active", ActivationID: 1, ScopeID: 0, Provides: []string{"key"}, Requires: []string{}}},
			Edges: []insight.RuntimeEdge{{ConsumerFiberID: 2, ProviderFiberID: 1, Key: "key", Status: "Satisfied", Reason: ""}},
		},
	}
	raw, err := json.Marshal(g)
	if err != nil {
		t.Fatal(err)
	}
	var doc map[string]any
	if err := json.Unmarshal(raw, &doc); err != nil {
		t.Fatal(err)
	}
	checkKeys(t, doc, "eventSequence", "declared", "runtime")
	decl := doc["declared"].(map[string]any)
	checkKeys(t, decl, "nodes", "edges")
	checkKeys(t, decl["nodes"].([]any)[0].(map[string]any), "id", "name", "type", "requires", "provides")
	checkKeys(t, decl["edges"].([]any)[0].(map[string]any), "from", "to", "key")
	run := doc["runtime"].(map[string]any)
	checkKeys(t, run, "nodes", "edges")
	checkKeys(t, run["nodes"].([]any)[0].(map[string]any), "fiberId", "name", "state", "activationId", "scopeId", "provides", "requires")
	checkKeys(t, run["edges"].([]any)[0].(map[string]any), "consumerFiberId", "providerFiberId", "key", "status", "reason")
}

func checkKeys(t *testing.T, m map[string]any, keys ...string) {
	t.Helper()
	for _, k := range keys {
		if _, ok := m[k]; !ok {
			t.Fatalf("JSON object missing %q: %+v", k, m)
		}
	}
}
