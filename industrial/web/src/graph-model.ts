// Pure WorkflowDefinition → AntV G6 data projection. G6 data is disposable:
// every refresh rebuilds it from the domain model and calls graph.setData +
// draw on the SAME graph instance (never destroy/recreate — that regression
// produced an infinite re-render loop in the previous designer).

import type { NodeDescriptor, WorkflowDefinition } from "./types";

const NODE_WIDTH = 148;
const NODE_HEIGHT = 46;

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

/** Stable edge identity from the four domain fields, never from an array index. */
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
          config: { ...node.config },
        },
        style: {
          x: node.x,
          y: node.y,
          fill: colorOf(category),
          stroke: selected ? "#111111" : colorOf(category),
          lineWidth: selected ? 3 : 1.5,
          radius: 10,
          port: true,
          ports: [
            ...(descriptor?.inputs ?? []).map((port, index, all) => ({
              key: `in:${port.name}`,
              placement: [0, (index + 1) / (all.length + 1)] as [number, number],
              r: 5,
              stroke: port.required ? "#fa541c" : "#8a93a6",
              fill: "#fff",
            })),
            ...(descriptor?.outputs ?? []).map((port, index, all) => ({
              key: `out:${port.name}`,
              placement: [1, (index + 1) / (all.length + 1)] as [number, number],
              r: 5,
              stroke: port.type === "Control" ? "#52c41a" : "#1677ff",
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

/** Read positions back from G6. `onlyIds` limits a node drag to the nodes G6
 * actually moved; passing all ids used to commit the whole layout. */
export function positionsFromG6(
  data: G6GraphData,
  allowedIds: ReadonlySet<string>,
  onlyIds?: readonly string[],
): Record<string, { x: number; y: number }> {
  const moved = onlyIds && onlyIds.length > 0 ? new Set(onlyIds) : null;
  const positions: Record<string, { x: number; y: number }> = {};
  for (const node of data.nodes) {
    if (!allowedIds.has(node.id)) continue;
    if (moved && !moved.has(node.id)) continue;
    const x = node.style.x;
    const y = node.style.y;
    if (typeof x === "number" && typeof y === "number") positions[node.id] = { x, y };
  }
  return positions;
}

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
        size: [NODE_WIDTH, NODE_HEIGHT] as [number, number],
        radius: 10,
        fill: "#f8fbff",
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
    plugins: [
      { type: "grid-line", size: 20, stroke: "#e8edf4" },
    ],
    behaviors: graphBehaviors(),
  };
}

interface GestureEvent {
  targetType?: string;
  target?: { id?: string };
  client?: { x: number; y: number };
  canvas?: { x: number; y: number };
  button?: number;
  buttons?: number;
  shiftKey?: boolean;
}

export interface GraphBehaviorHandlers {
  graph?: {
    getNodeData(): Array<{ id?: unknown; data?: unknown; style?: unknown }>;
    getCanvasByClient(point: [number, number]): [number, number];
  };
  /** Space held or middle button: pan even when the pointer is over a node. */
  isPanModifier?: () => boolean;
  /** G6 emits node:dragstart only after the pointer has moved past a threshold,
   * so the drag-start event position is no longer on the port. The pointerdown
   * origin is the reliable signal for a port-origin connect gesture. */
  gestureOrigin?: () => { x: number; y: number } | null;
  /** Shared arm state so a port-origin drag remains a connect gesture until pointerup. */
  connectionState?: { armed: boolean };
  onPositions?: (positions: Record<string, { x: number; y: number }>) => void;
  onSelectIds?: (ids: string[]) => void;
  allowedIds?: () => ReadonlySet<string>;
  onCreateEdge?: (source: string, target: string) => { sourcePort: string; targetPort: string } | null;
}

// G6 v5 treats these state names as "selected nodes to move together". Use
// private names so the designer's React selection never causes group drags.
const DRAG_STATE = "__industrial_drag_target__";
const CONNECT_STATE = "__industrial_connect_source__";

const isPanGesture = (handlers: GraphBehaviorHandlers, event: GestureEvent): boolean =>
  (handlers.isPanModifier?.() ?? false) || event.button === 1 || event.buttons === 4 ||
  event.shiftKey === true;

/** A draw.io-like hit test: ports are the small circles on the left/right
 * edges of a node. G6 reports port hits as node-scoped events, so geometry is
 * the most reliable signal across renderer versions. */
const isPortGesture = (handlers: GraphBehaviorHandlers, event: GestureEvent): boolean => {
  if (event.targetType !== "node" || !event.target?.id) return false;
  const point = handlers.gestureOrigin?.() ?? event.client ?? event.canvas;
  if (!point || !handlers.graph) return false;
  const nodeId = String(event.target.id);
  const node = handlers.graph.getNodeData().find((item) => String(item.id) === nodeId);
  const style = node?.style as { x?: number; y?: number } | undefined;
  if (typeof style?.x !== "number" || typeof style.y !== "number") return false;
  const [x, y] = handlers.graph.getCanvasByClient([point.x, point.y]);
  const dx = Math.abs(x - style.x);
  const dy = Math.abs(y - style.y);
  return Math.abs(dx - NODE_WIDTH / 2) <= 14 && dy <= NODE_HEIGHT / 2 + 10;
};

/** One canvas, direct gestures: drag a node to move it, drag from its port to
 * connect, drag the background (or Space/middle button) to pan. No modes. */
export function graphBehaviors(handlers: GraphBehaviorHandlers = {}): any[] {
  const connection = handlers.connectionState ?? { armed: false };
  return [
    "zoom-canvas",
    {
      type: "click-select",
      multiple: true,
      trigger: ["shift"],
      state: "__industrial_selected__",
    },
    {
      type: "brush-select",
      trigger: ["shift"],
      state: "__industrial_selected__",
      onSelect: (states: Record<string, unknown>) =>
        handlers.onSelectIds?.(Object.keys(states ?? {})),
    },
    {
      type: "drag-canvas",
      enable: (event: GestureEvent) =>
        event.targetType === "canvas" || isPanGesture(handlers, event),
      range: 0.9,
    },
    {
      type: "drag-element",
      state: DRAG_STATE,
      enable: (event: GestureEvent) =>
        (event.targetType === "node" || event.targetType === "combo") &&
        !isPortGesture(handlers, event) && !isPanGesture(handlers, event),
      onFinish: (ids?: string[] | string) => {
        const graph = handlers.graph;
        if (!graph) return;
        const moved = Array.isArray(ids) ? ids : ids ? [String(ids)] : undefined;
        const nodes = graph.getNodeData().map((node: any) => ({
          id: String(node.id),
          data: (node.data ?? {}) as Record<string, unknown>,
          style: (node.style ?? {}) as Record<string, unknown> & { x?: number; y?: number },
        }));
        handlers.onPositions?.(positionsFromG6(
          { nodes, edges: [] }, handlers.allowedIds?.() ?? new Set<string>(), moved));
      },
    },
    {
      type: "create-edge",
      trigger: "drag",
      state: CONNECT_STATE,
      enable: (event: GestureEvent) => {
        if (isPanGesture(handlers, event)) {
          connection.armed = false;
          return false;
        }
        if (connection.armed) return event.targetType === "node" || event.targetType === "combo";
        if (isPortGesture(handlers, event)) {
          connection.armed = true;
          return true;
        }
        return false;
      },
      onCreate: (edge: { source?: unknown; target?: unknown; [key: string]: unknown }) => {
        connection.armed = false;
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
