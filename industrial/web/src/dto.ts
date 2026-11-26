// Boundary parser for the Cordis hub wire contract. Every GET is
// `{data: ...}`; every transport failure is `{code, message}` with a non-2xx
// status (api.ts throws before these helpers run). Command results are inside
// `data` and may be `{accepted:false, code, error}` even on HTTP 202.
//
// Parsing is PURE and defensive: untrusted JSON is normalized once here, and
// the rest of the app deals only with the typed shapes from types.ts. Port type
// names are canonicalized case-insensitively; an unrecognized type becomes
// "Unknown" (incompatible with everything) instead of silently passing as Text.

import {
  PORT_TYPES,
  type CollectionRow,
  type CommandResult,
  type BackfillPreviewRow,
  type BackfillPreviewView,
  type CredentialRow,
  type DeliveryRow,
  type DiagnosisView,
  type FileDetailRow,
  type ExecutionCenterView,
  type ExecutionNodeView,
  type ExecutionRunRow,
  type ExecutionStateView,
  type ResourceRow,
  type ResourceTestRow,
  type TriggerStatusRow,
  type JsonSchemaLike,
  type NodeDescriptor,
  type NodePort,
  type PortType,
  type ResourceOption,
  type TriggerDefinition,
  type ValidationIssue,
  type WorkflowDefinition,
  type WorkflowEdge,
  type WorkflowNode,
  type WorkflowSummary,
} from "./types";

export class DtoError extends Error {
  constructor(message: string) {
    super(message);
    this.name = "DtoError";
  }
}

type Obj = Record<string, unknown>;

const asObj = (v: unknown): Obj =>
  v !== null && typeof v === "object" && !Array.isArray(v) ? (v as Obj) : {};

const asArray = (v: unknown): unknown[] => (Array.isArray(v) ? v : []);

const text = (v: unknown, fallback: string | undefined = ""): string => {
  if (typeof v === "string") return v;
  if (typeof v === "number" || typeof v === "boolean") return String(v);
  return fallback ?? "";
};

const number = (v: unknown, fallback = 0): number => {
  const n = typeof v === "string" ? Number(v) : v;
  return typeof n === "number" && Number.isFinite(n) ? n : fallback;
};

const pick = (o: Obj, ...keys: string[]): unknown => {
  for (const key of keys) {
    if (o[key] !== undefined && o[key] !== null) return o[key];
  }
  return undefined;
};

/** Unwrap the `{data:...}` envelope. A bare payload is accepted so pure unit
 * tests can call parsers directly; an error envelope is rejected. */
export function unwrapData(raw: unknown): unknown {
  const o = asObj(raw);
  if (Object.prototype.hasOwnProperty.call(o, "data")) return o.data;
  if (typeof o.code === "string" && typeof o.message === "string") {
    throw new DtoError(`${o.code}: ${o.message}`);
  }
  return raw;
}

export function canonicalPortType(raw: unknown): PortType {
  const name = text(raw).trim().toLowerCase();
  const found = PORT_TYPES.find((candidate) => candidate.toLowerCase() === name);
  return found ?? "Unknown";
}

function parsePort(raw: unknown): NodePort | null {
  if (typeof raw === "string") {
    return { name: raw, type: "Unknown", required: false };
  }
  const o = asObj(raw);
  const name = text(pick(o, "name", "port", "id")).trim();
  if (!name) return null;
  return {
    name,
    type: canonicalPortType(pick(o, "type", "portType", "dataType")),
    required: pick(o, "required", "isRequired") === true,
  };
}

function parsePorts(raw: unknown): NodePort[] {
  const list = asArray(raw)
    .map(parsePort)
    .filter((port): port is NodePort => port !== null);
  const seen = new Set<string>();
  return list.filter((port) => {
    if (seen.has(port.name)) return false;
    seen.add(port.name);
    return true;
  });
}

const asSchema = (raw: unknown): JsonSchemaLike => {
  const o = asObj(raw);
  return Object.keys(o).length === 0 ? { type: "object", properties: {} } : o;
};

/** One backend node description (§7.4). The live `industrial/nodes` query
 * currently returns the identity trio; ports/schema/activity are optional and
 * parsed when a richer descriptor is published. */
