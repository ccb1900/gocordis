package config_test

import (
	"context"
	"encoding/json"
	"errors"
	"testing"

	"dynamic-runtime/extensions/config"
	"dynamic-runtime/runtime"

	jsonschema "github.com/santhosh-tekuri/jsonschema/v5"
)

// ---------------------------------------------------------------------------
// 1. Generated JSON Schema compiles and validates the required subset.
// ---------------------------------------------------------------------------

func complexSchema() *config.Schema {
	return &config.Schema{
		Type:        "object",
		Title:       "worker",
		Description: "a nested worker config",
		Required:    []string{"mode"},
		Properties: map[string]*config.Schema{
			"mode":    {Type: "string", Enum: []any{"basic", "advanced"}},
			"enabled": {Type: "boolean", Default: true},
			"ratio":   {Type: "number"},
			"tags":    {Type: "array", Items: &config.Schema{Type: "string"}},
			"nested": {
				Type:     "object",
				Required: []string{"name"},
				Properties: map[string]*config.Schema{
					"name": {Type: "string"},
					"size": {Type: "integer"},
				},
			},
		},
		If: &config.Schema{
			Required:   []string{"mode"},
			Properties: map[string]*config.Schema{"mode": {Enum: []any{"advanced"}}},
		},
		Then: &config.Schema{Required: []string{"retries"}},
		Else: &config.Schema{Required: []string{"note"}},
	}
}

func TestSchemaJSONSchemaCompiles(t *testing.T) {
	raw, err := complexSchema().JSONSchema()
	if err != nil {
		t.Fatalf("JSONSchema: %v", err)
	}
	var doc map[string]any
	if err := json.Unmarshal(raw, &doc); err != nil {
		t.Fatalf("generated JSON is invalid: %v", err)
	}
	if doc["$schema"] != "https://json-schema.org/draft/2020-12/schema" {
		t.Fatalf("$schema = %v", doc["$schema"])
	}
	if doc["type"] != "object" || doc["title"] != "worker" {
		t.Fatalf("unexpected root keywords: %v", doc)
	}
	props, ok := doc["properties"].(map[string]any)
	if !ok || props["tags"] == nil || props["nested"] == nil {
		t.Fatalf("properties not rendered: %v", doc["properties"])
	}
	if doc["if"] == nil || doc["then"] == nil || doc["else"] == nil {
		t.Fatalf("conditionals not rendered: %v", doc)
	}
	if _, err := jsonschema.CompileString("schema.json", string(raw)); err != nil {
		t.Fatalf("generated schema does not compile: %v", err)
	}

	if _, err := (*config.Schema)(nil).JSONSchema(); !errors.Is(err, config.ErrNilSchema) {
		t.Fatalf("nil JSONSchema error = %v, want ErrNilSchema", err)
	}
}

func TestSchemaValidateAcceptsRejects(t *testing.T) {
	s := complexSchema()
	cases := []struct {
		name    string
		value   map[string]any
		wantErr bool
	}{
		{"valid basic", map[string]any{"mode": "basic", "note": "ok", "enabled": true, "ratio": 1.5, "tags": []any{"a", "b"}, "nested": map[string]any{"name": "n", "size": 2}}, false},
		{"valid advanced", map[string]any{"mode": "advanced", "retries": 3, "nested": map[string]any{"name": "n"}}, false},
		{"missing required mode", map[string]any{"note": "x"}, true},
		{"bad enum", map[string]any{"mode": "other", "note": "x"}, true},
		{"wrong primitive type", map[string]any{"mode": "basic", "note": "x", "enabled": "yes"}, true},
		{"wrong array item type", map[string]any{"mode": "basic", "note": "x", "tags": []any{"a", 7}}, true},
		{"nested missing required", map[string]any{"mode": "basic", "note": "x", "nested": map[string]any{"size": 2}}, true},
		{"nested wrong type", map[string]any{"mode": "basic", "note": "x", "nested": map[string]any{"name": 7}}, true},
		{"then branch missing retries", map[string]any{"mode": "advanced"}, true},
		{"else branch missing note", map[string]any{"mode": "basic"}, true},
	}
	for _, tc := range cases {
		t.Run(tc.name, func(t *testing.T) {
			err := s.Validate(tc.value)
			if (err != nil) != tc.wantErr {
				t.Fatalf("Validate(%v) error = %v, wantErr %v", tc.value, err, tc.wantErr)
			}
		})
	}
}

