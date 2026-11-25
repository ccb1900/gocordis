// JSON Schema subset → the FieldSpec model used by ConfigEditor. Pure logic
// only (no React / DOM): the matching SchemaForm component renders the specs
// with the existing antd controls. Missing or partial declarations always
// degrade to the caller's fallback specs — a schema can never break editing.

export interface JSONSchema {
  type?: string | string[];
  title?: string;
  description?: string;
  properties?: Record<string, JSONSchema>;
  required?: string[];
  default?: unknown;
  enum?: unknown[];
  const?: unknown;
  items?: JSONSchema;
  additionalProperties?: boolean | JSONSchema;
  if?: JSONSchema;
  then?: JSONSchema;
  else?: JSONSchema;
}

export interface FieldSpec {
  key: string;
  label: string;
  type?: "text" | "number" | "bool" | "select" | "json";
  options?: string[];
  width?: number | "flex";
  /** Schema-derived metadata (absent on the legacy hardcoded specs). */
  required?: boolean;
  default?: unknown;
  description?: string;
}

export interface ConfigSchemaEntry {
  type: string;
  schema: JSONSchema;
}

/** Payload of GET /api/query/config/schema. */
export interface ConfigSchemaDTO {
  components: ConfigSchemaEntry[];
}

const isObj = (v: unknown): v is Record<string, unknown> =>
  v !== null && typeof v === "object" && !Array.isArray(v);

const asSchema = (v: unknown): JSONSchema | null => (isObj(v) ? (v as JSONSchema) : null);

function primaryType(s: JSONSchema): string {
  const t = s.type;
  if (Array.isArray(t)) return t.find((x) => typeof x === "string" && x !== "null") ?? "";
  return typeof t === "string" ? t : "";
}

function typeMatches(type: string, value: unknown): boolean {
  switch (type) {
    case "boolean": return typeof value === "boolean";
    case "number": return typeof value === "number";
    case "integer": return typeof value === "number" && Number.isInteger(value);
    case "string": return typeof value === "string";
    case "array": return Array.isArray(value);
    case "object": return isObj(value);
    default: return true;
  }
}

/** Evaluate an `if` schema against the current values. Only the declarative
 * subset used by component schemas is supported (const / enum / type / required
 * on direct properties); an empty condition does not select a branch. */
function matchesCondition(cond: JSONSchema, values: Record<string, unknown>): boolean {
  const props = asSchema(cond.properties) ?? {};
  const required = Array.isArray(cond.required) ? cond.required : [];
  if (Object.keys(props).length === 0 && required.length === 0) return false;

  for (const [k, raw] of Object.entries(props)) {
    const sub = asSchema(raw);
    if (!sub) continue;
    const value = values[k];
    if (sub.const !== undefined) {
      if (value !== sub.const) return false;
    } else if (Array.isArray(sub.enum) && sub.enum.length > 0) {
      if (!sub.enum.some((x) => x === value)) return false;
    } else if (primaryType(sub)) {
      if (!typeMatches(primaryType(sub), value)) return false;
    }
  }
  for (const k of required) if (!(k in values)) return false;
  return true;
}

function mergeSchemas(base: JSONSchema, branch: JSONSchema): JSONSchema {
  return {
    ...base,
    ...branch,
    properties: { ...(base.properties ?? {}), ...(branch.properties ?? {}) },
    required: Array.from(new Set([...(base.required ?? []), ...(branch.required ?? [])])),
  };
}

/** Flatten `if/then/else` into an effective schema for the supplied values,
 * recursively for nested objects. Conditional keys are stripped afterwards so
 * a branch can never re-trigger itself. Depth is bounded (hostile/untrusted
 * schema payloads must not hang the editor). */
export function applyConditionals(
  schema: JSONSchema | null | undefined,
  values: Record<string, unknown> = {},
  depth = 0
): JSONSchema {
  const s = asSchema(schema);
  if (!s || depth > 8) return s ?? {};

  let effective = s;
  const cond = asSchema(s.if);
  if (cond) {
    const branch = asSchema(matchesCondition(cond, values) ? s.then : s.else);
    if (branch) effective = mergeSchemas(effective, branch);
  }
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { if: _if, then: _then, else: _else, ...rest } = effective;
  const props = asSchema(rest.properties);
  if (props) {
    const resolved: Record<string, JSONSchema> = {};
    for (const [k, raw] of Object.entries(props)) {
      const sub = asSchema(raw);
      const subValues = isObj(values[k]) ? (values[k] as Record<string, unknown>) : {};
      resolved[k] = applyConditionals(sub, subValues, depth + 1);
    }
    return { ...rest, properties: resolved };
  }
  return rest;
}

/** Convert a JSON Schema into legacy-compatible FieldSpecs.
 * string → text, integer/number → number, boolean → bool, enum → select,
 * array/object → json (the structured JSON textarea). */
export function schemaToFieldSpecs(
  schema: JSONSchema | null | undefined,
  values: Record<string, unknown> = {}
): FieldSpec[] {
  const effective = applyConditionals(schema, values);
  const props = asSchema(effective.properties) ?? {};
  const required = Array.isArray(effective.required) ? effective.required : [];
  return Object.entries(props).map(([key, raw]) => {
    const p = asSchema(raw) ?? {};
    let type: FieldSpec["type"] = "text";
    let options: string[] | undefined;
    if (Array.isArray(p.enum) && p.enum.length > 0) {
      type = "select";
      options = p.enum.map(String);
    } else {
      switch (primaryType(p)) {
        case "boolean": type = "bool"; break;
        case "integer":
        case "number": type = "number"; break;
        case "array":
        case "object": type = "json"; break;
        default: type = "text";
      }
    }
    return {
      key,
      label: typeof p.title === "string" ? p.title : key,
      type,
      ...(options ? { options } : {}),
      required: required.includes(key),
      default: p.default,
      description: typeof p.description === "string" ? p.description : undefined,
    };
  });
}

/** Fallback: reconstruct the legacy FieldSpec choice from live config values,
 * preserving the old "form follows the value type" behavior. Structured
 * values (non-null objects) are left to the editor's dedicated JSON fields. */
export function inferFieldSpecs(values: Record<string, unknown> | null | undefined): FieldSpec[] {
  if (!isObj(values)) return [];
  return Object.keys(values)
    .filter((key) => values[key] === null || typeof values[key] !== "object")
    .sort()
    .map((key) => {
      const v = values[key];
      const type: FieldSpec["type"] = typeof v === "boolean" ? "bool" : typeof v === "number" ? "number" : "text";
      return { key, label: key, type, required: false };
    });
}

/** Look up the declared schema for one component type. Never throws on a
 * missing DTO, non-array components, or a schema that is not an object. */
export function schemaForType(
  dto: ConfigSchemaDTO | null | undefined,
  type: string | undefined
): JSONSchema | null {
  if (!type || !dto || !Array.isArray(dto.components)) return null;
  const entry = dto.components.find((c) => isObj(c) && c.type === type);
  return asSchema(entry?.schema);
}

/** Prefer the declared schema; otherwise return the caller's fallback specs. */
export function fieldSpecsFor(
  dto: ConfigSchemaDTO | null | undefined,
  type: string | undefined,
  fallback: FieldSpec[],
  values: Record<string, unknown> = {}
): FieldSpec[] {
  const schema = schemaForType(dto, type);
  return schema ? schemaToFieldSpecs(schema, values) : fallback;
}
