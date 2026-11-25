// @vitest-environment jsdom
import { Graph } from "@antv/g6";
import { Renderer as SVGRenderer } from "@antv/g-svg";
import { describe, expect, it, vi } from "vitest";
import { graphBehaviors, workflowToG6 } from "./graph-model";
import { addEdge, emptyWorkflow, resolveEdgePorts } from "./workflow-model";
import type { NodeDescriptor, WorkflowDefinition } from "./types";

// G6 measures text through an offscreen 2D canvas. jsdom has no real canvas;
// this minimal context is enough for the SVG renderer's layout pass.
(HTMLCanvasElement.prototype as any).getContext = () => ({
  font: "", fillStyle: "", textBaseline: "",
  measureText: (text: string) => ({ width: text.length * 6 }),
  fillRect: () => undefined,
  fillText: () => undefined,
  getImageData: (_x: number, _y: number, w: number, h: number) =>
    ({ data: new Uint8ClampedArray(Math.max(1, w * h * 4)) }),
});

const descriptor = (
  id: string, inputs: Array<[string, string]>, outputs: Array<[string, string]>
): NodeDescriptor => ({
  nodeTypeId: id, name: id, description: "", category: "test", icon: "",
  version: "1.0.0",
  inputs: inputs.map(([name, type]) => ({ name, type: type as any, required: true })),
  outputs: outputs.map(([name, type]) => ({ name, type: type as any, required: true })),
  configurationSchema: { properties: {} }, activityType: id, dependencies: [],
});

const catalog = new Map([
  ["source", descriptor("source", [], [["files", "FileBatch"]])],
  ["reader", descriptor("reader", [["input", "FileBatch"]], [["records", "RecordSet"]])],
  ["sink", descriptor("sink", [["records", "RecordSet"]], [])],
]);

const definition = (): WorkflowDefinition => ({
  ...emptyWorkflow("interaction"),
  nodes: [
    { id: "a", type: "source", config: {}, x: 0, y: 0 },
    { id: "b", type: "reader", config: {}, x: 200, y: 0 },
    { id: "c", type: "sink", config: {}, x: 400, y: 0 },
  ],
});

async function makeGraph(options: {
  connectMode: boolean;
  initial: WorkflowDefinition;
  onCreateEdge: (source: string, target: string) => { sourcePort: string; targetPort: string } | null;
}) {
  const container = document.createElement("div");
  container.style.width = "800px";
  container.style.height = "600px";
  document.body.appendChild(container);
  let current = options.initial;
  let graph: Graph | null = null;
  graph = new Graph({
    container,
    renderer: () => new SVGRenderer(),
    data: workflowToG6(current, catalog),
    node: { type: "rect", style: { size: [140, 42], labelText: (d: any) => d?.data?.label ?? "" } },
    behaviors: graphBehaviors({
      graph: { getNodeData: () => graph?.getNodeData() ?? [] },
      allowedIds: () => new Set(current.nodes.map((node) => node.id)),
      connectMode: () => options.connectMode,
      onCreateEdge: options.onCreateEdge,
    }),
  } as any);
  await graph.render();
  return { graph: graph!, getDefinition: () => current, apply: (next: WorkflowDefinition) => { current = next; } };
}

function domainConnect(get: () => WorkflowDefinition, apply: (next: WorkflowDefinition) => void) {
  return (source: string, target: string) => {
    const current = get();
    const sourceNode = current.nodes.find((node) => node.id === source)!;
    const targetNode = current.nodes.find((node) => node.id === target)!;
    const ports = resolveEdgePorts(catalog.get(sourceNode.type)!, catalog.get(targetNode.type)!);
    if (!ports) return null;
    const result = addEdge(current, {
      source, sourcePort: ports.sourcePort, target, targetPort: ports.targetPort,
    }, catalog);
    if (result.issue) return null;
    apply(result.definition);
    return ports;
  };
}

const drag = (graph: Graph, source: string, target: string) => {
  graph.emit("node:dragstart", { target: { id: source }, targetType: "node", client: { x: 50, y: 50 } });
  graph.emit("pointermove", { client: { x: 150, y: 80 } });
  graph.emit("pointerup", { target: { id: target }, targetType: "node", client: { x: 250, y: 80 } });
};

describe("G6 interactive connection", () => {
  it("drag-create-edge writes a port-validated edge into the workflow domain", async () => {
    let current = definition();
    const onCreateEdge = vi.fn(domainConnect(() => current, (next) => { current = next; }));
    const state = await makeGraph({ connectMode: true, initial: current, onCreateEdge });
    drag(state.graph, "a", "b");
    expect(onCreateEdge).toHaveBeenCalledWith("a", "b");
    expect(current.edges).toEqual([
      { source: "a", sourcePort: "files", target: "b", targetPort: "input" },
    ]);
    state.graph.destroy();
  });

  it("rejects incompatible ports instead of creating a phantom edge", async () => {
    let current = definition();
    const onCreateEdge = vi.fn(domainConnect(() => current, (next) => { current = next; }));
    const state = await makeGraph({ connectMode: true, initial: current, onCreateEdge });
    drag(state.graph, "a", "c"); // FileBatch -> RecordSet
    expect(onCreateEdge).toHaveBeenCalledWith("a", "c");
    expect(current.edges).toHaveLength(0);
    state.graph.destroy();
  });

  it("does not create edges when connect mode is off (drag moves the node)", async () => {
    const onCreateEdge = vi.fn(() => ({ sourcePort: "files", targetPort: "input" }));
    const state = await makeGraph({ connectMode: false, initial: definition(), onCreateEdge });
    drag(state.graph, "a", "b");
    expect(onCreateEdge).not.toHaveBeenCalled();
    state.graph.destroy();
  });
});
