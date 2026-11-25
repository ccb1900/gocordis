// Pure WorkflowDefinition → AntV G6 data projection. G6 data is disposable:
// every refresh rebuilds it from the domain model and calls graph.setData +
// render on the SAME graph instance (never destroy/recreate — that regression
// produced an infinite re-render loop in the previous designer).

import type { NodeDescriptor, WorkflowDefinition } from "./types";

export interface G6Node {
  id: string;
  data: Record<string, unknown>;
  style: Record<string, unknown> & { x?: number; y?: number };
  [key: string]: unknown;
}

export interface G6Edge {
  id: string;
  source: string;
  target: string;
  data: Record<string, unknown>;
  style?: Record<string, unknown>;
  [key: string]: unknown;
}

export interface G6GraphData {
  nodes: G6Node[];
  edges: G6Edge[];
}

const CATEGORY_COLOR: Record<string, string> = {
  trigger: "#52c41a",
  Trigger: "#52c41a",
  filesystem: "#1677ff",
  Filesystem: "#1677ff",
  source: "#1677ff",
  parsing: "#722ed1",
  Parsing: "#722ed1",
  transform: "#fa8c16",
  Transform: "#fa8c16",
  delivery: "#13a8a8",
  Delivery: "#13a8a8",
  sink: "#13a8a8",
};

const colorOf = (category: string): string =>
  CATEGORY_COLOR[category] ?? CATEGORY_COLOR[category.split(".")[0]] ?? "#4d6bfe";

/** Stable edge identity: derived from the four domain fields, never from an
 * array index (an index changes when an unrelated edge is removed). */
export function edgeId(edge: {
  source: string; sourcePort: string; target: string; targetPort: string;
}): string {
  return `e:${edge.source}:${edge.sourcePort}>${edge.target}:${edge.targetPort}`;
}

export function workflowToG6(
  definition: WorkflowDefinition,
  catalog: ReadonlyMap<string, NodeDescriptor> = new Map(),
  selectedId?: string | null,
): G6GraphData {
  return {
    nodes: definition.nodes.map((node) => {
      const descriptor = catalog.get(node.type);
      const category = descriptor?.category ?? "General";
      const selected = selectedId === node.id;
      return {
        id: node.id,
        data: {
          label: descriptor?.name ?? node.type,
          nodeTypeId: node.type,
          category,
          icon: descriptor?.icon ?? "AppstoreOutlined",
          // Domain config travels with the node so selection can edit it
          // without a second lookup; it is never written back from G6.
          config: { ...node.config },
        },
        style: {
          x: node.x,
          y: node.y,
          fill: colorOf(category),
          stroke: selected ? "#111111" : colorOf(category),
          lineWidth: selected ? 3 : 1.5,
          radius: 8,
        },
      };
    }),
    edges: definition.edges.map((edge) => ({
      id: edgeId(edge),
      source: edge.source,
      target: edge.target,
      data: {
        label: `${edge.sourcePort} → ${edge.targetPort}`,
        sourcePort: edge.sourcePort,
        targetPort: edge.targetPort,
      },
      style: { stroke: "#8a93a6", lineWidth: 1.5, endArrow: true },
    })),
  };
}

/** Read positions back from the graph data after a node drag. Only domain
 * node ids are accepted; G6-internal ids never enter WorkflowDefinition. */
export function positionsFromG6(
  data: G6GraphData, allowedIds: ReadonlySet<string>
): Record<string, { x: number; y: number }> {
  const positions: Record<string, { x: number; y: number }> = {};
  for (const node of data.nodes) {
    if (!allowedIds.has(node.id)) continue;
    const x = node.style.x;
    const y = node.style.y;
    if (typeof x === "number" && typeof y === "number") {
      positions[node.id] = { x, y };
    }
  }
  return positions;
}

/** Graph options factory kept pure so a unit test can lock the container
 * requirement: G6's default container id 'container' crashes canvas init. */
export function graphOptions(data: G6GraphData, container: string | HTMLElement) {
  return {
    container,
    autoFit: "view" as const,
    data,
    node: {
      type: "rect",
      style: {
        labelText: (datum: { data?: { label?: string } }) => datum?.data?.label ?? "",
        labelPlacement: "bottom" as const,
        size: [140, 42] as [number, number],
        radius: 8,
        fill: "#e6f0ff",
        stroke: "#4d6bfe",
        lineWidth: 1.5,
        labelFill: "#1f1f1f",
        labelFontSize: 11,
      },
    },
    edge: {
      style: {
        labelText: (datum: { data?: { label?: string } }) => datum?.data?.label ?? "",
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: true,
      },
    },
    behaviors: [
      "drag-canvas",
      "zoom-canvas",
      { type: "drag-element", onFinish: () => undefined },
      { type: "create-edge", trigger: "drag", onCreate: (edge: G6Edge) => edge },
    ],
  };
}
