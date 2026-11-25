// Pure workflow domain operations (docs/collector.md §7.4–§7.7). No React, no
// DOM, no G6. Illegal states are rejected here rather than in the renderer:
// a connection is only legal when both ports exist and their declared types
// are compatible.

import type {
  NodeDescriptor, NodePort, PortType, ValidationIssue, WorkflowDefinition,
  WorkflowEdge, WorkflowNode,
} from "./types";

export type NodeCatalog = ReadonlyMap<string, NodeDescriptor>;

export const emptyWorkflow = (workflowId = "daily-collection"): WorkflowDefinition => ({
  workflowId,
  name: workflowId,
  version: 1,
  nodes: [],
  edges: [],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: [],
  layout: {},
});

/** Deterministic ids: no Math.random / Date.now, so a replayed session and a
 * saved definition produce the same next id. */
export function nextNodeId(nodes: readonly WorkflowNode[]): string {
  const used = new Set(nodes.map((node) => node.id));
  for (let i = 1; ; i++) {
    const candidate = `n${i}`;
    if (!used.has(candidate)) return candidate;
  }
}

function defaultsFromSchema(descriptor: NodeDescriptor): Record<string, unknown> {
  const config: Record<string, unknown> = {};
  for (const [key, schema] of Object.entries(descriptor.configurationSchema.properties ?? {})) {
    if (schema.default !== undefined) config[key] = schema.default;
  }
  return config;
}

export function makeNode(descriptor: NodeDescriptor, id: string, x = 0, y = 0): WorkflowNode {
  return { id, type: descriptor.nodeTypeId, config: defaultsFromSchema(descriptor), x, y };
}

export function descriptorOf(catalog: NodeCatalog, type: string): NodeDescriptor | undefined {
  return catalog.get(type);
}

function portOf(
  descriptor: NodeDescriptor, name: string, direction: "inputs" | "outputs"
): NodePort | undefined {
  return descriptor[direction].find((port) => port.name === name);
}

/** Only identical, well-known port types connect. "Unknown" (an unrecognized
 * wire value) never connects: a malformed descriptor must fail loudly. */
export function portTypesCompatible(a: PortType, b: PortType): boolean {
  return a !== "Unknown" && b !== "Unknown" && a === b;
}

/** Pick the first compatible output→input pair. The G6 canvas has no port
 * handles yet, so a drag-created edge binds to the descriptors' declared
 * ports; ports remain editable in the edge list of the property panel. */
export function resolveEdgePorts(
  source: NodeDescriptor, target: NodeDescriptor
): { sourcePort: string; targetPort: string } | null {
  for (const out of source.outputs) {
    for (const input of target.inputs) {
      if (portTypesCompatible(out.type, input.type)) {
        return { sourcePort: out.name, targetPort: input.name };
      }
    }
  }
  return null;
}

const issue = (path: string, code: string, message: string): ValidationIssue => ({ path, code, message });

export function sameEdge(a: WorkflowEdge, b: WorkflowEdge): boolean {
  return a.source === b.source && a.sourcePort === b.sourcePort &&
    a.target === b.target && a.targetPort === b.targetPort;
}

export interface EdgeAddResult {
  definition: WorkflowDefinition;
  issue?: ValidationIssue;
}

/** Idempotent: adding an identical edge twice leaves exactly one edge. */
export function addEdge(
  definition: WorkflowDefinition, edge: WorkflowEdge, catalog: NodeCatalog
): EdgeAddResult {
  const at = `edges.${definition.edges.length}`;
  const sourceNode = definition.nodes.find((node) => node.id === edge.source);
  const targetNode = definition.nodes.find((node) => node.id === edge.target);
  if (!sourceNode || !targetNode) {
    return { definition, issue: issue(at, "ENDPOINT_MISSING", "edge endpoint is not on the canvas") };
  }
  if (definition.edges.some((existing) => sameEdge(existing, edge))) {
    return { definition };
  }
  const source = descriptorOf(catalog, sourceNode.type);
  const target = descriptorOf(catalog, targetNode.type);
  if (!source || !target) {
    return { definition, issue: issue(at, "NODE_TYPE_UNKNOWN", "endpoint has no node descriptor") };
  }
  const sourcePort = portOf(source, edge.sourcePort, "outputs");
  const targetPort = portOf(target, edge.targetPort, "inputs");
  if (!sourcePort || !targetPort) {
    return { definition, issue: issue(at, "PORT_MISSING", "declared port does not exist") };
  }
  if (!portTypesCompatible(sourcePort.type, targetPort.type)) {
    return {
      definition,
      issue: issue(at, "PORT_TYPE_INCOMPATIBLE",
        `${sourcePort.type} → ${targetPort.type} is not a legal connection`),
    };
  }
  return { definition: { ...definition, edges: [...definition.edges, edge] } };
}

export function removeNode(definition: WorkflowDefinition, id: string): WorkflowDefinition {
  return {
    ...definition,
    nodes: definition.nodes.filter((node) => node.id !== id),
    edges: definition.edges.filter((edge) => edge.source !== id && edge.target !== id),
  };
}

export function removeEdge(definition: WorkflowDefinition, index: number): WorkflowDefinition {
  return { ...definition, edges: definition.edges.filter((_, i) => i !== index) };
}

export function setNodeConfig(
  definition: WorkflowDefinition, id: string, config: Record<string, unknown>
): WorkflowDefinition {
  return {
    ...definition,
    nodes: definition.nodes.map((node) => node.id === id ? { ...node, config } : node),
  };
}

