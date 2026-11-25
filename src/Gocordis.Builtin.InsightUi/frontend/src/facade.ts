// Module-scoped facade binding. The console owns the renderer registries; a
// plugin bundle must register through the facade it receives instead of
// importing the runtime by value (that would create a second registry).
import type { api as consoleApi } from "@gocordis/console-runtime";

export interface BuiltinFacade {
  api: typeof consoleApi;
  pages: { register(def: { name: string; component: unknown }): () => void };
  panels: { register(def: { name: string; component: unknown }): () => void };
  blocks: { register(kind: string, component: unknown): () => void };
  widgets: { register(name: string, component: unknown): () => void };
  settings: { register(component: unknown): () => void };
  actions: { register(def: { id: string; title: string; run: (ctx: unknown) => void | Promise<void> }): () => void };
  registeredBlockKinds(): string[];
  getSettings(pluginId: string): unknown;
  subscribeRenderers(listener: () => void): () => void;
  rendererRegistryVersion(): number;
}

let current: BuiltinFacade | null = null;

export function bindFacade(facade: BuiltinFacade): void {
  current = facade;
}

export function facade(): BuiltinFacade {
  if (!current) throw new Error("builtin console facade is not bound");
  return current;
}
