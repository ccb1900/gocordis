// Console DTO types. Domain payloads travel through hub named queries as
// opaque JSON; these shapes are what every console renders regardless of
// application.

// ViewBlock has one canonical definition (views/schema.ts) — re-exported
// here so composition DTOs and renderers cannot drift.
export type { ViewBlock } from "./views/schema";
import type { ViewBlock } from "./views/schema";

export type StreamStatus = "live" | "connecting" | "offline";

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
  /** Optional human-readable detail (e.g. why a composition apply failed). */
  message?: string;
}

export interface ExplorerPlugin {
  id: string;
  name: string;
  type: string;
  state: string;
  /** Fiber failure reason when state is Failed. */
  error?: string;
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
