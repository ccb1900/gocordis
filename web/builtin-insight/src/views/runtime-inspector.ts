// Pure DTO → table-row mapping for the `runtime/snapshot` named query.
// Kept React/antd-free so row mapping and missing-data handling are testable
// under vitest's node environment; RuntimeInspector.tsx only renders the rows.

import type {
  RuntimeDependencyDTO, RuntimeEffectDTO, RuntimeFiberDTO,
  RuntimeProviderDTO, RuntimeScopeDTO,
} from "../types";

const MISSING = "—";

function text(v: unknown): string {
  return v === null || v === undefined || v === "" ? MISSING : String(v);
}

function numberValue(v: unknown): number {
  if (typeof v === "number") return Number.isFinite(v) ? v : 0;
  if (typeof v === "string" && v.trim() !== "") {
    const n = Number(v);
    return Number.isFinite(n) ? n : 0;
  }
  return 0;
}

function idList(v: unknown): string {
  if (!Array.isArray(v) || v.length === 0) return MISSING;
  return v.map((x) => text(x)).join(", ");
}

function asObject(v: unknown): Record<string, unknown> {
  return v && typeof v === "object" && !Array.isArray(v)
    ? v as Record<string, unknown>
    : {};
}

function asArray(v: unknown): unknown[] {
  return Array.isArray(v) ? v : [];
}

function dto<T>(v: unknown): Partial<T> {
  return asObject(v) as Partial<T>;
}

// Invalid entries (null, primitives) are skipped rather than rendered as
// empty placeholder rows — a partially published DTO must not create noise.
function mapList<T>(raw: unknown, fn: (v: unknown, index: number) => T): T[] {
  const out: T[] = [];
  asArray(raw).forEach((r, i) => {
    if (r && typeof r === "object" && !Array.isArray(r)) out.push(fn(r, i));
  });
  return out;
}

// ---- table rows -------------------------------------------------------------

export interface FiberRow {
  key: string;
  id: string;
  name: string;
  component: string;
  state: string;
  intent: string;
  err: string;
  activationID: string;
  scopeID: string;
  parentFiberIDs: string;
  childFiberIDs: string;
}

export interface ScopeRow {
  key: string;
  id: string;
  parentID: string;
  providerKeys: string;
  fiberIDs: string;
}

export interface ProviderRow {
  key: string;
  capabilityKey: string;
  state: string;
  retiring: boolean;
  ownerFiberID: string;
  ownerActivationID: string;
  scopeID: string;
}

export interface DependencyRow {
  key: string;
  capabilityKey: string;
  consumerFiberID: string;
  consumerActivationID: string;
  providerFiberID: string;
  providerActivationID: string;
  status: string;
  reason: string;
}

export interface EffectRow {
  key: string;
  ownerFiberID: string;
  activationID: string;
  seq: number;
  kind: string;
  capabilityKey: string;
  state: string;
}

export function mapFiber(raw: unknown, index = 0): FiberRow {
  const f = dto<RuntimeFiberDTO>(raw);
  const id = text(f.id);
  return {
    key: id === MISSING ? `fiber:${index}` : `fiber:${id}`,
    id,
    name: text(f.name),
    component: text(f.component),
    state: text(f.state),
    intent: text(f.intent),
    err: text(f.err),
    activationID: text(f.activationID),
    scopeID: text(f.scopeID),
    parentFiberIDs: idList(f.parentFiberIDs),
    childFiberIDs: idList(f.childFiberIDs),
  };
}

export function mapFibers(raw: unknown): FiberRow[] {
  const rows = mapList(raw, mapFiber);
  // Keys must stay unique even when the runtime reports duplicate Fiber IDs.
  const seen = new Map<string, number>();
  return rows.map((row, i) => {
    const count = seen.get(row.key) ?? 0;
    seen.set(row.key, count + 1);
    return count === 0 ? row : { ...row, key: `${row.key}:${i}` };
  });
}

export function mapScope(raw: unknown, index = 0): ScopeRow {
  const s = dto<RuntimeScopeDTO>(raw);
  const id = text(s.id);
  return {
    key: id === MISSING ? `scope:${index}` : `scope:${id}`,
    id,
    parentID: text(s.parentID),
    providerKeys: idList(s.providerKeys),
    fiberIDs: idList(s.fiberIDs),
  };
}

