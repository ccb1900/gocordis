// Plugin client modules: the console is a homogeneous frontend — a plugin's
// renderer module is frontend code distributed with the plugin and served
// same-origin by the console server. Installing a plugin means installing
// its client module; the module registers custom page/panel/block renderers
// into the same keyed registry the built-ins use. Backend heterogeneity
// (in-process, out-of-process, WASM) is invisible here: registration is a
// frontend concern.
//
// Trust model: same-origin + operator install. A module poses no risk
// beyond the plugin itself — an out-of-process plugin already runs with
// host privileges. What a module must NOT be treated as is sandboxed code.
//
// Lifecycle (the Active Module Registry): the manager reconciles the desired
// module set (the composition manifest) with the set of active module
// activations, mirroring the runtime's activation semantics — a register
// function's returned dispose plays the role of the Apply-returned Cleanup
// (the activation's LAST effect, unwound FIRST, see runtime/component.go),
// and everything registered through the facade is collected as Effect-style
// inverses unwound LIFO after it. Disabling a plugin removes its module from
// the manifest → every renderer it registered disappears; re-enabling
// re-executes register on the cached ESM (imports are per-URL, so the module
// body stays cached — only the registrations are reversed). A manifest URL
// that changed (?v= content revision) re-imports and re-registers.
//
// Failure & retry anchoring (paper §4.4 raise vs the Controller's diff
// reconcile): a failed import/register rolls back what it already installed
// and the activation never lands — the runtime-side ui-client component is
// untouched, so no fiber outcome is involved. Retries happen ONLY on
// reconcile triggers (composition.changed, bootstrap), never spontaneously:
// this mirrors extensions/config/config.go, where a failed add never enters
// the Controller's applied set and every Reconcile re-attempts it by diff
// ("no automatic retry" = no self-loop; the trigger re-runs the attempt).
// The old loadedModules Set behavior — remembering failures for the whole
// session — had no counterpart in either layer and is gone.
import * as React from "react";
import * as reactJsxRuntime from "react/jsx-runtime";
import * as ReactDOM from "react-dom";
import * as ReactDOMClient from "react-dom/client";
import * as antd from "antd";
import { api } from "../api";
import {
  getBlockRenderer,
  getPanelRenderer,
  getPageRenderer,
  getSettings,
  getWidget,
  listActions,
  registerAction,
  registerBlockRenderer,
  registerPanelRenderer,
  registerPageRenderer,
  registerSettings,
  registerWidget,
  registeredBlockKinds,
  registeredWidgets,
  type ActionDefinition,
  type BlockRenderer,
  type PageRenderer,
  type PanelRenderer,
  type SettingsComponent,
  type WidgetComponent,
} from "../views/registry";

/** FACADE_VERSION is the console's module-facade contract version. Plugin
 * bundles (web/plugin-kit) and register functions may feature-detect it;
 * the rule is major-alignment: a bundle targets the facade contract it was
 * built against. Content updates of a bundle itself are covered separately
 * by the manifest URL's ?v= revision. */
export const FACADE_VERSION = "1";

/** The static half of the module facade: everything a module (or an esbuild
 * shim reading globalThis.__CORDIS_CONSOLE) needs that does not depend on the
 * activating module's identity. */
export interface BaseModuleAPI {
  /** The console's facade contract version (see FACADE_VERSION). */
  frameworkVersion: string;
  React: typeof React;
  /** The console's own react/jsx-runtime: JSX build shims re-export it, so
   * automatic JSX in a bundled plugin runs on the console's React instance
   * (one React per page — hooks require it). */
  jsxRuntime: typeof reactJsxRuntime;
  /** The console's react-dom instances — antd's portals (Modal, message,
   * notification...) must render through the same renderer as the page. */
  reactDom: typeof ReactDOM;
  reactDomClient: typeof ReactDOMClient;
  antd: typeof antd;
  /** Hub named queries/commands — the unified data channel. */
  api: typeof api;
  registerBlockRenderer: typeof registerBlockRenderer;
  registerPageRenderer: typeof registerPageRenderer;
  registerPanelRenderer: typeof registerPanelRenderer;
  registeredBlockKinds: typeof registeredBlockKinds;
}

declare global {
  // eslint-disable-next-line no-var
  var __CORDIS_CONSOLE: BaseModuleAPI | undefined;
}

export function clientModuleAPI(): BaseModuleAPI {
  return {
    frameworkVersion: FACADE_VERSION,
    React, jsxRuntime: reactJsxRuntime,
    reactDom: ReactDOM, reactDomClient: ReactDOMClient,
    antd, api,
    registerBlockRenderer, registerPageRenderer, registerPanelRenderer,
    registeredBlockKinds,
  };
}

