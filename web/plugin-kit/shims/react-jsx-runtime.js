// Build-time shim for esbuild --jsx=automatic: the console's own automatic
// JSX runtime.
const J = globalThis.__CORDIS_CONSOLE.jsxRuntime;

export default J;
export const { jsx, jsxs, jsxDEV, Fragment } = J;
