// Domain layout helpers. Positions are persisted in WorkflowDefinition; G6 is
// only a projection. Dagre (installed with G6) provides the layered layout so
// we never hand-write a graph layout algorithm.

import dagre from "dagre";
import type { WorkflowDefinition } from "./types";

export const NODE_WIDTH = 148;
export const NODE_HEIGHT = 46;
const GAP_X = 80;
const GAP_Y = 44;

export function autoLayout(definition: WorkflowDefinition): WorkflowDefinition {
  if (definition.nodes.length === 0) return definition;
  const graph = new dagre.graphlib.Graph();
  graph.setDefaultEdgeLabel(() => ({}));
  graph.setGraph({ rankdir: "LR", nodesep: 60, ranksep: 120, marginx: 40, marginy: 40 });
  for (const node of definition.nodes) graph.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  for (const edge of definition.edges) graph.setEdge(edge.source, edge.target);
  dagre.layout(graph);
  const positions: Record<string, { x: number; y: number }> = {};
  for (const node of definition.nodes) {
    const laid = graph.node(node.id);
    positions[node.id] = laid
      ? { x: Math.round(laid.x - NODE_WIDTH / 2), y: Math.round(laid.y - NODE_HEIGHT / 2) }
      : { x: node.x, y: node.y };
  }
  return {
    ...definition,
    nodes: definition.nodes.map((node) => ({ ...node, ...positions[node.id] })),
    layout: { ...definition.layout, positions },
  };
}

const overlaps = (a: { x: number; y: number }, b: { x: number; y: number }) =>
  Math.abs(a.x - b.x) < NODE_WIDTH + GAP_X && Math.abs(a.y - b.y) < NODE_HEIGHT + GAP_Y;

/** Repairs imported/legacy definitions whose coordinates collapse on top of
 * each other. Deterministic, so save/reload keeps the same layout. */
export function repairOverlaps(definition: WorkflowDefinition): WorkflowDefinition {
  const occupied: Array<{ x: number; y: number }> = [];
  const nodes = definition.nodes.map((node) => {
    let x = Number.isFinite(node.x) ? node.x : 0;
    let y = Number.isFinite(node.y) ? node.y : 0;
    let guard = 0;
    while (occupied.some((other) => overlaps({ x, y }, other)) && guard++ < 200) {
      x += NODE_WIDTH + GAP_X;
      if (x > 2400) { x = 80; y += NODE_HEIGHT + GAP_Y; }
    }
    occupied.push({ x, y });
    return { ...node, x, y };
  });
  if (nodes.every((node, index) => node.x === definition.nodes[index].x &&
      node.y === definition.nodes[index].y)) return definition;
  const positions = Object.fromEntries(nodes.map((node) => [node.id, { x: node.x, y: node.y }]));
  return { ...definition, nodes, layout: { ...definition.layout, positions } };
}

/** Finds a non-overlapping drop position near the requested point. */
export function freePosition(
  definition: WorkflowDefinition, x: number, y: number
): { x: number; y: number } {
  let candidate = { x: Math.round(x), y: Math.round(y) };
  let guard = 0;
  while (definition.nodes.some((node) => overlaps(candidate, node)) && guard++ < 200) {
    candidate = { x: candidate.x + NODE_WIDTH + GAP_X, y: candidate.y };
    if (candidate.x > 2400) candidate = { x: 80, y: candidate.y + NODE_HEIGHT + GAP_Y };
  }
  return candidate;
}
