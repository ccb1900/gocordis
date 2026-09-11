// Console client — the single entry point.
// Applications import the api/stream surface and mount their own shell
// (the shared one lives in web/console); the console reads everything
// from the hub API (/api/ui/pages, /api/query/<name>, /api/stream, etc.)
// and renders whatever the composition declares.
export { api } from "./api";
export { onObservation, onStreamStatus, ensureStream } from "./api";
export type { StreamStatus, UIPage, UIPanel, UIObservation } from "./types";
export type { ExplorerPlugin, ViewBlock, ViewColumn, ViewField, ViewAction } from "./types";
