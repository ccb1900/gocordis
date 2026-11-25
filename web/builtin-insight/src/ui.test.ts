import { describe, expect, it } from "vitest";
import register from "./ui";
import type { BuiltinFacade } from "./facade";

function fakeApi() {
  const state = { pages: [] as string[], blocks: [] as string[], panels: [] as string[] };
  const track = () => () => undefined;
  const api = {
    api: {} as never,
    pages: { register: (def: { name: string }) => { state.pages.push(def.name); return track(); } },
    panels: { register: (def: { name: string }) => { state.panels.push(def.name); return track(); } },
    blocks: { register: (kind: string) => { state.blocks.push(kind); return track(); } },
    widgets: { register: () => track() },
    settings: { register: () => track() },
    actions: { register: () => track() },
    registeredBlockKinds: () => [],
    getSettings: () => undefined,
    subscribeRenderers: () => () => undefined,
    rendererRegistryVersion: () => 0,
  };
  return { api: api as unknown as BuiltinFacade, state };
}

describe("builtin insight UI client module", () => {
  it("registers only insight pages and blocks", () => {
    const { api, state } = fakeApi();
    register(api);
    expect(state.pages).toEqual([
      "framework-dashboard", "insight-graph", "runtime-inspector", "market-catalog",
    ]);
    expect(state.blocks).toEqual([
      "framework-dashboard", "insight-graph", "runtime-inspector", "market-catalog",
    ]);
    expect(state.panels).toEqual([]);
  });
});
