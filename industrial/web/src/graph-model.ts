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
  /** G6 port keys: `out:<declared name>` / `in:<declared name>`. */
  sourcePort?: string;
  targetPort?: string;
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
          // Typed anchors: the user can see which side accepts which port,
          // and G6 binds edges to these keys instead of node centers.
          port: true,
          ports: [
            ...(descriptor?.inputs ?? []).map((port, index, all) => ({
              key: `in:${port.name}`,
              placement: [0, (index + 1) / (all.length + 1)] as [number, number],
              r: 4,
              stroke: port.required ? "#fa541c" : "#8a93a6",
              fill: "#fff",
            })),
            ...(descriptor?.outputs ?? []).map((port, index, all) => ({
              key: `out:${port.name}`,
              placement: [1, (index + 1) / (all.length + 1)] as [number, number],
              r: 4,
              stroke: "#1677ff",
              fill: "#fff",
            })),
          ],
        },
      };
    }),
    edges: definition.edges.map((edge) => ({
      id: edgeId(edge),
      source: edge.source,
      sourcePort: `out:${edge.sourcePort}`,
      target: edge.target,
      targetPort: `in:${edge.targetPort}`,
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
    behaviors: graphBehaviors(),
  };
}

export interface GraphBehaviorHandlers {
  graph?: { getNodeData(): Array<{ id?: unknown; data?: unknown; style?: unknown }> };
  onSelect?: (id: string | null) => void;
  onDropNode?: (type: string, x: number, y: number) => void;
  /** Returns the resolved domain port pair, or null when incompatible. */
  onCreateEdge?: (source: string, target: string) => { sourcePort: string; targetPort: string } | null;
  onPositions?: (positions: Record<string, { x: number; y: number }>) => void;
  allowedIds?: () => ReadonlySet<string>;
  /** The two drag gestures share the same pointer; exactly one is enabled. */
  connectMode?: () => boolean;
}

/** One place owns the G6 behavior wiring. The component and the interaction
 * test use the SAME configuration — a test that hand-builds different
 * behaviors would prove nothing about the shipped designer. */
export function graphBehaviors(handlers: GraphBehaviorHandlers = {}): any[] {
  return [
    "drag-canvas",
    "zoom-canvas",
    {
      type: "drag-element",
      enable: () => !(handlers.connectMode?.() ?? false),
      onFinish: () => {
        const graph = handlers.graph;
        if (!graph) return;
        const nodes = graph.getNodeData().map((node: any) => ({
          id: String(node.id),
          data: (node.data ?? {}) as Record<string, unknown>,
          style: (node.style ?? {}) as Record<string, unknown> & { x?: number; y?: number },
        }));
        handlers.onPositions?.(positionsFromG6(
          { nodes, edges: [] }, handlers.allowedIds?.() ?? new Set<string>()));
      },
    },
    {
      type: "create-edge",
      trigger: "drag",
      enable: () => handlers.connectMode?.() ?? false,
      onCreate: (edge: { source?: unknown; target?: unknown; [key: string]: unknown }) => {
        const source = String(edge.source ?? "");
        const target = String(edge.target ?? "");
        if (!source || !target || source === target) return undefined;
        const ports = handlers.onCreateEdge?.(source, target);
        if (!ports) return undefined;
        return {
          ...edge,
          sourcePort: `out:${ports.sourcePort}`,
          targetPort: `in:${ports.targetPort}`,
        };
      },
    },
  ];
}
