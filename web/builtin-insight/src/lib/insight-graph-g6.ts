// Pure DTO → AntV G6 graph data mapping. Kept free of React / DOM / G6 imports
// so it stays node-testable; the rendering concern lives in
// views/InsightGraphBlock.tsx. G6 data is {nodes:[{id,data,style}], edges:[...]}.
import type { GraphView, InsightGraphDTO } from "./insight-graph";

const arr = <T,>(v: T[] | null | undefined): T[] => (Array.isArray(v) ? v : []);
const txt = (v: unknown): string => (v === undefined || v === null || v === "" ? "—" : String(v));
const idOf = (primary: unknown, alias: unknown): string =>
  txt(primary !== undefined && primary !== null && primary !== "" ? primary : alias);

export interface G6Node {
  id: string;
  data: Record<string, unknown>;
  style?: Record<string, unknown>;
  // G6 NodeData carries an open index signature; keep our mapper assignable.
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

const STATE_COLOR: Record<string, string> = {
  Active: "#52c41a",
  Pending: "#faad14",
  Failed: "#f5222d",
  Loading: "#1677ff",
  Unloading: "#8c8c8c",
  Gone: "#d9d9d9",
};

/** Declared view: one node per declaration, edges = requires→provides key matches. */
export function buildDeclaredGraph(dto: Partial<InsightGraphDTO> | null | undefined): G6GraphData {
  const nodes = arr(dto?.declared?.nodes);
  const edges = arr(dto?.declared?.edges);
  const known = new Set(nodes.map((n) => String(n?.id)));
  return {
    nodes: nodes.map((n) => ({
      id: String(n?.id ?? ""),
      data: {
        label: `${txt(n?.name)} · ${txt(n?.type)}`,
        type: "declaration",
        requires: txt((n?.requires ?? []).join(", ")),
        provides: txt((n?.provides ?? []).join(", ")),
      },
      style: { fill: "#e6f0ff", stroke: "#4d6bfe", lineWidth: 1.5 },
    })),
    edges: edges.map((e, i) => ({
      id: `d-edge-${i}-${txt(e?.key)}`,
      source: String(e?.from ?? ""),
      // A declared edge pointing at an unknown provider is kept as a dangling
      // target so the missing declaration is visible rather than silent.
      target: String(e?.to ?? ""),
      data: { label: txt(e?.key), key: txt(e?.key), satisfied: known.has(String(e?.to)) },
      style: { stroke: known.has(String(e?.to)) ? "#8a93a6" : "#f5222d", lineDash: known.has(String(e?.to)) ? undefined : [4, 4] },
    })),
  };
}

/** Runtime view: one node per live fiber, edges = bound provider captures. */
export function buildRuntimeGraph(dto: Partial<InsightGraphDTO> | null | undefined): G6GraphData {
  const nodes = arr(dto?.runtime?.nodes);
  const edges = arr(dto?.runtime?.edges);
  const names = new Map<string, string>();
  for (const n of nodes) names.set(idOf(n?.fiberID, n?.fiberId), txt(n?.name));

  return {
    nodes: nodes.map((n) => {
      const state = txt(n?.state);
      return {
        id: idOf(n?.fiberID, n?.fiberId),
        data: {
          label: `${txt(n?.name)} · ${state}`,
          type: "fiber",
          state,
          activationID: idOf(n?.activationID, n?.activationId),
          provides: txt((n?.provides ?? []).join(", ")),
          requires: txt((n?.requires ?? []).join(", ")),
        },
        style: { fill: STATE_COLOR[state] ?? "#e6f0ff", stroke: "#4d6bfe", lineWidth: 1.5 },
      };
    }),
    edges: edges.map((e, i) => {
      const consumer = idOf(e?.consumerFiberID, e?.consumerFiberId);
      const provider = idOf(e?.providerFiberID, e?.providerFiberId);
      const status = txt(e?.status);
      return {
        id: `r-edge-${i}-${txt(e?.key)}`,
        source: consumer,
        target: provider,
        data: {
          label: txt(e?.key),
          key: txt(e?.key),
          status,
          reason: txt(e?.reason),
          fromName: names.get(consumer) ?? consumer,
          toName: names.get(provider) ?? provider,
        },
        style: { stroke: status === "Satisfied" ? "#52c41a" : "#faad14", lineWidth: 1.5 },
      };
    }),
  };
}

/** Build the graph for the selected toggle view. */
export function buildGraph(dto: Partial<InsightGraphDTO> | null | undefined, view: GraphView): G6GraphData {
  return view === "runtime" ? buildRuntimeGraph(dto) : buildDeclaredGraph(dto);
}

/**
 * G6 render options for the graph. `container` is REQUIRED: G6's default is a
 * string id 'container'; when no such element exists its canvas context init
 * throws "Cannot read properties of undefined (reading 'getContext')". We keep
 * option construction here so a unit test can lock the container requirement.
 */
export function graphOptions(data: G6GraphData, container: string | HTMLElement) {
  return {
    container,
    autoFit: "view" as const,
    data,
    layout: { type: "dagre", rankdir: "LR", nodesep: 28, ranksep: 72 },
    node: {
      style: {
        labelText: (d: { data?: { label?: string } }) => d?.data?.label ?? "",
        labelPlacement: "bottom" as const,
        size: [124, 38] as [number, number],
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
        labelText: (d: { data?: { label?: string } }) => d?.data?.label ?? "",
        labelFontSize: 10,
        stroke: "#8a93a6",
        endArrow: true,
      },
    },
    behaviors: ["drag-canvas", "zoom-canvas", "drag-element"],
  };
}
