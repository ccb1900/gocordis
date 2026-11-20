// Build-time shim: see react-dom.js — single renderer per page.
const F = globalThis.__CORDIS_CONSOLE;
if (!F) throw new Error("gocordis plugin: console facade missing — a bundled plugin must be loaded by a GoCordis console (webui/host), never standalone");
const C = F.reactDomClient;

export default C;
export const { createRoot, hydrateRoot } = C;
