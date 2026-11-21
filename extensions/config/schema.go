package config

import (
	"encoding/json"
	"errors"
	"sync"

	jsonschema "github.com/santhosh-tekuri/jsonschema/v5"
)

// Schema is the JSON-Schema subset used to describe a component's Config map.
//
// It is deliberately a subset: primitive types (string/integer/boolean/number),
// object (Properties/Required), array (Items), Enum, Default and the
// if/then/else conditionals. It is a DECLARATIVE annotation only — attaching a
// Schema to a factory never changes how the Runtime resolves dependencies or
// drives lifecycles; it only adds validation at Reconcile time.
//
// The same struct is emitted as JSON Schema (JSONSchema) and is intended to be
// read back by the Console "config/schema" query. TOML tags are provided so an
// application MAY store a Schema in its declaration store, but the authoritative
// wire form is JSON Schema.
type Schema struct {
	Type        string             `json:"type,omitempty" toml:"type,omitempty"`
	Title       string             `json:"title,omitempty" toml:"title,omitempty"`
	Description string             `json:"description,omitempty" toml:"description,omitempty"`
	Properties  map[string]*Schema `json:"properties,omitempty" toml:"properties,omitempty"`
	Required    []string           `json:"required,omitempty" toml:"required,omitempty"`
	Items       *Schema            `json:"items,omitempty" toml:"items,omitempty"`
	Enum        []any              `json:"enum,omitempty" toml:"enum,omitempty"`
	Default     any                `json:"default,omitempty" toml:"default,omitempty"`
	If          *Schema            `json:"if,omitempty" toml:"if,omitempty"`
	Then        *Schema            `json:"then,omitempty" toml:"then,omitempty"`
	Else        *Schema            `json:"else,omitempty" toml:"else,omitempty"`
}

// schemaDocument injects the dialect marker without adding a public field to
// Schema. The embedded pointer inlines every Schema field into the emitted
// object, so the wire form is a plain JSON Schema document.
type schemaDocument struct {
	Draft string `json:"$schema"`
	*Schema
}

const schemaDraft = "https://json-schema.org/draft/2020-12/schema"

// ErrNilSchema is returned when a nil *Schema is used.
var ErrNilSchema = errors.New("nil config schema")

// JSONSchema renders s as a self-contained JSON Schema (draft 2020-12)
// document. The result compiles directly with jsonschema.CompileString.
func (s *Schema) JSONSchema() ([]byte, error) {
	if s == nil {
		return nil, ErrNilSchema
	}
	return json.Marshal(schemaDocument{Draft: schemaDraft, Schema: s})
}

// compiledSchema caches one compiled validator per distinct schema document.
// Keying by the rendered bytes (rather than by pointer) means Schema values may
// be copied freely and identical schemas share a single compilation.
type compiledSchema struct {
	schema *jsonschema.Schema
	err    error
}

var compiledSchemas sync.Map // map[string]compiledSchema

// Validate checks value against s. The JSON Schema is compiled once per
// distinct schema document and reused; validation itself is delegated entirely
// to github.com/santhosh-tekuri/jsonschema/v5 (no hand-rolled checking).
//
// A Schema is expected to be immutable after first use; mutating it changes its
// rendered bytes and therefore compiles a new validator rather than reusing the
// previous one.
func (s *Schema) Validate(value map[string]any) error {
	raw, err := s.JSONSchema()
	if err != nil {
		return err
	}
	key := string(raw)
	entry, _ := compiledSchemas.LoadOrStore(key, compileSchema(raw))
	cs := entry.(compiledSchema)
	if cs.err != nil {
		return cs.err
	}
	return cs.schema.Validate(value)
}

func compileSchema(raw []byte) compiledSchema {
	sch, err := jsonschema.CompileString("config-schema.json", string(raw))
	return compiledSchema{schema: sch, err: err}
}
