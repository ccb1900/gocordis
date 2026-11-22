// Local plugin store renderer. This is a *local inventory* (GET
// /api/plugins/store), not a remote marketplace — there is deliberately no
// Koishi-style catalog discovery and no hub command buttons here. Installing /
// upgrading / uninstalling happens only in the plugin explorer, which uploads
// a package to the hub. Keeping this view read-only means a composition that
// references it can never trigger a remote fetch or an unverified mutation.
//
// All row decisions are pure functions in ./market-catalog (node-testable);
// this file only maps them to antd nodes.

import { Alert, Button, Empty, Input, Select, Space, Table, Tag, Typography } from "antd";
import type { TableColumnsType } from "antd";
import { useEffect, useMemo, useState } from "react";
import { api } from "../api";
import { PLUGIN, useDomainVersion } from "../lib/projections";
import type { BlockRenderer } from "./registry";
import type { ViewBlock } from "./schema";
import {
  compareRows, filterRows, mapStoreEntry,
  type CatalogFilter, type StoreRow,
} from "./market-catalog";

function boolTag(v: boolean) {
  return <Tag color={v ? "success" : "default"}>{v ? "是" : "否"}</Tag>;
}

export const MarketCatalogBlock: BlockRenderer = ({ block }: { block: ViewBlock }) => {
  const [rows, setRows] = useState<StoreRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<CatalogFilter>({});
  const [reload, setReload] = useState(0);
  const version = useDomainVersion(PLUGIN);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    api
      .storePlugins()
      .then((entries) => {
        if (alive) {
          setRows(entries.map(mapStoreEntry));
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
  }, [version, reload]);

  const visible = useMemo(() => filterRows(rows, filter), [rows, filter]);

  const columns: TableColumnsType<StoreRow> = [
    { title: "ID", dataIndex: "id", key: "id", width: 150, ellipsis: true, sorter: (a, b) => compareRows(a, b, "id") },
    { title: "名称", dataIndex: "name", key: "name", ellipsis: true, sorter: (a, b) => compareRows(a, b, "name") },
    { title: "版本", dataIndex: "version", key: "version", width: 110, sorter: (a, b) => compareRows(a, b, "version") },
    { title: "宿主范围", dataIndex: "hostVersionRange", key: "hostVersionRange", width: 130,
      render: (v: string) => v },
    { title: "安装路径", dataIndex: "installPath", key: "installPath", ellipsis: true,
      render: (v: string) => <span title={v}>{v}</span> },
    { title: "依赖", dataIndex: "dependencies", key: "dependencies", width: 180, ellipsis: true,
      render: (v: string) => <span title={v}>{v}</span> },
    { title: "声明", dataIndex: "declared", key: "declared", width: 80,
      sorter: (a, b) => Number(a.declared) - Number(b.declared), render: (v: boolean) => boolTag(v) },
    { title: "运行", dataIndex: "running", key: "running", width: 80,
      sorter: (a, b) => Number(a.running) - Number(b.running), render: (v: boolean) => boolTag(v) },
    { title: "校验和", dataIndex: "checksum", key: "checksum", width: 130, ellipsis: true,
      render: (v: string) => <Typography.Text code style={{ fontSize: 11 }} title={v}>{v}</Typography.Text> },
  ];

  return (
    <div className="card" style={{ padding: 12 }}>
      {block.title && <h3 style={{ margin: "0 0 8px", fontSize: 15 }}>{block.title}</h3>}
      <Space wrap style={{ marginBottom: 10 }}>
        <Input.Search
          aria-label="搜索本地 store"
          allowClear
          placeholder="搜索 ID / 名称 / 安装路径"
          style={{ width: 240 }}
          onSearch={(value) => setFilter((f) => ({ ...f, search: value }))}
          onChange={(e) => { if (!e.target.value) setFilter((f) => ({ ...f, search: "" })); }}
        />
        <Select
          aria-label="声明状态筛选"
          style={{ width: 140 }}
          placeholder="声明状态"
          allowClear
          options={[
            { value: "installed", label: "已声明" },
            { value: "not-installed", label: "未声明" },
          ]}
          onChange={(value) => setFilter((f) => ({
            ...f,
            installed: value === "installed" ? true : value === "not-installed" ? false : undefined,
          }))}
        />
        <Button size="small" onClick={() => setReload((v) => v + 1)} loading={loading}>刷新</Button>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>本地 store · {rows.length} 项</Typography.Text>
      </Space>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 10 }} />}
      <Alert
        type="info"
        showIcon
        message="本地插件仓库（只读）"
        description="此处仅列出宿主本地已安装的插件包；不连接任何远程市场。安装、升级与卸载请在插件管理页上传包完成。"
        style={{ marginBottom: 10 }}
      />
      <Table<StoreRow>
        size="small"
        rowKey="id"
        loading={loading}
        scroll={{ x: "max-content" }}
        dataSource={visible}
        columns={columns}
        pagination={{ pageSize: block.pageSize ?? 20, hideOnSinglePage: true }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="本地 store 暂无条目" /> }}
      />
    </div>
  );
};