export interface PageRendererDef {
  name: string;
  component: PageRenderer;
}
export interface PanelRendererDef {
  name: string;
  component: PanelRenderer;
}

/** The facade handed to every client module's register function: the five
 * extension points (page / panel / widget / settings / action renderers and
 * handlers) plus the block-kind palette. Every register returns an idempotent
 * dispose and is OWNED BY THE MANAGER — the module may release early, and
 * module deactivation unwinds everything LIFO. */
export interface ClientModuleAPI extends BaseModuleAPI {
  /** Page RENDERERS keyed by name. Page definitions themselves are Go-side
   * composition declarations (ui-page components / RegisterPage) — the
   * frontend never declares pages, or it would become a second lifecycle
   * authority. */
  pages: { register(def: PageRendererDef): () => void };
  panels: { register(def: PanelRendererDef): () => void };
  blocks: { register(kind: string, component: BlockRenderer): () => void };
  /** Composable components referenced by declarative `widget` view blocks. */
  widgets: { register(name: string, component: WidgetComponent): () => void };
  /** This plugin's settings UI; bound to the activating module's name. */
  settings: { register(component: SettingsComponent): () => void };
  /** Context-bound operations surfaced by the shell's ActionBar. */
  actions: { register(def: ActionDefinition): () => void };
  listWidgets: typeof registeredWidgets;
  listActions: typeof listActions;
}

export interface ClientModuleRef {
  name: string;
  url: string;
}

type ModuleImporter = (url: string) => Promise<unknown>;

async function nativeImport(url: string): Promise<unknown> {
  return import(/* @vite-ignore */ url);
}

/** One active module activation: everything reversible the module created,
 * owned by the manager (the module never cleans the global registries
 * itself). */
interface ModuleActivation {
  name: string;
  url: string;
  /** Framework-collected inverses in creation order; unwound LIFO. */
  disposables: Array<() => void>;
  /** The dispose returned by register — the activation's last effect,
   * unwound before the collected inverses (mirrors runtime/component.go:
   * the Apply-returned Cleanup is the last effect, unwound first). */
  moduleDispose?: () => void;
}

export interface ModuleManagerDeps {
  manifestUrl?: string;
  importer?: ModuleImporter;
  apiFactory?: () => BaseModuleAPI;
}

/** ModuleManagerState is the manager's observable state. reconciling is
 * true from the moment a reconcile is requested until the LAST queued one
 * settles. Consumers use it to tell an honest in-between (plugin modules
 * still arriving — render a loading state) from a real composition
 * mismatch (module set settled, renderer genuinely missing). */
export interface ModuleManagerState {
  reconciling: boolean;
}

export interface ModuleManager {
  /** reconcile brings the active module set to the manifest: loads and
   * registers newly declared modules, disposes modules no longer declared,
   * re-imports modules whose URL (content revision) changed. Failures are
   * contained per module: the rest still load, and a failed module is
   * retried on the next reconcile. Returns the names active afterwards. */
  reconcile(): Promise<string[]>;
  /** Names of the modules with a live activation (diagnostics/tests). */
  activeNames(): string[];
  state(): ModuleManagerState;
  /** subscribeState registers a state listener; returns the unsubscribe. */
  subscribeState(listener: () => void): () => void;
}