export function parseNodeDescriptor(raw: unknown): NodeDescriptor | null {
  const o = asObj(raw);
  const nodeTypeId = text(pick(o, "nodeTypeId", "node_type_id", "type", "id")).trim();
  if (!nodeTypeId) return null;
  return {
    nodeTypeId,
    name: text(pick(o, "name", "title"), nodeTypeId),
    description: text(pick(o, "description", "desc")),
    category: text(pick(o, "category", "group"), "General"),
    icon: text(pick(o, "icon"), "AppstoreOutlined"),
    version: text(pick(o, "version"), "1.0.0"),
    inputs: parsePorts(pick(o, "inputs", "inputPorts", "input_ports")),
    outputs: parsePorts(pick(o, "outputs", "outputPorts", "output_ports")),
    configurationSchema: asSchema(pick(o, "configurationSchema", "configSchema", "schema")),
    activityType: text(pick(o, "activityType", "activity_type")),
    dependencies: asArray(pick(o, "dependencies")).map((item) => text(item)).filter(Boolean),
  };
}

export function parseNodeDescriptors(raw: unknown): NodeDescriptor[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data)
    ? data
    : asArray(pick(asObj(data), "nodes", "items"));
  const seen = new Set<string>();
  return list
    .map(parseNodeDescriptor)
    .filter((node): node is NodeDescriptor => node !== null)
    .filter((node) => {
      if (seen.has(node.nodeTypeId)) return false;
      seen.add(node.nodeTypeId);
      return true;
    });
}

export function parseResourceOptions(raw: unknown): ResourceOption[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data) ? data : asArray(pick(asObj(data), "resources", "items"));
  const seen = new Set<string>();
  return list
    .map((item) => {
      const o = asObj(item);
      const id = text(pick(o, "id")).trim();
      if (!id || seen.has(id)) return null;
      seen.add(id);
      const option: ResourceOption = {
        id,
        name: text(pick(o, "name"), id),
        type: text(pick(o, "type"), "unknown"),
        enabled: o.enabled !== false,
        credentialId: typeof o.credentialId === "string" ? o.credentialId : null,
        credentialName: typeof o.credentialName === "string" ? o.credentialName : null,
      };
      return option;
    })
    .filter((option): option is ResourceOption => option !== null);
}

function parseSummary(raw: unknown): WorkflowSummary | null {
  const o = asObj(raw);
  const workflowId = text(pick(o, "workflowId", "workflow_id", "id")).trim();
  if (!workflowId) return null;
  return {
    workflowId,
    name: text(pick(o, "name", "title"), workflowId),
    version: number(pick(o, "version"), 1),
  };
}

export function parseWorkflowSummaries(raw: unknown): WorkflowSummary[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data)
    ? data
    : asArray(pick(asObj(data), "workflows", "items"));
  return list.map(parseSummary).filter((w): w is WorkflowSummary => w !== null);
}

function parseConfig(raw: unknown): Record<string, unknown> {
  const o = asObj(raw);
  const result: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(o)) {
    if (value !== undefined) result[key] = value;
  }
  return result;
}

function parseWorkflowNode(raw: unknown): WorkflowNode | null {
  const o = asObj(raw);
  const id = text(pick(o, "id", "nodeId", "node_id")).trim();
  if (!id) return null;
  const position = asObj(pick(o, "position", "layout"));
  return {
    id,
    type: text(pick(o, "type", "nodeType", "nodeTypeId"), "unknown"),
    config: parseConfig(pick(o, "config", "configuration")),
    x: number(pick(o, "x") ?? pick(position, "x"), 0),
    y: number(pick(o, "y") ?? pick(position, "y"), 0),
  };
}

function parseWorkflowEdge(raw: unknown): WorkflowEdge | null {
  const o = asObj(raw);
  const source = text(pick(o, "source", "from", "sourceId")).trim();
  const target = text(pick(o, "target", "to", "targetId")).trim();
  if (!source || !target) return null;
  return {
    source,
    sourcePort: text(pick(o, "sourcePort", "source_port", "fromPort"), "out"),
    target,
    targetPort: text(pick(o, "targetPort", "target_port", "toPort"), "in"),
  };
}

