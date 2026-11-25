// Minimal bridge from the plugin-kit build output to a package UI declaration.
// The kit keeps bundling with esbuild; after the bundle lands it writes
// gocordis-ui.json next to it. A packaging step can zip that directory and
// copy the generated `resources` array into manifest.json's `ui` field — or
// simply ship the file, which the package reader validates and checksums as
// payload. No bundler concepts leak into the host or the package format.

import { mkdir, writeFile } from "node:fs/promises";
import { basename, dirname, extname, join } from "node:path";

const safeId = (id) => {
  if (typeof id !== "string" || !/^[a-z0-9][a-z0-9._-]*$/i.test(id)) {
    throw new Error(`invalid UI resource id: ${id}`);
  }
  return id;
};

/** Build the declaration object without touching the filesystem (testable). */
export function uiDeclaration({
  module,
  entry,
  pages = [],
  panels = [],
  routes = [],
  navigation = [],
  extensions = [],
  resources = [],
}) {
  const declared = [{
    kind: "client-module",
    id: safeId(module),
    path: entry.replace(/\\/g, "/"),
  }];
  const definitions = [];
  for (const page of pages) {
    const id = safeId(page.id);
    const path = `ui/pages/${id}.json`;
    definitions.push([path, {
      id,
      title: page.title ?? id,
      route: page.route ?? `/plugins/${id}`,
      renderer: page.renderer ?? "generic",
      ...(page.icon ? { icon: page.icon } : {}),
      ...(page.description ? { description: page.description } : {}),
      ...(page.group ? { group: page.group } : {}),
      ...(page.order !== undefined ? { order: page.order } : {}),
      ...(page.views ? { views: page.views } : {}),
      ...(page.actions ? { actions: page.actions } : {}),
    }]);
    declared.push({ kind: "page", id, path });
  }
  for (const panel of panels) {
    const id = safeId(panel.id);
    const path = `ui/panels/${id}.json`;
    definitions.push([path, {
      id,
      title: panel.title ?? id,
      position: panel.position ?? "right",
      renderer: panel.renderer ?? "event-feed",
      ...(panel.pages ? { pages: panel.pages } : {}),
      ...(panel.views ? { views: panel.views } : {}),
    }]);
    declared.push({ kind: "panel", id, path });
  }

  // Routes, navigation, extension slots and static resources are first-class
  // package UI contributions. Emitting them here keeps a self-contained plugin
  // to one ui.config.json + one bundle instead of hand-maintained JSON shards.
  const emit = (kind, folder, items) => {
    for (const item of items) {
      const id = safeId(item.id);
      const path = `ui/${folder}/${id}.json`;
      const { id: _declaredId, ...rest } = item;
      definitions.push([path, { id, ...rest }]);
      declared.push({ kind, id, path });
    }
  };
  emit("route", "routes", routes);
  emit("navigation", "navigation", navigation);
  emit("extension", "extensions", extensions);
  emit("resource", "resources", resources);

  return { declaration: { resources: declared }, definitions };
}

/** Emit gocordis-ui.json and its definition files next to the bundle. */
export async function emitUiDeclaration(outfile, config = {}) {
  const outputDir = dirname(outfile);
  const module = config.module ?? basename(outfile, extname(outfile));
  const { declaration, definitions } = uiDeclaration({
    module,
    entry: basename(outfile),
    pages: config.pages ?? [],
    panels: config.panels ?? [],
    routes: config.routes ?? [],
    navigation: config.navigation ?? [],
    extensions: config.extensions ?? [],
    resources: config.resources ?? [],
  });
  for (const [relative, definition] of definitions) {
    const path = join(outputDir, relative);
    await mkdir(dirname(path), { recursive: true });
    await writeFile(path, `${JSON.stringify(definition, null, 2)}\n`, "utf8");
  }
  const declarationPath = join(outputDir, "gocordis-ui.json");
  await writeFile(declarationPath, `${JSON.stringify(declaration, null, 2)}\n`, "utf8");
  return { declarationPath, declaration };
}
