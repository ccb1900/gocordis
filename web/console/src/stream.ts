import { useEffect, useState } from "react";
import type { StreamStatus, UIObservation } from "./types";

// One shared SSE connection per page. Observation only invalidates; state
// always comes from re-running named queries.
let source: EventSource | null = null;
const handlers = new Set<(ev: UIObservation) => void>();
const statusHandlers = new Set<(s: StreamStatus) => void>();

function report() {
  const s: StreamStatus =
    source?.readyState === 1 ? "live" : source?.readyState === 2 ? "offline" : "connecting";
  for (const fn of statusHandlers) fn(s);
}

function ensure() {
  if (source) return;
  source = new EventSource("/api/stream");
  source.addEventListener("observation", (e: MessageEvent) => {
    try {
      const ev = JSON.parse(String(e.data)) as UIObservation;
      for (const fn of handlers) fn(ev);
    } catch {
      /* ignore malformed frame */
    }
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
