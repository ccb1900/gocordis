import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
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

  it("does not leave an unresolved facade or ctx reference in product renderers", () => {
    const root = fileURLToPath(new URL(".", import.meta.url));
    for (const name of ["MarketCatalog", "FrameworkDashboard", "RuntimeInspector", "InsightGraphBlock"]) {
      const source = readFileSync(new URL(`./views/${name}.tsx`, import.meta.url), "utf8");
      expect(source).not.toMatch(/^\s*api$/m);
      if (name === "MarketCatalog") {
        expect(source).toContain("{ block, ctx }");
        expect(source).toMatch(/facade\(\)\.api/);
      }
      expect(root).toContain("frontend");
    }
  });
});
