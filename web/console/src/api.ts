// Console API: the single integration surface. The console only speaks the
// hub contract — composition (/api/ui/*), named queries/commands
// (/api/query/<name>, /api/command/<name>), plugin lifecycle (/api/plugins*)
// and the observation stream (/api/stream).

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  const body = await res.json().catch(() => null);
  if (!res.ok) throw new Error(`${body?.code ?? "error"}: ${body?.message ?? res.statusText}`);
  return (body as { data: T }).data;
}

async function post<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  });
  const j = await res.json().catch(() => null);
  if (!res.ok) throw new Error(`${j?.code ?? "error"}: ${j?.message ?? res.statusText}`);
  return (j as { data: T }).data;
}

const qs = (params?: Record<string, string | number>) =>
  Object.entries(params ?? {})
    .filter(([, v]) => v !== undefined && v !== "")
    .map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`)
    .join("&");

export const api = {
  pages: () => get<{ pages: import("./types").UIPage[] }>("/api/ui/pages").then((r) => r.pages),
  panels: () => get<{ panels: import("./types").UIPanel[] }>("/api/ui/panels").then((r) => r.panels),
  hubQuery: <T = unknown>(name: string, params?: Record<string, string | number>) =>
    get<T>(`/api/query/${name}${qs(params) ? `?${qs(params)}` : ""}`),
  hubCommand: (name: string, body?: unknown) => post<void>(`/api/command/${name}`, body),
  plugins: () => get<{ plugins: import("./types").ExplorerPlugin[] }>("/api/plugins").then((r) => r.plugins),
  removedPlugins: () => get<{ id: string; name: string }[]>("/api/plugins/removed"),
  uninstallPlugin: (id: string) => post<void>("/api/plugins/uninstall", { pluginId: id }),
  installPlugin: (id: string) => post<void>("/api/plugins/install", { pluginId: id }),
  controlPlugin: (pluginId: string, enable: boolean) =>
    post<import("./types").ExplorerControlResult>("/api/plugins/control", { pluginId, enable }),
  pluginConfig: (id: string) => get<Record<string, unknown>>(`/api/plugins/${encodeURIComponent(id)}/config`),
  setPluginConfig: (id: string, config: Record<string, unknown>) =>
    post<void>(`/api/plugins/${encodeURIComponent(id)}/config`, { config }),
};
