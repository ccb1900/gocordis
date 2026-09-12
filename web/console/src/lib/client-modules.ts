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
import * as React from "react";
import * as reactJsxRuntime from "react/jsx-runtime";
import * as ReactDOM from "react-dom";
import * as ReactDOMClient from "react-dom/client";
import * as antd from "antd";
import { api } from "../api";
import {
  registerBlockRenderer,
  registerPageRenderer,
  registerPanelRenderer,
  registeredBlockKinds,
} from "../views/registry";

/** The facade handed to every client module's register function. */
export interface ClientModuleAPI {
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
  var __CORDIS_CONSOLE: ClientModuleAPI | undefined;
}

export function clientModuleAPI(): ClientModuleAPI {
  return {
    React, jsxRuntime: reactJsxRuntime,
    reactDom: ReactDOM, reactDomClient: ReactDOMClient,
    antd, api,
    registerBlockRenderer, registerPageRenderer, registerPanelRenderer,
    registeredBlockKinds,
  };
}

export interface ClientModuleRef {
  name: string;
  url: string;
}

// Modules imported during this console session; loadClientModules is safe
// to call again (composition.changed → newly installed modules load, known
// ones are not re-imported).
const loadedModules = new Set<string>();

type ModuleImporter = (url: string) => Promise<unknown>;

async function nativeImport(url: string): Promise<unknown> {
  return import(/* @vite-ignore */ url);
}

/** loadClientModules fetches the manifest and registers every module.
 * One broken module never blocks the console: it is skipped with a
 * console warning. Returns the names that registered successfully. */
export async function loadClientModules(
  manifestUrl: string = "/api/ui/client-modules",
  importer: ModuleImporter = nativeImport,
  apiFactory: () => ClientModuleAPI = clientModuleAPI
): Promise<string[]> {
  let modules: ClientModuleRef[] = [];
  try {
    const res = await fetch(manifestUrl);
    if (!res.ok) return [];
    const body = (await res.json()) as { data?: { modules?: ClientModuleRef[] } };
    modules = body.data?.modules ?? [];
  } catch {
    return []; // console infrastructure absent (or offline dev server): fine
  }
  const registered: string[] = [];
  // The facade is also exposed on globalThis so a bundled plugin can alias
  // bare specifiers (e.g. esbuild --alias:react=shim.js reading this global)
  // and keep using the console's own React instance — required for hooks.
  globalThis.__CORDIS_CONSOLE = apiFactory();
  for (const m of modules) {
    if (loadedModules.has(m.url)) {
      registered.push(m.name); // already imported this session
      continue;
    }
    try {
      loadedModules.add(m.url);
      const mod = (await importer(m.url)) as {
        default?: unknown;
        register?: unknown;
      };
      const register = mod?.default ?? mod?.register;
      if (typeof register !== "function") {
        console.warn(`[console] client module "${m.name}" exports no register function; skipped`);
        continue;
      }
      (register as (api: ClientModuleAPI) => void)(apiFactory());
      registered.push(m.name);
    } catch (e) {
      console.warn(`[console] client module "${m.name}" failed to load`, e);
    }
  }
  return registered;
}
