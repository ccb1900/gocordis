// Client module of the protected builtin Web Console transport plugin. It
// owns only its overview page, status panel and its own small UI extensions.
// Dashboard/graph/inspector/control-plane renderers belong to their own
// protected builtin plugins, not to this module.
import React, { useEffect, useState } from "react";
import { Alert, Button, Card, Descriptions, Space, Statistic, Tag, Typography } from "antd";
import { bindFacade, facade, type BuiltinFacade } from "./facade";
import type { BlockRenderer, PageRenderer, PanelRenderer, ViewContext } from "./types";

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
        这个页面只属于 Web Console 传输插件；其它内置页面由各自插件提供。
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
  return <Card size="small">Console 状态面板由 Web Console 插件自带 ESM 注册。</Card>;
}

function ConsoleCallout({ block }: { block: { title?: string; text?: string } }) {
  return <Alert type="info" showIcon message={block.title ?? "内置控制台"} description={block.text ?? "插件块"} />;
}

function ConsoleBadge({ params }: { params?: Record<string, string> }) {
  return <Tag color="blue">内置控制台 · {params?.text ?? "ready"}</Tag>;
}

const OverviewPage: PageRenderer = ({ ctx }) => <ConsoleOverview ctx={ctx} />;
const StatusPanel: PanelRenderer = () => <ConsoleStatus />;

export default function register(api: BuiltinFacade) {
  bindFacade(api);
  const disposers: Array<() => void> = [];
  disposers.push(api.pages.register({ name: "gocordis-console-overview", component: OverviewPage }) as never);
  disposers.push(api.panels.register({ name: "gocordis-console-status", component: StatusPanel }) as never);
  disposers.push(api.blocks.register("gocordis-console-callout", ConsoleCallout as BlockRenderer) as never);
  disposers.push(api.widgets.register("gocordis-console-badge", ConsoleBadge) as never);
  disposers.push(api.actions.register({
    id: "gocordis-console.ping",
    title: "Console Ping",
    run: () => facade().api.hubCommand("gocordis-console/ping", { source: "action" }),
  }) as never);
  disposers.push(api.settings.register(({ pluginId }: { pluginId: string }) => (
    <Typography.Text>内置控制台设置 · {pluginId}</Typography.Text>
  )) as never);
  return () => { for (const dispose of disposers.reverse()) dispose(); };
}
