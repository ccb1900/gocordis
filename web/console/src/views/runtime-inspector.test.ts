// @vitest-environment node
// Pure DTO → table-row tests for the runtime/snapshot inspector. No React,
// antd or DOM involved.
import { describe, expect, it } from "vitest";
import {
  mapDependency, mapEffect, mapFiber, mapProvider, mapScope,
  parseSnapshot,
} from "./runtime-inspector";

const snapshot = () => ({
  sequence: 42,
  runtimeID: "rt-1",
  state: "Running",
  fibers: [{
    id: 7, name: "alpha", component: "Alpha", state: "Failed", intent: "Stop",
    err: "Apply exploded", activationID: 9, scopeID: 3,
    parentFiberIDs: [1, 2], childFiberIDs: [8],
  }],
  scopes: [{ id: 3, parentID: 0, providerKeys: ["codec", "clock"], fiberIDs: [7, 8] }],
  providers: [{
    key: "codec", state: "Active", retiring: true,
    ownerFiberID: 7, ownerActivationID: 9, scopeID: 3,
  }],
  dependencies: [{
    key: "codec", consumerFiberID: 8, consumerActivationID: 10,
    providerFiberID: 7, providerActivationID: 9,
    status: "Withdrawn", reason: "Provider unavailable",
  }],
  effects: [{
    ownerFiberID: 7, activationID: 9, seq: 4,
    kind: "Provider", key: "codec", state: "Undoing",
  }],
  effectFailures: [{
    fiberId: 7, activationId: 9, slotIndex: 1, label: "open-resource",
    isInstall: true, exceptionType: "InvalidOperationException", message: "install blew up",
  }],
});

describe("DTO → table rows", () => {
  it("maps every snapshot section, keeping identity, state and diagnostic fields", () => {
    const p = parseSnapshot(snapshot());
    expect(p).toMatchObject({ sequence: 42, runtimeID: "rt-1", state: "Running" });

    expect(p.fibers).toHaveLength(1);
    expect(p.fibers[0]).toMatchObject({
      id: "7", name: "alpha", component: "Alpha", state: "Failed", intent: "Stop",
      err: "Apply exploded", activationID: "9", scopeID: "3",
      parentFiberIDs: "1, 2", childFiberIDs: "8",
    });
    expect(p.scopes[0]).toMatchObject({
      id: "3", parentID: "0", providerKeys: "codec, clock", fiberIDs: "7, 8",
    });
    expect(p.providers[0]).toMatchObject({
      capabilityKey: "codec", state: "Active", retiring: true,
      ownerFiberID: "7", ownerActivationID: "9", scopeID: "3",
    });
    expect(p.dependencies[0]).toMatchObject({
      capabilityKey: "codec", consumerFiberID: "8", consumerActivationID: "10",
      providerFiberID: "7", providerActivationID: "9",
      status: "Withdrawn", reason: "Provider unavailable",
    });
    expect(p.effects[0]).toMatchObject({
      ownerFiberID: "7", activationID: "9", seq: 4,
      kind: "Provider", capabilityKey: "codec", state: "Undoing",
    });

    expect(p.effectFailures).toEqual([{
      key: "effect-failure-0", fiberID: "7", activationID: "9", slotIndex: 1,
      label: "open-resource", isInstall: true,
      exceptionType: "InvalidOperationException", message: "install blew up",
    }]);
  });

  it("uses unique row keys even when identity fields are absent", () => {
    const p = parseSnapshot({
      fibers: [{ id: 1 }, {}, { id: 1 }],
      providers: [{ key: "codec" }, { key: "codec" }],
    });
    expect(new Set(p.fibers.map((r) => r.key)).size).toBe(3);
    expect(new Set(p.providers.map((r) => r.key)).size).toBe(2);
    expect(p.fibers[0].key).toBe("fiber:1");
  });

  it("renders missing fields as placeholders and coerces boolean-ish retiring", () => {
    expect(mapFiber({})).toMatchObject({
      id: "—", name: "—", state: "—", intent: "—", err: "—",
      activationID: "—", scopeID: "—", parentFiberIDs: "—", childFiberIDs: "—",
    });
    expect(mapScope({}).providerKeys).toBe("—");
    expect(mapProvider({ retiring: "true" }).retiring).toBe(true);
    expect(mapProvider({ retiring: false }).retiring).toBe(false);
    expect(mapDependency({}).reason).toBe("—");
    expect(mapEffect({ seq: "nope" }).seq).toBe(0);
  });
});

describe("missing-data handling", () => {
  it("never throws on absent, null, primitive or wrapped payloads", () => {
    const empty = { sequence: 0, runtimeID: "—", state: "—", fibers: [], scopes: [], providers: [], dependencies: [], effects: [], effectFailures: [] };
    expect(parseSnapshot(null)).toEqual(empty);
    expect(parseSnapshot(undefined)).toEqual(empty);
    expect(parseSnapshot({})).toEqual(empty);
    expect(parseSnapshot("oops")).toEqual(empty);
    expect(parseSnapshot({ data: { fibers: [{ id: 1 }], sequence: 5 } }).fibers).toHaveLength(1);
  });

  it("treats non-array sections and invalid entries as empty", () => {
    const p = parseSnapshot({
      fibers: null, scopes: "no", providers: {}, dependencies: [null, 3, { key: "ok" }], effects: [{}, [1]],
    });
    expect(p.fibers).toEqual([]);
    expect(p.scopes).toEqual([]);
    expect(p.providers).toEqual([]);
    expect(p.dependencies).toHaveLength(1);
    expect(p.dependencies[0].capabilityKey).toBe("ok");
    expect(p.effects).toHaveLength(1);
    expect(p.effectFailures).toEqual([]);
  });
});
