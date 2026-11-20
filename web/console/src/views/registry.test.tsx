// Renderer registry reactivity: registration/disposal must bump a version
// that React consumers can subscribe to (useSyncExternalStore), so a page
// rendered before its renderer arrived re-renders once the module registers.
// Identity-guarded stale dispose calls must NOT bump (nothing changed).
// Widget/Action/Settings registries follow the same semantics.
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  getPageRenderer,
  getSettings,
  getWidget,
  listActions,
  registerAction,
  registerBlockRenderer,
  registerPageRenderer,
  registerSettings,
  registerWidget,
  rendererRegistryVersion,
  subscribeRenderers,
  type BlockRenderer,
} from "./registry";

afterEach(() => {
  vi.restoreAllMocks();
});

const noop: BlockRenderer = () => null;

describe("renderer registry version", () => {
  it("bumps on every real registration and disposal", () => {
    const before = rendererRegistryVersion();
    const seen: number[] = [];
    const unsub = subscribeRenderers(() => seen.push(rendererRegistryVersion()));

    const disposeBlock = registerBlockRenderer("rx-block", noop);
    expect(rendererRegistryVersion()).toBe(before + 1);

    const disposePage = registerPageRenderer("rx-page", () => null);
    expect(rendererRegistryVersion()).toBe(before + 2);

    disposeBlock();
    expect(rendererRegistryVersion()).toBe(before + 3);

    disposePage();
    expect(rendererRegistryVersion()).toBe(before + 4);

    expect(seen).toEqual([before + 1, before + 2, before + 3, before + 4]);
    unsub();
  });

  it("does not bump when a stale dispose is a no-op", () => {
    const before = rendererRegistryVersion();
    const dispose = registerBlockRenderer("rx-stale", noop);
    dispose(); // owner releases its own renderer
    const afterSelf = rendererRegistryVersion();

    dispose(); // stale second call — nothing left to remove
    expect(rendererRegistryVersion()).toBe(afterSelf);

    // Overridden renderer: the OLD dispose must not remove the NEW renderer
    // and must not bump (nothing changed).
    const disposeOld = registerBlockRenderer("rx-stale", () => null);
    registerBlockRenderer("rx-stale", () => null);
    const afterOverride = rendererRegistryVersion();
    disposeOld();
    expect(rendererRegistryVersion()).toBe(afterOverride);
  });
});

describe("widget registry", () => {
  it("registers, looks up, and disposes widgets with version bumps", () => {
    const before = rendererRegistryVersion();
    const w = () => null;
    const dispose = registerWidget("e2e-widget", w);
    expect(getWidget("e2e-widget")).toBe(w);
    expect(rendererRegistryVersion()).toBe(before + 1);
    dispose();
    expect(getWidget("e2e-widget")).toBeUndefined();
    expect(rendererRegistryVersion()).toBe(before + 2);
  });
});

describe("action registry", () => {
  it("registers actions in order and disposes them", () => {
    const before = rendererRegistryVersion();
    const a = { id: "a1", title: "A1", run: () => undefined };
    const b = { id: "a2", title: "A2", when: () => false, run: () => undefined };
    const da = registerAction(a);
    registerAction(b);
    expect(listActions().map((x) => x.id)).toEqual(["a1", "a2"]);
    expect(rendererRegistryVersion()).toBe(before + 2);
    da();
    expect(listActions().map((x) => x.id)).toEqual(["a2"]);
    expect(rendererRegistryVersion()).toBe(before + 3);
  });
});

describe("settings registry", () => {
  it("registers a settings UI and looks it up with :client-suffix tolerance", () => {
    const before = rendererRegistryVersion();
    const s = () => null;
    const dispose = registerSettings("alarm", s);
    expect(getSettings("alarm")).toBe(s);
    expect(getSettings("alarm:client")).toBe(s); // ui-client row id tolerance
    expect(rendererRegistryVersion()).toBe(before + 1);
    dispose();
    expect(getSettings("alarm")).toBeUndefined();
    expect(getSettings("alarm:client")).toBeUndefined();
  });
});

// P1 regression (2026-11 audit): a module overriding another ACTIVE module's
// key shadows it; disabling only the SHADOWING module must RESTORE the
// shadowed contribution. The old set/delete pair deleted the key outright,
// leaving the still-active earlier module unregistered for the session.
describe("shadow-stack restore on override disposal", () => {
  it("restores the shadowed page renderer when the overriding module disposes", () => {
    const before = rendererRegistryVersion();
    const disposeA = registerPageRenderer("shared-page", () => null);
    const disposeB = registerPageRenderer("shared-page", () => null);

    // B shadows A.
    expect(getPageRenderer("shared-page")).not.toBeUndefined();

    disposeB(); // only the LATER module is disabled
    expect(rendererRegistryVersion()).toBe(before + 3);
    expect(getPageRenderer("shared-page")).not.toBeUndefined(); // A restored

    disposeA(); // A's own dispose then frees the key
    expect(rendererRegistryVersion()).toBe(before + 4);
    expect(getPageRenderer("shared-page")).toBeUndefined();
  });

  it("keeps stale-dispose semantics: a shadowed (non-top) dispose is a no-op", () => {
    const before = rendererRegistryVersion();
    const disposeA = registerWidget("shared-widget", () => null);
    const disposeB = registerWidget("shared-widget", () => null);

    disposeA(); // A is shadowed by B: no-op, B stays active
    expect(rendererRegistryVersion()).toBe(before + 2);
    expect(getWidget("shared-widget")).not.toBeUndefined();

    disposeB();
    expect(rendererRegistryVersion()).toBe(before + 3);
    expect(getWidget("shared-widget")).toBeUndefined();
  });

  it("restores shadowed actions and settings the same way", () => {
    const disposeA = registerAction({ id: "shared-action", title: "A", run: () => undefined });
    const disposeB = registerAction({ id: "shared-action", title: "B", run: () => undefined });
    expect(listActions().find((a) => a.id === "shared-action")?.title).toBe("B");
    disposeB();
    expect(listActions().find((a) => a.id === "shared-action")?.title).toBe("A");
    disposeA();
    expect(listActions().find((a) => a.id === "shared-action")).toBeUndefined();

    const disposeSA = registerSettings("shared-plugin", () => null);
    const disposeSB = registerSettings("shared-plugin", () => null);
    disposeSB();
    expect(getSettings("shared-plugin")).not.toBeUndefined();
    disposeSA();
    expect(getSettings("shared-plugin")).toBeUndefined();
  });
});
