#!/usr/bin/env node
// gocordis-plugin-build — bundle a console plugin's frontend into one
// self-contained ES module (entry's bare imports of react / react-dom /
// antd are aliased to the console-owned shims; everything else, e.g.
// echarts, is bundled from npm dependencies).
//
// Usage: gocordis-plugin-build [entry] [-o outfile]
import { createRequire } from "node:module";
import { readFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { emitUiDeclaration } from "../lib/ui-declaration.mjs";

// Resolve esbuild from the CONSUMING plugin project, falling back to the
// kit's own install (dev mode). esbuild is a peer dependency: the project
// that builds a plugin provides the engine.
function loadEsbuild() {
  try {
    return createRequire(pathToFileURL(`${process.cwd()}/package.json`))("esbuild");
  } catch {
    return createRequire(import.meta.url)("esbuild");
  }
}
const { build } = loadEsbuild();

const kit = join(dirname(fileURLToPath(import.meta.url)), "..");
const shim = (f) => join(kit, "shims", f);

const args = process.argv.slice(2);
const option = (name) => args.includes(name) ? args[args.indexOf(name) + 1] : undefined;
const entry = args.find((a) => !a.startsWith("-") && a !== option("-o") && a !== option("--ui-config")) ?? "src/ui.tsx";
const outfile = option("-o") ?? "ui.js";

await build({
  entryPoints: [entry],
  outfile,
  bundle: true,
  format: "esm",
  minify: true,
  jsx: "automatic",
  alias: {
    react: shim("react.js"),
    "react/jsx-runtime": shim("react-jsx-runtime.js"),
    "react-dom": shim("react-dom.js"),
    "react-dom/client": shim("react-dom-client.js"),
    antd: shim("antd.js"),
  },
});
// A UI declaration is emitted for every build: a package can now zip the
// output directory directly, and the page/panel/module resources are visible
// to the package reader and the host without any frontend-tooling changes.
const configPath = option("--ui-config");
const config = configPath
  ? JSON.parse(await readFile(configPath, "utf8"))
  : {};
const { declarationPath } = await emitUiDeclaration(outfile, config);
console.log(`plugin built: ${outfile}`);
console.log(`ui declaration: ${declarationPath}`);
