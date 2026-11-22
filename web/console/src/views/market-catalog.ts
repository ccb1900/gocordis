// Pure logic for the `market-catalog` renderer. Kept DOM/React-free so every
// decision (row mapping, native read-only rule, action eligibility, sorting,
// filtering, missing-data handling) is unit-testable under vitest's node
// environment — the .tsx renderer only turns these results into antd nodes.

import type { MarketCatalogEntry, StorePluginEntry } from "../types";

export type CatalogAction = "install" | "upgrade" | "remove";
export type CatalogField =
  | "id" | "name" | "version" | "backend" | "moduleType" | "description"
  | "source" | "trust" | "installed" | "compatible";

/** A row ready for antd Table. Missing backend fields become explicit
 * placeholders instead of `undefined`, so no cell ever prints "[object]". */
export interface CatalogRow {
  id: string;
  name: string;
  version: string;
  backend: string;
  moduleType: string;
  description: string;
  source: string;
  trust: string;
  installed: boolean;
  compatible: boolean;
  /** Native artifacts are compiled into the host binary; they are visible
   * in the catalog but can never be installed/upgraded/removed at runtime. */
  native: boolean;
}

export const ACTION_LABELS: Record<CatalogAction, string> = {
  install: "Install",
  upgrade: "Upgrade",
  remove: "Remove",
};

const MISSING = "—";

function text(v: unknown): string {
  return v === null || v === undefined || v === "" ? MISSING : String(v);
}

function flag(v: unknown): boolean {
  return v === true || v === "true" || v === 1 || v === "1";
}

function nativeToken(v: string): boolean {
  return v === "native" || v === "builtin" || v === "built-in" ||
    v === "build-time" || v.startsWith("native:") || v.startsWith("builtin:");
}

/** A catalog entry is native when its loading backend OR its source is a
 * native/builtin artifact. A logical module type of "native"/"builtin" is
 * also treated as native (the builtin loader uses that type). */
export function isNativeEntry(entry: unknown): boolean {
  if (!entry || typeof entry !== "object") return false;
  const e = entry as Record<string, unknown>;
  const norm = (v: unknown) => (typeof v === "string" ? v.trim().toLowerCase() : "");
  const moduleType = norm(e.moduleType);
  return nativeToken(norm(e.backend)) || nativeToken(norm(e.source)) ||
    moduleType === "native" || moduleType === "builtin";
}

/** Maps one raw entry; null/primitive entries are skipped by mapEntries. */
export function mapEntry(raw: unknown): CatalogRow {
  const e = (raw && typeof raw === "object" ? raw : {}) as Partial<MarketCatalogEntry>;
  return {
    id: text(e.id),
    name: text(e.name),
    version: text(e.version),
    backend: text(e.backend),
    moduleType: text(e.moduleType),
    description: text(e.description),
    source: text(e.source),
    trust: text(e.trust),
    installed: flag(e.installed),
    compatible: flag(e.compatible),
    native: isNativeEntry(e),
  };
}

export function mapEntries(entries: unknown): CatalogRow[] {
  if (!Array.isArray(entries)) return [];
  const out: CatalogRow[] = [];
  for (const raw of entries) {
    if (!raw || typeof raw !== "object") continue;
    out.push(mapEntry(raw));
  }
  return out;
}

/** Local store entries are a *local inventory*, not a remote marketplace.
 * They are mapped into the same row shape so the read-only renderer can reuse
 * its filter/sort logic without ever talking to an external registry. */
export interface StoreRow extends CatalogRow {
  hostVersionRange: string;
  installPath: string;
  checksum: string;
  dependencies: string;
  declared: boolean;
  running: boolean;
}

export function mapStoreEntry(raw: unknown): StoreRow {
  const e = (raw && typeof raw === "object" ? raw : {}) as Partial<StorePluginEntry>;
  const deps = Array.isArray(e.dependencies) ? e.dependencies : [];
  const name = e.displayName ? String(e.displayName) : text(e.id);
  return {
    id: text(e.id),
    name,
    version: text(e.version),
    backend: "local",
    moduleType: text(e.id),
    description: text(e.installPath),
    source: "local store",
    trust: "local",
    installed: flag(e.declared) || flag(e.running),
    compatible: true,
    native: false,
    declared: flag(e.declared),
    running: flag(e.running),
    hostVersionRange: text(e.hostVersionRange),
    installPath: text(e.installPath),
    checksum: text(e.checksum),
    dependencies: deps.length
      ? deps.map((d) => `${text(d?.id)} ${text(d?.versionRange)}`).join(", ")
      : MISSING,
  };
}

