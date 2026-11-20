// Build-time shim for esbuild --jsx=automatic: the console's own automatic
// JSX runtime.
const F = globalThis.__CORDIS_CONSOLE;
if (!F) throw new Error("gocordis plugin: console facade missing — a bundled plugin must be loaded by a GoCordis console (webui/host), never standalone");
const J = F.jsxRuntime;

// jsxDEV exists only in react/jsx-dev-runtime, not in the production runtime
// the facade re-exports. A development-condition esbuild bundle emits
// jsxDEV(...) on its first JSX element; exporting an `undefined` binding
// crashed there. Degrade to plain jsx instead — correct rendering, minus the
// dev-only source annotations.
const jsxDEVShim =
  typeof J.jsxDEV === "function"
    ? J.jsxDEV
    : (type, props, key) => J.jsx(type, props, key);

export default J;
export const { jsx, jsxs, Fragment } = J;
export const jsxDEV = jsxDEVShim;
