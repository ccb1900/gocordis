// Client module manager tests: the reconcile lifecycle — activation,
// disposal on disable, re-registration on re-enable, content-revision
// re-import, failure rollback + retry, and leak-free repeated cycles.
// The dynamic import and manifest fetch are injected (no network, no real
// modules); renderer assertions go through the real keyed registry so the
// tests pin actual resource ownership, not mocks of it.
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  FACADE_VERSION,
  clientModuleAPI,
  createModuleManager,
  type ClientModuleAPI,
} from "./client-modules";
import {
  getBlockRenderer,
  getPageRenderer,
  getPanelRenderer,
  getSettings,
  getWidget,
  listActions,
  registeredBlockKinds,
  type BlockRenderer,
  type PageRenderer,
  type PanelRenderer,
} from "../views/registry";

afterEach(() => {
  vi.restoreAllMocks();
});

function okManifest(modules: unknown) {
  return async () => ({
    ok: true,
    json: async () => ({ data: { modules } }),
  });
}

type Register = (api: ClientModuleAPI) => unknown;

function manager(register: Register, opts: { importer?: (url: string) => Promise<unknown> } = {}) {
  return createModuleManager({
    manifestUrl: "/x",
    importer: opts.importer ?? (async () => ({ default: register })),
  });
}

const noopBlock: BlockRenderer = () => null;
const noopPage: PageRenderer = () => null;
const noopPanel: PanelRenderer = () => null;

