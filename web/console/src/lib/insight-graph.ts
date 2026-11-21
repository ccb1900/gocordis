// Pure DTO → tree logic for the insight-graph renderer. Kept free of React /
// DOM imports so it can be unit-tested in a node environment: rendering is a
// separate concern (views/InsightGraphBlock.tsx) that only maps these nodes
// onto antd Tree's title/key/children shape.

export interface DeclaredNode {
  id: string;
  name: string;
  type: string;
  requires: string[];
  provides: string[];
}

export interface DeclaredEdge {
  from: string;
  to: string;
  key: string;
}

/** IDs arrive as JSON numbers from some producers and strings from others. */
export type GraphID = string | number;

export interface RuntimeNode {
  fiberID: GraphID;
  name: string;
  state: string;
  activationID: GraphID;
  scopeID: GraphID;
  provides: string[];
  requires: string[];
  /** Lower-cased aliases used by the Go insight query — accepted as well. */
  fiberId?: GraphID;
  activationId?: GraphID;
  scopeId?: GraphID;
}

export interface RuntimeEdge {
  consumerFiberID: GraphID;
  providerFiberID: GraphID;
  key: string;
  status: string;
  reason?: string;
  consumerFiberId?: GraphID;
  providerFiberId?: GraphID;
}

export interface InsightGraphDTO {
  declared: { nodes: DeclaredNode[]; edges: DeclaredEdge[] };
  runtime: { nodes: RuntimeNode[]; edges: RuntimeEdge[] };
  sequence?: number;
  eventSequence?: number;
}

export type GraphView = "declared" | "runtime";

/** A renderer-neutral tree node. `detail` is rendered as secondary text;
 * `status` carries the fiber/edge status for optional styling. */
export interface GraphTreeNode {
  key: string;
  label: string;
  detail?: string;
  status?: string;
  children?: GraphTreeNode[];
}

const arr = <T,>(v: T[] | null | undefined): T[] => (Array.isArray(v) ? v : []);
const txt = (v: unknown): string => (v === undefined || v === null || v === "" ? "—" : String(v));
const list = (v: string[] | undefined): string => arr(v).join(", ") || "—";
/** Canonical spec spelling wins; the Go query's lower-cased alias is accepted. */
const idOf = (primary: unknown, alias: unknown): string =>
  txt(primary !== undefined && primary !== null && primary !== "" ? primary : alias);

/** The toggle is a pure state transition — the renderer just applies it. */
export function nextView(view: GraphView): GraphView {
  return view === "declared" ? "runtime" : "declared";
}

/** Defensive coercion of an untrusted view value (query param, stored UI
 * state): anything other than "runtime" selects the declared view. */
export function normalizeView(view: unknown): GraphView {
  return view === "runtime" ? "runtime" : "declared";
}

export function buildDeclaredTree(dto: Partial<InsightGraphDTO> | null | undefined): GraphTreeNode[] {
  const nodes = arr(dto?.declared?.nodes);
  const edges = arr(dto?.declared?.edges);
  const byId = new Map<string, DeclaredNode>();
  for (const n of nodes) if (n && n.id !== undefined) byId.set(String(n.id), n);

  const outgoing = new Map<string, DeclaredEdge[]>();
  for (const e of edges) {
    if (!e || e.from === undefined) continue;
    const from = String(e.from);
    const bucket = outgoing.get(from) ?? [];
    bucket.push(e);
    outgoing.set(from, bucket);
  }

  return nodes.map((n, i) => {
    const id = String(n.id ?? "");
    const children: GraphTreeNode[] = (outgoing.get(id) ?? []).map((e, j) => {
      const target = byId.get(String(e.to));
      return {
        key: `d-edge:${id}:${j}:${txt(e.key)}:${txt(e.to)}`,
        label: `${txt(e.key)} → ${txt(target?.name ?? e.to)}`,
        // An edge pointing at a node missing from the DTO stays visible
        // instead of crashing or silently vanishing.
        detail: target ? `provides=[${list(target.provides)}]` : "目标节点不在声明图中",
      };
    });
    return {
      key: `d-node:${id || i}`,
      label: `${txt(n.name)} · ${txt(n.type)}`,
      detail: `id=${txt(n.id)} · requires=[${list(n.requires)}] · provides=[${list(n.provides)}]`,
      children,
    };
  });
}

export function buildRuntimeTree(dto: Partial<InsightGraphDTO> | null | undefined): GraphTreeNode[] {
  const nodes = arr(dto?.runtime?.nodes);
  const edges = arr(dto?.runtime?.edges);
  const consumerOf = (e: RuntimeEdge) => idOf(e.consumerFiberID, e.consumerFiberId);
  const providerOf = (e: RuntimeEdge) => idOf(e.providerFiberID, e.providerFiberId);

  const byId = new Map<string, RuntimeNode>();
  for (const n of nodes) {
    const fid = idOf(n?.fiberID, n?.fiberId);
    if (n && fid !== "—") byId.set(fid, n);
  }

  const outgoing = new Map<string, RuntimeEdge[]>();
  for (const e of edges) {
    if (!e) continue;
    const from = consumerOf(e);
    if (from === "—") continue;
    const bucket = outgoing.get(from) ?? [];
    bucket.push(e);
    outgoing.set(from, bucket);
  }

  return nodes.map((n, i) => {
    const fid = idOf(n.fiberID, n.fiberId);
    const children: GraphTreeNode[] = (outgoing.get(fid) ?? []).map((e, j) => {
      const providerID = providerOf(e);
      const provider = byId.get(providerID);
      return {
        key: `r-edge:${fid}:${j}:${txt(e.key)}:${providerID}`,
        label: `${txt(e.key)} → ${txt(provider?.name ?? providerID)}`,
        status: txt(e.status),
        detail: [e.reason ? `原因: ${txt(e.reason)}` : null, `status=${txt(e.status)}`]
          .filter(Boolean)
          .join(" · "),
      };
    });
    // A Pending fiber's reason can sit on an edge where it is the provider
    // (dependency missing) as well as the consumer — look on both ends.
    const reasons = edges
      .filter((e) => e && (consumerOf(e) === fid || providerOf(e) === fid))
      .map((e) => e.reason)
      .filter((r): r is string => typeof r === "string" && r.length > 0);
    return {
      key: `r-node:${fid === "—" ? i : fid}`,
      label: `${txt(n.name)} · ${txt(n.state)}`,
      status: txt(n.state),
      detail: [
        `fiberID=${fid}`,
        `activationID=${idOf(n.activationID, n.activationId)}`,
        `scopeID=${idOf(n.scopeID, n.scopeId)}`,
        `requires=[${list(n.requires)}]`,
        `provides=[${list(n.provides)}]`,
        n.state === "Pending" && reasons.length ? `Pending 原因: ${reasons.join("; ")}` : null,
      ]
        .filter(Boolean)
        .join(" · "),
      children,
    };
  });
}

/** Build the tree for the selected toggle view. Null / partial DTOs yield an
 * empty tree so the renderer can show an explicit empty state. */
export function buildTree(dto: Partial<InsightGraphDTO> | null | undefined, view: GraphView): GraphTreeNode[] {
  return normalizeView(view) === "runtime" ? buildRuntimeTree(dto) : buildDeclaredTree(dto);
}
