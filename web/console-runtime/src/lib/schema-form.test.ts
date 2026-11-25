import { describe, expect, it } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import {
  applyConditionals, fieldSpecsFor, inferFieldSpecs, schemaForType, schemaToFieldSpecs,
  type ConfigSchemaDTO, type FieldSpec, type JSONSchema,
} from "./schema-form";

const schema: JSONSchema = {
  type: "object",
  required: ["batch"],
  properties: {
    endpoint: { type: "string", title: "地址", description: "目标 URL", default: "http://localhost" },
    batch: { type: "integer", default: 8 },
    ratio: { type: "number" },
    enabled: { type: "boolean", default: true },
    mode: { type: "string", enum: ["fast", "slow"], default: "fast" },
    tags: { type: "array", items: { type: "string" } },
    metadata: { type: "object" },
  },
  if: { properties: { mode: { const: "slow" } } },
  then: { properties: { threshold: { type: "number" } }, required: ["threshold"] },
  else: { properties: { fastPath: { type: "boolean" } } },
};

const spec = (s: FieldSpec[], key: string): FieldSpec => s.find((f) => f.key === key)!;

describe("schema → FieldSpec conversion", () => {
  it("maps every supported scalar and structured type", () => {
    const specs = schemaToFieldSpecs(schema, {});
    expect(specs.map((f) => f.key)).toEqual([
      "endpoint", "batch", "ratio", "enabled", "mode", "tags", "metadata", "fastPath",
    ]);
    expect(spec(specs, "endpoint")).toMatchObject({
      type: "text", label: "地址", description: "目标 URL", default: "http://localhost", required: false,
    });
    expect(spec(specs, "batch")).toMatchObject({ type: "number", default: 8, required: true });
    expect(spec(specs, "ratio").type).toBe("number");
    expect(spec(specs, "enabled").type).toBe("bool");
    expect(spec(specs, "mode")).toMatchObject({ type: "select", options: ["fast", "slow"] });
    expect(spec(specs, "tags").type).toBe("json");
    expect(spec(specs, "metadata").type).toBe("json");
  });

  it("selects then/else branches from the current values", () => {
    expect(spec(schemaToFieldSpecs(schema, { mode: "slow" }), "threshold").type).toBe("number");
    expect(schemaToFieldSpecs(schema, { mode: "slow" }).some((f) => f.key === "fastPath")).toBe(false);
    expect(spec(schemaToFieldSpecs(schema, { mode: "fast" }), "fastPath").type).toBe("bool");
    expect(spec(schemaToFieldSpecs(schema, { mode: "slow" }), "threshold").required).toBe(true);
    expect(applyConditionals(schema, {}).properties?.fastPath).toBeDefined();
  });

  it("resolves nested object conditionals without looping", () => {
    const nested: JSONSchema = {
      type: "object",
      properties: {
        outer: {
          type: "object",
          properties: { flag: { type: "boolean" } },
          if: { properties: { flag: { const: true } } },
          then: { properties: { extra: { type: "string" } } },
        },
      },
    };
    expect(schemaToFieldSpecs(nested, { outer: { flag: true } }).map((f) => f.key)).toEqual(["outer"]);
    expect(applyConditionals(nested, { outer: { flag: true } }).properties?.outer.properties?.extra).toBeDefined();
    expect(applyConditionals(nested, { outer: { flag: false } }).properties?.outer.properties?.extra).toBeUndefined();
  });

  it("tolerates missing / malformed schemas", () => {
    expect(schemaToFieldSpecs(null)).toEqual([]);
    expect(schemaToFieldSpecs(undefined)).toEqual([]);
    expect(schemaToFieldSpecs({})).toEqual([]);
    expect(schemaToFieldSpecs({ properties: null } as never)).toEqual([]);
  });
});

describe("fallback behavior", () => {
  const dto: ConfigSchemaDTO = { components: [{ type: "alpha", schema }] };
  const fallback: FieldSpec[] = [{ key: "legacy", label: "legacy", type: "text" }];

  it("finds a schema by exact component type only", () => {
    expect(schemaForType(dto, "alpha")).toBe(schema);
    expect(schemaForType(dto, "beta")).toBeNull();
    expect(schemaForType(null, "alpha")).toBeNull();
    expect(schemaForType(undefined, undefined)).toBeNull();
    expect(schemaForType({ components: null } as never, "alpha")).toBeNull();
  });

  it("prefers a declared schema and otherwise returns the hardcoded fallback", () => {
    expect(fieldSpecsFor(dto, "alpha", fallback)).toHaveLength(8);
    expect(fieldSpecsFor(dto, "beta", fallback)).toBe(fallback);
    expect(fieldSpecsFor(null, "alpha", fallback)).toBe(fallback);
    expect(fieldSpecsFor(dto, undefined, fallback)).toBe(fallback);
  });

  it("renders fields from the REAL C# config/schema wire fixture (no drift)", () => {
    // Same JSON file the .NET Phase6Tests asserts against. If the backend shape
    // changes, either test fails — this is the backend→frontend closed loop.
    const wire = JSON.parse(
      readFileSync(resolve(process.cwd(), "../../tests/fixtures/config-schema-wire.json"), "utf8"),
    ) as ConfigSchemaDTO;
    const specs = fieldSpecsFor(wire, "echo", fallback);
    expect(specs.map((f) => f.key).sort()).toEqual(["batch", "enabled", "mode"]);
    expect(specs.find((f) => f.key === "batch")).toMatchObject({
      type: "number", required: true, default: 8,
    });
    expect(specs.find((f) => f.key === "enabled")).toMatchObject({ type: "bool", default: true });
    expect(specs.find((f) => f.key === "mode")).toMatchObject({
      type: "select", options: ["fast", "slow"], default: "fast",
    });
  });

  it("infers legacy specs from config values", () => {
    expect(inferFieldSpecs({ n: 1, on: true, s: "x", o: {}, nil: null })).toEqual([
      { key: "n", label: "n", type: "number", required: false },
      { key: "nil", label: "nil", type: "text", required: false },
      { key: "on", label: "on", type: "bool", required: false },
      { key: "s", label: "s", type: "text", required: false },
    ]);
    expect(inferFieldSpecs(null)).toEqual([]);
  });
});
