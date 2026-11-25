// runtime-inspector: read-only view of the `runtime/snapshot` named query.
// Five tables (Fibers / Scopes / Providers / Dependencies / Effects) expose the
// kernel's value-only observation model: lifecycle state and intent, provider
// identity (fiber + activation + scope), dependency status/reason and effect
// slot state. Row mapping lives in ./runtime-inspector (node-testable).
import { Alert, Button, Empty, Space, Table, Tabs, Tag, Typography } from "antd";
import type { TableColumnsType, TableProps, TabsProps } from "antd";
import { useEffect, useState } from "react";
import type { RuntimeSnapshotDTO } from "../types";
import type { BlockRenderer } from "../types";
import {
  parseSnapshot,
  type DependencyRow, type EffectRow, type FiberRow, type ProviderRow, type ScopeRow,
} from "./runtime-inspector";

const DEFAULT_QUERY = "runtime/snapshot";

function tone(state: string): string {
  const s = state.toLowerCase();
  if (s.includes("fail") || s.includes("error")) return "red";
  if (s.includes("pending") || s.includes("wait") || s.includes("loading")) return "gold";
  if (s.includes("undo") || s.includes("withdraw") || s.includes("unload")) return "orange";
  if (s.includes("active") || s.includes("running") || s.includes("committed")) return "green";
  return "default";
}

function stateCell(v: string) {
  return <Tag color={tone(v)}>{v}</Tag>;
}

function tableProps<T extends { key: string }>(description: string): Pick<
  TableProps<T>,
  "size" | "rowKey" | "pagination" | "scroll" | "locale"
> {
  return {
    size: "small",
    rowKey: "key",
    pagination: { pageSize: 20, hideOnSinglePage: true },
    scroll: { x: "max-content" },
    locale: { emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description={description} /> },
  };
}