describe("client module manager", () => {
  it("registers renderers through the facade into the keyed registry", async () => {
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm1-block", noopBlock);
      api.registerPageRenderer("cm1-page", noopPage);
      api.registerPanelRenderer("cm1-panel", noopPanel);
    });
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = manager(register);

    expect(await mgr.reconcile()).toEqual(["demo"]);
    expect(register).toHaveBeenCalledTimes(1);
    expect(getBlockRenderer("cm1-block")).toBeDefined();
    expect(getPageRenderer("cm1-page")).toBeDefined();
    expect(getPanelRenderer("cm1-panel")).toBeDefined();

    // The facade keeps the full contract (React instance, hub client, ...).
    const facade = register.mock.calls[0][0] as ClientModuleAPI;
    expect(typeof facade.React.createElement).toBe("function");
    expect(typeof facade.jsxRuntime.jsx).toBe("function");
    expect(facade.reactDom).toBeTruthy();
    expect(typeof facade.reactDomClient.createRoot).toBe("function");
    expect(facade.antd).toBeTruthy();
    expect(typeof facade.api.hubQuery).toBe("function");
    expect(typeof facade.registeredBlockKinds).toBe("function");
    // The five extension points, grouped (target API shape).
    expect(typeof facade.pages.register).toBe("function");
    expect(typeof facade.panels.register).toBe("function");
    expect(typeof facade.blocks.register).toBe("function");
    expect(typeof facade.widgets.register).toBe("function");
    expect(typeof facade.settings.register).toBe("function");
    expect(typeof facade.actions.register).toBe("function");
    expect(typeof facade.listWidgets).toBe("function");
    expect(typeof facade.listActions).toBe("function");
    expect(facade.frameworkVersion).toBe(FACADE_VERSION);
  });

  it("tracks grouped extension-point registrations on the activation", async () => {
    const register = vi.fn((api: ClientModuleAPI) => {
      const disposePage = api.pages.register({ name: "grp-page", component: () => null });
      api.panels.register({ name: "grp-panel", component: () => null });
      api.blocks.register("grp-kind", () => null);
      api.widgets.register("grp-widget", () => null);
      api.actions.register({ id: "grp-action", title: "Do", run: () => undefined });
      api.settings.register(() => null);
      expect(getPageRenderer("grp-page")).toBeDefined();
      expect(getWidget("grp-widget")).toBeDefined();
      expect(listActions().map((a) => a.id)).toContain("grp-action");
      disposePage(); // early self-release stays allowed and idempotent
      expect(getPageRenderer("grp-page")).toBeUndefined();
    });
    vi.stubGlobal("fetch", okManifest([{ name: "grouped", url: "/cm/grouped.js" }]));
    const mgr = manager(register);
    await mgr.reconcile();
    // The Settings UI binds to the MODULE name (the plugin), not a component id.
    expect(getSettings("grouped")).toBeDefined();

    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    // Deactivation unwinds every grouped registration.
    expect(getBlockRenderer("grp-kind")).toBeUndefined();
    expect(getPanelRenderer("grp-panel")).toBeUndefined();
    expect(getWidget("grp-widget")).toBeUndefined();
    expect(getSettings("grouped")).toBeUndefined();
    expect(listActions().map((a) => a.id)).not.toContain("grp-action");
  });

  it("accepts a bare register export", async () => {
    const register = vi.fn();
    vi.stubGlobal("fetch", okManifest([{ name: "bare", url: "/cm/bare.js" }]));
    const mgr = manager(register, { importer: async () => ({ register }) });
    expect(await mgr.reconcile()).toEqual(["bare"]);
  });

  it("disable disposes everything: module dispose first, then collected inverses LIFO", async () => {
    // Mirror of runtime/component.go: the register-returned dispose is the
    // activation's LAST effect (unwound first); facade registrations are the
    // Effect inverses (unwound LIFO after it). Instrumented here to pin order.
    const order: string[] = [];
    const base = clientModuleAPI();
    const apiFactory = () =>
      ({
        ...base,
        registerBlockRenderer: (kind: string, renderer: BlockRenderer) => {
          const real = base.registerBlockRenderer(kind, renderer);
          return () => {
            order.push("block");
            real();
          };
        },
        registerPageRenderer: (name: string, renderer: PageRenderer) => {
          const real = base.registerPageRenderer(name, renderer);
          return () => {
            order.push("page");
            real();
          };
        },
      }) as unknown as ClientModuleAPI;
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm2-block", noopBlock);
      api.registerPageRenderer("cm2-page", noopPage);
      return () => {
        order.push("module");
      };
    });
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = createModuleManager({
      manifestUrl: "/x",
      importer: async () => ({ default: register }),
      apiFactory,
    });
    await mgr.reconcile();
    expect(mgr.activeNames()).toEqual(["demo"]);

    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    expect(mgr.activeNames()).toEqual([]);
    expect(getBlockRenderer("cm2-block")).toBeUndefined();
    expect(getPageRenderer("cm2-page")).toBeUndefined();
    expect(order).toEqual(["module", "page", "block"]);
  });

  it("re-enable re-registers on the cached module without re-importing", async () => {
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm3-block", noopBlock);
    });
    const importer = vi.fn(async () => ({ default: register }));
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = manager(register, { importer });
    await mgr.reconcile();
    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    await mgr.reconcile();

    // The ESM body stays cached (one import); register re-executes (twice).
    expect(importer).toHaveBeenCalledTimes(1);
    expect(register).toHaveBeenCalledTimes(2);
    expect(getBlockRenderer("cm3-block")).toBeDefined();
  });

  it("a changed URL (content revision) re-imports and re-registers", async () => {
    const renderers: BlockRenderer[] = [];
    const register = vi.fn((api: ClientModuleAPI) => {
      const renderer = () => null; // a fresh renderer instance per registration
      renderers.push(renderer);
      api.registerBlockRenderer("cm4-block", renderer);
    });
    const importer = vi.fn(async () => ({ default: register }));
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js?v=11111111" }]));
    const mgr = manager(register, { importer });
    await mgr.reconcile();
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js?v=22222222" }]));
    await mgr.reconcile();

    expect(importer).toHaveBeenCalledTimes(2);
    expect(register).toHaveBeenCalledTimes(2);
    expect(getBlockRenderer("cm4-block")).toBe(renderers[1]);
    expect(getBlockRenderer("cm4-block")).not.toBe(renderers[0]);
  });

  it("a module failing mid-register rolls back, isolates siblings, and retries", async () => {
    const good = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm5-good", noopBlock);
    });
    const bad = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm5-bad", noopBlock);
      throw new Error("boom");
    });
    const importer = vi.fn(async (url: string) =>
      url.includes("bad") ? { default: bad } : { default: good }
    );
    const manifest = okManifest([
      { name: "bad", url: "/cm/bad.js" },
      { name: "good", url: "/cm/good.js" },
    ]);
    vi.stubGlobal("fetch", manifest);
    const mgr = manager(good, { importer });

    expect(await mgr.reconcile()).toEqual(["good"]);
    expect(getBlockRenderer("cm5-bad")).toBeUndefined(); // rolled back
    expect(getBlockRenderer("cm5-good")).toBeDefined(); // sibling unaffected

    // Retry: the failure is not remembered (the old loadedModules Set
    // poisoned failed URLs for the session). The module body stays cached,
    // so only register re-runs.
    await mgr.reconcile();
    expect(importer).toHaveBeenCalledTimes(2); // bad once, good once
    expect(bad).toHaveBeenCalledTimes(2);
    expect(good).toHaveBeenCalledTimes(1);
    expect(mgr.activeNames()).toEqual(["good"]);
    expect(getBlockRenderer("cm5-bad")).toBeUndefined();
    expect(getBlockRenderer("cm5-good")).toBeDefined();
  });

  it("an import failure is not remembered and retries on the next reconcile", async () => {
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm6-block", noopBlock);
    });
    let fail = true;
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const importer = vi.fn(async () => {
      if (fail) throw new Error("network");
      return { default: register };
    });
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = manager(register, { importer });

    expect(await mgr.reconcile()).toEqual([]);
    fail = false;
    expect(await mgr.reconcile()).toEqual(["demo"]);
    expect(register).toHaveBeenCalledTimes(1);
    expect(warn).toHaveBeenCalled();
  });

  it("a failing manifest fetch keeps current activations (no unload)", async () => {
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm7-block", noopBlock);
    });
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = manager(register);
    await mgr.reconcile();

    // Unknown desired state ≠ empty desired state: a flaky fetch must not
    // unload every live plugin.
    vi.stubGlobal("fetch", async () => {
      throw new Error("offline");
    });
    expect(await mgr.reconcile()).toEqual(["demo"]);
    expect(getBlockRenderer("cm7-block")).toBeDefined();
    vi.stubGlobal("fetch", async () => ({ ok: false }));
    expect(await mgr.reconcile()).toEqual(["demo"]);
    expect(getBlockRenderer("cm7-block")).toBeDefined();
  });

  it("an unchanged desired set does not re-register", async () => {
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm8-block", noopBlock);
    });
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = manager(register);
    await mgr.reconcile();
    await mgr.reconcile();
    await mgr.reconcile();
    expect(register).toHaveBeenCalledTimes(1);
    expect(await mgr.reconcile()).toEqual(["demo"]);
  });

  it("a module releasing its own contribution early does not double-dispose", async () => {
    const underlying = vi.fn();
    const base = clientModuleAPI();
    const apiFactory = () =>
      ({
        ...base,
        registerBlockRenderer: (kind: string, renderer: BlockRenderer) => {
          const real = base.registerBlockRenderer(kind, renderer);
          return () => {
            underlying();
            real();
          };
        },
      }) as unknown as ClientModuleAPI;
    const register = vi.fn((api: ClientModuleAPI) => {
      const dispose = api.registerBlockRenderer("cm9-block", noopBlock);
      dispose(); // module releases it early — the manager must not re-run it
    });
    vi.stubGlobal("fetch", okManifest([{ name: "demo", url: "/cm/demo.js" }]));
    const mgr = createModuleManager({
      manifestUrl: "/x",
      importer: async () => ({ default: register }),
      apiFactory,
    });
    await mgr.reconcile();
    expect(underlying).toHaveBeenCalledTimes(1);
    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    expect(underlying).toHaveBeenCalledTimes(1);
    expect(getBlockRenderer("cm9-block")).toBeUndefined();
  });

  it("100 enable/disable cycles leave no renderer, activation, or dispose residue", async () => {
    const moduleDispose = vi.fn();
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm10-block", noopBlock);
      api.registerPageRenderer("cm10-page", noopPage);
      api.registerPanelRenderer("cm10-panel", noopPanel);
      return moduleDispose;
    });
    const importer = vi.fn(async () => ({ default: register }));
    const on = okManifest([{ name: "demo", url: "/cm/demo.js" }]);
    const off = okManifest([]);
    const mgr = manager(register, { importer });
    for (let i = 0; i < 100; i++) {
      vi.stubGlobal("fetch", on);
      await mgr.reconcile();
      expect(getBlockRenderer("cm10-block")).toBeDefined();
      expect(mgr.activeNames()).toEqual(["demo"]);
      vi.stubGlobal("fetch", off);
      await mgr.reconcile();
      expect(getBlockRenderer("cm10-block")).toBeUndefined();
      expect(getPageRenderer("cm10-page")).toBeUndefined();
      expect(getPanelRenderer("cm10-panel")).toBeUndefined();
      expect(mgr.activeNames()).toEqual([]);
    }
    // Nothing accumulates: one import, 100 registrations, 100 disposals.
    expect(importer).toHaveBeenCalledTimes(1);
    expect(register).toHaveBeenCalledTimes(100);
    expect(moduleDispose).toHaveBeenCalledTimes(100);
    expect(registeredBlockKinds()).not.toContain("cm10-block");
  });

  it("overlapping reconciles serialize and produce a consistent state", async () => {
    const registerA = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cmA-block", noopBlock);
    });
    const registerB = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cmB-block", noopBlock);
    });
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const importer = vi.fn(async (url: string) => {
      if (url.includes("a.js")) await gate; // the first import hangs
      return url.includes("a.js") ? { default: registerA } : { default: registerB };
    });
    // Manifest by fetch call: [a] for the first reconcile, [a, b] for the
    // second (a call-count stub avoids a re-stub race with queued microtasks).
    let manifestCalls = 0;
    vi.stubGlobal("fetch", async () => {
      manifestCalls++;
      const modules =
        manifestCalls === 1
          ? [{ name: "a", url: "/cm/a.js" }]
          : [
              { name: "a", url: "/cm/a.js" },
              { name: "b", url: "/cm/b.js" },
            ];
      return { ok: true, json: async () => ({ data: { modules } }) };
    });
    const mgr = manager(registerA, { importer });
    const first = mgr.reconcile();
    const second = mgr.reconcile();
    release();

    expect(await first).toEqual(["a"]);
    expect(await second).toEqual(["a", "b"]);
    expect(registerA).toHaveBeenCalledTimes(1); // no double activation
    expect(registerB).toHaveBeenCalledTimes(1);
    expect(getBlockRenderer("cmA-block")).toBeDefined();
    expect(getBlockRenderer("cmB-block")).toBeDefined();
  });

  it("skips modules without a register function", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    vi.stubGlobal("fetch", okManifest([{ name: "empty", url: "/cm/empty.js" }]));
    const mgr = manager(vi.fn(), { importer: async () => ({}) });
    expect(await mgr.reconcile()).toEqual([]);
    expect(warn).toHaveBeenCalled();
    expect(mgr.activeNames()).toEqual([]);
  });

  it("one broken module never blocks the console", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const register = vi.fn((api: ClientModuleAPI) => {
      api.registerBlockRenderer("cm11-block", noopBlock);
    });
    const importer = vi.fn(async (url: string) => {
      if (url.includes("broken")) throw new Error("syntax error");
      return { default: register };
    });
    vi.stubGlobal(
      "fetch",
      okManifest([
        { name: "broken", url: "/cm/broken.js" },
        { name: "good", url: "/cm/good.js" },
      ])
    );
    const mgr = manager(register, { importer });
    expect(await mgr.reconcile()).toEqual(["good"]);
    expect(getBlockRenderer("cm11-block")).toBeDefined();
    expect(warn).toHaveBeenCalled();
  });

  it("reports reconciling across queue boundaries", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const importer = vi.fn(async (url: string) => {
      if (url.includes("slow")) await gate;
      return { default: vi.fn() };
    });
    vi.stubGlobal("fetch", okManifest([{ name: "slow", url: "/cm/slow.js" }]));
    const mgr = createModuleManager({ manifestUrl: "/x", importer });
    const seen: boolean[] = [];
    const unsub = mgr.subscribeState(() => seen.push(mgr.state().reconciling));
    expect(mgr.state().reconciling).toBe(false);

    const first = mgr.reconcile();
    expect(mgr.state().reconciling).toBe(true);
    const second = mgr.reconcile(); // queued: the flag must stay true, no blip
    release();
    await first;
    await second;
    expect(mgr.state().reconciling).toBe(false);
    unsub();
    expect(seen).toEqual([true, false]);
  });

  it("warns when an activation overrides another active module's key, silent on self re-register", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const sharedA = vi.fn((api: ClientModuleAPI) => {
      api.registerPageRenderer("shared", () => null);
    });
    const sharedB = vi.fn((api: ClientModuleAPI) => {
      api.registerPageRenderer("shared", () => null);
    });
    vi.stubGlobal(
      "fetch",
      okManifest([
        { name: "a", url: "/cm/a.js" },
        { name: "b", url: "/cm/b.js" },
      ])
    );
    const mgr = createModuleManager({
      manifestUrl: "/x",
      importer: async (url) => ({ default: url.includes("b") ? sharedB : sharedA }),
    });
    await mgr.reconcile();
    const overrides = warn.mock.calls
      .map((c) => String(c[0]))
      .filter((m) => m.includes("overrides page renderer"));
    expect(overrides.length).toBe(1);
    expect(overrides[0]).toContain(`"b"`);

    // Both modules disabled, then "a" alone re-enabled: its dispose freed the
    // key first — re-registration is its own, silent.
    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    warn.mockClear();
    vi.stubGlobal("fetch", okManifest([{ name: "a", url: "/cm/a.js" }]));
    await mgr.reconcile();
    expect(warn.mock.calls.filter((c) => String(c[0]).includes("overrides")).length).toBe(0);
  });
});

