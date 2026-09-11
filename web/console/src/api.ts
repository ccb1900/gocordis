async function get<T>(p: string): Promise<T> {
  const r = await fetch(p);
  const b = await r.json().catch(() => null);
  if (!r.ok) throw new Error(`${b?.code ?? "error"}: ${b?.message ?? r.statusText}`);
  return (b as { data: T }).data;
}
async function post<T>(p: string, body: unknown): Promise<T> {
  const r = await fetch(p, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body ?? {}) });
  const j = await r.json().catch(() => null);
  if (!r.ok) throw new Error(`${j?.code ?? "error"}: ${j?.message ?? r.statusText}`);
  return (j as { data: T }).data;
}
export const api = {
  pages: () => get<Page[]>("/api/ui/pages").then(r => r.pages),
  panels: () => get<{ panels: Panel[] }>("/api/ui/panels").then(r => r.panels),
  hubQuery: <T = unknown>(name: string, params?: Record<string,string>) => {
    const qs = params ? "?" + Object.entries(params).map(([k,v]) => `${k}=${encodeURIComponent(v)}`).join("&") : "";
    return get<T>(`/api/query/${name}${qs}`);
  },
  hubCommand: (name: string, body?: unknown) => post(`/api/command/${name}`, body),
  plugins: () => get<{ plugins: Plugin[] }>("/api/plugins").then(r => r.plugins),
  pluginConfig: (id: string) => get<Record<string,unknown>>(`/api/plugins/${encodeURIComponent(id)}/config`),
  setPluginConfig: (id: string, cfg: Record<string,unknown>) => post(`/api/plugins/${encodeURIComponent(id)}/config`, { config: cfg }),
  uninstall: (id: string) => post<void>(`/api/plugins/uninstall`, { pluginId: id }),
  install: (id: string) => post<void>(`/api/plugins/install`, { pluginId: id }),
};