/** Parses the `market/catalog` query payload. Accepts the described
 * {entries, sequence} shape, a bare entries array, and (defensively) a
 * still-wrapped {data: ...} body. Never throws on partial/missing data. */
export function parseCatalog(data: unknown): { rows: CatalogRow[]; sequence: number } {
  let payload: unknown = data;
  if (payload && typeof payload === "object" && !Array.isArray(payload)) {
    const obj = payload as Record<string, unknown>;
    if (!("entries" in obj) && "data" in obj) payload = obj.data;
  }
  const entries = Array.isArray(payload)
    ? payload
    : (payload && typeof payload === "object" ? (payload as Record<string, unknown>).entries : undefined);
  const raw = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
  const sequence = typeof raw.sequence === "number" && Number.isFinite(raw.sequence) ? raw.sequence : 0;
  return { rows: mapEntries(entries), sequence };
}

// ---- Actions ----------------------------------------------------------------

export interface DeclaredAction {
  label: string;
  command: string;
  args?: Record<string, string>;
}

export interface CatalogActionSpec extends DeclaredAction {
  action: CatalogAction;
}

/** Maps a label/command string to one of the three catalog actions. */
export function actionFor(label: string | undefined): CatalogAction | null {
  const v = (label ?? "").toLowerCase();
  if (v.includes("uninstall") || v.includes("remove")) return "remove";
  if (v.includes("install")) return "install";
  if (v.includes("upgrade") || v.includes("update")) return "upgrade";
  return null;
}

/** The command surface is declarative: with no rowActions wired by the
 * composition the host exposes no mutation commands and the table is
 * read-only. Duplicates keep their first declaration. */
export function declaredActions(actions: DeclaredAction[] | undefined): CatalogActionSpec[] {
  const out: CatalogActionSpec[] = [];
  for (const a of actions ?? []) {
    const action = actionFor(a.label) ?? actionFor(a.command);
    if (!action || out.some((s) => s.action === action)) continue;
    out.push({ ...a, action });
  }
  return out;
}

/** Eligibility: native rows are ALWAYS read-only; mutations additionally
 * require a declared command. Install targets absent entries; upgrade and
 * remove target installed ones. */
export function eligibleActions(row: CatalogRow, specs: CatalogActionSpec[]): CatalogActionSpec[] {
  if (row.native || specs.length === 0) return [];
  return specs.filter((s) =>
    s.action === "install" ? !row.installed : row.installed
  );
}

/** $row.<field> references resolve against the row; without declared args
 * the entry id is sent as the natural command payload. */
export function buildArgs(args: Record<string, string> | undefined, row: CatalogRow): Record<string, unknown> {
  if (!args || Object.keys(args).length === 0) return { id: row.id };
  const out: Record<string, unknown> = {};
  for (const [key, ref] of Object.entries(args)) {
    const m = /^\$row\.(.+)$/.exec(ref);
    out[key] = m ? (row as unknown as Record<string, unknown>)[m[1]] : ref;
  }
  return out;
}

// ---- Sorting & filtering ----------------------------------------------------

export function compareRows(a: CatalogRow, b: CatalogRow, key: CatalogField): number {
  if (key === "installed" || key === "compatible") {
    return Number(a[key]) - Number(b[key]);
  }
  return String(a[key] ?? "").localeCompare(String(b[key] ?? ""));
}

export function sortRows(rows: CatalogRow[], key: CatalogField, direction: "ascend" | "descend"): CatalogRow[] {
  const factor = direction === "descend" ? -1 : 1;
  return [...rows].sort((a, b) => factor * compareRows(a, b, key));
}

export interface CatalogFilter {
  /** Case-insensitive substring over id/name/backend/moduleType/source/trust. */
  search?: string;
  /** Exact backend; empty / "all" means no backend filter. */
  backend?: string;
  /** Tri-state installed filter; undefined means no constraint. */
  installed?: boolean;
  compatible?: boolean;
}

export function filterRows<T extends CatalogRow>(rows: T[], filter: CatalogFilter): T[] {
  const q = (filter.search ?? "").trim().toLowerCase();
  const backend = (filter.backend ?? "").trim().toLowerCase();
  return rows.filter((r) => {
    if (q && ![r.id, r.name, r.backend, r.moduleType, r.source, r.trust]
      .some((v) => v.toLowerCase().includes(q))) return false;
    if (backend && backend !== "all" && r.backend.toLowerCase() !== backend) return false;
    if (filter.installed !== undefined && r.installed !== filter.installed) return false;
    if (filter.compatible !== undefined && r.compatible !== filter.compatible) return false;
    return true;
  });
}
