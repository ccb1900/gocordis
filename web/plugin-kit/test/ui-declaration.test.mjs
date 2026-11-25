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

test("a self-contained console plugin can emit all first-class UI kinds", async () => {
  const dir = await mkdtemp(join(tmpdir(), "gocordis-kit-console-"));
  try {
    const outfile = join(dir, "ui.js");
    const { declaration, declarationPath } = await emitUiDeclaration(outfile, {
      module: "gocordis-console-ui",
      pages: [{ id: "console-page", title: "Console", route: "/console", renderer: "console-overview" }],
      panels: [{ id: "console-panel", title: "Status", position: "bottom", renderer: "console-status" }],
      routes: [{ id: "console-route", path: "/console", pageId: "console-page" }],
      navigation: [{ id: "console-nav", label: "Overview", route: "/console" }],
      extensions: [{ id: "console-footer", slot: "shell.footer", renderer: "host-status" }],
      resources: [{ id: "console-assets", kind: "assets", path: "/assets/" }],
    });

    assert.deepEqual(declaration.resources, [
      { kind: "client-module", id: "gocordis-console-ui", path: "ui.js" },
      { kind: "page", id: "console-page", path: "ui/pages/console-page.json" },
      { kind: "panel", id: "console-panel", path: "ui/panels/console-panel.json" },
      { kind: "route", id: "console-route", path: "ui/routes/console-route.json" },
      { kind: "navigation", id: "console-nav", path: "ui/navigation/console-nav.json" },
      { kind: "extension", id: "console-footer", path: "ui/extensions/console-footer.json" },
      { kind: "resource", id: "console-assets", path: "ui/resources/console-assets.json" },
    ]);
    assert.equal(
      JSON.parse(await readFile(join(dir, "ui/routes/console-route.json"), "utf8")).pageId,
      "console-page",
    );
    assert.equal(
      JSON.parse(await readFile(declarationPath, "utf8")).resources.length,
      declaration.resources.length,
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
