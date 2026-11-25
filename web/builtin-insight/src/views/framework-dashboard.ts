// Pure helpers for the `framework/dashboard` + `runtime/events` queries.
// The dashboard view renders these rows with antd; every decision (count
// reconciliation, event/diagnostic mapping, fallback between event sources)
// lives here so it is testable without React or a DOM.

import type {
  FrameworkDashboardDTO, RuntimeDiagnosticDTO, RuntimeEventDTO,
} from "../types";
import type { FiberRow } from "./runtime-inspector";

const MISSING = "—";

function text(v: unknown): string {
  return v === null || v === undefined || v === "" ? MISSING : String(v);
}

function numeric(v: unknown): number | null {
  if (typeof v === "number") return Number.isFinite(v) ? v : null;
  if (typeof v === "string" && v.trim() !== "") {
    const n = Number(v);
    return Number.isFinite(n) ? n : null;
  }
  return null;
}

function numberValue(v: unknown): number {
  return numeric(v) ?? 0;
}

function asObject(v: unknown): Record<string, unknown> {
  return v && typeof v === "object" && !Array.isArray(v)
    ? v as Record<string, unknown>
    : {};
}

function mapList<T>(raw: unknown, fn: (v: unknown, index: number) => T): T[] {
  const out: T[] = [];
  if (!Array.isArray(raw)) return out;
  raw.forEach((r, i) => {
    if (r && typeof r === "object" && !Array.isArray(r)) out.push(fn(r, i));
  });
  return out;
}

// ---- events -----------------------------------------------------------------

export interface EventRow {
  key: string;
  sequence: number;
  timestamp: string;
  type: string;
  fiberID: string;
  activationID: string;
  /** Safely stringified event Data payload (may be an object on the wire). */
  data: string;
}

function dataText(v: unknown): string {
  if (v === null || v === undefined) return "";
  if (typeof v === "string" || typeof v === "number" || typeof v === "boolean") return String(v);
  try {
    return JSON.stringify(v) ?? "";
  } catch {
    return "[unserializable]";
  }
}

export function mapEvent(raw: unknown, index = 0): EventRow {
  const e = (raw && typeof raw === "object" ? raw : {}) as Partial<RuntimeEventDTO>;
  const o = asObject(raw);
  return {
    key: `event:${index}`,
    sequence: numberValue(e.sequence),
    timestamp: text(o.timestamp),
    type: text(e.type),
    fiberID: text(e.fiberID),
    activationID: text(e.activationID),
    data: dataText(o.data),
  };
}

export function mapEvents(raw: unknown): EventRow[] {
  return mapList(raw, mapEvent);
}

export interface ParsedEvents {
  events: EventRow[];
  sequence: number;
}

/** Parses `runtime/events`. A defensive {data: ...} wrapper is tolerated. */
export function parseEvents(data: unknown): ParsedEvents {
  let payload: unknown = data;
  if (payload && typeof payload === "object" && !Array.isArray(payload)) {
    const outer = payload as Record<string, unknown>;
    if (!("events" in outer) && "data" in outer) payload = outer.data;
  }
  const raw = asObject(payload);
  return { events: mapEvents(raw.events), sequence: numberValue(raw.sequence) };
}

/** The dedicated event query is authoritative; the dashboard's embedded
 * recentEvents is the fallback when it is unavailable or still empty. */
export function resolveEvents(primary: EventRow[], fallback: EventRow[]): EventRow[] {
  return primary.length > 0 ? primary : fallback;
}

// ---- counts -----------------------------------------------------------------

export interface DashboardCounts {
  failed: number;
  pending: number;
  active: number;
  total: number;
  /** Every numeric entry in the raw counts object, for the secondary strip. */
  items: Array<{ key: string; label: string; value: number }>;
}

const COUNT_ALIASES = {
  failed: ["failed", "failures", "failure", "failedfibers", "fiberfailed", "failedactivations"],
  pending: ["pending", "waiting", "pendingfibers", "fiberpending", "pendingactivations"],
  active: ["active", "actives", "activefibers", "fiberactive", "activeactivations"],
  total: ["total", "fibers", "fibertotal", "totalfibers", "fiber", "components", "activations"],
};

