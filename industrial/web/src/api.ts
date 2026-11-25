// The single integration surface. Same wire contract as web/console/src/api.ts:
// GET  /api/query/<name>  → {data: ...}
// POST /api/command/<name> with a JSON body → {data: ...} (202 on acceptance)
// failures are {code, message} with a non-2xx status. A command may also
// return {data:{accepted:false, code, error}} — callers MUST inspect it.

export class ApiError extends Error {
  readonly code: string;
  readonly status: number;
  constructor(code: string, message: string, status: number) {
    super(code ? `${code}: ${message}` : message);
    this.name = "ApiError";
    this.code = code;
    this.status = status;
  }
}

async function read<T>(res: Response): Promise<T> {
  const body: unknown = await res.json().catch(() => null);
  if (!res.ok) {
    const o = body && typeof body === "object" ? (body as Record<string, unknown>) : {};
    const code = typeof o.code === "string" && o.code ? o.code : "error";
    const message = typeof o.message === "string" && o.message ? o.message : `HTTP ${res.status}`;
    throw new ApiError(code, message, res.status);
  }
  const envelope = body && typeof body === "object"
    ? (body as Record<string, unknown>)
    : {};
  return Object.prototype.hasOwnProperty.call(envelope, "data")
    ? envelope.data as T
    : body as T;
}

const qs = (params?: Record<string, string | number>): string =>
  Object.entries(params ?? {})
    .filter(([, value]) => value !== undefined && value !== "")
    .map(([key, value]) => `${key}=${encodeURIComponent(String(value))}`)
    .join("&");

function query<T>(name: string, params?: Record<string, string | number>): Promise<T> {
  const suffix = qs(params);
  return fetch(`/api/query/${name}${suffix ? `?${suffix}` : ""}`).then((res) => read<T>(res));
}

function command<T>(name: string, body: unknown): Promise<T> {
  return fetch(`/api/command/${name}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  }).then((res) => read<T>(res));
}

export const api = {
  query,
  command,
  nodes: () => query<unknown>("industrial/nodes"),
  workflows: () => query<unknown>("industrial/workflows"),
  collections: () => query<unknown>("industrial/collections"),
  resources: () => query<unknown>("industrial/resources"),
};
