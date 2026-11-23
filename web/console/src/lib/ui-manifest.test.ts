import { describe, expect, it } from "vitest";
import { normalizeUiManifest } from "./ui-manifest";

describe("normalizeUiManifest", () => {
  it("keeps packaged module URLs and content hashes verbatim", () => {
    const manifest = normalizeUiManifest({
      revision: 7,
      pages: [{ id: "demo", title: "Demo", route: "/demo", renderer: "demo" }],
      panels: [{ id: "feed", title: "Feed", position: "right", renderer: "event-feed" }],
      modules: [{
        name: "demo-client",
        url: "/client-modules/demo-client/ui.js?v=abcd1234",
        packageId: "demo",
        version: "2.0.0",
        contentHash: "0123456789abcdef",
      }],
    });

    expect(manifest.revision).toBe(7);
    expect(manifest.pages).toHaveLength(1);
    expect(manifest.panels).toHaveLength(1);
    expect(manifest.modules[0]).toEqual({
      name: "demo-client",
      url: "/client-modules/demo-client/ui.js?v=abcd1234",
      packageId: "demo",
      version: "2.0.0",
      contentHash: "0123456789abcdef",
    });
  });

  it("degrades on missing fields instead of dropping the whole composition", () => {
    const manifest = normalizeUiManifest({
      pages: [{ id: "broken" }, { id: "ok", title: "Ok", route: "/ok", renderer: "generic" }],
      modules: [{ name: "no-url" }, { name: "ok", url: "/client-modules/ok/ui.js?v=1" }],
    });

    expect(manifest.revision).toBe(0);
    expect(manifest.pages.map((page) => page.id)).toEqual(["ok"]);
    expect(manifest.modules.map((module) => module.name)).toEqual(["ok"]);
    expect(manifest.modules[0].packageId).toBeNull();
  });
});
