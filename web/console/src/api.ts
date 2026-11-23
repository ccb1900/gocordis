// Console API: the single integration surface. The console only speaks the
// hub contract — composition (/api/ui/*), named queries/commands
// (/api/query/<name>, /api/command/<name>), the plugin control plane
// (/api/plugins*) and the observation/control stream (/api/stream).
//
// Every response is `{data: ...}`; every transport error is `{code,message}`.
// Command results are *inside* `data`: an unaccepted mutation still returns
// HTTP 200 with `{accepted:false, code, error}` — callers MUST inspect it.

import type {
  ExplorerPlugin, PluginDependencyView, PluginMutationResult,
  StorePluginEntry, UICompositionManifest, UploadedPackage,
} from "./types";
import {
  normalizeDependencyView, normalizeMutation, normalizePlugins,
  normalizeStorePlugins, normalizeUpload,
} from "./lib/plugin-control";
import { normalizeUiManifest } from "./lib/ui-manifest";

export class ApiError extends Error {
  readonly code: string;
  constructor(code: string, message: string) {
    super(code ? `${code}: ${message}` : message);
    this.name = "ApiError";
    this.code = code;
  }
}

function failure(status: number, body: unknown): ApiError {
  const b = body && typeof body === "object" ? (body as Record<string, unknown>) : {};
  const code = typeof b.code === "string" && b.code ? b.code : "error";
  const message = typeof b.message === "string" && b.message ? b.message : `HTTP ${status}`;
  return new ApiError(code, message);
}

async function get<T>(path: string): Promise<T> {
  const res = await fetch(path);
  const j = await res.json().catch(() => null);
  if (!res.ok) throw failure(res.status, j);
  return (j as { data: T }).data;
}

async function post<T>(path: string, body: unknown): Promise<T> {
  const res = await fetch(path, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body ?? {}),
  });
  const j = await res.json().catch(() => null);
  if (!res.ok) throw failure(res.status, j);
  return (j as { data: T }).data;
}

async function upload<T>(path: string, field: string, file: File): Promise<T> {
  const form = new FormData();
  form.append(field, file, file.name);
  // No Content-Type header: the browser must add the multipart boundary.
  const res = await fetch(path, { method: "POST", body: form });
  const j = await res.json().catch(() => null);
  if (!res.ok) throw failure(res.status, j);
  return (j as { data: T }).data;
}

const qs = (params?: Record<string, string | number>) =>
  Object.entries(params ?? {})
    .filter(([, v]) => v !== undefined && v !== "")
    .map(([k, v]) => `${k}=${encodeURIComponent(String(v))}`)
    .join("&");

const enc = (id: string) => encodeURIComponent(id);

// ---- composition / named queries --------------------------------------------

export function pages() {
  return get<{ pages: import("./types").UIPage[] }>("/api/ui/pages").then((r) => r.pages);
}

export function panels() {
  return get<{ panels: import("./types").UIPanel[] }>("/api/ui/panels").then((r) => r.panels);
}

/** One snapshot of the host's UI composition, including packaged plugin
 * modules and their content-hash cache keys. */
export function uiManifest(): Promise<UICompositionManifest> {
  return get<unknown>("/api/ui/manifest").then(normalizeUiManifest);
}

export function hubQuery<T = unknown>(name: string, params?: Record<string, string | number>) {
  const query = qs(params);
  return get<T>(`/api/query/${name}${query ? `?${query}` : ""}`);
}

export function hubCommand(name: string, body?: unknown) {
  return post<void>(`/api/command/${name}`, body);
}

// ---- plugin control plane ---------------------------------------------------

/** GET /api/plugins → {data: PluginControlEntry[]} (nil slices normalize). */
export function plugins(): Promise<ExplorerPlugin[]> {
  return get<unknown>("/api/plugins").then(normalizePlugins);
}

/** GET /api/plugins/store → local package inventory, never a remote market. */
export function storePlugins(): Promise<StorePluginEntry[]> {
  return get<unknown>("/api/plugins/store").then(normalizeStorePlugins);
}

/** GET /api/plugins/{id}/dependencies → package + capability dependency view. */
export function dependencies(id: string): Promise<PluginDependencyView> {
  return get<unknown>(`/api/plugins/${enc(id)}/dependencies`).then((raw) =>
    normalizeDependencyView(raw, id)
  );
}

/** POST /api/plugins/packages — multipart field `package`. */
export function uploadPackage(file: File): Promise<UploadedPackage> {
  return upload<unknown>("/api/plugins/packages", "package", file).then(normalizeUpload);
}

export function installPlugin(uploadKey: string, enable?: boolean): Promise<PluginMutationResult> {
  return post<unknown>("/api/plugins/install", { uploadKey, enable }).then(normalizeMutation);
}

export function upgradePlugin(
  id: string,
  uploadKey: string,
  versionRange?: string
): Promise<PluginMutationResult> {
  return post<unknown>("/api/plugins/upgrade", { id, uploadKey, versionRange }).then(normalizeMutation);
}

export function uninstallPlugin(id: string, version?: string): Promise<PluginMutationResult> {
  return post<unknown>("/api/plugins/uninstall", { id, version }).then(normalizeMutation);
}

export function controlPlugin(pluginId: string, enable: boolean): Promise<PluginMutationResult> {
  return post<unknown>("/api/plugins/control", { pluginId, enable }).then(normalizeMutation);
}

/** POST /api/plugins/{id}/config — the desired-state override. There is no
 * dedicated GET: the current config travels on the plugin list entry, so the
 * editor reads it from there without hitting a 404 endpoint. */
export function savePluginConfig(
  id: string,
  config: Record<string, unknown>
): Promise<PluginMutationResult> {
  return post<unknown>(`/api/plugins/${enc(id)}/config`, { config }).then(normalizeMutation);
}

export async function pluginConfig(id: string): Promise<Record<string, unknown>> {
  const list = await plugins();
  return list.find((p) => p.id === id)?.config ?? {};
}

export const api = {
  pages,
  panels,
  uiManifest,
  hubQuery,
  hubCommand,
  plugins,
  storePlugins,
  dependencies,
  uploadPackage,
  installPlugin,
  upgradePlugin,
  uninstallPlugin,
  controlPlugin,
  savePluginConfig,
  pluginConfig,
};