/** Parse one workflow definition when a query publishes one. The current
 * `industrial/workflows` query publishes summaries only, so an empty graph is
 * the honest result until a full definition is available. */
export function parseWorkflow(raw: unknown): WorkflowDefinition {
  const data = unwrapData(raw);
  const o = asObj(data);
  const summary = parseSummary(o) ?? { workflowId: "", name: "", version: 1 };
  const nodes = asArray(pick(o, "nodes")).map(parseWorkflowNode)
    .filter((node): node is WorkflowNode => node !== null);
  const edges = asArray(pick(o, "edges")).map(parseWorkflowEdge)
    .filter((edge): edge is WorkflowEdge => edge !== null);
  const triggerRaw = asObj(pick(o, "trigger"));
  const trigger: TriggerDefinition = {
    kind: text(pick(triggerRaw, "kind", "type"), "manual"),
    cron: pick(triggerRaw, "cron") === undefined ? null : text(triggerRaw.cron),
    eventName: pick(triggerRaw, "eventName", "event_name") === undefined
      ? null : text(pick(triggerRaw, "eventName", "event_name")),
    config: parseConfig(pick(triggerRaw, "config")),
  };
  return {
    workflowId: summary.workflowId,
    name: summary.name,
    version: summary.version,
    nodes,
    edges,
    trigger,
    resourceReferences: asArray(pick(o, "resourceReferences", "resource_references"))
      .map((item) => text(item)).filter(Boolean),
    layout: parseConfig(pick(o, "layout")),
  };
}

export function parseCollections(raw: unknown): CollectionRow[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data)
    ? data
    : asArray(pick(asObj(data), "collections", "items", "files"));
  return list.map((item) => {
    const o = asObj(item);
    return {
      id: text(pick(o, "id"), "—"),
      path: text(pick(o, "path", "relativePath", "filePath"), "—"),
      sourceDate: text(pick(o, "sourceDate", "source_date"), ""),
      status: text(pick(o, "status"), "—"),
    };
  });
}

const bool = (v: unknown): boolean => v === true || (typeof v === "string" && v.toLowerCase() === "true");

export function parseResources(raw: unknown): ResourceRow[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data) ? data : asArray(pick(asObj(data), "resources", "items"));
  return list.map((item) => {
    const o = asObj(item);
    const credentialId = pick(o, "credentialId", "credential_id");
    return {
      id: text(pick(o, "id"), "—"),
      name: text(pick(o, "name"), "—"),
      type: text(pick(o, "type"), "—"),
      provider: text(pick(o, "provider"), "—"),
      enabled: bool(pick(o, "enabled")),
      credentialId: credentialId == null ? null : text(credentialId),
      credentialName: pick(o, "credentialName") == null ? null : text(pick(o, "credentialName")),
      config: parseConfig(pick(o, "config", "configuration")),
    };
  });
}

export function parseCredentials(raw: unknown): CredentialRow[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data) ? data : asArray(pick(asObj(data), "credentials", "items"));
  return list.map((item) => {
    const o = asObj(item);
    return {
      id: text(pick(o, "id"), "—"),
      name: text(pick(o, "name"), "—"),
      kind: text(pick(o, "kind"), "—"),
      version: number(pick(o, "version")),
    };
  });
}

export function parseResourceTests(raw: unknown): ResourceTestRow[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data) ? data : asArray(pick(asObj(data), "tests", "items"));
  return list.map((item) => {
    const o = asObj(item);
    return {
      resourceId: text(pick(o, "resourceId", "resource_id"), "—"),
      ok: bool(pick(o, "ok")),
      detail: text(pick(o, "detail"), ""),
    };
  });
}

const diagnosisOf = (value: unknown): DiagnosisView | null => {
  if (value === null || value === undefined) return null;
  const o = asObj(value);
  if (Object.keys(o).length === 0) return null;
  return {
    code: text(pick(o, "code"), "OTHER"),
    message: text(pick(o, "message"), ""),
    action: text(pick(o, "action"), ""),
  };
};

