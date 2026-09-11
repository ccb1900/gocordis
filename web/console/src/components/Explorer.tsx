import React, { useCallback, useEffect, useState } from "react";
import {
  Alert, Button, Descriptions, Empty, Popconfirm, Skeleton, Space,
  Table, Tag, Tooltip, Typography,
} from "antd";
import { UndoOutlined } from "@ant-design/icons";
import { api } from "../api";
import { onObservation } from "../stream";
import { ConfigEditor } from "./ConfigEditor";
import type { ExplorerControlResult, ExplorerPlugin } from "../types";

const errText = (e: unknown) => (e instanceof Error ? e.message : String(e));

// Type labels are generic (one label per type); when the desired config
// carries an identity field, prefer it so rows are tellable apart.
function displayName(p: ExplorerPlugin): string {
  if (p.config?.title) return p.config.title;
  if (p.config?.page_id) return String(p.config.page_id);
  if (p.config?.panel_id) return String(p.config.panel_id);
  if (p.config?.source_id) return String(p.config.source_id);
  return p.name;
}

function stateTag(state: string) {
  const label =
    state === "Active" ? "活跃" : state === "Gone" ? "已移除" : state === "Failed" ? "失败" : state;
  const color =
    state === "Active" ? "success" : state === "Gone" ? "default" : state === "Failed" ? "error" : "processing";
  return <Tag color={color}>{label}</Tag>;
}

// 控制台基础设施组件：卸载会导致控制台自身失效，服务端同样拒绝。
const CONSOLE_CRITICAL = new Set(["ui", "query-provider", "console-bridge", "plugin-explorer"]);

