// Industrial domain DTOs. These are the ONLY editable persisted shape: the G6
// graph model is a projection of WorkflowDefinition, never the other way round
// (docs/collector.md §7.2/§7.6). Wire DTOs are parsed once at the fetch boundary
// in dto.ts; everything below is trusted inside the app.

export const PORT_TYPES = [
  "Control", "File", "FileBatch", "Dataset", "RecordSet", "DeliveryResult", "Text", "Unknown",
] as const;
export type PortType = (typeof PORT_TYPES)[number];

export interface NodePort {
  name: string;
  type: PortType;
  required: boolean;
}

export interface JsonSchemaLike {
  type?: string;
  title?: string;
  description?: string;
  enum?: unknown[];
  default?: unknown;
  properties?: Record<string, JsonSchemaLike>;
  required?: string[];
  [key: string]: unknown;
}

export interface ResourceOption {
  id: string;
  name: string;
  type: string;
  enabled: boolean;
  credentialId?: string | null;
  credentialName?: string | null;
}

export interface NodeDescriptor {
  nodeTypeId: string;
  name: string;
  description: string;
  category: string;
  icon: string;
  version: string;
  inputs: NodePort[];
  outputs: NodePort[];
  configurationSchema: JsonSchemaLike;
  activityType: string;
  dependencies: string[];
}

export interface WorkflowNode {
  id: string;
  type: string;
  config: Record<string, unknown>;
  x: number;
  y: number;
}

export interface WorkflowEdge {
  source: string;
  sourcePort: string;
  target: string;
  targetPort: string;
}

export interface TriggerDefinition {
  kind: string;
  cron?: string | null;
  eventName?: string | null;
  config: Record<string, unknown>;
}

export interface WorkflowDefinition {
  workflowId: string;
  name: string;
  version: number;
  nodes: WorkflowNode[];
  edges: WorkflowEdge[];
  trigger: TriggerDefinition;
  resourceReferences: string[];
  layout: Record<string, unknown>;
}

export interface WorkflowSummary {
  workflowId: string;
  name: string;
  version: number;
}

export interface CollectionRow {
  id: string;
  path: string;
  sourceDate: string;
  status: string;
}

export interface ValidationIssue {
  /** Located to a node id, edge index or workflow-level path (§7.7). */
  path: string;
  code: string;
  message: string;
}

export interface CommandResult {
  /** Command mutations arrive inside `{data:...}`; an unaccepted mutation is
   * still HTTP 200/202 with `{accepted:false, code, error}`. */
  accepted: boolean;
  code?: string;
  error?: string;
}