export function parseTriggerStatus(raw: unknown): TriggerStatusRow[] {
  const data = unwrapData(raw);
  const list = asArray(pick(asObj(data), "triggers", "items"));
  return list.map((item) => {
    const o = asObj(item);
    const nullable = (key: string) => pick(o, key) == null ? null : text(pick(o, key));
    return {
      workflowId: text(pick(o, "workflowId", "workflow_id"), "—"),
      version: number(pick(o, "version")),
      kind: text(pick(o, "kind"), "manual"),
      cron: nullable("cron"),
      eventName: nullable("eventName"),
      timezone: nullable("timezone"),
      resourceId: nullable("resourceId"),
      nextRun: nullable("nextRun"),
      lastStatus: nullable("lastStatus"),
      lastStartedAt: nullable("lastStartedAt"),
      lastFireStatus: nullable("lastFireStatus"),
      lastFireDueAt: nullable("lastFireDueAt"),
      lastFireAttempts: pick(o, "lastFireAttempts") == null
        ? null : number(pick(o, "lastFireAttempts")),
      lastError: nullable("lastError"),
    };
  });
}

export function parseBackfillPreview(raw: unknown): BackfillPreviewView | null {
  if (raw === null || raw === undefined) return null;
  const data = unwrapData(raw);
  const o = asObj(data);
  if (Object.keys(o).length === 0) return null;
  const files: BackfillPreviewRow[] = asArray(pick(o, "files")).map((item) => {
    const row = asObj(item);
    return {
      path: text(pick(row, "path"), "—"),
      sourceDate: text(pick(row, "sourceDate"), ""),
      inLedger: bool(pick(row, "inLedger")),
      expectedTargets: number(pick(row, "expectedTargets")),
      succeededTargets: number(pick(row, "succeededTargets")),
      incompleteTargets: number(pick(row, "incompleteTargets")),
      missingTargets: text(pick(row, "missingTargets"), ""),
      wouldSkip: bool(pick(row, "wouldSkip")),
      wouldCollect: bool(pick(row, "wouldCollect")),
      duplicateRisk: bool(pick(row, "duplicateRisk")),
    };
  });
  const summary = asObj(pick(o, "summary"));
  return {
    resourceId: text(pick(o, "resourceId"), "—"),
    from: text(pick(o, "from"), ""),
    to: text(pick(o, "to"), ""),
    files,
    summary: {
      discovered: number(pick(summary, "discovered")),
      alreadyComplete: number(pick(summary, "alreadyComplete")),
      needsCollection: number(pick(summary, "needsCollection")),
      duplicateRisk: number(pick(summary, "duplicateRisk")),
    },
  };
}

export function parseFileDetails(raw: unknown): FileDetailRow[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data) ? data : asArray(pick(asObj(data), "files", "items"));
  return list.map((item) => {
    const o = asObj(item);
    return {
      id: text(pick(o, "id"), "—"),
      path: text(pick(o, "path"), "—"),
      identityKey: text(pick(o, "identityKey"), ""),
      sourceResourceId: text(pick(o, "sourceResourceId"), ""),
      length: number(pick(o, "length")),
      sourceDate: text(pick(o, "sourceDate"), ""),
      collectedAt: text(pick(o, "collectedAt"), ""),
      status: text(pick(o, "status"), "—"),
      deliveries: asArray(pick(o, "deliveries")).map((entry) => {
        const d = asObj(entry);
        return {
          id: text(pick(d, "id"), "—"),
          collectionFileId: text(pick(d, "collectionFileId"), ""),
          targetId: text(pick(d, "targetId"), "—"),
          targetKind: text(pick(d, "targetKind"), "record"),
          status: text(pick(d, "status"), "—"),
          attempts: number(pick(d, "attempts")),
          records: number(pick(d, "records")),
          error: pick(d, "error") == null ? null : text(pick(d, "error")),
          accepted: number(pick(d, "accepted")),
          rejected: number(pick(d, "rejected")),
          warnings: number(pick(d, "warnings")),
          diagnosis: diagnosisOf(pick(d, "diagnosis")),
        };
      }),
    };
  });
}

