import { describe, expect, it } from "vitest";
import {
  buildDeclaredTree, buildRuntimeTree, buildTree, nextView, normalizeView,
  type InsightGraphDTO,
} from "./insight-graph";

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
      status: "Pending", reason: "依赖 codec 缺失",
    }],
  },
  sequence: 42,
};

describe("declared tree mapping", () => {
  it("maps nodes and edges into antd-ready tree nodes", () => {
    const tree = buildDeclaredTree(dto);
    expect(tree).toHaveLength(2);
    expect(tree[0].key).toBe("d-node:alpha");
    expect(tree[0].label).toContain("Alpha");
    expect(tree[0].children).toHaveLength(1);
    expect(tree[0].children![0].label).toContain("codec");
    expect(tree[0].children![0].label).toContain("Codec"); // resolved via node id
    expect(tree[0].detail).toContain("provides=[alpha.out]");
    expect(tree[1].children).toEqual([]);
  });

  it("keeps edges whose target is absent from the node list", () => {
    const orphan = { declared: { nodes: [], edges: [{ from: "x", to: "ghost", key: "k" }] } };
    expect(buildDeclaredTree(orphan)).toEqual([]); // no node to hang the edge on
    const withNode: InsightGraphDTO = {
      ...dto,
      declared: {
        nodes: [{ id: "x", name: "X", type: "t", requires: [], provides: [] }],
        edges: orphan.declared.edges,
      },
    };
    expect(buildDeclaredTree(withNode)[0].children![0].detail).toBe("目标节点不在声明图中");
  });
});

describe("runtime tree mapping", () => {
  it("surfaces state, activationID, scopeID and the Pending reason", () => {
    const tree = buildRuntimeTree(dto);
    expect(tree).toHaveLength(2);
    const pending = tree[0];
    expect(pending.status).toBe("Pending");
    expect(pending.detail).toContain("fiberID=f-1");
    expect(pending.detail).toContain("activationID=a-7");
    expect(pending.detail).toContain("scopeID=s-3");
    expect(pending.detail).toContain("Pending 原因: 依赖 codec 缺失");
    expect(pending.children![0].status).toBe("Pending");
    expect(pending.children![0].detail).toContain("依赖 codec 缺失");
    expect(tree[1].status).toBe("Active");
  });

  it("finds a reason even when the pending fiber is the provider", () => {
    const reversed: InsightGraphDTO = {
      ...dto,
      runtime: {
        nodes: [{ ...dto.runtime.nodes[1], state: "Pending" }],
        edges: [{ ...dto.runtime.edges[0], consumerFiberID: "f-9", providerFiberID: "f-2" }],
      },
    };
    const tree = buildRuntimeTree(reversed);
    expect(tree[0].status).toBe("Pending");
    expect(tree[0].detail).toContain("Pending 原因: 依赖 codec 缺失");
  });
});

describe("wire-shape tolerance", () => {
  it("accepts numeric ids and the Go query's lower-cased aliases", () => {
    const goShaped = {
      eventSequence: 9,
      declared: { nodes: [], edges: [] },
      runtime: {
        nodes: [{
          fiberId: 12, name: "Alpha", state: "Pending", activationId: 3, scopeId: 4,
          provides: [], requires: ["codec"],
        }],
        edges: [{
          consumerFiberId: 12, providerFiberId: 0, key: "codec", status: "Waiting",
          reason: "缺少 codec",
        }],
      },
    } as unknown as InsightGraphDTO;
    const tree = buildRuntimeTree(goShaped);
    expect(tree[0].key).toBe("r-node:12");
    expect(tree[0].detail).toContain("fiberID=12");
    expect(tree[0].detail).toContain("activationID=3");
    expect(tree[0].detail).toContain("scopeID=4");
    expect(tree[0].detail).toContain("Pending 原因: 缺少 codec");
    expect(tree[0].children).toHaveLength(1);
  });
});

describe("toggle selection", () => {
  it("selects the matching view and flips deterministically", () => {
    expect(buildTree(dto, "declared")[0].key.startsWith("d-node")).toBe(true);
    expect(buildTree(dto, "runtime")[0].key.startsWith("r-node")).toBe(true);
    expect(nextView("declared")).toBe("runtime");
    expect(nextView("runtime")).toBe("declared");
    expect(normalizeView("runtime")).toBe("runtime");
    expect(normalizeView("unexpected")).toBe("declared");
  });
});

describe("missing data handling", () => {
  it("returns an empty tree for null, empty or partial payloads", () => {
    expect(buildTree(null, "declared")).toEqual([]);
    expect(buildTree(undefined, "runtime")).toEqual([]);
    expect(buildTree({}, "declared")).toEqual([]);
    expect(buildTree({}, "runtime")).toEqual([]);
    expect(buildTree({ declared: null } as never, "declared")).toEqual([]);
    expect(buildTree({ runtime: { nodes: null } } as never, "runtime")).toEqual([]);
  });
});