// ---------------------------------------------------------------------------
// 2. Additive factory attachment + Controller.Schemas aggregation.
// ---------------------------------------------------------------------------

type schemaFactory struct {
	schema *config.Schema
	calls  int
}

func (f *schemaFactory) Create(cc config.ComponentConfig) (runtime.Component, error) {
	f.calls++
	return &testComponent{name: cc.ID}, nil
}

func (f *schemaFactory) ConfigSchema() *config.Schema { return f.schema }

// minimalRegistry implements FactoryRegistry WITHOUT All(), exercising the
// optional-interface fallback (a custom registry must keep compiling).
type minimalRegistry struct{ f map[string]config.Factory }

func (r *minimalRegistry) Register(t string, f config.Factory) error {
	if r.f == nil {
		r.f = map[string]config.Factory{}
	}
	r.f[t] = f
	return nil
}
func (r *minimalRegistry) Lookup(t string) (config.Factory, bool) {
	f, ok := r.f[t]
	return f, ok
}

func TestControllerSchemasAggregation(t *testing.T) {
	e := newEnv(t)
	withSchema := &schemaFactory{schema: &config.Schema{Type: "object", Required: []string{"batch"}}}
	noSchema := &schemaFactory{schema: nil}
	if err := e.reg.Register("with-schema", withSchema); err != nil {
		t.Fatal(err)
	}
	if err := e.reg.Register("no-schema", noSchema); err != nil {
		t.Fatal(err)
	}
	if err := e.reg.Register("legacy", config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) {
		return &testComponent{name: "legacy"}, nil
	})); err != nil {
		t.Fatal(err)
	}

	got := e.ctrl.Schemas()
	if len(got) != 1 || got["with-schema"] != withSchema.schema {
		t.Fatalf("Schemas() = %v, want only with-schema", got)
	}

	// Returned map is a snapshot.
	delete(got, "with-schema")
	if e.ctrl.Schemas()["with-schema"] == nil {
		t.Fatal("mutating returned map affected controller")
	}

	// A registry without All() yields an empty aggregation, not a panic.
	rt, err := runtime.New()
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { _ = rt.Close(context.Background()) })
	custom := config.NewController(rt, &minimalRegistry{})
	defer custom.Close()
	if s := custom.Schemas(); len(s) != 0 {
		t.Fatalf("Schemas() on custom registry = %v, want empty", s)
	}
}

func TestControllerDeclarations(t *testing.T) {
	e := newEnv(t)
	key := runtime.NewKey[string]("declarations.value")
	provider := &testComponent{
		name:    "provider",
		provide: []runtime.Capability{key.Capability()},
	}
	consumer := &testComponent{
		name:   "consumer",
		inject: []runtime.Dependency{runtime.Requires(key)},
	}
	if err := e.reg.Register("provider", componentFactory(provider)); err != nil {
		t.Fatal(err)
	}
	if err := e.reg.Register("consumer", componentFactory(consumer)); err != nil {
		t.Fatal(err)
	}
	disabled := false
	err := e.ctrl.Reconcile(ctxT(t), config.Config{Components: []config.ComponentConfig{
		{ID: "c", Type: "consumer"},
		{ID: "p", Type: "provider"},
		{ID: "off", Type: "provider", Enabled: &disabled},
	}})
	if err != nil {
		t.Fatalf("Reconcile: %v", err)
	}
	decls := e.ctrl.Declarations()
	if len(decls) != 3 {
		t.Fatalf("Declarations() len = %d, want 3: %+v", len(decls), decls)
	}
	byID := map[string]config.Declaration{}
	for _, d := range decls {
		byID[d.ID] = d
	}
	if decls[0].ID != "c" || decls[1].ID != "off" || decls[2].ID != "p" {
		t.Fatalf("Declarations() not sorted by ID: %+v", decls)
	}
	if byID["c"].Name != "consumer" || len(byID["c"].Requires) != 1 || byID["c"].Requires[0] != key.Capability().String() {
		t.Fatalf("consumer declaration = %+v", byID["c"])
	}
	if len(byID["p"].Provides) != 1 || byID["p"].Provides[0] != key.Capability().String() {
		t.Fatalf("provider declaration = %+v", byID["p"])
	}
	if byID["off"].Enabled || byID["off"].Name != "provider" || len(byID["off"].Provides) != 1 {
		t.Fatalf("disabled declaration = %+v", byID["off"])
	}
}