export function parseExecutionCenter(raw: unknown): ExecutionCenterView {
  const data = unwrapData(raw);
  const o = asObj(data);
  const runs: ExecutionRunRow[] = asArray(pick(o, "runs")).map((item) => {
    const run = asObj(item);
    return {
      id: text(pick(run, "id"), "—"),
      workflowId: text(pick(run, "workflowId", "workflow_id"), "—"),
      status: text(pick(run, "status"), "—"),
      startedAt: text(pick(run, "startedAt", "started_at"), ""),
      finishedAt: pick(run, "finishedAt", "finished_at") == null
        ? null : text(pick(run, "finishedAt", "finished_at")),
      error: pick(run, "error") == null ? null : text(pick(run, "error")),
    };
  });
  const deliveries: DeliveryRow[] = asArray(pick(o, "deliveries")).map((item) => {
    const delivery = asObj(item);
    return {
      id: text(pick(delivery, "id"), "—"),
      collectionFileId: text(pick(delivery, "collectionFileId", "collection_file_id"), ""),
      targetId: text(pick(delivery, "targetId", "target_id"), "—"),
      targetKind: text(pick(delivery, "targetKind", "target_kind"), "record"),
      status: text(pick(delivery, "status"), "—"),
      attempts: number(pick(delivery, "attempts")),
      records: number(pick(delivery, "records")),
      error: pick(delivery, "error") == null ? null : text(pick(delivery, "error")),
      diagnosis: diagnosisOf(pick(delivery, "diagnosis")),
    };
  });
  return { runs, deliveries };
}

/** Validation errors may arrive as `{errors:[...]}` or a bare array. Located
 * paths are preserved verbatim so the UI can select the offending node. */
export function parseValidationIssues(raw: unknown): ValidationIssue[] {
  const data = unwrapData(raw);
  const list = Array.isArray(data)
    ? data
    : asArray(pick(asObj(data), "errors", "issues", "validationErrors"));
  return list.map((item, index) => {
    if (typeof item === "string") {
      return { path: "workflow", code: "VALIDATION", message: item };
    }
    const o = asObj(item);
    return {
      path: text(pick(o, "path", "location", "nodeId"), "workflow"),
      code: text(pick(o, "code", "gate"), "VALIDATION"),
      message: text(pick(o, "message", "error", "detail"), `error ${index + 1}`),
    };
  });
}

/** Command POST responses are `{data: ...}`; "accepted:false" inside data is
 * an unaccepted mutation, NOT a transport success to be ignored. */
/** Execution diagnostics are read-only. Node views are bounded by the server,
 * but the parser still caps samples so untrusted payloads cannot flood the UI. */
export function parseExecution(raw: unknown): ExecutionStateView | null {
  if (raw === null || raw === undefined) return null;
  const data = unwrapData(raw);
  const o = asObj(data);
  if (Object.keys(o).length === 0) return null;
  const nodes: ExecutionNodeView[] = asArray(pick(o, "nodes")).slice(0, 64).map((item) => {
    const node = asObj(item);
    return {
      nodeId: text(pick(node, "nodeId", "node_id"), "—"),
      status: text(pick(node, "status"), "—"),
      records: number(pick(node, "records")),
      files: number(pick(node, "files")),
      error: pick(node, "error") == null ? null : text(pick(node, "error"), undefined),
      samples: asArray(pick(node, "samples")).slice(0, 3).map((sample) => text(sample)),
    };
  });
  return {
    status: text(pick(o, "status"), "—"),
    error: pick(o, "error") == null ? null : text(pick(o, "error"), undefined),
    executed: asArray(pick(o, "executed")).map((step) => text(step)),
    instanceId: pick(o, "instanceId") == null ? null : text(pick(o, "instanceId"), undefined),
    definitionId: pick(o, "definitionId") == null ? null : text(pick(o, "definitionId"), undefined),
    nodes,
  };
}

export function parseCommandResult(raw: unknown): CommandResult {
  const data = unwrapData(raw);
  const o = asObj(data);
  const accepted = data === null || data === undefined
    ? true
    : pick(o, "accepted", "ok", "success") !== false;
  return {
    accepted,
    code: text(pick(o, "code"), undefined),
    error: text(pick(o, "error", "message"), undefined),
  };
}
