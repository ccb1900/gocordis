// Client module loader tests: manifest handling and the register contract,
// with the dynamic import injected (no network, no real modules).
import { afterEach, describe, expect, it, vi } from "vitest";
import { loadClientModules, type ClientModuleAPI } from "./client-modules";

afterEach(() => {
  vi.restoreAllMocks();
});

function okManifest(modules: unknown) {
  return async () => ({
    ok: true,
    json: async () => ({ data: { modules } }),
  });
}

describe("loadClientModules", () => {
  it("calls the register function with the facade", async () => {
    const register = vi.fn();
    const fetchMock = okManifest([{ name: "demo", url: "/client-modules/demo.js" }]);
    vi.stubGlobal("fetch", fetchMock);
    const loaded = await loadClientModules("/x", async () => ({ default: register }));
    expect(loaded).toEqual(["demo"]);
    const facade = register.mock.calls[0][0] as ClientModuleAPI;
    expect(typeof facade.registerBlockRenderer).toBe("function");
    expect(typeof facade.registerPageRenderer).toBe("function");
    expect(typeof facade.registerPanelRenderer).toBe("function");
    expect(typeof facade.api.hubQuery).toBe("function");
    expect(typeof facade.React.createElement).toBe("function");
    expect(typeof facade.jsxRuntime.jsx).toBe("function");
    expect(facade.reactDom).toBeTruthy();
    expect(typeof facade.reactDomClient.createRoot).toBe("function");
    expect(facade.antd).toBeTruthy();
  });

  it("accepts a bare register export", async () => {
    const register = vi.fn();
    vi.stubGlobal("fetch", okManifest([{ name: "bare", url: "/client-modules/bare.js" }]));
    const loaded = await loadClientModules("/x", async () => ({ register }));
    expect(loaded).toEqual(["bare"]);
  });

  it("skips a broken module and keeps loading the rest", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const register = vi.fn();
    vi.stubGlobal(
      "fetch",
      okManifest([
        { name: "broken", url: "/client-modules/broken.js" },
        { name: "good", url: "/client-modules/good.js" },
      ])
    );
    const loaded = await loadClientModules("/x", async (url) => {
      if (url.includes("broken")) throw new Error("syntax error");
      return { default: register };
    });
    expect(loaded).toEqual(["good"]);
    expect(warn).toHaveBeenCalled();
  });

  it("skips modules without a register function", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    vi.stubGlobal("fetch", okManifest([{ name: "empty", url: "/client-modules/empty.js" }]));
    const loaded = await loadClientModules("/x", async () => ({}));
    expect(loaded).toEqual([]);
    expect(warn).toHaveBeenCalled();
  });

  it("returns nothing when the manifest is absent or failing", async () => {
    vi.stubGlobal("fetch", async () => ({ ok: false }));
    expect(await loadClientModules("/x")).toEqual([]);
    vi.stubGlobal("fetch", async () => {
      throw new Error("offline");
    });
    expect(await loadClientModules("/x")).toEqual([]);
  });
});
