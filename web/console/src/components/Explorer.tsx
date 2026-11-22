// Plugin management control plane. Every column reflects a real wire field
// from GET /api/plugins, and every action is a POST that returns a
// PluginMutationResult — the console never assumes a command succeeded just
// because HTTP 200 came back. `protected` / `controllable` come from the hub,
// not from a client-side allow-list.

import React, { useCallback, useEffect, useMemo, useState } from "react";
import {
  Alert, Button, Descriptions, Empty, Popconfirm, Select, Skeleton, Space,
  Switch, Table, Tag, Tooltip, Typography,
} from "antd";
import { ReloadOutlined } from "@ant-design/icons";
import { api } from "../api";
import { onPluginState } from "../stream";
import { PLUGIN, useDomainVersion } from "../lib/projections";
import { mutationMessage } from "../lib/plugin-control";
import { PluginDependencies } from "./PluginDependencies";
import { PluginFileButton } from "./PluginFileButton";
import { SettingsSlot } from "./SettingsSlot";
import type {
  ExplorerPlugin, PluginMutationResult, PluginStateEvent, UploadedPackage,
} from "../types";

const errText = (e: unknown) => (e instanceof Error ? e.message : String(e));

function displayName(p: ExplorerPlugin): string {
  return p.name || p.id;
}

function stateTag(state: string) {
  const label =
    state === "Active" ? "活跃" : state === "Gone" ? "已移除" : state === "Failed" ? "失败" :
    state === "Pending" ? "等待依赖" : state;
  const color =
    state === "Active" ? "success" : state === "Gone" ? "default" : state === "Failed" ? "error" :
    state === "Pending" ? "warning" : "processing";
  return <Tag color={color}>{label}</Tag>;
}

function keyTags(values: string[], color: string) {
  return values.length ? (
    <Space size={4} wrap>
      {values.map((v) => <Tag key={v} color={color}>{v}</Tag>)}
    </Space>
  ) : "—";
}

interface Notice {
  text: string;
  result: PluginMutationResult;
}

/** One place that turns accepted/rejected mutations into UI feedback. A
 * rejected command carries a machine `code` plus a human `error`; a transient
 * accepted command is marked by `persisted: false`. */
function feedback(result: PluginMutationResult, okText: string): { error: string | null; notice: Notice | null } {
  if (!result.accepted) {
    return { error: mutationMessage(result, "运行时未接受该操作"), notice: null };
  }
  return { error: null, notice: { text: okText, result } };
}

function resultDetail(result: PluginMutationResult) {
  const persisted =
    result.persisted === undefined ? "未知" : result.persisted ? "是" : "否（临时）";
  const changes = result.changes?.length ? ` · ${result.changes.length} 项变更` : "";
  return `状态 ${result.state || "—"} · 已持久化 ${persisted}${changes}`;
}

