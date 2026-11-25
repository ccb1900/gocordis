// Product built-in Console plugin frontend. This TSX is bundled by
// @gocordis/plugin-kit into ONE same-origin ESM (ui.js). react/antd come from
// the console facade, so there is one React instance; no UI code lives in the
// host. All data comes from the plugin's own managed ApiGateway query.
import React, { useEffect, useState } from "react";
import { Alert, Button, Card, Descriptions, Space, Statistic, Tag, Typography } from "antd";

type Status = {
  plugin?: string;
  component?: string;
  installed?: boolean;
  undone?: number;
  pings?: number;
  frontend?: string;
};

type ViewCtx = {
  hubQuery: <T>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
};

/** Console overview page — rendered by the plugin, not by host view stacks. */
function ConsoleOverview({ ctx }: { ctx: ViewCtx }) {
  const [status, setStatus] = useState<Status | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    ctx.hubQuery<Status>("gocordis-console/status")
      .then((value) => { setStatus(value); setError(null); })
      .catch((cause) => setError(String(cause)));
  };
  useEffect(load, []);

  const ping = async () => {
    setBusy(true);
    try { await ctx.hubCommand("gocordis-console/ping", { source: "builtin-ui" }); load(); }
    catch (cause) { setError(String(cause)); }
    finally { setBusy(false); }
  };

  return (
    <div>
      <Typography.Title level={3}>内置控制台插件</Typography.Title>
      <Typography.Paragraph type="secondary">
        页面、前端模块与后端查询都随 <code>gocordis-console</code> 包安装；宿主只提供 ApiGateway 与静态资源。
      </Typography.Paragraph>
      {error ? <Alert type="error" message={error} /> : null}
      <Space direction="vertical" style={{ width: "100%" }}>
        <Card title="插件状态" size="small">
          <Descriptions size="small" column={2}>
            <Descriptions.Item label="插件">{status?.plugin ?? "—"}</Descriptions.Item>
            <Descriptions.Item label="组件">{status?.component ?? "—"}</Descriptions.Item>
            <Descriptions.Item label="已安装">{String(status?.installed ?? false)}</Descriptions.Item>
            <Descriptions.Item label="前端模块">{status?.frontend ?? "—"}</Descriptions.Item>
          </Descriptions>
        </Card>
        <Card size="small">
          <Space>
            <Statistic title="Ping 次数" value={Number(status?.pings ?? 0)} />
            <Statistic title="已撤销 Effect" value={Number(status?.undone ?? 0)} />
            <Button type="primary" loading={busy} onClick={ping}>调用插件命令</Button>
          </Space>
        </Card>
      </Space>
    </div>
  );
}

function ConsoleStatusPanel() {
  return <Card size="small">Console 状态面板由插件自带 ESM 注册，随插件卸载而撤销。</Card>;
}

function ConsoleBadge({ params }: { params?: Record<string, string> }) {
  return <Tag color="blue">内置控制台 · {params?.text ?? "ready"}</Tag>;
}

function ConsoleCallout({ block }: { block: { title?: string; text?: string } }) {
  return (
    <Alert
      type="info"
      showIcon
      message={block.title ?? "内置控制台"}
      description={block.text ?? "由插件前端注册的自定义块"}
    />
  );
}

export default function register(api: any) {
  const disposers = [
    api.pages.register({ name: "gocordis-console-overview", component: ConsoleOverview }),
    api.panels.register({ name: "gocordis-console-status", component: ConsoleStatusPanel }),
    api.blocks.register("gocordis-console-callout", ConsoleCallout),
    api.widgets.register("gocordis-console-badge", ConsoleBadge),
    api.actions.register({
      id: "gocordis-console.ping",
      title: "Console Ping",
      run: ({ ctx }: { ctx: ViewCtx }) => ctx.hubCommand("gocordis-console/ping", { source: "action" }),
    }),
    api.settings.register(({ pluginId }: { pluginId: string }) => (
      <Typography.Text>内置控制台设置 · {pluginId}</Typography.Text>
    )),
  ];
  return () => { for (const dispose of disposers.reverse()) dispose(); };
}
