import { describe, expect, it } from "vitest";
import {
  editorKindForSchema, listSuggestions, normalizeStringList, rowsToRecord, valueOptionsOf,
} from "./config-editor-model";
import type { JsonSchemaLike, NodeDescriptor } from "./types";

const mapping: JsonSchemaLike = {
  type: "object",
  properties: {},
  additionalProperties: { type: "string" },
};
const validation: JsonSchemaLike = {
  type: "object",
  properties: {},
  required: ["required"],
};
const validationProps: Record<string, JsonSchemaLike> = {
  required: { type: "array", items: { type: "string" } },
  types: {
    type: "object",
    additionalProperties: { type: "string", enum: ["int", "integer", "decimal", "double", "datetime"] },
  },
};

describe("dedicated config editor model", () => {
  it("selects editors from schema shape, not node type", () => {
    expect(editorKindForSchema(mapping)).toBe("key-value");
    expect(editorKindForSchema(validationProps.types)).toBe("key-value");
    expect(editorKindForSchema(validationProps.required)).toBe("string-list");
    expect(editorKindForSchema({ type: "string", format: "resource" })).toBe("resource");
    expect(editorKindForSchema({ type: "object", properties: {} })).toBe("json");
    expect(editorKindForSchema({ type: "string" })).toBe("text");
    expect(valueOptionsOf(validationProps.types)).toEqual(["int", "integer", "decimal", "double", "datetime"]);
  });

  it("normalizes key/value rows and string lists without emitting invalid keys", () => {
    expect(rowsToRecord([
      { key: " value ", value: "reading" },
      { key: "", value: "ignored" },
      { key: "value", value: "duplicate" },
    ])).toEqual({ record: { value: "duplicate" }, duplicateKeys: ["value"] });
    expect(normalizeStringList([" a ", "", "a", "b", 42])).toEqual(["a", "b"]);
  });

  it("derives field suggestions from sibling mapping schemas", () => {
    const descriptor = { configurationSchema: { properties: validationProps } } as NodeDescriptor;
    expect(listSuggestions(descriptor.configurationSchema.properties!, {
      required: ["value"],
      types: { value: "int" },
    })).toContain("value");
  });
});
