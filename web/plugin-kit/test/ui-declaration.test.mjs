import test from "node:test";
import assert from "node:assert/strict";
import { mkdtemp, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { emitUiDeclaration, uiDeclaration } from "../lib/ui-declaration.mjs";

test("a built bundle emits a package-ready UI declaration", async () => {
  const dir = await mkdtemp(join(tmpdir(), "gocordis-kit-"));
  try {
    const outfile = join(dir, "ui.js");
    const { declarationPath, declaration } = await emitUiDeclaration(outfile, {
      module: "demo-ui",
      pages: [{ id: "demo", title: "Demo", route: "/demo", renderer: "demo" }],
      panels: [{ id: "demo-panel", title: "Panel", position: "right", renderer: "event-feed" }],
    });

    assert.deepEqual(declaration.resources, [
      { kind: "client-module", id: "demo-ui", path: "ui.js" },
      { kind: "page", id: "demo", path: "ui/pages/demo.json" },
      { kind: "panel", id: "demo-panel", path: "ui/panels/demo-panel.json" },
    ]);
    assert.deepEqual(JSON.parse(await readFile(declarationPath, "utf8")), declaration);
    assert.equal(
      JSON.parse(await readFile(join(dir, "ui/pages/demo.json"), "utf8")).renderer,
      "demo",
    );
    assert.equal(
      JSON.parse(await readFile(join(dir, "ui/panels/demo-panel.json"), "utf8")).position,
      "right",
    );
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
});

test("UI resource ids reject traversal before a package can be built", () => {
  assert.throws(() => uiDeclaration({ module: "../escape", entry: "ui.js" }), /invalid UI resource id/);
  assert.throws(() => uiDeclaration({
    module: "demo-ui",
    entry: "ui.js",
    pages: [{ id: "a/b", title: "bad" }],
  }), /invalid UI resource id/);
});
