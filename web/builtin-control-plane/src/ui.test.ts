import { describe, expect, it } from "vitest";
import register from "./ui";
import type { BuiltinFacade } from "./facade";

function fakeApi() {
  const state = { pages: [] as string[], panels: [] as string[], blocks: [] as string[] };
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

describe("builtin control-plane UI client module", () => {
  it("registers only the control surface and event feed", () => {
    const { api, state } = fakeApi();
    register(api);
    expect(state.pages).toEqual(["plugin-explorer", "plugin-control-plane"]);
    expect(state.panels).toEqual(["event-feed"]);
    expect(state.blocks).toEqual([]);
  });
});
