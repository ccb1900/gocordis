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
  /** Domain-free route change channel supplied by the host shell. */
  navigate?: (route: string) => void;
  focus: Focus | null;
  onFocus: (sourceId: string, date: string) => void;
  busy: boolean;
  /** Kept for compatibility: coarse invalidation counter (all events). */
  generation: number;
}

export type BlockRenderer = ComponentType<{ block: ViewBlock; ctx: ViewContext }>;
export type PageRenderer = ComponentType<{ page: UIPage; ctx: ViewContext }>;
export type PanelRenderer = ComponentType<{ panel: UIPanel; ctx: ViewContext; generation: number }>;

// Every keyed registry holds a SHADOW STACK per key (P1 fix, 2026-11 audit):
// a later module registering the same key shadows the earlier one instead of
// replacing it, and disposing the shadowing module pops the stack — the
// shadowed contribution is restored automatically. With plain set/delete, a
// module overriding another's key and then being disabled destroyed the
// STILL-ACTIVE earlier module's contribution, leaving it "active" but
// unregistered for the whole session.
const blockRenderers = new Map<string, BlockRenderer[]>();
const pageRenderers = new Map<string, PageRenderer[]>();
const panelRenderers = new Map<string, PanelRenderer[]>();

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

// pushShadow pushes one contribution onto a key's shadow stack.
function pushShadow<T>(m: Map<string, T[]>, key: string, value: T): void {
  let stack = m.get(key);
  if (!stack) {
    stack = [];
    m.set(key, stack);
  }
  stack.push(value);
}

// disposeShadow removes `value` from the key's stack WHEREVER it sits and
// reports whether the ACTIVE top changed. Disposing the top pops it — the
// shadowed contribution below is restored. Disposing a SHADOWED entry removes
// it silently (top unchanged, no observable change): the owning module is
// gone, and leaving it in the stack would resurrect a dead module's
// contribution when the entries above it pop. Callers bump only when this
// returns true — the observable registry state changed.
function disposeShadow<T>(m: Map<string, T[]>, key: string, value: T): boolean {
  const stack = m.get(key);
  if (!stack) return false;
  const idx = stack.lastIndexOf(value);
  if (idx < 0) return false;
  stack.splice(idx, 1);
  const wasTop = idx === stack.length; // after removal, top changed iff it was the last
  if (stack.length === 0) {
    m.delete(key);
  }
  return wasTop;
}

function topShadow<T>(m: Map<string, T[]>, key: string): T | undefined {
  const stack = m.get(key);
  return stack ? stack[stack.length - 1] : undefined;
}

/** registerBlockRenderer adds (or shadows) the renderer for one view kind. */
export function registerBlockRenderer(kind: string, renderer: BlockRenderer): () => void {
  pushShadow(blockRenderers, kind, renderer);
  bump();
  return () => {
    if (disposeShadow(blockRenderers, kind, renderer)) {
      bump();
    }
  };
}

/** registerPageRenderer adds (or shadows) a page-level renderer by name. */
export function registerPageRenderer(name: string, renderer: PageRenderer): () => void {
  pushShadow(pageRenderers, name, renderer);
  bump();
  return () => {
    if (disposeShadow(pageRenderers, name, renderer)) {
      bump();
    }
  };
}

/** registerPanelRenderer adds (or shadows) a panel-level renderer by name. */
export function registerPanelRenderer(name: string, renderer: PanelRenderer): () => void {
  pushShadow(panelRenderers, name, renderer);
  bump();
  return () => {
    if (disposeShadow(panelRenderers, name, renderer)) {
      bump();
    }
  };
}

export function getBlockRenderer(kind: string): BlockRenderer | undefined {
  return topShadow(blockRenderers, kind);
}

export function getPageRenderer(name: string): PageRenderer | undefined {
  return topShadow(pageRenderers, name);
}

export function getPanelRenderer(name: string): PanelRenderer | undefined {
  return topShadow(panelRenderers, name);
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

const widgets = new Map<string, WidgetComponent[]>();

/** registerWidget contributes one composable component under a name
 * (shadow-stack semantics — see the registry header). */
export function registerWidget(name: string, component: WidgetComponent): () => void {
  pushShadow(widgets, name, component);
  bump();
  return () => {
    if (disposeShadow(widgets, name, component)) {
      bump();
    }
  };
}

export function getWidget(name: string): WidgetComponent | undefined {
  return topShadow(widgets, name);
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
  /** Optional DISPLAY filter — the shell surfaces the action only when it
   * returns true. It is never a security boundary: hiding the button does
   * not revoke the underlying hub command. Server-side authorization lives
   * at the webui AccessPolicy seam; plugins with side-effectful operations
   * must rely on that, not on this filter. */
  when?: (cx: ActionContext) => boolean;
  run: (cx: ActionContext) => void | Promise<void>;
}

const actions = new Map<string, ActionDefinition[]>();

/** registerAction contributes one context action; re-registering the same id
 * shadows in place (key insertion order preserved, shadow-stack semantics). */
export function registerAction(def: ActionDefinition): () => void {
  pushShadow(actions, def.id, def);
  bump();
  return () => {
    if (disposeShadow(actions, def.id, def)) {
      bump();
    }
  };
}

export function listActions(): ActionDefinition[] {
  const out: ActionDefinition[] = [];
  for (const stack of actions.values()) {
    const top = stack[stack.length - 1];
    if (top) out.push(top);
  }
  return out;
}

// ---- Settings extension point ----------------------------------------------
// A plugin may replace the generic config editor with its own settings UI for
// its plugin id; without one, the explorer falls back to the structured
// ConfigEditor.

export interface SettingsProps {
  pluginId: string;
  /** The plugin's current config (null when it has none yet). */
  config: Record<string, unknown> | null;
  /** Persist the edited config (api.savePluginConfig); resolves after the
   * save round-trip so the component can report success. */
  onSaved: (config: Record<string, unknown>) => Promise<void>;
}
export type SettingsComponent = ComponentType<SettingsProps>;

const settings = new Map<string, SettingsComponent[]>();

/** registerSettings contributes the settings UI for one plugin id
 * (shadow-stack semantics — see the registry header). */
export function registerSettings(name: string, component: SettingsComponent): () => void {
  pushShadow(settings, name, component);
  bump();
  return () => {
    if (disposeShadow(settings, name, component)) {
      bump();
    }
  };
}

export function getSettings(pluginId: string): SettingsComponent | undefined {
  const direct = topShadow(settings, pluginId);
  if (direct) return direct;
  // The explorer lists the ui-client row as "<plugin>:client"; settings bind
  // to the plugin's module name — tolerate both spellings.
  return topShadow(settings, pluginId.replace(/:client$/, ""));
}
