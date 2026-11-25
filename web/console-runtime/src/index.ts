// Public surface of the Cordis console runtime library.
//
// This package owns shell infrastructure only: extension registries, client
// modules, observation projections, the view DSL and the hub API client. It
// deliberately contains no product page names or product query names; those
// belong to plugins (including the builtin Web Console frontend module).

export * from "./api";
export * from "./types";
export * from "./stream";

export {
  buildMenuTree, groupSegments, menuRoutes,
} from "./lib/menu-tree";
export type { MenuNode } from "./lib/menu-tree";

export { observationView, relativeTime } from "./lib/observations";
export type { ObservationView } from "./lib/observations";

export {
  ALL, COMPOSITION, PLUGIN, domainVersion, subscribeDomain, useDomainVersion,
  registerProjection, projectionState, subscribeProjection, useProjection,
  bumpDomains, ingestObservation, ingestPluginState, ingestUIManifest, testFlush,
} from "./lib/projections";

export {
  dependencyRefs, normalizePlugin, normalizePlugins, normalizeStorePlugin,
  normalizeStorePlugins, normalizeDependencyView, normalizeUpload, normalizeMutation,
  errorCodeOf, mutationMessage, diagnosticText,
} from "./lib/plugin-control";

export {
  FACADE_VERSION, clientModuleAPI, createModuleManager,
  reconcileClientModules, clientModuleManagerState, subscribeClientModulesState,
} from "./lib/client-modules";
export type {
  BaseModuleAPI, ClientModuleAPI, ClientModuleRef, ModuleManager,
  ModuleManagerDeps, ModuleManagerState, PageRendererDef, PanelRendererDef,
} from "./lib/client-modules";

export { normalizeUiManifest } from "./lib/ui-manifest";

export { GenericPage } from "./views/GenericPage";
export { ViewBlockRenderer } from "./views/blocks";
export type { ViewBlock, ViewContext, BlockRenderer } from "./views/blocks";

export {
  subscribeRenderers, rendererRegistryVersion,
  registerBlockRenderer, registerPageRenderer, registerPanelRenderer,
  getBlockRenderer, getPageRenderer, getPanelRenderer, registeredBlockKinds,
  registerWidget, getWidget, registeredWidgets,
  registerAction, listActions, registerSettings, getSettings,
} from "./views/registry";
export type {
  WidgetProps, WidgetComponent, ActionContext, ActionDefinition,
  SettingsProps, SettingsComponent, PageRenderer, PanelRenderer,
} from "./views/registry";

export {
  formatRow, matchFilter, needsFocus, resolveParams,
} from "./views/schema";
export type { Filter, Focus, PageAction } from "./views/schema";

export { ConfigEditor } from "./components/ConfigEditor";
export { SchemaForm } from "./components/SchemaForm";
export { TrendChart } from "./views/TrendChart";
export type { TrendPoint } from "./views/TrendChart";
