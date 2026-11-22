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
  /** Menu icon hint from the console's curated set. */
  icon?: string;
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

// ---- Plugin control-plane DTOs -----------------------------------------------
// Wire shape is lowercase JSON from the Go hub. The console renders exactly
// these fields: declaration (`enabled`, `declared`) vs. actual fiber `state`,
// plus version/install identity, capabilities and declared dependencies.

export interface PluginDependencyRef {
  id: string;
  versionRange: string;
}

/** `POST` results are *not* transport errors: an unaccepted command still
 * returns 200 with `accepted: false` and a machine-readable `code`. */
export interface PluginMutationResult {
  accepted: boolean;
  code?: string;
  error?: string;
  id?: string;
  state?: string;
  /** False means the declaration was not persisted (transient switch). */
  persisted?: boolean;
  changes?: Array<string | Record<string, unknown>>;
}

export interface ExplorerPlugin {
  id: string;
  name: string;
  type: string;
  /** Desired declaration from the manifest. */
  enabled: boolean;
  /** Actual fiber state ("Active" / "Pending" / "Failed" / "Gone" / ...). */
  state: string;
  /** Fiber failure reason when state is Failed. */
  error?: string;
  version?: string;
  installedVersion?: string;
  /** Console-critical plugins cannot be uninstalled or switched. */
  protected: boolean;
  controllable: boolean;
  declared: boolean;
  config: Record<string, unknown>;
  components: string[];
  capabilities: string[];
  /** Declared capability keys this plugin requires from others. */
  requires: string[];
  /** Declared capability keys this plugin provides. */
  provides: string[];
  /** Some backends flatten package dependencies onto the list entry. */
  dependencies?: string[];
}

export type PluginControlEntry = ExplorerPlugin;
export type ExplorerControlResult = PluginMutationResult;

/** `GET /api/plugins/store` — installed/available packages in the local
 * store. This is a local inventory, never a remote marketplace. */
export interface StorePluginEntry {
  id: string;
  version: string;
  displayName?: string;
  hostVersionRange?: string;
  installPath?: string;
  dependencies: PluginDependencyRef[];
  provides: string[];
  checksum?: string;
  declared: boolean;
  running: boolean;
}

/** One row of `GET /api/plugins/{id}/dependencies`. */
export interface PackageDependencyView extends PluginDependencyRef {
  installedVersion?: string;
  satisfied: boolean;
}

export interface PluginDiagnostic {
  code?: string;
  message?: string;
  level?: string;
  severity?: string;
  [key: string]: unknown;
}

export interface PluginDependencyView {
  id: string;
  version?: string;
  packageDependencies: PackageDependencyView[];
  dependents: string[];
  capabilityRequires: string[];
  capabilityProvides: string[];
  diagnostics: Array<string | PluginDiagnostic>;
}

/** Result of `POST /api/plugins/packages` (multipart field `package`). */
export interface UploadedPackage {
  uploadKey: string;
  id: string;
  version: string;
  type?: string;
  hostVersionRange?: string;
  checksum?: string;
  dependencies?: PluginDependencyRef[];
}

/** SSE `plugin-state` frame. */
export interface PluginStateEvent {
  revision: number;
  action: string;
}

/** SSE `ui-manifest` frame — composition/client-module manifest changed. */
export interface UIManifestEvent {
  revision?: number;
  action?: string;
}

// Market catalog: payload of the `market/catalog` named query. The console
// only renders it; install/upgrade/remove (when wired) are hub commands.
export interface MarketCatalogEntry {
  id: string;
  name: string;
  version: string;
  backend: string;
  moduleType: string;
  description: string;
  source: string;
  trust: string;
  installed: boolean;
  compatible: boolean;
}

export interface MarketCatalogQuery {
  entries: MarketCatalogEntry[];
  sequence: number;
}

// ---- Runtime observation DTOs -----------------------------------------------
// Payloads of the `runtime/snapshot`, `runtime/events` and
// `framework/dashboard` named queries. IDs are numeric in the kernel but the
// wire may carry them as strings; all fields are optional so the renderers can
// degrade gracefully while a backend query is still being rolled out.

export type RuntimeID = string | number;

export interface RuntimeFiberDTO {
  id?: RuntimeID;
  name?: string;
  component?: string;
  state?: string;
  intent?: string;
  err?: string;
  activationID?: RuntimeID;
  scopeID?: RuntimeID;
  parentFiberIDs?: RuntimeID[];
  childFiberIDs?: RuntimeID[];
}

export interface RuntimeScopeDTO {
  id?: RuntimeID;
  parentID?: RuntimeID;
  providerKeys?: string[];
  fiberIDs?: RuntimeID[];
}

export interface RuntimeProviderDTO {
  key?: string;
  state?: string;
  retiring?: boolean;
  ownerFiberID?: RuntimeID;
  ownerActivationID?: RuntimeID;
  scopeID?: RuntimeID;
}

export interface RuntimeDependencyDTO {
  key?: string;
  consumerFiberID?: RuntimeID;
  consumerActivationID?: RuntimeID;
  providerFiberID?: RuntimeID;
  providerActivationID?: RuntimeID;
  status?: string;
  reason?: string;
}

export interface RuntimeEffectDTO {
  ownerFiberID?: RuntimeID;
  activationID?: RuntimeID;
  seq?: RuntimeID;
  kind?: string;
  key?: string;
  state?: string;
}

export interface RuntimeSnapshotDTO {
  sequence?: RuntimeID;
  runtimeID?: RuntimeID;
  state?: string;
  fibers?: RuntimeFiberDTO[];
  scopes?: RuntimeScopeDTO[];
  providers?: RuntimeProviderDTO[];
  dependencies?: RuntimeDependencyDTO[];
  effects?: RuntimeEffectDTO[];
}

export interface RuntimeEventDTO {
  sequence?: RuntimeID;
  timestamp?: string;
  type?: string;
  fiberID?: RuntimeID;
  activationID?: RuntimeID;
  data?: unknown;
}

export interface RuntimeEventsDTO {
  events?: RuntimeEventDTO[];
  sequence?: RuntimeID;
}

export interface RuntimeDiagnosticDTO {
  id?: RuntimeID;
  level?: string;
  severity?: string;
  code?: string;
  message?: string;
  detail?: string;
  source?: string;
  fiberID?: RuntimeID;
}

export interface FrameworkDashboardDTO {
  sequence?: RuntimeID;
  state?: string;
  counts?: Record<string, unknown>;
  recentEvents?: RuntimeEventDTO[];
  diagnostics?: Array<RuntimeDiagnosticDTO | string>;
}
