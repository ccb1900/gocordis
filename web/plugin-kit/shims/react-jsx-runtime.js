// Build-time shim for esbuild --jsx=automatic: the console's own automatic
// JSX runtime.
const F = globalThis.__CORDIS_CONSOLE;
if (!F) throw new Error("gocordis plugin: console facade missing — a bundled plugin must be loaded by a GoCordis console (webui/host), never standalone");
const J = F.jsxRuntime;

export default J;
export const { jsx, jsxs, jsxDEV, Fragment } = J;