export function applyPositions(
  definition: WorkflowDefinition, positions: Readonly<Record<string, { x: number; y: number }>>
): WorkflowDefinition {
  return {
    ...definition,
    nodes: definition.nodes.map((node) => {
      const pos = positions[node.id];
      return pos ? { ...node, x: pos.x, y: pos.y } : node;
    }),
    // One canonical layout field: the domain ships positions with the
    // definition, never as a separate browser-only cache.
    layout: { ...definition.layout, positions: { ...positions } },
  };
}

/** DFS cycle detection. Returns the first node chain that closes a cycle, or
 * null for an acyclic graph. Layout data never participates. */
export function findCycle(definition: WorkflowDefinition): string[] | null {
  const adjacency = new Map<string, string[]>();
  for (const node of definition.nodes) adjacency.set(node.id, []);
  for (const edge of definition.edges) {
    adjacency.get(edge.source)?.push(edge.target);
  }
  const state = new Map<string, 0 | 1 | 2>();
  const stack: string[] = [];
  const visit = (id: string): string[] | null => {
    state.set(id, 1);
    stack.push(id);
    for (const next of adjacency.get(id) ?? []) {
      if (state.get(next) === 1) {
        return [...stack.slice(stack.indexOf(next)), next];
      }
      if (state.get(next) !== 2) {
        const found = visit(next);
        if (found) return found;
      }
    }
    stack.pop();
    state.set(id, 2);
    return null;
  };
  for (const node of definition.nodes) {
    if (state.get(node.id) !== 2) {
      const found = visit(node.id);
      if (found) return found;
    }
  }
  return null;
}

/** Client-side mirror of the collector §7.7 gates that can be checked without
 * a server round-trip. Server validation remains authoritative; this gives
 * located errors while editing and never claims to check plugin install or
 * Elsa activity registration. */
export function validateWorkflow(
  definition: WorkflowDefinition,
  catalog: NodeCatalog,
  knownResourceIds?: ReadonlySet<string>,
): ValidationIssue[] {
  const errors: ValidationIssue[] = [];
  if (!definition.workflowId.trim()) {
    errors.push(issue("workflow", "INVALID_IDENTITY", "workflowId is required"));
  }
  if (definition.version < 1) {
    errors.push(issue("workflow", "INVALID_VERSION", "version must be positive"));
  }

  const byId = new Map<string, WorkflowNode>();
  for (const node of definition.nodes) {
    if (byId.has(node.id)) {
      errors.push(issue(`nodes.${node.id}`, "DUPLICATE_NODE_ID", `duplicate node '${node.id}'`));
    }
    byId.set(node.id, node);
  }

  const descriptors = new Map<string, NodeDescriptor>();
  for (const node of definition.nodes) {
    const descriptor = descriptorOf(catalog, node.type);
    if (!descriptor) {
      errors.push(issue(`nodes.${node.id}`, "UNKNOWN_NODE_TYPE",
        `node type '${node.type}' is not registered`));
      continue;
    }
    descriptors.set(node.id, descriptor);
    const required = descriptor.configurationSchema.required ?? [];
    for (const key of required) {
      const value = node.config[key];
      if (value === undefined || value === null || value === "") {
        errors.push(issue(`nodes.${node.id}.config.${key}`, "REQUIRED_CONFIG_MISSING",
          `required configuration '${key}' is missing`));
      }
    }
    if (knownResourceIds && typeof node.config.resourceId === "string") {
      if (!knownResourceIds.has(node.config.resourceId)) {
        errors.push(issue(`nodes.${node.id}.config.resourceId`, "RESOURCE_MISSING",
          `resource '${node.config.resourceId}' does not exist`));
      }
    }
  }

  definition.edges.forEach((edge, index) => {
    const source = byId.get(edge.source);
    const target = byId.get(edge.target);
    if (!source || !target) {
      errors.push(issue(`edges.${index}`, "ENDPOINT_MISSING", "edge endpoint is missing"));
      return;
    }
    const sourceDescriptor = descriptors.get(edge.source);
    const targetDescriptor = descriptors.get(edge.target);
    if (!sourceDescriptor || !targetDescriptor) return; // already reported above
    const out = portOf(sourceDescriptor, edge.sourcePort, "outputs");
    const input = portOf(targetDescriptor, edge.targetPort, "inputs");
    if (!out || !input) {
      errors.push(issue(`edges.${index}`, "PORT_MISSING", "edge references an unknown port"));
      return;
    }
    if (!portTypesCompatible(out.type, input.type)) {
      errors.push(issue(`edges.${index}`, "PORT_TYPE_INCOMPATIBLE",
        `${out.type} → ${input.type} is not a legal connection`));
    }
  });

  for (const node of definition.nodes) {
    const descriptor = descriptors.get(node.id);
    if (!descriptor) continue;
    for (const port of descriptor.inputs.filter((p) => p.required)) {
      // An edge whose source node is absent is not a real input; it is already
      // reported as ENDPOINT_MISSING, so do not count it as satisfying the port.
      if (!definition.edges.some((edge) =>
        edge.target === node.id && edge.targetPort === port.name && byId.has(edge.source))) {
        errors.push(issue(`nodes.${node.id}.inputs.${port.name}`, "MISSING_INPUT",
          `required input '${port.name}' is not connected`));
      }
    }
  }

  const cycle = findCycle(definition);
  if (cycle) {
    errors.push(issue(`nodes.${cycle[0]}`, "GRAPH_CYCLE", `cycle: ${cycle.join(" → ")}`));
  }
  return errors;
}