export const RuntimeInspectorBlock: BlockRenderer = ({ block, ctx }) => {
  const [data, setData] = useState(() => parseSnapshot(null));
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const version = ctx.generation;
  const query = block.query || DEFAULT_QUERY;

  useEffect(() => {
    let alive = true;
    setLoading(true);
    ctx
      .hubQuery<RuntimeSnapshotDTO>(query)
      .then((d) => {
        if (alive) {
          setData(parseSnapshot(d));
          setError(null);
        }
      })
      .catch((e) => {
        if (alive) setError(e instanceof Error ? e.message : String(e));
      })
      .finally(() => {
        if (alive) setLoading(false);
      });
    return () => {
      alive = false;
    };
  }, [ctx.hubQuery, query, version, reload]);

  const fiberColumns: TableColumnsType<FiberRow> = [
    { title: "ID", dataIndex: "id", key: "id", width: 80 },
    { title: "Name", dataIndex: "name", key: "name", width: 140, ellipsis: true },
    { title: "Component", dataIndex: "component", key: "component", width: 160, ellipsis: true },
    { title: "State", dataIndex: "state", key: "state", width: 110, render: (v: string) => stateCell(v) },
    { title: "Intent", dataIndex: "intent", key: "intent", width: 100 },
    { title: "Error", dataIndex: "err", key: "err", ellipsis: true },
    { title: "ActivationID", dataIndex: "activationID", key: "activationID", width: 120 },
    { title: "ScopeID", dataIndex: "scopeID", key: "scopeID", width: 90 },
    { title: "Parent fibers", dataIndex: "parentFiberIDs", key: "parentFiberIDs", width: 140 },
    { title: "Child fibers", dataIndex: "childFiberIDs", key: "childFiberIDs", width: 140 },
  ];
  const scopeColumns: TableColumnsType<ScopeRow> = [
    { title: "ID", dataIndex: "id", key: "id", width: 90 },
    { title: "ParentID", dataIndex: "parentID", key: "parentID", width: 100 },
    { title: "Provider keys", dataIndex: "providerKeys", key: "providerKeys", ellipsis: true },
    { title: "Fiber IDs", dataIndex: "fiberIDs", key: "fiberIDs", ellipsis: true },
  ];
  const providerColumns: TableColumnsType<ProviderRow> = [
    { title: "Key", dataIndex: "capabilityKey", key: "capabilityKey", width: 180, ellipsis: true },
    { title: "State", dataIndex: "state", key: "state", width: 110, render: (v: string) => stateCell(v) },
    {
      title: "Retiring", dataIndex: "retiring", key: "retiring", width: 100,
      render: (v: boolean) => <Tag color={v ? "orange" : "default"}>{v ? "是" : "否"}</Tag>,
    },
    { title: "Owner fiber", dataIndex: "ownerFiberID", key: "ownerFiberID", width: 120 },
    { title: "Owner activation", dataIndex: "ownerActivationID", key: "ownerActivationID", width: 150 },
    { title: "ScopeID", dataIndex: "scopeID", key: "scopeID", width: 90 },
  ];
  const dependencyColumns: TableColumnsType<DependencyRow> = [
    { title: "Key", dataIndex: "capabilityKey", key: "capabilityKey", width: 180, ellipsis: true },
    { title: "Status", dataIndex: "status", key: "status", width: 120, render: (v: string) => stateCell(v) },
    { title: "Consumer fiber", dataIndex: "consumerFiberID", key: "consumerFiberID", width: 130 },
    { title: "Consumer activation", dataIndex: "consumerActivationID", key: "consumerActivationID", width: 160 },
    { title: "Provider fiber", dataIndex: "providerFiberID", key: "providerFiberID", width: 130 },
    { title: "Provider activation", dataIndex: "providerActivationID", key: "providerActivationID", width: 160 },
    { title: "Reason", dataIndex: "reason", key: "reason", width: 200, ellipsis: true },
  ];
  const effectColumns: TableColumnsType<EffectRow> = [
    { title: "Owner fiber", dataIndex: "ownerFiberID", key: "ownerFiberID", width: 130 },
    { title: "ActivationID", dataIndex: "activationID", key: "activationID", width: 130 },
    { title: "Seq", dataIndex: "seq", key: "seq", width: 80, sorter: (a, b) => a.seq - b.seq },
    { title: "Kind", dataIndex: "kind", key: "kind", width: 150 },
    { title: "Key", dataIndex: "capabilityKey", key: "capabilityKey", width: 180, ellipsis: true },
    { title: "State", dataIndex: "state", key: "state", width: 120, render: (v: string) => stateCell(v) },
  ];

  const items: TabsProps["items"] = [
    {
      key: "fibers", label: `Fibers (${data.fibers.length})`,
      children: <Table<FiberRow> {...tableProps<FiberRow>("暂无 Fiber")} loading={loading} dataSource={data.fibers} columns={fiberColumns} />,
    },
    {
      key: "scopes", label: `Scopes (${data.scopes.length})`,
      children: <Table<ScopeRow> {...tableProps<ScopeRow>("暂无 Scope")} loading={loading} dataSource={data.scopes} columns={scopeColumns} />,
    },
    {
      key: "providers", label: `Providers (${data.providers.length})`,
      children: <Table<ProviderRow> {...tableProps<ProviderRow>("暂无 Provider")} loading={loading} dataSource={data.providers} columns={providerColumns} />,
    },
    {
      key: "dependencies", label: `Dependencies (${data.dependencies.length})`,
      children: <Table<DependencyRow> {...tableProps<DependencyRow>("暂无依赖")} loading={loading} dataSource={data.dependencies} columns={dependencyColumns} />,
    },
    {
      key: "effects", label: `Effects (${data.effects.length})`,
      children: <Table<EffectRow> {...tableProps<EffectRow>("暂无 Effect")} loading={loading} dataSource={data.effects} columns={effectColumns} />,
    },
  ];

  return (
    <div className="card" style={{ padding: 12 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12, flexWrap: "wrap", marginBottom: 10 }}>
        <div>
          <Typography.Text strong style={{ fontSize: 15 }}>{block.title || "运行时检视"}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12, marginLeft: 10 }}>
            {data.state} · 序列 {data.sequence} · {data.runtimeID}
          </Typography.Text>
        </div>
        <Space size={8}>
          <Button size="small" loading={loading} onClick={() => setReload((v) => v + 1)}>刷新</Button>
        </Space>
      </div>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 8 }} />}
      <Tabs items={items} />
    </div>
  );
};
