// Builtin Web Console frontend module. All product renderers live here; the
// shell only owns extension points. The C# WebConsoleComponent declares the
// pages/routes/navigation and this module supplies their renderers.
import React, { useEffect, useState } from "react";
import { Alert, Button, Card, Descriptions, Space, Statistic, Tag, Typography } from "antd";
import { bindFacade, facade, type BuiltinFacade } from "./facade";
import type { BlockRenderer, PageRenderer, PanelRenderer, ViewBlock, ViewContext } from "./types";
import { FrameworkDashboardBlock } from "./views/FrameworkDashboard";
import { InsightGraphBlock } from "./views/InsightGraphBlock";
import { RuntimeInspectorBlock } from "./views/RuntimeInspector";
import { MarketCatalogBlock } from "./views/MarketCatalog";
import { PluginExplorer } from "./components/Explorer";
import { EventFeed } from "./components/EventFeed";

function blockPage(
  Block: BlockRenderer,
  kind: string,
  query: string,
  params?: Record<string, string>,
): PageRenderer {
  return function ProductBlockPage({ page, ctx }) {
    const block: ViewBlock = { kind, title: page.title, query, params };
    return <Block block={block} ctx={ctx} />;
  };
}

function ConsoleOverview({ ctx }: { ctx: ViewContext }) {
  const [status, setStatus] = useState<Record<string, unknown> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const load = () => {
    facade().api
      .hubQuery<Record<string, unknown>>("gocordis-console/status")
      .then((value) => { setStatus(value); setError(null); })
      .catch((cause) => setError(String(cause)));
  };
  useEffect(load, [ctx.generation]);

  const ping = async () => {
    setBusy(true);
    try {
      await facade().api.hubCommand("gocordis-console/ping", { source: "builtin-ui" });
      load();
    } catch (cause) {
      setError(String(cause));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div>
      <Typography.Title level={3}>内置控制台插件</Typography.Title>
      <Typography.Paragraph type="secondary">
        页面、前端模块与后端查询都随产品内置的 Web Console 插件交付；宿主只提供扩展点。
      </Typography.Paragraph>
      {error ? <Alert type="error" message={error} /> : null}
      <Space direction="vertical" style={{ width: "100%" }}>
        <Card title="插件状态" size="small">
          <Descriptions size="small" column={2}>
            <Descriptions.Item label="插件">{String(status?.plugin ?? "—")}</Descriptions.Item>
            <Descriptions.Item label="组件">{String(status?.component ?? "—")}</Descriptions.Item>
            <Descriptions.Item label="已安装">{String(status?.installed ?? false)}</Descriptions.Item>
            <Descriptions.Item label="地址">{String(status?.address ?? "—")}</Descriptions.Item>
          </Descriptions>
        </Card>
        <Card size="small">
          <Space>
            <Statistic title="Ping 次数" value={Number(status?.pings ?? 0)} />
            <Button type="primary" loading={busy} onClick={ping}>调用插件命令</Button>
          </Space>
        </Card>
      </Space>
    </div>
  );
}

function ConsoleStatus() {
  return <Card size="small">Console 状态面板由插件自带 ESM 注册，随插件卸载而撤销。</Card>;
}

function ConsoleCallout({ block }: { block: { title?: string; text?: string } }) {
  return <Alert type="info" showIcon message={block.title ?? "内置控制台"} description={block.text ?? "插件块"} />;
}

function ConsoleBadge({ params }: { params?: Record<string, string> }) {
  return <Tag color="blue">内置控制台 · {params?.text ?? "ready"}</Tag>;
}

const OverviewPage: PageRenderer = ({ ctx }) => <ConsoleOverview ctx={ctx} />;
const ExplorerPage: PageRenderer = ({ ctx }) => <PluginExplorer ctx={ctx} />;
const EventFeedPanel: PanelRenderer = ({ generation }) => <EventFeed generation={generation} />;

export default function register(api: BuiltinFacade) {
  bindFacade(api);
  const disposers: Array<() => void> = [];
  const page = (name: string, component: PageRenderer) =>
    disposers.push(api.pages.register({ name, component }) as never);
  const block = (kind: string, component: BlockRenderer) =>
    disposers.push(api.blocks.register(kind, component) as never);

  page("gocordis-console-overview", OverviewPage);
  // Stable renderer name declared by the builtin control-plane page.
  page("plugin-explorer", ExplorerPage);
  page("plugin-control-plane", ExplorerPage);
  page("framework-dashboard", blockPage(
    FrameworkDashboardBlock as BlockRenderer, "framework-dashboard", "framework/dashboard",
    { inspectorRoute: "/console/inspector" }));
  page("insight-graph", blockPage(InsightGraphBlock as BlockRenderer, "insight-graph", "insight/graph"));
  page("runtime-inspector", blockPage(RuntimeInspectorBlock as BlockRenderer, "runtime-inspector", "runtime/snapshot"));

  block("framework-dashboard", FrameworkDashboardBlock as BlockRenderer);
  block("insight-graph", InsightGraphBlock as BlockRenderer);
  block("runtime-inspector", RuntimeInspectorBlock as BlockRenderer);
  block("market-catalog", MarketCatalogBlock as BlockRenderer);
  block("gocordis-console-callout", ConsoleCallout as BlockRenderer);

  disposers.push(api.panels.register({ name: "gocordis-console-status", component: ConsoleStatus }) as never);
  disposers.push(api.panels.register({ name: "event-feed", component: EventFeedPanel }) as never);
  disposers.push(api.widgets.register("gocordis-console-badge", ConsoleBadge) as never);
  disposers.push(api.actions.register({
    id: "gocordis-console.ping",
    title: "Console Ping",
    run: (cx: unknown) => facade().api.hubCommand("gocordis-console/ping", { source: "action" }),
  }) as never);
  disposers.push(api.settings.register(({ pluginId }: { pluginId: string }) => (
    <Typography.Text>内置控制台设置 · {pluginId}</Typography.Text>
  )) as never);

  return () => { for (const dispose of disposers.reverse()) dispose(); };
}
