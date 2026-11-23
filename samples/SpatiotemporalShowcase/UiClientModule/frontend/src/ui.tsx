// A REAL plugin frontend written in TSX. Built by @gocordis/plugin-kit into a
// single self-contained ESM (ui.js): bare react/react-dom/antd imports are
// aliased at build time to the console's facade shims, so there is exactly one
// React instance on the page (hooks and antd portals are safe).
//
// The module registers all six console extension points. Every register()
// returns an idempotent dispose owned by the module manager; deactivating the
// plugin unwinds them LIFO. Data comes from the plugin's own API queries via
// ctx.hubQuery — the frontend never reads Runtime internals.
import React, { useEffect, useState } from "react";
import { Alert, Button, Card, Descriptions, Space, Statistic, Tag, Typography } from "antd";

type ViewCtx = {
  hubQuery: <T>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
};

type WidgetProps = { ctx: ViewCtx; params: Record<string, string> };

/** Custom page component — real TSX with hooks, not a declarative view stack. */
function ShowcasePage({ ctx }: { ctx: ViewCtx }) {
  const [store, setStore] = useState<Record<string, unknown> | null>(null);
  const [worker, setWorker] = useState<Record<string, unknown> | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = () => {
    Promise.all([
      ctx.hubQuery<Record<string, unknown>>("showcase/store"),
      ctx.hubQuery<Record<string, unknown>>("showcase/worker"),
    ])
      .then(([s, w]) => { setStore(s); setWorker(w); setError(null); })
      .catch((e) => setError(String(e)));
  };
  useEffect(load, []);

  const report = async () => {
    setBusy(true);
    try { await ctx.hubCommand("showcase/report", { from: "tsx-module" }); }
    catch (e) { setError(String(e)); }
    finally { setBusy(false); }
  };

  return (
    <div>
      <Typography.Title level={3}>自定义页面 · TSX 插件前端</Typography.Title>
      <Typography.Paragraph type="secondary">
        这个页面由插件包内的 <code>ui.tsx</code> 构建而成，react / antd 来自控制台门面；
        数据经插件自己的 API 查询读取。
      </Typography.Paragraph>
      {error ? <Alert type="error" message={error} /> : null}
      <Space direction="vertical" style={{ width: "100%" }}>
        <Card title="存储提供者（showcase/store）" size="small">
          <Descriptions size="small" column={2}>
            <Descriptions.Item label="提供者">{String(store?.provider ?? "—")}</Descriptions.Item>
            <Descriptions.Item label="写入次数">{String(store?.writes ?? "—")}</Descriptions.Item>
            <Descriptions.Item label="版本">{String(store?.revision ?? "—")}</Descriptions.Item>
          </Descriptions>
        </Card>
        <Card title="工作者（showcase/worker）" size="small">
          <Space>
            <Statistic title="观察到事件" value={Number(worker?.observed ?? 0)} />
            <Tag color={worker?.subscribed ? "green" : "default"}>
              {worker?.subscribed ? "已订阅" : "未订阅"}
            </Tag>
          </Space>
        </Card>
        <Button type="primary" loading={busy} onClick={report}>触发 report 命令</Button>
      </Space>
    </div>
  );
}

/** Custom panel component. */
function ShowcasePanel({ panel }: { panel: { title: string } }) {
  return <Card size="small">面板由 TSX 模块渲染：{panel.title}</Card>;
}

/** Custom declarative block renderer. */
function CalloutBlock({ block }: { block: { text?: string; title?: string } }) {
  return (
    <Alert
      type="info"
      showIcon
      message={block.title ?? "自定义块"}
      description={block.text ?? "由 TSX 模块注册"}
    />
  );
}

/** Composable widget used by the declarative `widget` view block. */
function BadgeWidget({ params }: WidgetProps) {
  return <Tag color="blue">★ {params.text || "showcase"}</Tag>;
}

export default function register(api: any) {
  const d1 = api.pages.register({ name: "showcase-custom-page", component: ShowcasePage });
  const d2 = api.panels.register({ name: "showcase-custom-panel", component: ShowcasePanel });
  const d3 = api.blocks.register("showcase-callout", CalloutBlock);
  const d4 = api.widgets.register("showcase-badge", BadgeWidget);
  const d5 = api.actions.register({
    id: "showcase.tsx.ping",
    title: "TSX 模块动作",
    run: (cx: { ctx: ViewCtx }) => cx.ctx.hubCommand("showcase/report", { from: "action" }),
  });
  const d6 = api.settings.register(({ pluginId }: { pluginId: string }) => (
    <Typography.Text>TSX 自定义设置 · {pluginId}</Typography.Text>
  ));

  return () => { d6(); d5(); d4(); d3(); d2(); d1(); };
}
