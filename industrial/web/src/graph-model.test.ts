import { describe, expect, it } from "vitest";
import {
  edgeId, graphBehaviors, graphOptions, positionsFromG6, workflowToG6,
} from "./graph-model";
import type { NodeDescriptor, WorkflowDefinition } from "./types";

const catalog = new Map<string, NodeDescriptor>([
  ["industrial.filesystem.local", {
    nodeTypeId: "industrial.filesystem.local", name: "Local Files", description: "",
    category: "Filesystem.source", icon: "FolderOutlined", version: "1.0.0",
    inputs: [], outputs: [{ name: "files", type: "FileBatch", required: false }],
    configurationSchema: {}, activityType: "", dependencies: [],
  }],
  ["industrial.reader.csv", {
    nodeTypeId: "industrial.reader.csv", name: "CSV Reader", description: "",
    category: "Parsing.reader", icon: "FileTextOutlined", version: "1.0.0",
    inputs: [{ name: "input", type: "FileBatch", required: true }],
    outputs: [{ name: "records", type: "RecordSet", required: false }],
    configurationSchema: {}, activityType: "", dependencies: [],
  }],
]);

const definition: WorkflowDefinition = {
  workflowId: "daily-collection", name: "Daily", version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "share" }, x: 40, y: 60 },
    { id: "rd", type: "industrial.reader.csv", config: { encoding: "utf-8" }, x: 240, y: 60 },
  ],
  edges: [{ source: "src", sourcePort: "files", target: "rd", targetPort: "input" }],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: ["share"],
  layout: {},
};

describe("WorkflowDefinition → G6 projection", () => {
  it("maps nodes/edges with stable ids, labels, ports and layout positions", () => {
    const graph = workflowToG6(definition, catalog);
    expect(graph.nodes).toHaveLength(2);
    expect(graph.edges).toHaveLength(1);
    expect(graph.nodes[0]).toMatchObject({
      id: "src",
      data: { label: "Local Files", nodeTypeId: "industrial.filesystem.local", category: "Filesystem.source" },
      style: { x: 40, y: 60 },
    });
    expect(graph.nodes[1].data.config).toEqual({ encoding: "utf-8" });
    expect(graph.edges[0]).toMatchObject({
      source: "src", target: "rd",
      data: { label: "files → input", sourcePort: "files", targetPort: "input" },
    });
  });

  it("produces index-independent, repeatable edge ids", () => {
    const edge = definition.edges[0];
    expect(edgeId(edge)).toBe("e:src:files>rd:input");
    expect(edgeId(edge)).toBe(edgeId({ ...edge }));
    expect(edgeId({ ...edge, targetPort: "records" })).not.toBe(edgeId(edge));
    const graph = workflowToG6(definition, catalog);
    expect(graph.edges[0].id).toBe(edgeId(edge));
  });

  it("never mutates the domain definition and tolerates unknown node types", () => {
    const before = JSON.stringify(definition);
    const graph = workflowToG6({ ...definition, nodes: [
      { id: "weird", type: "not.registered", config: {}, x: 0, y: 0 },
    ] }, catalog);
    expect(graph.nodes[0].data.label).toBe("not.registered");
    expect(JSON.stringify(definition)).toBe(before);
  });

  it("reads back only allowed positions with finite coordinates", () => {
    const graph = workflowToG6(definition, catalog);
    graph.nodes.push({ id: "foreign", data: {}, style: { x: 1, y: 2 } });
    expect(positionsFromG6(graph, new Set(["src"]))).toEqual({ src: { x: 40, y: 60 } });
  });

  it("commits only the nodes G6 actually moved", () => {
    const graph = workflowToG6(definition, catalog);
    expect(positionsFromG6(graph, new Set(["src", "rd"]), ["rd"]))
      .toEqual({ rd: { x: 240, y: 60 } });
  });
});

describe("G6 render options", () => {
  it("ALWAYS carries the real container (the blank-canvas regression)", () => {
    const container = { nodeName: "DIV" } as unknown as HTMLElement;
    const options = graphOptions({ nodes: [], edges: [] }, container);
    expect(options.container).toBe(container);
    expect(options.container).not.toBe("container");
    expect(options.behaviors).toContain("zoom-canvas");
    const types = options.behaviors
      .map((item: unknown) => (item as { type?: string })?.type)
      .filter(Boolean);
    expect(types).toContain("drag-canvas");
    expect(types).toContain("create-edge");
    expect(types).toContain("drag-element");
  });

  it("owns the pointer exclusively: pan only in pan mode, connect only in connect mode", () => {
    let mode: any = "select";
    const behaviors: any[] = graphBehaviors({ mode: () => mode });
    const canvasPan = behaviors.find((item) => item?.type === "drag-canvas");
    const dragElement = behaviors.find((item) => item?.type === "drag-element");
    const createEdge = behaviors.find((item) => item?.type === "create-edge");
    expect(canvasPan.enable({ targetType: "canvas" })).toBe(false);
    expect(dragElement.enable({ targetType: "node" })).toBe(true);
    expect(createEdge.enable()).toBe(false);

    mode = "pan";
    expect(canvasPan.enable({ targetType: "canvas" })).toBe(true);
    expect(canvasPan.enable({ targetType: "node" })).toBe(false);
    expect(dragElement.enable({ targetType: "node" })).toBe(false);

    mode = "connect";
    expect(createEdge.enable()).toBe(true);
    expect(dragElement.enable({ targetType: "node" })).toBe(false);
    expect(canvasPan.enable({ targetType: "canvas" })).toBe(false);
  });
});
