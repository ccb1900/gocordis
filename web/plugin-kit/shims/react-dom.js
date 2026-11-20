// Build-time shim: antd's portals (Modal, message, notification...) must
// render through the console's react-dom.
const F = globalThis.__CORDIS_CONSOLE;
if (!F) throw new Error("gocordis plugin: console facade missing — a bundled plugin must be loaded by a GoCordis console (webui/host), never standalone");
const D = F.reactDom;

export default D;
export const { createPortal, flushSync, version } = D;
