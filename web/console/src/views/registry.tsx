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

// Registry reactivity: the Maps are module-global (not React state), so a
// renderer registered AFTER the first render (client module import finishing
// late) would otherwise never reach an already-rendered page. Bumping a
// version on every real change and subscribing via useSyncExternalStore makes
// renderer availability a trigger, not a race: pages re-render when the
// resource they depend on arrives or departs.
let registryVersion = 0;
const registryListeners = new Set<() => void>();

function bump(): void {
  registryVersion++;
  for (const listener of registryListeners) listener();
}

/** subscribeRenderers registers a change listener; returns the unsubscribe. */
export function subscribeRenderers(listener: () => void): () => void {
  registryListeners.add(listener);
  return () => {
    registryListeners.delete(listener);
  };
}

/** rendererRegistryVersion is the current registry generation. */
export function rendererRegistryVersion(): number {
  return registryVersion;
}

/** registerBlockRenderer adds (or overrides) the renderer for one view kind. */
export function registerBlockRenderer(kind: string, renderer: BlockRenderer): () => void {
  blockRenderers.set(kind, renderer);
  bump();
  return () => {
    if (blockRenderers.get(kind) === renderer) {
      blockRenderers.delete(kind);
      bump();
    }
  };
}

/** registerPageRenderer adds (or overrides) a page-level renderer by name. */
export function registerPageRenderer(name: string, renderer: PageRenderer): () => void {
  pageRenderers.set(name, renderer);
  bump();
  return () => {
    if (pageRenderers.get(name) === renderer) {
      pageRenderers.delete(name);
      bump();
    }
  };
}

/** registerPanelRenderer adds (or overrides) a panel-level renderer by name. */
export function registerPanelRenderer(name: string, renderer: PanelRenderer): () => void {
  panelRenderers.set(name, renderer);
  bump();
  return () => {
    if (panelRenderers.get(name) === renderer) {
      panelRenderers.delete(name);
      bump();
    }
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

// ---- Widget extension point -----------------------------------------------
// Widgets are composable components a plugin contributes; declarative views
// compose them via the built-in `widget` block kind ({kind: "widget",
// params: {name: ...}}), so pages stack widgets from different plugins
// without imperative glue.

export interface WidgetProps {
  ctx: ViewContext;
  /** The widget block's params with $focus references already resolved. */
  params: Record<string, string>;
}
export type WidgetComponent = ComponentType<WidgetProps>;

const widgets = new Map<string, WidgetComponent>();

/** registerWidget contributes one composable component under a name. */
export function registerWidget(name: string, component: WidgetComponent): () => void {
  widgets.set(name, component);
  bump();
  return () => {
    if (widgets.get(name) === component) {
      widgets.delete(name);
      bump();
    }
  };
}

export function getWidget(name: string): WidgetComponent | undefined {
  return widgets.get(name);
}

export function registeredWidgets(): string[] {
  return Array.from(widgets.keys()).sort();
}

// ---- Action extension point ------------------------------------------------
// Actions are context-bound operations a plugin surfaces through the shell
// (a dropdown above the page card). `when` filters by the live context; `run`
// executes with the same hub/focus channel the declarative views use.

export interface ActionContext {
  ctx: ViewContext;
  page: UIPage | null;
}
export interface ActionDefinition {
  id: string;
  title: string;
  /** Optional filter — the shell surfaces the action only when it returns true. */
  when?: (cx: ActionContext) => boolean;
  run: (cx: ActionContext) => void | Promise<void>;
}

const actions = new Map<string, ActionDefinition>();

/** registerAction contributes one context action; re-registering the same id
 * overrides in place (insertion order preserved). */
export function registerAction(def: ActionDefinition): () => void {
  actions.set(def.id, def);
  bump();
  return () => {
    if (actions.get(def.id) === def) {
      actions.delete(def.id);
      bump();
    }
  };
}

export function listActions(): ActionDefinition[] {
  return Array.from(actions.values());
}

// ---- Settings extension point ----------------------------------------------
// A plugin may replace the generic config editor with its own settings UI for
// its plugin id; without one, the explorer falls back to the structured
// ConfigEditor.

export interface SettingsProps {
  pluginId: string;
  /** The plugin's current config (null when it has none yet). */
  config: Record<string, unknown> | null;
  /** Persist the edited config (api.setPluginConfig); resolves after the
   * save round-trip so the component can report success. */
  onSaved: (config: Record<string, unknown>) => Promise<void>;
}
export type SettingsComponent = ComponentType<SettingsProps>;

const settings = new Map<string, SettingsComponent>();

/** registerSettings contributes the settings UI for one plugin id. */
export function registerSettings(name: string, component: SettingsComponent): () => void {
  settings.set(name, component);
  bump();
  return () => {
    if (settings.get(name) === component) {
      settings.delete(name);
      bump();
    }
  };
}

export function getSettings(pluginId: string): SettingsComponent | undefined {
  const direct = settings.get(pluginId);
  if (direct) return direct;
  // The explorer lists the ui-client row as "<plugin>:client"; settings bind
  // to the plugin's module name — tolerate both spellings.
  return settings.get(pluginId.replace(/:client$/, ""));
}
