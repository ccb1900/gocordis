// Normalization for the combined composition manifest. The host sends nullable
// metadata for modules that were not installed from a package; keep the URL
// verbatim (it carries the content-hash cache key) and drop malformed entries
// so one bad row cannot hide the rest of the composition.

import type { UIClientModuleRef, UICompositionManifest, UIPage, UIPanel } from "../types";

const record = (value: unknown): Record<string, unknown> | null =>
  value && typeof value === "object" && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null;

const text = (value: unknown): string | null =>
  typeof value === "string" && value.length > 0 ? value : null;

export function normalizeUiManifest(raw: unknown): UICompositionManifest {
  const root = record(raw) ?? {};
  const pages = Array.isArray(root.pages)
    ? (root.pages as unknown[]).map(record).filter((p): p is Record<string, unknown> => p !== null)
      .filter((p) => text(p.id) && text(p.title) && text(p.route) && text(p.renderer))
      .map((p) => p as unknown as UIPage)
    : [];
  const panels = Array.isArray(root.panels)
    ? (root.panels as unknown[]).map(record).filter((p): p is Record<string, unknown> => p !== null)
      .filter((p) => text(p.id) && text(p.title) && text(p.position) && text(p.renderer))
      .map((p) => p as unknown as UIPanel)
    : [];
  const modules: UIClientModuleRef[] = Array.isArray(root.modules)
    ? (root.modules as unknown[]).map(record).filter((m): m is Record<string, unknown> => m !== null)
      .map((m) => ({
        name: text(m.name) ?? "",
        url: text(m.url) ?? "",
        packageId: text(m.packageId),
        version: text(m.version),
        contentHash: text(m.contentHash),
      }))
      .filter((m) => m.name.length > 0 && m.url.length > 0)
    : [];
  const revision = typeof root.revision === "number" && Number.isFinite(root.revision)
    ? root.revision
    : 0;
  return { pages, panels, modules, revision };
}
