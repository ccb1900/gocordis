// Build-time shim: antd's portals (Modal, message, notification...) must
// render through the console's react-dom.
const D = globalThis.__CORDIS_CONSOLE.reactDom;

export default D;
export const { createPortal, flushSync, version } = D;
