// Projection seam tests: folds, interest filters, domain counters and the
// coalesced notification window — all as pure store operations (no React).
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ALL,
  domainVersion,
  domainsOf,
  ingestObservation,
  projectionState,
  registerProjection,
  subscribeDomain,
  subscribeProjection,
  testFlush,
  type Projection,
} from "./projections";
import type { UIObservation } from "../types";

const ev = (over: Partial<UIObservation> = {}): UIObservation => ({
  type: "FileCompleted",
  sourceId: "production-source",
  timestamp: "2026-09-08T10:00:00Z",
  ...over,
});

beforeEach(() => {
  vi.useFakeTimers();
});

afterEach(() => {
  vi.useRealTimers();
});

describe("projection fold", () => {
  it("folds events into the registered read model", () => {
    const p: Projection<string[]> = {
      id: "test.fold",
      init: [],
      apply: (prev, e) => [...prev, e.type],
    };
    const stop = registerProjection(p);
    ingestObservation(ev());
    testFlush();
    expect(projectionState<string[]>("test.fold")).toEqual(["FileCompleted"]);
    stop();
    expect(projectionState("test.fold")).toBeUndefined();
  });

  it("respects the interest filter", () => {
    const p: Projection<number> = {
      id: "test.interested",
      init: 0,
      interest: (e) => e.type === "composition.changed",
      apply: (prev) => prev + 1,
    };
    const stop = registerProjection(p);
    ingestObservation(ev({ type: "FileCompleted" }));
    testFlush();
    expect(projectionState("test.interested")).toBe(0);
    ingestObservation(ev({ type: "composition.changed" }));
    testFlush();
    expect(projectionState("test.interested")).toBe(1);
    stop();
  });

  it("keeps the reference when the fold changes nothing", () => {
    const p: Projection<number> = { id: "test.same", init: 7, apply: () => 7 };
    const stop = registerProjection(p);
    let notified = 0;
    const unsubscribe = subscribeProjection("test.same", () => notified++);
    ingestObservation(ev());
    testFlush();
    expect(notified).toBe(0);
    unsubscribe();
    stop();
  });
});

describe("domain counters", () => {
  it("routes composition events to the composition domain, others to collection", () => {
    expect(domainsOf(ev({ type: "composition.changed" }))).toContain("composition");
    expect(domainsOf(ev({ type: "composition.failed" }))).toContain("composition");
    expect(domainsOf(ev({ type: "CollectionCompleted" }))).toContain("collection");
    expect(domainsOf(ev())).toContain(ALL);
    expect(domainsOf(ev())).toContain("source:production-source");
  });

  it("bumps only the touched domains, coalesced per window", () => {
    let notifications = 0;
    const stopCollection = subscribeDomain("collection", () => notifications++);
    const stopComposition = subscribeDomain("composition", () => notifications++);
    // Domain counters are module-global across tests: assert deltas.
    const collectionBefore = domainVersion("collection");
    const compositionBefore = domainVersion("composition");

    ingestObservation(ev({ type: "FileCompleted" }));
    ingestObservation(ev({ type: "FileFailed" }));
    vi.advanceTimersByTime(300);

    expect(domainVersion("collection")).toBe(collectionBefore + 1); // burst coalesced to one bump
    expect(domainVersion("composition")).toBe(compositionBefore);
    expect(notifications).toBe(1);

    ingestObservation(ev({ type: "composition.changed", sourceId: undefined }));
    vi.advanceTimersByTime(300);
    expect(domainVersion("composition")).toBe(compositionBefore + 1);
    expect(notifications).toBe(2);
    stopCollection();
    stopComposition();
  });
});
