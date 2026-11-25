import type { JsonSchemaLike } from "./types";

export type EditorKind =
  | "resource" | "enum" | "number" | "boolean"
  | "key-value" | "string-list" | "json" | "text";

const asSchema = (value: unknown): JsonSchemaLike =>
  value !== null && typeof value === "object" && !Array.isArray(value)
    ? value as JsonSchemaLike
    : {};

export function valueOptionsOf(schema: JsonSchemaLike): string[] | null {
  const options = asSchema(schema.additionalProperties).enum;
  if (!Array.isArray(options)) return null;
  const values = options.filter((value): value is string => typeof value === "string");
  return values.length > 0 ? values : null;
}

export function editorKindForSchema(schema: JsonSchemaLike): EditorKind {
  if (schema.format === "resource") return "resource";
  if (Array.isArray(schema.enum) && schema.enum.length > 0) return "enum";
  if (schema.type === "number" || schema.type === "integer") return "number";
  if (schema.type === "boolean") return "boolean";
  if (schema.type === "array" && asSchema(schema.items).type === "string") return "string-list";
  if (schema.type === "object" && asSchema(schema.additionalProperties).type === "string") {
    return "key-value";
  }
  if (schema.type === "object" || schema.type === "array") return "json";
  return "text";
}

export interface KeyValueRow { key: string; value: string }

export function recordToRows(value: unknown): KeyValueRow[] {
  if (value === null || typeof value !== "object" || Array.isArray(value)) return [];
  return Object.entries(value as Record<string, unknown>)
    .filter(([, item]) => item !== null && item !== undefined && typeof item !== "object")
    .map(([key, item]) => ({ key, value: String(item) }));
}

export function rowsToRecord(rows: readonly KeyValueRow[]): {
  record: Record<string, string>;
  duplicateKeys: string[];
} {
  const record: Record<string, string> = {};
  const duplicateKeys: string[] = [];
  for (const row of rows) {
    const key = row.key.trim();
    if (!key) continue;
    if (Object.prototype.hasOwnProperty.call(record, key)) duplicateKeys.push(key);
    record[key] = row.value;
  }
  return { record, duplicateKeys };
}

export function normalizeStringList(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  const seen = new Set<string>();
  const result: string[] = [];
  for (const item of value) {
    if (typeof item !== "string") continue;
    const trimmed = item.trim();
    if (!trimmed || seen.has(trimmed)) continue;
    seen.add(trimmed);
    result.push(trimmed);
  }
  return result;
}

export function listSuggestions(
  properties: Record<string, JsonSchemaLike>, config: Record<string, unknown>,
): string[] {
  const seen = new Set<string>();
  for (const [name, schema] of Object.entries(properties)) {
    if (editorKindForSchema(schema) !== "key-value") continue;
    for (const row of recordToRows(config[name])) {
      if (row.key.trim()) seen.add(row.key.trim());
    }
  }
  return [...seen];
}
