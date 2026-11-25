import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";

const blocks = fileURLToPath(new URL("./views/blocks.tsx", import.meta.url));

describe("console runtime library", () => {
  it("registers only domain-free view DSL blocks", () => {
    const source = readFileSync(blocks, "utf8");
    for (const kind of [
      "insight-graph", "runtime-inspector", "framework-dashboard", "market-catalog", "plugin-explorer",
    ]) {
      expect(source).not.toContain(`registerBlockRenderer("${kind}"`);
    }
    for (const query of ["framework/dashboard", "insight/graph", "runtime/snapshot"]) {
      expect(source).not.toContain(`"${query}"`);
    }
    for (const kind of ["table", "kv", "list", "stats", "trend", "form", "json", "query-table"]) {
      expect(source).toContain(`registerBlockRenderer("${kind}"`);
    }
  });
});
