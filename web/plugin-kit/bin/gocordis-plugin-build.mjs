#!/usr/bin/env node
// gocordis-plugin-build — bundle a console plugin's frontend into one
// self-contained ES module (entry's bare imports of react / react-dom /
// antd are aliased to the console-owned shims; everything else, e.g.
// echarts, is bundled from npm dependencies).
//
// Usage: gocordis-plugin-build [entry] [-o outfile]
import { createRequire } from "node:module";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

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
const entry = args.find((a) => !a.startsWith("-")) ?? "src/ui.tsx";
const outfile = args.includes("-o") ? args[args.indexOf("-o") + 1] : "ui.js";

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
console.log(`plugin built: ${outfile}`);
