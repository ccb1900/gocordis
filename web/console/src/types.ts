// Console DTO types. Domain payloads travel through hub named queries as
// opaque JSON; these shapes are what every console renders regardless of
// application.

export type StreamStatus = "live" | "connecting" | "offline";

export interface ViewColumn {
  key: string;
  title: string;
  /** "{a}/{b}" templates interpolate row fields. */
  format?: string;
}

export interface ViewAction {
  label: string;
  command: string;
  args?: Record<string, string>;
}

export interface ViewStatItem {
  label: string;
  query?: string;
  op?: "count" | "sum";
  field?: string;
  warn?: boolean;
}

export interface ViewSeries {
  key: string;
  label: string;
}

export interface ViewFilter {
  key: string;
  label: string;
  default?: string;
  /** Options come from a named query (dropdown); omit for free text. */
  optionsQuery?: string;
  optionKey?: string;
  optionLabel?: string;
  type?: "date";
  required?: boolean;
}

export interface ViewBlock {
  kind: string;
  title?: string;
  query?: string;
  params?: Record<string, string>;
  columns?: ViewColumn[];
  rowActions?: ViewAction[];
  items?: ViewStatItem[];
  dateKey?: string;
  series?: ViewSeries[];
  fields?: Array<{ key: string; label: string }>;
  filters?: ViewFilter[];
  titleKey?: string;
  pageSize?: number;
  selectFocus?: boolean;
  /** "metadata" expands rows showing the open key-value map of that field. */
  expand?: string;
}

export interface PageAction {
  label: string;
  command: string;
  datePicker?: boolean;
}

export interface UIPage {
  id: string;
  title: string;
  route: string;
  renderer: string;
  description?: string;
  view?: unknown;
  views?: ViewBlock[];
  actions?: PageAction[];
}

export interface UIPanel {
  id: string;
  title: string;
  position: string;
  renderer: string;
  pages?: string[];
  views?: ViewBlock[];
}

export interface UIObservation {
  type: string;
  sourceId?: string;
  timestamp: string;
}

export interface ExplorerPlugin {
  id: string;
  name: string;
  type: string;
  state: string;
  components: string[];
  capabilities: string[];
  controllable: boolean;
  config?: Record<string, string>;
}

export interface ExplorerControlResult {
  pluginId: string;
  accepted: boolean;
  rejected: boolean;
  failed: boolean;
  state: string;
  error: string;
}