function normalize(k: string): string {
  return k.toLowerCase().replace(/[^a-z0-9]/g, "");
}

function readCount(raw: Record<string, unknown>, aliases: readonly string[]): number | undefined {
  for (const [key, value] of Object.entries(raw)) {
    if (aliases.includes(normalize(key))) {
      const n = numeric(value);
      if (n !== null) return n;
    }
  }
  return undefined;
}

function countsFromFibers(fibers: FiberRow[] | undefined): DashboardCounts {
  let failed = 0;
  let pending = 0;
  let active = 0;
  for (const f of fibers ?? []) {
    const s = f.state.toLowerCase();
    if (s.includes("fail") || s.includes("error")) failed++;
    else if (s.includes("pending") || s.includes("wait")) pending++;
    else if (s.includes("active") || s.includes("running")) active++;
  }
  return { failed, pending, active, total: (fibers ?? []).length, items: [] };
}

/** Counts are taken from the dashboard query first (alias keys are
 * case/punctuation-insensitive); when a key is absent the fiber rows from a
 * snapshot provide a best-effort fallback. Never throws on partial data. */
export function mapCounts(raw: unknown, fibers?: FiberRow[]): DashboardCounts {
  const source = asObject(raw);
  const fallback = countsFromFibers(fibers);
  const items: DashboardCounts["items"] = [];
  for (const [key, value] of Object.entries(source)) {
    const n = numeric(value);
    if (n !== null) items.push({ key, label: key, value: n });
  }
  return {
    failed: readCount(source, COUNT_ALIASES.failed) ?? fallback.failed,
    pending: readCount(source, COUNT_ALIASES.pending) ?? fallback.pending,
    active: readCount(source, COUNT_ALIASES.active) ?? fallback.active,
    total: readCount(source, COUNT_ALIASES.total) ?? fallback.total,
    items,
  };
}

// ---- diagnostics ------------------------------------------------------------

export interface DiagnosticRow {
  key: string;
  level: string;
  message: string;
  code: string;
  source: string;
  fiberID: string;
  detail: string;
}

export function mapDiagnostic(raw: unknown, index = 0): DiagnosticRow {
  const key = `diagnostic:${index}`;
  if (typeof raw === "string") {
    return { key, level: "info", message: raw, code: MISSING, source: MISSING, fiberID: MISSING, detail: MISSING };
  }
  const d = (raw && typeof raw === "object" ? raw : {}) as Partial<RuntimeDiagnosticDTO>;
  const o = asObject(raw);
  return {
    key,
    level: text(o.level ?? o.severity ?? o.type ?? "info"),
    message: text(o.message ?? o.detail ?? o.summary ?? o.code ?? o.reason),
    code: text(o.code ?? d.id),
    source: text(o.source ?? o.component),
    fiberID: text(d.fiberID),
    detail: text(o.detail ?? o.description),
  };
}

export function mapDiagnostics(raw: unknown): DiagnosticRow[] {
  if (!Array.isArray(raw)) return [];
  const out: DiagnosticRow[] = [];
  raw.forEach((r, i) => {
    if (r === null || r === undefined) return;
    if (typeof r !== "object" && typeof r !== "string") return;
    out.push(mapDiagnostic(r, i));
  });
  return out;
}

// ---- dashboard query --------------------------------------------------------

export interface ParsedDashboard {
  sequence: number;
  state: string;
  counts: DashboardCounts;
  recentEvents: EventRow[];
  diagnostics: DiagnosticRow[];
}

/** Parses `framework/dashboard`; missing sections degrade to empty values. */
export function parseDashboard(data: unknown, fibers?: FiberRow[]): ParsedDashboard {
  let payload: unknown = data;
  if (payload && typeof payload === "object" && !Array.isArray(payload)) {
    const outer = payload as Record<string, unknown>;
    if (!("counts" in outer) && !("recentEvents" in outer) && "data" in outer) payload = outer.data;
  }
  const raw = asObject(payload);
  return {
    sequence: numberValue(raw.sequence),
    state: text(raw.state),
    counts: mapCounts(raw.counts, fibers),
    recentEvents: mapEvents(raw.recentEvents),
    diagnostics: mapDiagnostics(raw.diagnostics),
  };
}
