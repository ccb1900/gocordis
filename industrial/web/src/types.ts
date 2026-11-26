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

export interface ExecutionNodeView {
  nodeId: string;
  status: string;
  records: number;
  files: number;
  error: string | null;
  samples: string[];
}

export interface ResourceRow {
  id: string;
  name: string;
  type: string;
  provider: string;
  enabled: boolean;
  credentialId: string | null;
  credentialName: string | null;
  config: Record<string, unknown>;
}

export interface CredentialRow {
  id: string;
  name: string;
  kind: string;
  version: number;
}

export interface ResourceTestRow {
  resourceId: string;
  ok: boolean;
  detail: string;
}

export interface ExecutionRunRow {
  id: string;
  workflowId: string;
  status: string;
  startedAt: string;
  finishedAt: string | null;
  error: string | null;
}

export interface DiagnosisView {
  code: string;
  message: string;
  action: string;
}

export interface TriggerStatusRow {
  workflowId: string;
  version: number;
  kind: string;
  cron: string | null;
  eventName: string | null;
  timezone: string | null;
  resourceId: string | null;
  nextRun: string | null;
  lastStatus: string | null;
  lastStartedAt: string | null;
  lastError: string | null;
}

export interface BackfillPreviewRow {
  path: string;
  sourceDate: string;
  inLedger: boolean;
  expectedTargets: number;
  succeededTargets: number;
  incompleteTargets: number;
  missingTargets: string;
  wouldSkip: boolean;
  wouldCollect: boolean;
  duplicateRisk: boolean;
}

export interface BackfillPreviewView {
  resourceId: string;
  from: string;
  to: string;
  files: BackfillPreviewRow[];
  summary: {
    discovered: number;
    alreadyComplete: number;
    needsCollection: number;
    duplicateRisk: number;
  };
}

export interface FileDeliveryDetail extends DeliveryRow {
  accepted: number;
  rejected: number;
  warnings: number;
  diagnosis: DiagnosisView | null;
}

export interface FileDetailRow {
  id: string;
  path: string;
  identityKey: string;
  sourceResourceId: string;
  length: number;
  sourceDate: string;
  collectedAt: string;
  status: string;
  deliveries: FileDeliveryDetail[];
}

export interface DeliveryRow {
  id: string;
  collectionFileId: string;
  targetId: string;
  targetKind: string;
  status: string;
  attempts: number;
  records: number;
  error: string | null;
  diagnosis?: DiagnosisView | null;
}

export interface ExecutionCenterView {
  runs: ExecutionRunRow[];
  deliveries: DeliveryRow[];
}

export interface ExecutionStateView {
  status: string;
  error: string | null;
  executed: string[];
  instanceId: string | null;
  definitionId: string | null;
  nodes: ExecutionNodeView[];
}
