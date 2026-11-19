// Renderer registry reactivity: registration/disposal must bump a version
// that React consumers can subscribe to (useSyncExternalStore), so a page
// rendered before its renderer arrived re-renders once the module registers.
// Identity-guarded stale dispose calls must NOT bump (nothing changed).
import { afterEach, describe, expect, it, vi } from "vitest";
import {
  registerBlockRenderer,
  registerPageRenderer,
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