export function PluginExplorer() {
  const [plugins, setPlugins] = useState<ExplorerPlugin[]>([]);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<Notice | null>(null);
  const [installEnabled, setInstallEnabled] = useState(true);
  const [lastEvent, setLastEvent] = useState<PluginStateEvent | null>(null);
  const [typeFilter, setTypeFilter] = useState<string | undefined>(undefined);
  const pluginVersion = useDomainVersion(PLUGIN);

  const refresh = useCallback(async () => {
    try {
      setPlugins(await api.plugins());
      setError(null);
    } catch (e) {
      setError(errText(e));
    } finally {
      setLoading(false);
    }
  }, []);

  // The list is a projection of the control-plane domain: SSE `plugin-state`
  // bumps it (via projections), so an enable/upgrade in another tab or by the
  // runtime shows up here without an observation event or a manual reload.
  useEffect(() => {
    void refresh();
  }, [refresh, pluginVersion]);
  useEffect(() => onPluginState(setLastEvent), []);

  const selected = plugins.find((p) => p.id === selectedId) ?? plugins[0] ?? null;
  const types = useMemo(() => Array.from(new Set(plugins.map((p) => p.type))).sort(), [plugins]);
  const visible = typeFilter ? plugins.filter((p) => p.type === typeFilter) : plugins;
  const active = plugins.filter((p) => p.state === "Active").length;

  const report = (result: PluginMutationResult, okText: string) => {
    const f = feedback(result, okText);
    setError(f.error);
    setNotice(f.notice);
  };

  const toggle = useCallback(
    async (plugin: ExplorerPlugin, enable: boolean) => {
      setBusyId(plugin.id);
      setError(null);
      setNotice(null);
      try {
        const result = await api.controlPlugin(plugin.id, enable);
        report(result, enable ? "已提交启用" : "已提交停用");
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
        await refresh();
      }
    },
    [refresh]
  );

  const install = useCallback(
    async (pkg: UploadedPackage) => {
      setBusyId(pkg.id || "__install__");
      setError(null);
      setNotice(null);
      try {
        const result = await api.installPlugin(pkg.uploadKey, installEnabled);
        report(result, `已安装 ${pkg.id}@${pkg.version}${installEnabled ? "并启用" : "（未启用）"}`);
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
        await refresh();
      }
    },
    [refresh, installEnabled]
  );

  const upgrade = useCallback(
    async (plugin: ExplorerPlugin, pkg: UploadedPackage) => {
      setBusyId(plugin.id);
      setError(null);
      setNotice(null);
      try {
        const result = await api.upgradePlugin(plugin.id, pkg.uploadKey);
        report(result, `已从 ${pkg.id}@${pkg.version} 提交升级`);
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
        await refresh();
      }
    },
    [refresh]
  );

  const uninstall = useCallback(
    async (plugin: ExplorerPlugin) => {
      setBusyId(plugin.id);
      setError(null);
      setNotice(null);
      try {
        // The control service matches this against the exact installed store
        // version; the declared `version` is a NuGet range and would not match.
        const result = await api.uninstallPlugin(plugin.id, plugin.installedVersion || undefined);
        report(result, `已提交卸载 ${plugin.id}`);
      } catch (e) {
        setError(errText(e));
      } finally {
        setBusyId(null);
        await refresh();
      }
    },
    [refresh]
  );

  // Honest empty state: the endpoint is part of the hub, so an empty list
  // means either no plugin is declared or the control component is not active.
  if (!loading && plugins.length === 0) {
    return (
      <section className="card" style={{ padding: "20px 22px" }}>
        <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", marginBottom: 12 }}>
          <h1 style={{ margin: 0, fontSize: 22 }}>插件</h1>
          <Space>
            <PluginFileButton label="安装插件" onUploaded={(pkg) => void install(pkg)} />
            <Button size="small" icon={<ReloadOutlined />} onClick={() => void refresh()}>刷新</Button>
          </Space>
        </div>
        <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />
        {notice && <Alert type="success" showIcon message={notice.text} description={resultDetail(notice.result)} style={{ marginBottom: 12 }} />}
        <Empty description="插件清单为空：声明一个插件，或激活 plugin-explorer 组件后刷新" />
      </section>
    );
  }

  return (
    <section className="card" style={{ padding: "20px 22px" }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", marginBottom: 16, flexWrap: "wrap", gap: 12 }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 22 }}>插件</h1>
          <p style={{ color: "#99a2b6", marginBottom: 0 }}>
            声明（enabled / declared）与实际状态（state）分列；开关、升级与卸载均由 hub 控制面执行。
            {lastEvent && (
              <Typography.Text type="secondary" style={{ fontSize: 11, marginLeft: 8 }}>
                SSE rev {lastEvent.revision} · {lastEvent.action || "plugin-state"}
              </Typography.Text>
            )}
          </p>
        </div>
        <Space wrap>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>安装后启用</Typography.Text>
          <Switch size="small" checked={installEnabled} onChange={setInstallEnabled} />
          <PluginFileButton label="安装插件" onUploaded={(pkg) => void install(pkg)} />
          <Select
            size="small"
            style={{ minWidth: 150 }}
            allowClear
            placeholder="按类型筛选"
            value={typeFilter}
            onChange={(v) => setTypeFilter(v)}
            options={types.map((ty) => ({ value: ty, label: ty }))}
          />
          <Tag color="blue">{typeFilter ? `${visible.length}/${plugins.length} 个组件` : `${active}/${plugins.length} 活跃`}</Tag>
          <Button size="small" icon={<ReloadOutlined />} loading={loading} onClick={() => void refresh()}>刷新</Button>
        </Space>
      </div>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 12 }} />}
      {notice && (
        <Alert
          type="success"
          showIcon
          message={notice.text}
          description={resultDetail(notice.result)}
          style={{ marginBottom: 12 }}
        />
      )}
      <div style={{ display: "flex", gap: 20, alignItems: "flex-start", flexWrap: "wrap" }}>
        <div style={{ flex: "1 1 380px", minWidth: 320 }}>
          <Table
            size="small"
            rowKey="id"
            dataSource={visible}
            loading={loading && plugins.length === 0}
            pagination={{ pageSize: 12, hideOnSinglePage: true, size: "small" }}
            rowClassName={(p) => (selected?.id === p.id ? "ant-table-row-selected" : "")}
            onRow={(p) => ({ onClick: () => setSelectedId(p.id), style: { cursor: "pointer" } })}
            scroll={{ x: "max-content" }}
            columns={[
              { title: "名称", dataIndex: "name", key: "name", ellipsis: true,
                render: (_: unknown, p: ExplorerPlugin) => (
                  <span>
                    <Typography.Text strong>{displayName(p)}</Typography.Text>
                    <Typography.Text type="secondary" style={{ fontSize: 11, display: "block" }}>{p.id}</Typography.Text>
                  </span>
                ) },
              { title: "类型", dataIndex: "type", key: "type", ellipsis: true, width: 130,
                render: (t: string) => <Typography.Text code style={{ fontSize: 12 }}>{t || "—"}</Typography.Text> },
              { title: "版本", key: "version", width: 130,
                render: (_: unknown, p: ExplorerPlugin) => (
                  <span style={{ fontSize: 12 }}>
                    {p.version || p.installedVersion || "—"}
                    {p.version && p.installedVersion && p.version !== p.installedVersion && (
                      <Typography.Text type="secondary" style={{ display: "block", fontSize: 11 }}>
                        已装 {p.installedVersion}
                      </Typography.Text>
                    )}
                  </span>
                ) },
              { title: "声明", key: "enabled", width: 100,
                render: (_: unknown, p: ExplorerPlugin) => (
                  <Space size={2} wrap>
                    <Tag color={p.enabled ? "blue" : "default"}>{p.enabled ? "启用" : "停用"}</Tag>
                    {!p.declared && <Tag color="gold">未声明</Tag>}
                  </Space>
                ) },
              { title: "状态", dataIndex: "state", key: "state", width: 100,
                render: (s: string, p: ExplorerPlugin) => (
                  <Tooltip title={p.error || undefined}>{stateTag(s)}</Tooltip>
                ) },
              { title: "能力/依赖", key: "caps", width: 110,
                render: (_: unknown, p: ExplorerPlugin) => (
                  <Tooltip title={`provides: ${p.provides.join(", ") || "—"}\nrequires: ${p.requires.join(", ") || "—"}`}>
                    <Typography.Text style={{ fontSize: 12 }}>
                      {p.provides.length}P / {p.requires.length}R
                    </Typography.Text>
                  </Tooltip>
                ) },
              { title: "操作", key: "actions", width: 240,
                render: (_: unknown, p: ExplorerPlugin) => {
                  const canControl = p.controllable && !p.protected;
                  const why = p.protected
                    ? "受保护插件不能完全启停/卸载"
                    : !p.controllable ? "该插件不接受运行时控制" : "";
                  return (
                    <Space size={4} wrap onClick={(e) => e.stopPropagation()}>
                      <Tooltip title={why || undefined}>
                        <span>
                          <Button
                            size="small"
                            type={p.enabled ? "default" : "primary"}
                            danger={p.enabled}
                            disabled={!canControl}
                            loading={busyId === p.id}
                            onClick={() => void toggle(p, !p.enabled)}
                          >
                            {p.enabled ? "停用" : "启用"}
                          </Button>
                        </span>
                      </Tooltip>
                      <Tooltip title={why || undefined}>
                        <span>
                          <PluginFileButton
                            label="升级"
                            type="default"
                            disabled={!canControl}
                            onUploaded={(pkg) => void upgrade(p, pkg)}
                          />
                        </span>
                      </Tooltip>
                      <Tooltip title={why || "从声明中移除并回滚副作用"}>
                        <span>
                          <Popconfirm
                            title={`卸载 ${p.id}？`}
                            description="从期望配置中移除并回滚其全部副作用，可再次上传包安装。"
                            okText="卸载" cancelText="取消"
                            okButtonProps={{ danger: true }}
                            disabled={!canControl}
                            onConfirm={() => void uninstall(p)}
                          >
                            <Button size="small" danger disabled={!canControl} loading={busyId === p.id}>卸载</Button>
                          </Popconfirm>
                        </span>
                      </Tooltip>
                    </Space>
                  );
                } },
            ]}
          />
        </div>
        <div style={{ flex: "1.4 1 380px", minWidth: 320 }}>
          {selected ? (
            <>
              <div style={{ display: "flex", justifyContent: "space-between", alignItems: "flex-start", gap: 12, flexWrap: "wrap", marginBottom: 12 }}>
                <div style={{ minWidth: 0 }}>
                  <Typography.Title level={5} style={{ margin: 0 }}>{displayName(selected)}</Typography.Title>
                  <Typography.Text type="secondary" copyable style={{ fontSize: 12 }}>{selected.id}</Typography.Text>
                </div>
                <Space size={4} wrap>
                  {selected.protected && (
                    <Tooltip title="宿主标记为受保护：不能完全启停/卸载；升级与配置由服务端裁决">
                      <Tag color="gold">受保护</Tag>
                    </Tooltip>
                  )}
                  {!selected.controllable && <Tag>不可控</Tag>}
                  {stateTag(selected.state)}
                </Space>
              </div>
              {selected.state === "Failed" && selected.error && (
                <Alert type="error" showIcon style={{ marginBottom: 12 }} message="组件激活失败" description={selected.error} />
              )}
              <Descriptions size="small" bordered column={1}>
                <Descriptions.Item label="ID">
                  <Typography.Text code copyable>{selected.id}</Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item label="类型">
                  <Typography.Text code>{selected.type || "—"}</Typography.Text>
                </Descriptions.Item>
                <Descriptions.Item label="版本">{selected.version || "—"}</Descriptions.Item>
                <Descriptions.Item label="已安装版本">{selected.installedVersion || "—"}</Descriptions.Item>
                <Descriptions.Item label="声明状态">
                  <Space size={4} wrap>
                    <Tag color={selected.enabled ? "blue" : "default"}>{selected.enabled ? "enabled" : "disabled"}</Tag>
                    <Tag color={selected.declared ? "green" : "gold"}>{selected.declared ? "已声明" : "未声明"}</Tag>
                    {selected.error && <Typography.Text type="danger" style={{ fontSize: 12 }}>{selected.error}</Typography.Text>}
                  </Space>
                </Descriptions.Item>
                <Descriptions.Item label="组件">{keyTags(selected.components, "default")}</Descriptions.Item>
                <Descriptions.Item label="提供能力（provides）">{keyTags(selected.provides, "blue")}</Descriptions.Item>
                <Descriptions.Item label="依赖能力（requires）">{keyTags(selected.requires, "purple")}</Descriptions.Item>
                {selected.dependencies && (
                  <Descriptions.Item label="包依赖（dependencies）">{keyTags(selected.dependencies, "cyan")}</Descriptions.Item>
                )}
                {Object.keys(selected.config).length > 0 && (
                  <Descriptions.Item label="配置">
                    <Space size={4} wrap>
                      {Object.keys(selected.config).sort().map((k) => (
                        <Tag key={k} style={{ fontFamily: "monospace", fontSize: 11 }}>
                          {k}={String(selected.config[k])}
                        </Tag>
                      ))}
                    </Space>
                  </Descriptions.Item>
                )}
              </Descriptions>
              <PluginDependencies pluginId={selected.id} />
              <div style={{ marginTop: 14, borderTop: "1px solid var(--line-soft, #232a3a)", paddingTop: 10 }}>
                <Typography.Text strong style={{ fontSize: 13 }}>配置</Typography.Text>
                <div style={{ marginTop: 8 }}>
                  <SettingsSlot key={selected.id} pluginId={selected.id} componentType={selected.type} />
                </div>
              </div>
            </>
          ) : (
            <Skeleton active />
          )}
        </div>
      </div>
    </section>
  );
}
