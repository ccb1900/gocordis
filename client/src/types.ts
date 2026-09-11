// Console DTO types. Domain payloads travel through hub named queries as
// opaque JSON; these shapes are what every console renders regardless of
// application.
export type StreamStatus = "live" | "connecting" | "offline";
export interface UIPage {
  id: string; title: string; route: string; renderer: string;
  views?: ViewBlock[];
}
export interface UIPanel {
  id: string; title: string; position: string; renderer: string;
  pages?: string[];
}
export interface UIObservation { type: string; sourceId?: string; timestamp: string }
export interface UIError { code: string; message: string }
export interface RemovedPlugin { id: string; name: string }
export interface ExplorerPlugin {
  id: string; name: string; type: string; state: string;
  components: string[]; capabilities: string[]; controllable: boolean;
  config?: Record<string, string>;
}
export interface ExplorerControlRequest { pluginId: string; enable: boolean }
export interface ExplorerControlResult {
  pluginId: string; accepted: boolean; rejected: boolean;
  failed: boolean; state: string; error: string;
}
export interface ViewColumn { key: string; title: string }
export interface ViewField { key: string; label: string }
export interface ViewAction { label: string; command: string; args?: Record<string, string> }
export interface ViewBlock {
  kind: "table" | "kv" | "list" | "trend" | "stats" | string;
  query?: string; params?: Record<string, string>;
  columns?: ViewColumn[]; fields?: ViewField[];
  rowActions?: ViewAction[]; selectFocus?: boolean;
  pageSize?: number; titleKey?: string;
  items?: Array<{ label: string; op?: string; field?: string; warn?: boolean }>;
  dateKey?: string; series?: Array<{ key: string; label: string }>;
}
export interface PageAction { label: string; command: string; datePicker?: boolean }
