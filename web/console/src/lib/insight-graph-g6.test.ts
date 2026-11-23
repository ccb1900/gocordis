import { describe, expect, it } from "vitest";
import { buildDeclaredGraph, buildGraph, buildRuntimeGraph } from "./insight-graph-g6";
import type { InsightGraphDTO } from "./insight-graph";

const dto: InsightGraphDTO = {
  declared: {
    nodes: [
      { id: "alpha", name: "Alpha", type: "alpha", requires: ["codec"], provides: ["alpha.out"] },
      { id: "codec", name: "Codec", type: "codec", requires: [], provides: ["codec"] },
    ],
    edges: [{ from: "alpha", to: "codec", key: "codec" }],
  },
  runtime: {
    nodes: [
      {
        fiberID: "f-1", name: "Alpha", state: "Pending", activationID: "a-7", scopeID: "s-3",
        provides: [], requires: ["codec"],
      },
      {
        fiberID: "f-2", name: "Codec", state: "Active", activationID: "a-2", scopeID: "s-3",
        provides: ["codec"], requires: [],
      },
    ],
    edges: [{
      consumerFiberID: "f-1", providerFiberID: "f-2", key: "codec",
      status: "Waiting", reason: "依赖 codec 缺失",
    }],
  },
  eventSequence: 42,
};

describe("DTO → AntV G6 graph mapping", () => {
  it("maps the declared view into G6 nodes and key edges", () => {
    const g = buildDeclaredGraph(dto);
    expect(g.nodes).toHaveLength(2);
    expect(g.nodes[0]).toMatchObject({ id: "alpha", data: { type: "declaration" } });
    expect(g.edges).toHaveLength(1);
    expect(g.edges[0]).toMatchObject({ source: "alpha", target: "codec", data: { key: "codec", satisfied: true } });
  });

  it("keeps a dangling declared edge visible and styled as failed", () => {
    const g = buildDeclaredGraph({
      declared: {
        nodes: [{ id: "alpha", name: "A", type: "a", requires: ["gone"], provides: [] }],
        edges: [{ from: "alpha", to: "gone", key: "gone" }],
      },
    } as unknown as InsightGraphDTO);
    expect(g.edges[0]).toMatchObject({ target: "gone", data: { satisfied: false } });
    expect(g.edges[0].style?.stroke).toBe("#f5222d");
  });

  it("maps the runtime view with fiber states and status-coloured edges", () => {
    const g = buildRuntimeGraph(dto);
    expect(g.nodes).toHaveLength(2);
    expect(g.nodes.map((n) => n.data.state)).toEqual(["Pending", "Active"]);
    expect(g.nodes[0].style?.fill).toBe("#faad14");
    expect(g.nodes[1].style?.fill).toBe("#52c41a");
    expect(g.edges[0]).toMatchObject({ source: "f-1", target: "f-2" });
    expect(g.edges[0].data).toMatchObject({ status: "Waiting", fromName: "Alpha", toName: "Codec" });
  });

  it("accepts lower-cased Go field aliases and partial DTOs defensively", () => {
    const g = buildGraph({
      runtime: {
        nodes: [{ fiberId: "f-9", name: "N", state: "Active", activationId: 1, scopeId: 2, provides: [], requires: [] }],
        edges: [{ consumerFiberId: "f-9", providerFiberId: "f-8", key: "k", status: "Satisfied" }],
      },
    } as unknown as InsightGraphDTO, "runtime");
    expect(g.nodes[0].id).toBe("f-9");
    expect(g.edges[0]).toMatchObject({ source: "f-9", target: "f-8" });
    expect(buildGraph(null, "declared")).toEqual({ nodes: [], edges: [] });
  });
});