func componentFactory(c runtime.Component) config.Factory {
	return config.FactoryFunc(func(config.ComponentConfig) (runtime.Component, error) { return c, nil })
}

// ---------------------------------------------------------------------------
// 3. Reconcile validates ONLY schema-declaring factories.
// ---------------------------------------------------------------------------

func TestReconcileSchemaValidation(t *testing.T) {
	e := newEnv(t)
	schema := &config.Schema{
		Type:     "object",
		Required: []string{"batch"},
		Properties: map[string]*config.Schema{
			"batch": {Type: "integer"},
			"mode":  {Type: "string", Enum: []any{"fast", "slow"}},
		},
	}
	validated := &schemaFactory{schema: schema}
	if err := e.reg.Register("validated", validated); err != nil {
		t.Fatal(err)
	}
	legacyCalls := 0
	if err := e.reg.Register("legacy", config.FactoryFunc(func(cc config.ComponentConfig) (runtime.Component, error) {
		legacyCalls++
		return &testComponent{name: cc.ID}, nil
	})); err != nil {
		t.Fatal(err)
	}

	valid := config.Config{Components: []config.ComponentConfig{{ID: "v", Type: "validated", Config: map[string]any{"batch": 8, "mode": "fast"}}}}
	if err := e.ctrl.Reconcile(ctxT(t), valid); err != nil {
		t.Fatalf("valid schema reconcile: %v", err)
	}
	if validated.calls != 1 {
		t.Fatalf("factory calls = %d, want 1", validated.calls)
	}

	for _, bad := range []map[string]any{
		{"mode": "fast"},                // missing required
		{"batch": "eight"},              // wrong type
		{"batch": 8, "mode": "unknown"}, // enum
	} {
		invalid := config.Config{Components: []config.ComponentConfig{{ID: "v", Type: "validated", Config: bad}}}
		err := e.ctrl.Reconcile(ctxT(t), invalid)
		if !errors.Is(err, config.ErrSchemaValidation) || !errors.Is(err, config.ErrInvalidConfig) {
			t.Fatalf("Reconcile(%v) error = %v, want schema validation error", bad, err)
		}
	}
	if validated.calls != 1 {
		t.Fatalf("invalid configs reached factory (calls = %d)", validated.calls)
	}
	if _, ok := ownedByID(t, e.ctrl, "v"); !ok {
		t.Fatal("invalid config mutated applied state")
	}

	// Legacy factory accepts arbitrary maps, including values no schema allows.
	legacy := config.Config{Components: []config.ComponentConfig{{ID: "l", Type: "legacy", Config: map[string]any{"anything": []any{1, "x"}}}}}
	if err := e.ctrl.Reconcile(ctxT(t), legacy); err != nil {
		t.Fatalf("legacy reconcile: %v", err)
	}
	if legacyCalls != 1 {
		t.Fatalf("legacy factory calls = %d, want 1", legacyCalls)
	}
}
