// @vitest-environment node
// Logic-only tests for the market-catalog renderer: no React, no antd, no DOM.
import { describe, expect, it } from "vitest";
import {
  actionFor, buildArgs, compareRows, declaredActions, eligibleActions,
  filterRows, isNativeEntry, mapEntry, mapEntries, mapStoreEntry, parseCatalog,
  sortRows, storeRowKey,
  type CatalogActionSpec, type CatalogRow, type DeclaredAction,
} from "./market-catalog";

const entry = (over: Record<string, unknown> = {}) => ({
  id: "cam", name: "Camera", version: "1.2.0", backend: "wasm",
  moduleType: "industrial.camera", description: "A camera module",
  source: "oci://registry/cam", trust: "verified", installed: false, compatible: true,
  ...over,
});

const row = (over: Partial<CatalogRow> = {}): CatalogRow => ({ ...mapEntry(entry()), ...over });

describe("row mapping", () => {
  it("maps the query payload into display rows with a native flag", () => {
    const { rows, sequence } = parseCatalog({
      entries: [entry(), entry({ id: "builtin", backend: "native", source: "native:build", installed: true })],
      sequence: 9,
    });
    expect(sequence).toBe(9);
    expect(rows).toHaveLength(2);
    expect(rows[0]).toMatchObject({
      id: "cam", name: "Camera", version: "1.2.0", backend: "wasm",
      trust: "verified", source: "oci://registry/cam", installed: false, compatible: true, native: false,
    });
    expect(rows[1].native).toBe(true);
  });

  it("detects native artifacts from backend, source or logical module type", () => {
    expect(isNativeEntry({ backend: "native" })).toBe(true);
    expect(isNativeEntry({ source: "builtin:core" })).toBe(true);
    expect(isNativeEntry({ moduleType: "builtin" })).toBe(true);
    expect(isNativeEntry(entry())).toBe(false);
    expect(isNativeEntry(null)).toBe(false);
  });

  it("keeps missing fields as placeholders and coerces boolean-ish flags", () => {
    const r = mapEntry({ id: "partial", installed: "true", compatible: 1 });
    expect(r).toMatchObject({
      id: "partial", name: "—", version: "—", backend: "—", source: "—", trust: "—",
      installed: true, compatible: true, native: false,
    });
    expect(mapEntry(null).installed).toBe(false);
    expect(mapEntries([null, 3, "x", { id: "ok" }]).map((x) => x.id)).toEqual(["ok"]);
  });

  it("uses id+version as the store row key so multiple versions coexist", () => {
    const v1 = mapStoreEntry({ id: "demo", version: "1.0.0" });
    const v2 = mapStoreEntry({ id: "demo", version: "2.0.0" });
    expect(storeRowKey(v1)).not.toBe(storeRowKey(v2));
    expect(storeRowKey(v1)).toBe(storeRowKey(mapStoreEntry({ id: "demo", version: "1.0.0" })));
    expect(storeRowKey(v2)).not.toBe(storeRowKey(mapStoreEntry({ id: "other", version: "2.0.0" })));
    // Field text must not be able to forge another row's composite key.
    expect(storeRowKey({ id: "a@1", version: "0" })).not.toBe(storeRowKey({ id: "a", version: "1@0" }));
  });
});

describe("missing-data handling", () => {
  it("never throws on absent, null or wrapped payloads", () => {
    expect(parseCatalog(null)).toEqual({ rows: [], sequence: 0 });
    expect(parseCatalog({})).toEqual({ rows: [], sequence: 0 });
    expect(parseCatalog({ entries: null, sequence: "bad" })).toEqual({ rows: [], sequence: 0 });
    expect(parseCatalog({ data: { entries: [entry()], sequence: 4 } }).sequence).toBe(4);
    expect(parseCatalog([entry()]).rows).toHaveLength(1);
  });
});

describe("action eligibility", () => {
  const specs: CatalogActionSpec[] = declaredActions([
    { label: "Install", command: "market.install" },
    { label: "Upgrade", command: "market.upgrade", args: { id: "$row.id" } },
    { label: "Remove", command: "market.remove" },
    { label: "Unrelated", command: "market.ping" },
  ] as DeclaredAction[]);

  it("maps declared commands to the three catalog actions, keeping first match", () => {
    expect(specs.map((s) => s.action)).toEqual(["install", "upgrade", "remove"]);
    expect(actionFor("Uninstall plugin")).toBe("remove");
    expect(declaredActions(undefined)).toEqual([]);
  });

  it("keeps native entries strictly read-only even when commands exist", () => {
    expect(eligibleActions(row({ native: true, installed: false }), specs)).toEqual([]);
    expect(eligibleActions(row({ native: true, installed: true }), specs)).toEqual([]);
  });

  it("gates actions on installed state and on commands being available", () => {
    expect(eligibleActions(row(), specs).map((s) => s.action)).toEqual(["install"]);
    expect(eligibleActions(row({ installed: true }), specs).map((s) => s.action)).toEqual(["upgrade", "remove"]);
    expect(eligibleActions(row(), [])).toEqual([]); // no commands → read-only catalog
  });

  it("resolves $row args and defaults to the entry id", () => {
    expect(buildArgs(undefined, row())).toEqual({ id: "cam" });
    expect(buildArgs({ target: "$row.id", fixed: "x" }, row())).toEqual({ target: "cam", fixed: "x" });
  });
});

describe("sorting and filtering", () => {
  const rows = [
    row({ id: "b", name: "Beta", backend: "wasm", installed: false, compatible: true }),
    row({ id: "a", name: "Alpha", backend: "native", installed: true, compatible: false, native: true }),
    row({ id: "c", name: "Gamma", backend: "wasm", installed: true, compatible: true }),
  ];

  it("sorts by string and boolean fields without mutating the source", () => {
    expect(sortRows(rows, "name", "ascend").map((r) => r.name)).toEqual(["Alpha", "Beta", "Gamma"]);
    expect(sortRows(rows, "name", "descend").map((r) => r.name)).toEqual(["Gamma", "Beta", "Alpha"]);
    expect(sortRows(rows, "installed", "ascend").map((r) => r.id)).toEqual(["b", "a", "c"]);
    expect(rows[0].id).toBe("b");
    expect(compareRows(rows[1], rows[0], "id")).toBeLessThan(0); // "a" < "b"
  });

  it("filters by search text, backend and flags, and composes the clauses", () => {
    expect(filterRows(rows, {})).toHaveLength(3);
    expect(filterRows(rows, { search: "ALPHA" }).map((r) => r.id)).toEqual(["a"]);
    expect(filterRows(rows, { search: "oci://" })).toHaveLength(3);
    expect(filterRows(rows, { backend: "wasm" }).map((r) => r.id)).toEqual(["b", "c"]);
    expect(filterRows(rows, { backend: "all" })).toHaveLength(3);
    expect(filterRows(rows, { installed: false }).map((r) => r.id)).toEqual(["b"]);
    expect(filterRows(rows, { compatible: false }).map((r) => r.id)).toEqual(["a"]);
    expect(filterRows(rows, { backend: "wasm", installed: true }).map((r) => r.id)).toEqual(["c"]);
    expect(filterRows(rows, { search: "nothing" })).toEqual([]);
  });
});