export function mapScopes(raw: unknown): ScopeRow[] {
  return mapList(raw, mapScope);
}

export function mapProvider(raw: unknown, index = 0): ProviderRow {
  const p = dto<RuntimeProviderDTO>(raw);
  const retiring = asObject(raw).retiring;
  return {
    key: `provider:${index}`,
    capabilityKey: text(p.key),
    state: text(p.state),
    retiring: retiring === true || retiring === "true",
    ownerFiberID: text(p.ownerFiberID),
    ownerActivationID: text(p.ownerActivationID),
    scopeID: text(p.scopeID),
  };
}

export function mapProviders(raw: unknown): ProviderRow[] {
  return mapList(raw, mapProvider);
}

export function mapDependency(raw: unknown, index = 0): DependencyRow {
  const d = dto<RuntimeDependencyDTO>(raw);
  return {
    key: `dependency:${index}`,
    capabilityKey: text(d.key),
    consumerFiberID: text(d.consumerFiberID),
    consumerActivationID: text(d.consumerActivationID),
    providerFiberID: text(d.providerFiberID),
    providerActivationID: text(d.providerActivationID),
    status: text(d.status),
    reason: text(d.reason),
  };
}

export function mapDependencies(raw: unknown): DependencyRow[] {
  return mapList(raw, mapDependency);
}

export function mapEffect(raw: unknown, index = 0): EffectRow {
  const e = dto<RuntimeEffectDTO>(raw);
  return {
    key: `effect:${index}`,
    ownerFiberID: text(e.ownerFiberID),
    activationID: text(e.activationID),
    seq: numberValue(e.seq),
    kind: text(e.kind),
    capabilityKey: text(e.key),
    state: text(e.state),
  };
}

export function mapEffects(raw: unknown): EffectRow[] {
  return mapList(raw, mapEffect);
}

export interface EffectFailureRow {
  key: string;
  fiberID: string;
  activationID: string;
  slotIndex: number;
  label: string;
  isInstall: boolean;
  exceptionType: string;
  message: string;
}

function mapEffectFailure(e: unknown, index: number): EffectFailureRow {
  const r = asObject(e);
  return {
    key: `effect-failure-${index}`,
    fiberID: text(r.fiberId),
    activationID: text(r.activationId),
    slotIndex: numberValue(r.slotIndex),
    label: text(r.label),
    isInstall: Boolean(r.isInstall),
    exceptionType: text(r.exceptionType),
    message: text(r.message),
  };
}

export function mapEffectFailures(raw: unknown): EffectFailureRow[] {
  return mapList(raw, mapEffectFailure);
}

// ---- query parsing ----------------------------------------------------------

export interface ParsedSnapshot {
  sequence: number;
  runtimeID: string;
  state: string;
  fibers: FiberRow[];
  scopes: ScopeRow[];
  providers: ProviderRow[];
  dependencies: DependencyRow[];
  effects: EffectRow[];
  effectFailures: EffectFailureRow[];
}

/** Parses the `runtime/snapshot` payload. Missing/null arrays become empty
 * tables, missing scalars become placeholders, and a defensive {data: ...}
 * wrapper is tolerated. Never throws on partial data. */
export function parseSnapshot(data: unknown): ParsedSnapshot {
  let payload: unknown = data;
  if (payload && typeof payload === "object" && !Array.isArray(payload)) {
    const outer = payload as Record<string, unknown>;
    if (!("fibers" in outer) && !("scopes" in outer) && "data" in outer) payload = outer.data;
  }
  const raw = asObject(payload);
  return {
    sequence: numberValue(raw.sequence),
    runtimeID: text(raw.runtimeID),
    state: text(raw.state),
    fibers: mapFibers(raw.fibers),
    scopes: mapScopes(raw.scopes),
    providers: mapProviders(raw.providers),
    dependencies: mapDependencies(raw.dependencies),
    effects: mapEffects(raw.effects),
    effectFailures: mapEffectFailures(raw.effectFailures),
  };
}
