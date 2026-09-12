// Projection seam. The observation stream only ever invalidates; it does
// not carry state. Projections are registered reducers that fold
// invalidation events into typed read models, and every consumer — a panel,
// a view block — subscribes to the projection or to a coarse domain counter
// instead of re-rendering on every raw event. Notifications coalesce in one
// 250ms window (snapshot batching), so a burst of file events costs one
// render pass, not one per event.
import { useSyncExternalStore } from "react";
import type { UIObservation } from "../types";

export interface Projection<S> {
  /** Stable identity; registering twice with one id replaces. */
  id: string;
  init: S;
  /** Events this projection ignores entirely (default: none). */
  interest?: (ev: UIObservation) => boolean;
  /** Pure fold: return the same reference when nothing changed. */
  apply(state: S, ev: UIObservation): S;
}

const projections = new Map<string, Projection<unknown>>();
const states = new Map<string, unknown>();
const projectionSubscribers = new Map<string, Set<() => void>>();

export function registerProjection<S>(p: Projection<S>): () => void {
  projections.set(p.id, p as Projection<unknown>);
  if (!states.has(p.id)) states.set(p.id, p.init);
  return () => {
    if (projections.get(p.id) === (p as Projection<unknown>)) {
      projections.delete(p.id);
      states.delete(p.id);
    }
  };
}

export function projectionState<S>(id: string): S | undefined {
  return states.get(id) as S | undefined;
}

export function subscribeProjection(id: string, fn: () => void): () => void {
  let set = projectionSubscribers.get(id);
  if (!set) {
    set = new Set();
    projectionSubscribers.set(id, set);
  }
  set.add(fn);
  return () => set!.delete(fn);
}

export function useProjection<S>(id: string): S | undefined {
  return useSyncExternalStore(
    (cb) => subscribeProjection(id, cb),
    () => projectionState<S>(id)
  );
}

// ---- domain counters -------------------------------------------------------
// Coarse interest keys derived from one event. "composition" covers
// composition.* lifecycle events; "collection" covers collection work;
// "source:<id>" scopes to one source unit. View blocks declare the domain
// they depend on and only re-query when THAT domain's version moves — a
// composition edit no longer re-fetches every data table.

export const ALL = "all";

export function domainsOf(ev: UIObservation): string[] {
  const out = [ALL];
  if (typeof ev.type === "string" && ev.type.startsWith("composition.")) {
    out.push("composition");
  } else {
    out.push("collection");
  }
  if (ev.sourceId) out.push(`source:${ev.sourceId}`);
  return out;
}

const versions = new Map<string, number>();
const domainSubscribers = new Map<string, Set<() => void>>();

export function domainVersion(domain: string): number {
  return versions.get(domain) ?? 0;
}

export function subscribeDomain(domain: string, fn: () => void): () => void {
  let set = domainSubscribers.get(domain);
  if (!set) {
    set = new Set();
    domainSubscribers.set(domain, set);
  }
  set.add(fn);
  return () => set!.delete(fn);
}

export function useDomainVersion(domain: string): number {
  return useSyncExternalStore(
    (cb) => subscribeDomain(domain, cb),
    () => domainVersion(domain)
  );
}

// ---- ingest + coalesced notification --------------------------------------

const coalesceMs = 250;
let timer: ReturnType<typeof setTimeout> | null = null;
const dirtyProjections = new Set<string>();
const dirtyDomains = new Set<string>();

function emit(subscribers: Map<string, Set<() => void>>, key: string) {
  const set = subscribers.get(key);
  if (!set) return;
  for (const fn of Array.from(set)) fn();
}

function flush() {
  timer = null;
  for (const id of dirtyProjections) emit(projectionSubscribers, id);
  dirtyProjections.clear();
  for (const d of dirtyDomains) {
    versions.set(d, domainVersion(d) + 1);
    emit(domainSubscribers, d);
  }
  dirtyDomains.clear();
}

/** ingestObservation folds one event into every interested projection and
 * marks its domains dirty; notification happens at most once per window. */
export function ingestObservation(ev: UIObservation): void {
  for (const p of projections.values()) {
    if (p.interest && !p.interest(ev)) continue;
    const prev = states.get(p.id);
    const next = p.apply(prev, ev);
    if (next !== prev) {
      states.set(p.id, next);
      dirtyProjections.add(p.id);
    }
  }
  for (const d of domainsOf(ev)) dirtyDomains.add(d);
  if (!timer) timer = setTimeout(flush, coalesceMs);
}

/** testFlush forces the pending coalesced notification (vitest helper). */
export function testFlush(): void {
  if (timer) {
    clearTimeout(timer);
    flush();
  }
}
