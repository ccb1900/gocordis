import { describe, expect, it } from "vitest";
import register from "./ui";
import type { BuiltinFacade } from "./facade";

function fakeApi() {
  const state = {
    pages: [] as string[],
    panels: [] as string[],
    blocks: [] as string[],
    widgets: [] as string[],
    actions: [] as string[],
    settings: 0,
    dispose: [] as Array<() => void>,
  };
  const track = () => {
    const dispose = () => { state.dispose.push(() => undefined); };
    return dispose;
  };
  const api = {
    api: {} as never,
    pages: { register: (def: { name: string }) => { state.pages.push(def.name); return track(); } },
    panels: { register: (def: { name: string }) => { state.panels.push(def.name); return track(); } },
    blocks: { register: (kind: string) => { state.blocks.push(kind); return track(); } },
    widgets: { register: (name: string) => { state.widgets.push(name); return track(); } },
    actions: { register: (def: { id: string }) => { state.actions.push(def.id); return track(); } },
    settings: { register: () => { state.settings += 1; return track(); } },
    registeredBlockKinds: () => [],
    getSettings: () => undefined,
    subscribeRenderers: () => () => undefined,
    rendererRegistryVersion: () => 0,
  };
  return { api: api as unknown as BuiltinFacade, state };
}

describe("builtin console client module", () => {
  it("registers every product renderer through the facade", () => {
    const { api, state } = fakeApi();
    register(api);

    expect(state.pages).toEqual([
      "gocordis-console-overview",
      "plugin-explorer",
      "plugin-control-plane",
      "framework-dashboard",
      "insight-graph",
      "runtime-inspector",
    ]);
    expect(state.panels).toEqual(["gocordis-console-status", "event-feed"]);
    expect(state.blocks).toEqual([
      "framework-dashboard", "insight-graph", "runtime-inspector",
      "market-catalog", "gocordis-console-callout",
    ]);
    expect(state.widgets).toEqual(["gocordis-console-badge"]);
    expect(state.actions).toEqual(["gocordis-console.ping"]);
    expect(state.settings).toBe(1);
  });
});
