// Client module of the protected builtin insight UI plugin. It owns only
// dashboard/graph/inspector/inventory renderers and pages.
import React from "react";
import { bindFacade, type BuiltinFacade } from "./facade";
import type { BlockRenderer, PageRenderer, ViewBlock } from "./types";
import { FrameworkDashboardBlock } from "./views/FrameworkDashboard";
import { InsightGraphBlock } from "./views/InsightGraphBlock";
import { RuntimeInspectorBlock } from "./views/RuntimeInspector";
import { MarketCatalogBlock } from "./views/MarketCatalog";

function blockPage(Block: BlockRenderer, kind: string, query: string, params?: Record<string, string>): PageRenderer {
  return function InsightPage({ page, ctx }) {
    const block: ViewBlock = { kind, title: page.title, query, params };
    return <Block block={block} ctx={ctx} />;
  };
}

export default function register(api: BuiltinFacade) {
  bindFacade(api);
  const disposers: Array<() => void> = [];
  const page = (name: string, component: PageRenderer) =>
    disposers.push(api.pages.register({ name, component }) as never);
  const block = (kind: string, component: BlockRenderer) =>
    disposers.push(api.blocks.register(kind, component) as never);

  page("framework-dashboard", blockPage(
    FrameworkDashboardBlock, "framework-dashboard", "framework/dashboard",
    { inspectorRoute: "/console/inspector" }));
  page("insight-graph", blockPage(InsightGraphBlock, "insight-graph", "insight/graph"));
  page("runtime-inspector", blockPage(RuntimeInspectorBlock, "runtime-inspector", "runtime/snapshot"));
  page("market-catalog", blockPage(MarketCatalogBlock, "market-catalog", "plugins/store"));

  block("framework-dashboard", FrameworkDashboardBlock);
  block("insight-graph", InsightGraphBlock);
  block("runtime-inspector", RuntimeInspectorBlock);
  block("market-catalog", MarketCatalogBlock);

  return () => { for (const dispose of disposers.reverse()) dispose(); };
}