export function createModuleManager(deps: ModuleManagerDeps = {}): ModuleManager {
  const manifestUrl = deps.manifestUrl ?? "/api/ui/client-modules";
  const importer = deps.importer ?? nativeImport;
  const apiFactory = deps.apiFactory ?? clientModuleAPI;

  // Imported module namespace objects, keyed by manifest URL. Kept across
  // enable/disable cycles: the browser's ESM cache holds them anyway, and
  // re-registering on re-enable only re-runs register(), not the module body.
  // A changed URL (?v= revision) is a different key and imports fresh.
  const moduleCache = new Map<string, unknown>();
  const activations = new Map<string, ModuleActivation>();

  // Reconciles are serialized: only one diff runs at a time, later callers
  // queue behind the in-flight one (composition bumps can overlap the
  // bootstrap call).
  let queue: Promise<unknown> = Promise.resolve();

  /** once guards a dispose: a module releasing its own contribution early
   * must not double-run when the manager later disposes the activation. */
  function once(dispose: () => void): () => void {
    let done = false;
    return () => {
      if (done) return;
      done = true;
      dispose();
    };
  }

  /** activationFacade wraps the base facade so EVERY extension-point
   * registration made through it is recorded on the activation — the
   * framework owns the resources, not the module. Registrations that would
   * silently override a contribution of ANOTHER active module are warned:
   * the keyed registry stays first-class open, but cross-plugin shadowing
   * becomes visible instead of confusing (paper 铁律 11: one provider per
   * key within a namespace). Re-registering one's own key (module restart
   * after dispose, or an intentional in-module override) is silent. */
  function activationFacade(activation: ModuleActivation, moduleName: string): ClientModuleAPI {
    const base = apiFactory();
    const ownKeys = new Set<string>();
    const guard = (type: string, key: string, occupied: () => boolean): void => {
      const id = `${type}:${key}`;
      if (!ownKeys.has(id) && occupied()) {
        console.warn(
          `[console] client module "${moduleName}" overrides ${type} "${key}" — an active module already contributes it`,
        );
      }
      ownKeys.add(id);
    };
    const track = (dispose: () => void): (() => void) => {
      const guarded = once(dispose);
      activation.disposables.push(guarded);
      return guarded;
    };
    return {
      ...base,
      registerBlockRenderer: (kind, renderer) => {
        guard("block kind", kind, () => getBlockRenderer(kind) !== undefined);
        return track(base.registerBlockRenderer(kind, renderer));
      },
      registerPageRenderer: (name, renderer) => {
        guard("page renderer", name, () => getPageRenderer(name) !== undefined);
        return track(base.registerPageRenderer(name, renderer));
      },
      registerPanelRenderer: (name, renderer) => {
        guard("panel renderer", name, () => getPanelRenderer(name) !== undefined);
        return track(base.registerPanelRenderer(name, renderer));
      },
      pages: {
        register: (def) => {
          guard("page renderer", def.name, () => getPageRenderer(def.name) !== undefined);
          return track(base.registerPageRenderer(def.name, def.component));
        },
      },
      panels: {
        register: (def) => {
          guard("panel renderer", def.name, () => getPanelRenderer(def.name) !== undefined);
          return track(base.registerPanelRenderer(def.name, def.component));
        },
      },
      blocks: {
        register: (kind, component) => {
          guard("block kind", kind, () => getBlockRenderer(kind) !== undefined);
          return track(base.registerBlockRenderer(kind, component));
        },
      },
      widgets: {
        register: (name, component) => {
          guard("widget", name, () => getWidget(name) !== undefined);
          return track(registerWidget(name, component));
        },
      },
      actions: {
        register: (def) => {
          guard("action", def.id, () => listActions().some((a) => a.id === def.id));
          return track(registerAction(def));
        },
      },
      // The Settings extension point binds to the activating module's name —
      // the plugin never repeats its own id (and cannot bind to someone else's).
      settings: {
        register: (component) => {
          guard("settings", moduleName, () => getSettings(moduleName) !== undefined);
          return track(registerSettings(moduleName, component));
        },
      },
      listWidgets: registeredWidgets,
      listActions,
    };
  }

  /** unwind reverses one activation: the module's own dispose first (the
   * "last effect"), then the collected inverses LIFO — the order the runtime
   * prescribes for Apply-returned Cleanup vs Context.Effect. Individual
   * failures are contained: unwinding continues past them. */
  function unwind(activation: ModuleActivation): void {
    const steps: Array<() => void> = [];
    if (activation.moduleDispose) steps.push(activation.moduleDispose);
    for (let i = activation.disposables.length - 1; i >= 0; i--) {
      steps.push(activation.disposables[i]);
    }
    for (const step of steps) {
      try {
        step();
      } catch (e) {
        console.warn(`[console] client module "${activation.name}" dispose step failed`, e);
      }
    }
  }

  async function activate(m: ClientModuleRef): Promise<void> {
    let mod = moduleCache.get(m.url);
    if (mod === undefined) {
      try {
        mod = await importer(m.url);
      } catch (e) {
        console.warn(`[console] client module "${m.name}" failed to load`, e);
        return; // not activated; retried on the next reconcile
      }
      moduleCache.set(m.url, mod);
    }
    const register = (mod as { default?: unknown; register?: unknown })?.default
      ?? (mod as { register?: unknown })?.register;
    if (typeof register !== "function") {
      console.warn(`[console] client module "${m.name}" exports no register function; skipped`);
      return;
    }
    const activation: ModuleActivation = { name: m.name, url: m.url, disposables: [] };
    try {
      let returned = (register as (api: ClientModuleAPI) => unknown)(
        activationFacade(activation, m.name)
      );
      // P2 fix (2026-11 audit): an async `register` used to LAND the
      // activation immediately and drop both its returned dispose and its
      // rollback — a throw after the first `await` rejected outside this try,
      // leaving partial registrations live. A returned Promise is now awaited
      // INSIDE the guarded window: the activation lands only when the module
      // body has settled, and a rejection rolls back exactly like a sync
      // throw. (The reconcile's `reconciling` flag honestly covers the await.)
      if (
        returned
        && typeof (returned as { then?: unknown }).then === "function"
      ) {
        returned = await (returned as Promise<unknown>);
      }
      if (typeof returned === "function") activation.moduleDispose = returned as () => void;
    } catch (e) {
      // Partial registration rolls back: everything registered so far
      // unwinds, the activation never lands, the next reconcile retries.
      // One broken module never blocks the console — or its siblings.
      unwind(activation);
      console.warn(`[console] client module "${m.name}" failed to register`, e);
      return;
    }
    activations.set(m.name, activation);
  }

  function disposeModule(name: string): void {
    const activation = activations.get(name);
    if (!activation) return;
    activations.delete(name);
    unwind(activation);
  }

  async function reconcileOnce(): Promise<void> {
    let desired: ClientModuleRef[];
    try {
      const res = await fetch(manifestUrl);
      if (!res.ok) return; // unknown desired state: keep current activations
      const body = (await res.json()) as { data?: { modules?: ClientModuleRef[] } };
      desired = body.data?.modules ?? [];
    } catch {
      return; // console infrastructure absent (or offline dev server): fine
    }
    // The facade is also exposed on globalThis so a bundled plugin can alias
    // bare specifiers (e.g. esbuild --alias:react=shim.js reading this global)
    // and keep using the console's own React instance — required for hooks.
    globalThis.__CORDIS_CONSOLE = apiFactory();
    for (const m of desired) {
      const current = activations.get(m.name);
      if (current && current.url === m.url) continue; // already active, same content
      if (current) {
        disposeModule(m.name); // URL changed → content revision changed
        // P3 fix (2026-11 audit): evict the superseded generation from the
        // module cache — the cache never deleted, so a long-lived operator
        // session retained every dead ?v= namespace forever.
        if (current.url !== m.url) moduleCache.delete(current.url);
      }
      await activate(m);
    }
    const desiredNames = new Set(desired.map((m) => m.name));
    for (const name of Array.from(activations.keys())) {
      if (!desiredNames.has(name)) disposeModule(name);
    }
  }

  function activeNames(): string[] {
    return Array.from(activations.keys());
  }

  // Observable reconcile state: true from the request until the LAST queued
  // reconcile settles (consumers render "loading", not "missing", in between).
  const stateListeners = new Set<() => void>();
  let inflight = 0;
  function bumpInflight(delta: number): void {
    const before = inflight > 0;
    inflight += delta;
    if ((inflight > 0) !== before) {
      for (const listener of stateListeners) listener();
    }
  }
  function state(): ModuleManagerState {
    return { reconciling: inflight > 0 };
  }

  return {
    reconcile(): Promise<string[]> {
      bumpInflight(1);
      // The chain never rejects (reconcileOnce contains its own failures and
      // this catch guards injected-dependency surprises): callers may `void` it.
      const run = queue
        .then(reconcileOnce, reconcileOnce)
        .catch((e) => console.warn("[console] client module reconcile failed", e))
        .then(activeNames)
        .then((names) => {
          bumpInflight(-1);
          return names;
        });
      queue = run;
      return run;
    },
    activeNames,
    state,
    subscribeState(listener: () => void): () => void {
      stateListeners.add(listener);
      return () => {
        stateListeners.delete(listener);
      };
    },
  };
}

// The console-wide manager. App and the bootstrap entry drive it on every
// composition change; tests build isolated managers via createModuleManager.
const defaultManager = createModuleManager();

/** reconcileClientModules syncs the active client modules with the
 * composition manifest. Safe to call repeatedly: identical desired sets are
 * no-ops, and concurrent calls are serialized. */
export function reconcileClientModules(): Promise<string[]> {
  return defaultManager.reconcile();
}

/** clientModuleManagerState exposes the console-wide manager's observable
 * state for React consumers (useSyncExternalStore with
 * subscribeClientModulesState): a renderer lookup that misses while a
 * reconcile is in flight is an in-between, not a failure. */
export function clientModuleManagerState(): ModuleManagerState {
  return defaultManager.state();
}

export function subscribeClientModulesState(listener: () => void): () => void {
  return defaultManager.subscribeState(listener);
}
