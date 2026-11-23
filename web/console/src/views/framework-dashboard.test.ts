// @vitest-environment node
// Pure mapping/count tests for the framework dashboard + event timeline.
import { describe, expect, it } from "vitest";
import {
  mapCounts, mapDiagnostic, mapEvent, mapEvents, parseDashboard,
  parseEvents, resolveEvents,
} from "./framework-dashboard";
import { mapFiber } from "./runtime-inspector";

const dashboard = () => ({
  sequence: 12,
  state: "Degraded",
  counts: {
    Failed: 2, pending: 3, active: 7, fibers: 12,
    providers: 4, effects: 31, notANumber: "n/a",
  },
  recentEvents: [{
    sequence: 11, timestamp: "2026-11-21T10:00:00Z", type: "ActivationActive",
    fiberID: 7, activationID: 9, data: { State: "Active" },
  }],
  diagnostics: [
    { level: "error", code: "E1", message: "Apply failed", detail: "boom" },
    "plain diagnostic",
  ],
});

describe("event mapping", () => {
  it("maps runtime/events rows including stringified payloads", () => {
    const p = parseEvents({
      sequence: "5",
      events: [
        { sequence: "4", timestamp: "t", type: "Failure", fiberID: 1, activationID: 0, data: { Phase: "Apply", Err: "x" } },
        { sequence: 3, type: "EffectUndone" },
      ],
    });
    expect(p.sequence).toBe(5);
    expect(p.events[0]).toMatchObject({
      sequence: 4, timestamp: "t", type: "Failure", fiberID: "1", activationID: "0",
      data: JSON.stringify({ Phase: "Apply", Err: "x" }),
    });
    expect(p.events[1]).toMatchObject({ sequence: 3, type: "EffectUndone", data: "" });
    expect(new Set(p.events.map((e) => e.key)).size).toBe(2);
  });

  it("skips invalid entries and handles missing data defensively", () => {
    expect(mapEvents(null)).toEqual([]);
    expect(mapEvents([null, 7, "x", { type: "FiberCreated" }])).toHaveLength(1);
    expect(mapEvent({})).toMatchObject({
      sequence: 0, timestamp: "—", type: "—", fiberID: "—", activationID: "—", data: "",
    });
    expect(parseEvents(null)).toEqual({ events: [], sequence: 0 });
    expect(parseEvents({ data: { events: [{ type: "X" }] } }).events).toHaveLength(1);
  });

  it("prefers the dedicated event query over the dashboard fallback", () => {
    const primary = mapEvents([{ type: "A" }]);
    const fallback = mapEvents([{ type: "B" }]);
    expect(resolveEvents(primary, fallback)[0].type).toBe("A");
    expect(resolveEvents([], fallback)[0].type).toBe("B");
  });
});

describe("counts mapping", () => {
  it("maps failed/pending prominently with alias keys and keeps a numeric strip", () => {
    const counts = mapCounts(dashboard().counts);
    expect(counts).toMatchObject({ failed: 2, pending: 3, active: 7, total: 12 });
    expect(counts.items).toContainEqual({ key: "providers", label: "providers", value: 4 });
    expect(counts.items.some((i) => i.key === "notANumber")).toBe(false);
  });

  it("falls back to fiber states when explicit counts are missing", () => {
    const fibers = [
      mapFiber({ state: "Failed" }), mapFiber({ state: "Failed" }),
      mapFiber({ state: "Pending" }), mapFiber({ state: "Active" }), mapFiber({}),
    ];
    expect(mapCounts(undefined, fibers)).toMatchObject({ failed: 2, pending: 1, active: 1, total: 5 });
    expect(mapCounts({ failed: 9 }, fibers)).toMatchObject({ failed: 9, pending: 1, active: 1, total: 5 });
    expect(mapCounts(null)).toMatchObject({ failed: 0, pending: 0, active: 0, total: 0 });
  });
});

describe("dashboard + diagnostics mapping", () => {
  it("maps the full dashboard payload", () => {
    const p = parseDashboard(dashboard());
    expect(p).toMatchObject({ sequence: 12, state: "Degraded", counts: { failed: 2, pending: 3 } });
    expect(p.recentEvents[0]).toMatchObject({ type: "ActivationActive", fiberID: "7" });
    expect(p.diagnostics[0]).toMatchObject({ level: "error", message: "Apply failed", code: "E1", detail: "boom" });
    expect(p.diagnostics[1]).toMatchObject({ level: "info", message: "plain diagnostic" });
  });

  it("normalizes diagnostic shapes and never throws on partial data", () => {
    expect(mapDiagnostic({ severity: "warn", detail: "slow", component: "alpha" })).toMatchObject({
      level: "warn", message: "slow", source: "alpha", code: "—",
    });
    expect(mapDiagnostic(null)).toMatchObject({ message: "—", level: "info" });
    expect(parseDashboard(null)).toMatchObject({
      sequence: 0, state: "—", counts: { failed: 0, pending: 0 }, recentEvents: [], diagnostics: [],
    });
    expect(parseDashboard({ data: dashboard() }).counts.failed).toBe(2);
  });
});

// ---- C# wire contract regression ------------------------------------------
// The .NET ConsoleHost initially returned a FLAT {total,active,...} dashboard
// and no runtime/events query at all. The API looked healthy but every page
// rendered zeros / "no events" because parseDashboard/parseEvents expect the
// canonical Go DTO shapes. Feed the exact C# payload through the real parsers:
// this is the missing end-to-end link between backend and rendered page.
describe("dotnet console wire contract", () => {
  it("parses the actual C# framework/dashboard payload into non-zero counts", () => {
    const wire = {
      sequence: 18,
      state: "active",
      counts: { fibers: 6, active: 6, pending: 0, failed: 0, providers: 10, dependencies: 9, effects: 8 },
      recentEvents: [
        { sequence: 11, timestamp: "2026-11-23T03:52:58Z", type: "ActivationLoading", fiberID: "f-1", activationID: 4 },
      ],
      diagnostics: [],
    };
    const parsed = parseDashboard(wire);
    expect(parsed.counts.active).toBe(6);
    expect(parsed.counts.total).toBe(6);
    expect(parsed.recentEvents).toHaveLength(1);
  });

  it("parses the actual C# runtime/events payload (GUID fiber ids, null data)", () => {
    const wire = {
      events: [
        { sequence: 1, timestamp: "2026-11-23T03:52:58Z", type: "FiberCreated", fiberID: "cd4ac0d2", activationID: 0, data: null },
        { sequence: 2, timestamp: "2026-11-23T03:52:58Z", type: "ActivationLoading", fiberID: "cd4ac0d2", activationID: 1, data: null },
      ],
      sequence: 18,
    };
    const parsed = parseEvents(wire);
    expect(parsed.sequence).toBe(18);
    expect(parsed.events).toHaveLength(2);
    expect(parsed.events[0].fiberID).toBe("cd4ac0d2");
  });
});
