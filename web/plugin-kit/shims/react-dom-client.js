// Build-time shim: see react-dom.js — single renderer per page.
const C = globalThis.__CORDIS_CONSOLE.reactDomClient;

export default C;
export const { createRoot, hydrateRoot } = C;