export function PluginExplorer() {
  const [plugins, setPlugins] = useState<ExplorerPlugin[]>([]);
  const [removed, setRemoved] = useState<{ id: string; name: string }[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [outcome, setOutcome] = useState<ExplorerControlResult | null>(null);

  const refresh = useCallback(async () => {
    try {
      const list = await api.plugins();
      setPlugins(list);
      api
        .removedPlugins()
        .then(setRemoved)
        .catch(() => setRemoved([]));
      setError(null);
    } catch (e) {
      setError(errText(e));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
    return onObservation(() => void refresh());
  }, [refresh]);

  const selected = plugins.find((p) => p.id === selectedId) ?? plugins[0] ?? null;

  const uninstall = useCallback(
    async (plugin: ExplorerPlugin) => {
      setBusyId(plugin.id);
      setError(null);
      try {
        await api.uninstallPlugin(plugin.id);
        await refresh();
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
      }
    },
    [refresh]
  );

  const install = useCallback(
    async (id: string) => {
      setBusyId(id);
      setError(null);
      try {
        await api.installPlugin(id);
        await refresh();
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
      }
    },
    [refresh]
  );

  const toggle = useCallback(
    async (plugin: ExplorerPlugin, enable: boolean) => {
      setBusyId(plugin.id);
      setError(null);
      setOutcome(null);
      try {
        const result: ExplorerControlResult = await api.controlPlugin(plugin.id, enable);
        setOutcome(result);
        if (result.rejected || result.failed) {
          setError(result.error || "运行时未接受该控制请求");
        }
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
        await refresh();
      }
    },
    [refresh]
  );

  const active = plugins.filter((p) => p.state === "Active").length;

  // Explorer 页面可以由 ui-page 组合出来，而提供运行时数据的
  // plugin-explorer 组件并未激活——明说，而不是渲染空壳。
  if (!loading && plugins.length === 0) {
    return (
      <section className="card" style={{ padding: "20px 22px" }}>
        <h1 style={{ margin: 0, fontSize: 22 }}>插件</h1>
        <p style={{ color: "#99a2b6" }}>
          此页面渲染运行时插件清单，但当前组合未激活 plugin-explorer 组件，背后没有数据源。
        </p>
        {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
        <Empty description="激活 plugin-explorer 组件后即可在此查看与控制插件" />
      </section>
    );
  }

  return (
    <section className="card" style={{ padding: "20px 22px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 16, flexWrap: "wrap", gap: 12 }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22 }}>插件</h1>
          <p style={{ color: "#99a2b6", marginBottom: 0 }}>
            控制台上的每个功能都是一次组件激活；停用即回滚其全部副作用，其余系统继续运行。
          </p>
        </div>
        <Tag color="blue">{active}/{plugins.length} 活跃</Tag>
      </div>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      <div style={{ display: "flex", gap: 20, alignItems: "flex-start", flexWrap: "wrap" }}>
        <div style={{ flex: "1 1 340px", minWidth: 300 }}>
          <Table
            size="small"
            rowKey="id"
            dataSource={plugins}
            loading={loading && plugins.length === 0}
            pagination={{ pageSize: 12, hideOnSinglePage: true, size: "small" }}
            rowClassName={(p) => (selected?.id === p.id ? "ant-table-row-selected" : "")}
            onRow={(p) => ({ onClick: () => setSelectedId(p.id), style: { cursor: "pointer" } })}
            columns={[
              { title: "名称", dataIndex: "name", key: "name", ellipsis: true,
                render: (_: unknown, p: ExplorerPlugin) => displayName(p) },
              { title: "类型", dataIndex: "type", key: "type", ellipsis: true, width: 140,
                render: (t: string) => <Typography.Text code style={{ fontSize: 12 }}>{t}</Typography.Text> },
              { title: "状态", dataIndex: "state", key: "state", width: 84,
                render: (s: string) => stateTag(s) },
            ]}
          />
        </div>
        <div style={{ flex: "1.4 1 380px", minWidth: 320 }}>
          {selected ? (
            <>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 12, flexWrap: "wrap", marginBottom: 12 }}>
                <div style={{ minWidth: 0 }}>
                  <Typography.Title level={5} style={{ margin: 0 }}>{selected.name}</Typography.Title>
                  <Typography.Text type="secondary" copyable style={{ fontSize: 12 }}>{selected.id}</Typography.Text>
                </div>
                <Space wrap>
                  {selected.controllable &&
                  (selected.state === "Active" || selected.state === "Gone" || selected.state === "Failed") ? (
                    selected.state === "Active" ? (
                      <Button size="small" danger loading={busyId === selected.id}
                        onClick={() => void toggle(selected, false)}>
                        停用
                      </Button>
                    ) : (
                      <Button size="small" type="primary" loading={busyId === selected.id}
                        onClick={() => void toggle(selected, true)}>
                        激活
                      </Button>
                    )
                  ) : null}
                  {selected.controllable && selected.state === "Active" ? (
                    CONSOLE_CRITICAL.has(selected.type) ? (
                      <Tooltip title="控制台基础设施，不能卸载">
                        <Tag color="gold">受保护</Tag>
                      </Tooltip>
                    ) : (
                      <Popconfirm
                        title="卸载该组件？"
                        description="从期望配置中移除并回滚其全部副作用，可通过安装恢复。"
                        okText="卸载" cancelText="取消"
                        okButtonProps={{ danger: true }}
                        onConfirm={() => void uninstall(selected)}
                      >
                        <Button size="small" danger loading={busyId === selected.id}>卸载</Button>
                      </Popconfirm>
                    )
                  ) : null}
                </Space>
              </div>
              {selected.state === "Failed" && selected.error && (
                <Alert
                  type="error" showIcon style={{ marginBottom: 12 }}
                  message="组件激活失败"
                  description={selected.error}
                />
              )}
              {outcome && outcome.accepted && !error && (
                <Typography.Text type="secondary" style={{ fontSize: 12, display: "block", marginBottom: 8 }}>
                  运行时已接受请求 — 以下状态重新读取自 fibers。
                </Typography.Text>
              )}
              <Descriptions size="small" bordered column={1}>
                <Descriptions.Item label="类型">
                  <Typography.Text code>{selected.type}</Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item label="状态">{stateTag(selected.state)}</Descriptions.Item>
                <Descriptions.Item label="组件">
                  {selected.components.length ? (
                    <Space size={4} wrap>
                      {selected.components.map((c) => <Tag key={c} style={{ fontFamily: "monospace", fontSize: 11 }}>{c}</Tag>)}
                    </Space>
                  ) : "—"}
                </Descriptions.Item>
                <Descriptions.Item label="能力">
                  {selected.capabilities.length ? (
                    <Space size={4} wrap>
                      {selected.capabilities.map((cap) => <Tag key={cap} color="blue">{cap}</Tag>)}
                    </Space>
                  ) : "—"}
                </Descriptions.Item>
                {selected.config && Object.keys(selected.config).length > 0 && (
                  <Descriptions.Item label="配置">
                    <Space size={4} wrap>
                      {Object.keys(selected.config).sort().map((k) => (
                        <Tag key={k} style={{ fontFamily: "monospace", fontSize: 11 }}>
                          {k}={String(selected.config![k])}
                        </Tag>
                      ))}
                    </Space>
                  </Descriptions.Item>
                )}
              </Descriptions>
              <ConfigEditor key={selected.id} pluginId={selected.id} />
              {removed.length > 0 && (
                <>
                  <Typography.Text type="secondary">已卸载 — 安装可恢复</Typography.Text>
                  <Space size={8} wrap>
                    {removed.map((r) => (
                      <Button key={r.id} size="small" icon={<UndoOutlined />}
                        disabled={busyId === r.id}
                        loading={busyId === r.id}
                        onClick={() => void install(r.id)}>
                        安装 {r.name || r.id}
                      </Button>
                    ))}
                  </Space>
                </>
              )}
            </>
          ) : (
            <Skeleton active />
          )}
        </div>
      </div>
    </section>
  );
}
