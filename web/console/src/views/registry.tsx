// Keyed renderer registry — the console palette is open, not closed. The
// built-in renderers register here at module load exactly like any other
// contributor, and a deployment or plugin client module can register more
// before first render. Unknown kinds degrade to an explicit fallback (the
// declaration is shown, never silently dropped) so a composition authored
// against a newer renderer set still loads.
//
// Trust note (same story as every keyed-renderer host): registered
// renderers run inside the console with full page privileges. Register
// code you would ship as a dependency — nothing less.
import type { ComponentType } from "react";
import type { UIPage, UIPanel } from "../types";
import type { Focus, ViewBlock } from "./schema";

export interface ViewContext {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
  focus: Focus | null;
  onFocus: (sourceId: string, date: string) => void;
  busy: boolean;
  /** Kept for compatibility: coarse invalidation counter (all events). */
  generation: number;
}

export type BlockRenderer = ComponentType<{ block: ViewBlock; ctx: ViewContext }>;
export type PageRenderer = ComponentType<{ page: UIPage; ctx: ViewContext }>;
export type PanelRenderer = ComponentType<{ panel: UIPanel; ctx: ViewContext; generation: number }>;

const blockRenderers = new Map<string, BlockRenderer>();
const pageRenderers = new Map<string, PageRenderer>();
const panelRenderers = new Map<string, PanelRenderer>();

/** registerBlockRenderer adds (or overrides) the renderer for one view kind. */
export function registerBlockRenderer(kind: string, renderer: BlockRenderer): () => void {
  blockRenderers.set(kind, renderer);
  return () => {
    if (blockRenderers.get(kind) === renderer) blockRenderers.delete(kind);
  };
}

/** registerPageRenderer adds (or overrides) a page-level renderer by name. */
export function registerPageRenderer(name: string, renderer: PageRenderer): () => void {
  pageRenderers.set(name, renderer);
  return () => {
    if (pageRenderers.get(name) === renderer) pageRenderers.delete(name);
  };
}

/** registerPanelRenderer adds (or overrides) a panel-level renderer by name. */
export function registerPanelRenderer(name: string, renderer: PanelRenderer): () => void {
  panelRenderers.set(name, renderer);
  return () => {
    if (panelRenderers.get(name) === renderer) panelRenderers.delete(name);
  };
}

export function getBlockRenderer(kind: string): BlockRenderer | undefined {
  return blockRenderers.get(kind);
}

export function getPageRenderer(name: string): PageRenderer | undefined {
  return pageRenderers.get(name);
}

export function getPanelRenderer(name: string): PanelRenderer | undefined {
  return panelRenderers.get(name);
}

/** registeredBlockKinds lists every renderable kind (built-ins included). */
export function registeredBlockKinds(): string[] {
  return Array.from(blockRenderers.keys()).sort();
}
