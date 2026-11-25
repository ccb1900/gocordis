import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const app = fileURLToPath(new URL("./App.tsx", import.meta.url));

describe("thin console shell", () => {
  it("does not know product page or query names", () => {
    const source = readFileSync(app, "utf8");
    for (const name of ["plugin-explorer", "event-feed", "framework-dashboard", "insight-graph", "runtime-inspector"]) {
      expect(source).not.toContain(`registerPageRenderer("${name}"`);
      expect(source).not.toContain(`registerPanelRenderer("${name}"`);
      expect(source).not.toContain(`import("${name}"`);
    }
    for (const query of ["framework/dashboard", "insight/graph", "runtime/snapshot"]) {
      expect(source).not.toContain(`"${query}"`);
    }
  });

  it("registers no renderers at module load", () => {
    const source = readFileSync(app, "utf8");
    expect(source).not.toContain("registerPageRenderer(");
    expect(source).not.toContain("registerPanelRenderer(");
  });
});
