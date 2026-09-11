// Console API client: the single integration surface between the console
// UI and the backend. Transport-agnostic (Wails bindings or HTTP fetch).

export type StreamStatus = "live" | "connecting" | "offline";

export interface UIPage { id: string; title: string; route: string; renderer: string; views?: unknown }
export interface UIPanel { id: string; title: string; position: string; renderer: string; pages?: string[] }
export interface UIObservation { type: string; sourceId?: string; timestamp: string }

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  const body = await res.json().catch(() => null);
  if (!res.ok) throw new Error(`${body?.code ?? "error"}: ${body?.message ?? res.statusText}`);
  return (body as { data: T }).data;
}
async function post<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body ?? {}) });
  const j = await res.json().catch(() => null);
  if (!res.ok) throw new Error(`${j?.code ?? "error"}: ${j?.message ?? res.statusText}`);
  return (j as { data: T }).data;
}

export const api = {
  pages: () => get<{ pages: { id: string; title: string; route: string; renderer: string }[] }>("/api/ui/pages").then(r => r.pages),
  panels: () => get<{ panels: { id: string; title: string; position: string; renderer: string; pages?: string[] }[] }>("/api/ui/panels").then(r => r.panels),
  plugins: () => get<{ plugins: Array<{ id: string; name: string; type: string; state: string; controllable: boolean }> }>("/api/plugins").then(r => r.plugins),
  uninstall: (id: string) => post("/api/plugins/uninstall", { pluginId: id }),
  install: (id: string) => post("/api/plugins/install", { pluginId: id }),
  pluginConfig: (id: string) => get<Record<string, unknown>>(`/api/plugins/${encodeURIComponent(id)}/config`),
  setPluginConfig: (id: string, cfg: Record<string, unknown>) => post(`/api/plugins/${encodeURIComponent(id)}/config`, { config: cfg }),
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => {
    const qs = params ? "?" + Object.entries(params).map(([k, v]) => `${k}=${encodeURIComponent(v)}`).join("&") : "";
    return get<T>(`/api/query/${name}${qs}`);
  },
  hubCommand: (name: string, body?: unknown) => post(`/api/command/${name}`, body),
  trigger: (date?: string) => post("/api/command/trigger", { date }),
};

// SSE observation stream — one shared EventSource for the whole console.
type ObsHandler = (ev: { type: string; sourceId?: string; timestamp: string }) => void;
let source: EventSource | null = null;
const handlers = new Set<ObsHandler>();
let streamStatus: StreamStatus = "connecting";
const statusHandlers = new Set<(s: StreamStatus) => void>();

function reportStatus() {
  const s = source && source.readyState === 1 ? "live" : source && source.readyState === 2 ? "offline" : "connecting";
  statusHandlers.forEach(fn => fn(s));
}

export function ensureStream(): void {
  if (source) return;
  source = new EventSource("/api/stream");
  source.onopen = () => reportStatus();
  source.onerror = () => reportStatus();
}

export function onObservation(fn: (ev: { type: string; sourceId?: string; timestamp: string }) => void): void {
  handlers.add(fn);
  ensureStream();
}

export function onStreamStatus(fn: (s: StreamStatus) => void): void {
  statusHandlers.add(fn);
  ensureStream();
}

function publish(ev: { type: string; sourceId?: string; timestamp: string }) {
  handlers.forEach(fn => fn(ev));
}
