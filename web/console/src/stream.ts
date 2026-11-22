import { useEffect, useState } from "react";
import type {
  PluginStateEvent, StreamStatus, UIManifestEvent, UIObservation,
} from "./types";
import { ingestObservation, ingestPluginState, ingestUIManifest } from "./lib/projections";

// One shared SSE connection per page. `observation` only invalidates; the
// control plane also emits `plugin-state` (plugin list / runtime snapshot)
// and `ui-manifest` (composition + client modules). State always comes from
// named queries — the frame carries just a revision and an action label.
let source: EventSource | null = null;
const handlers = new Set<(ev: UIObservation) => void>();
const statusHandlers = new Set<(s: StreamStatus) => void>();
const pluginHandlers = new Set<(ev: PluginStateEvent) => void>();
const manifestHandlers = new Set<(ev: UIManifestEvent) => void>();

function report() {
  const s: StreamStatus =
    source?.readyState === 1 ? "live" : source?.readyState === 2 ? "offline" : "connecting";
  for (const fn of statusHandlers) fn(s);
}

function frame(e: Event): Record<string, unknown> | null {
  try {
    const parsed = JSON.parse(String((e as MessageEvent).data)) as unknown;
    return parsed && typeof parsed === "object" && !Array.isArray(parsed)
      ? (parsed as Record<string, unknown>)
      : null;
  } catch {
    return null;
  }
}

function pluginFrame(e: Event): PluginStateEvent {
  const raw = frame(e) ?? {};
  const revision = Number(raw.revision);
  return {
    revision: Number.isFinite(revision) ? revision : 0,
    action: typeof raw.action === "string" ? raw.action : "",
  };
}

function manifestFrame(e: Event): UIManifestEvent {
  const raw = frame(e) ?? {};
  const revision = Number(raw.revision);
  return {
    revision: Number.isFinite(revision) ? revision : undefined,
    action: typeof raw.action === "string" ? raw.action : undefined,
  };
}

function ensure() {
  if (source) return;
  source = new EventSource("/api/stream");
  source.addEventListener("observation", (e: Event) => {
    try {
      const ev = JSON.parse(String((e as MessageEvent).data)) as UIObservation;
      ingestObservation(ev);
      for (const fn of handlers) fn(ev);
    } catch {
      /* ignore malformed frame */
    }
  });
  source.addEventListener("plugin-state", (e: Event) => {
    const ev = pluginFrame(e);
    ingestPluginState(ev);
    for (const fn of pluginHandlers) fn(ev);
  });
  source.addEventListener("ui-manifest", (e: Event) => {
    const ev = manifestFrame(e);
    ingestUIManifest(ev);
    for (const fn of manifestHandlers) fn(ev);
  });
  source.onopen = report;
  source.onerror = report;
}

export function onObservation(fn: (ev: UIObservation) => void): () => void {
  handlers.add(fn);
  ensure();
  return () => handlers.delete(fn);
}

export function onStreamStatus(fn: (s: StreamStatus) => void): () => void {
  statusHandlers.add(fn);
  ensure();
  return () => statusHandlers.delete(fn);
}

export function onPluginState(fn: (ev: PluginStateEvent) => void): () => void {
  pluginHandlers.add(fn);
  ensure();
  return () => pluginHandlers.delete(fn);
}

export function onUIManifest(fn: (ev: UIManifestEvent) => void): () => void {
  manifestHandlers.add(fn);
  ensure();
  return () => manifestHandlers.delete(fn);
}

// Observation bursts (a collection touching many files) coalesce into one
// generation bump per 250ms window so every view does not re-query per event.
let bumpTimer: ReturnType<typeof setTimeout> | null = null;

export function useObservationGeneration(): number {
  const [gen, setGen] = useState(0);
  useEffect(
    () =>
      onObservation(() => {
        if (bumpTimer) return;
        bumpTimer = setTimeout(() => {
          bumpTimer = null;
          setGen((g) => g + 1);
        }, 250);
      }),
    []
  );
  return gen;
}

export function useStreamStatus(): StreamStatus {
  const [status, setStatus] = useState<StreamStatus>("connecting");
  useEffect(() => onStreamStatus(setStatus), []);
  return status;
}