// P2 regression (2026-11 audit): an async `register` used to land the
// activation immediately, drop its returned dispose, and skip rollback when a
// throw happened after the first await. The promise is now awaited INSIDE the
// guarded window.
describe("async register contract", () => {
  it("awaits an async register, stores its dispose, and lands only after settle", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    let disposed = 0;
    const asyncRegister = vi.fn(async (api: ClientModuleAPI) => {
      await Promise.resolve(); // the body settles AFTER the sync return
      api.registerPageRenderer("async-page", () => null);
      return () => {
        disposed++;
      };
    });
    vi.stubGlobal("fetch", okManifest([{ name: "a", url: "/cm/a.js" }]));
    const mgr = createModuleManager({
      manifestUrl: "/x",
      importer: async () => ({ default: asyncRegister }),
    });
    await mgr.reconcile();
    expect(asyncRegister).toHaveBeenCalledTimes(1);
    expect(disposed).toBe(0);
    expect(mgr.activeNames()).toContain("a");

    // Disposing (module leaves the manifest) runs the module's returned dispose.
    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    expect(disposed).toBe(1);
    warn.mockRestore();
  });

  it("rolls back partial registrations when an async register rejects after awaiting", async () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => undefined);
    const disposed: string[] = [];
    const asyncRegister = vi.fn(async (api: ClientModuleAPI) => {
      api.registerPageRenderer("boom-page", () => null);
      const d = api.registerBlockRenderer("boom-block", () => null);
      disposed.push("block-registered");
      void d;
      await Promise.reject(new Error("late failure"));
      return () => disposed.push("never");
    });
    vi.stubGlobal("fetch", okManifest([{ name: "a", url: "/cm/a.js" }]));
    const mgr = createModuleManager({
      manifestUrl: "/x",
      importer: async () => ({ default: asyncRegister }),
    });
    await mgr.reconcile();
    expect(mgr.activeNames()).not.toContain("a"); // the activation never lands
    expect(warn.mock.calls.some((c) => String(c[0]).includes("failed to register"))).toBe(true);

    // Partial registrations are rolled back: re-registering the same keys is
    // not an override (no override warning would fire for a fresh module).
    warn.mockClear();
    vi.stubGlobal("fetch", okManifest([]));
    await mgr.reconcile();
    expect(warn.mock.calls.filter((c) => String(c[0]).includes("overrides")).length).toBe(0);
    warn.mockRestore();
  });
});
