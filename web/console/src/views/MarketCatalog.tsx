// Market catalog renderer. Data comes from the hub's `market/catalog` named
// query; mutations (when the composition wires hub commands) go through
// ViewContext.hubCommand, which is the existing api.ts command helper. If no
// commands are declared, the table degrades to a read-only catalog instead of
// failing: an app that only exposes a typed Service still renders its list.
//
// All decisions are pure functions in ./market-catalog (node-testable); this
// file only maps them to antd nodes.

import { Alert, Button, Empty, Input, Select, Space, Table, Tag, Typography } from "antd";
import type { TableColumnsType } from "antd";
import { useEffect, useMemo, useState } from "react";
import { useDomainVersion } from "../lib/projections";
import type { MarketCatalogQuery } from "../types";
import type { BlockRenderer, ViewContext } from "./registry";
import type { ViewBlock } from "./schema";
import {
  ACTION_LABELS, buildArgs, compareRows, declaredActions, eligibleActions,
  filterRows, parseCatalog,
  type CatalogActionSpec, type CatalogField, type CatalogFilter, type CatalogRow,
} from "./market-catalog";

const DEFAULT_QUERY = "market/catalog";

function trustColor(trust: string): string {
  const t = trust.toLowerCase();
  if (["trusted", "verified", "official", "high"].includes(t)) return "green";
  if (["untrusted", "low", "unknown"].includes(t)) return "red";
  return "default";
}

function ActionButtons({
  specs, row, ctx, pending, onRun,
}: {
  specs: CatalogActionSpec[];
  row: CatalogRow;
  ctx: { busy: boolean };
  pending: string | null;
  onRun: (spec: CatalogActionSpec, row: CatalogRow) => void;
}) {
  const allowed = eligibleActions(row, specs);
  // Native artifacts are build-time only — never offer a mutation for them.
  if (row.native) {
    return <Typography.Text type="secondary">build-time only</Typography.Text>;
  }
  if (allowed.length === 0) return <Typography.Text type="secondary">—</Typography.Text>;
  return (
    <Space size={4}>
      {allowed.map((spec) => {
        const key = `${spec.action}:${row.id}`;
        return (
          <Button
            key={spec.action}
            size="small"
            type={spec.action === "remove" ? undefined : "primary"}
            danger={spec.action === "remove"}
            disabled={ctx.busy || pending !== null}
            loading={pending === key}
            onClick={() => onRun(spec, row)}
          >
            {spec.label || ACTION_LABELS[spec.action]}
          </Button>
        );
      })}
    </Space>
  );
}

export const MarketCatalogBlock: BlockRenderer = ({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) => {
  const [rows, setRows] = useState<CatalogRow[]>([]);
  const [sequence, setSequence] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState<CatalogFilter>({});
  const [pending, setPending] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const version = useDomainVersion(block.domain ?? "market");
  const query = block.query || DEFAULT_QUERY;

  // A declaration with no rowActions means the host exposes no mutation
  // commands (e.g. only a typed Service) → read-only catalog, no probing.
  const specs = useMemo(() => declaredActions(block.rowActions), [block.rowActions]);
  const readOnly = specs.length === 0;

  useEffect(() => {
    let alive = true;
    setLoading(true);
    ctx
      .hubQuery<MarketCatalogQuery>(query)
      .then((data) => {
        if (!alive) return;
        const parsed = parseCatalog(data);
        setRows(parsed.rows);
        setSequence(parsed.sequence);
        setError(null);
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

  const visible = useMemo(() => filterRows(rows, filter), [rows, filter]);
  const backends = useMemo(
    () => Array.from(new Set(rows.map((r) => r.backend).filter((b) => b !== "—"))).sort(),
    [rows]
  );
  const hasNative = rows.some((r) => r.native);

  const run = async (spec: CatalogActionSpec, row: CatalogRow) => {
    setPending(`${spec.action}:${row.id}`);
    setError(null);
    try {
      await ctx.hubCommand(spec.command, buildArgs(spec.args, row));
      setReload((v) => v + 1);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    } finally {
      setPending(null);
    }
  };

  const columns: TableColumnsType<CatalogRow> = [
    { title: "ID", dataIndex: "id", key: "id", width: 150, ellipsis: true, sorter: (a, b) => compareRows(a, b, "id") },
    { title: "Name", dataIndex: "name", key: "name", ellipsis: true, sorter: (a, b) => compareRows(a, b, "name") },
    { title: "Version", dataIndex: "version", key: "version", width: 110, sorter: (a, b) => compareRows(a, b, "version") },
    {
      title: "Backend", dataIndex: "backend", key: "backend", width: 170,
      sorter: (a, b) => compareRows(a, b, "backend"),
      render: (v: unknown, row: CatalogRow) => (
        <Space size={4} wrap>
          <span>{String(v ?? "—")}</span>
          {row.native && <Tag color="gold">build-time only</Tag>}
        </Space>
      ),
    },
    {
      title: "Trust", dataIndex: "trust", key: "trust", width: 110,
      sorter: (a, b) => compareRows(a, b, "trust"),
      render: (v: unknown) => <Tag color={trustColor(String(v ?? ""))}>{String(v ?? "—")}</Tag>,
    },
    {
      title: "Installed", dataIndex: "installed", key: "installed", width: 100,
      sorter: (a, b) => compareRows(a, b, "installed"),
      render: (v: unknown) => <Tag color={v === true ? "green" : "default"}>{v === true ? "是" : "否"}</Tag>,
    },
    {
      title: "Compatible", dataIndex: "compatible", key: "compatible", width: 110,
      sorter: (a, b) => compareRows(a, b, "compatible"),
      render: (v: unknown) => <Tag color={v === true ? "green" : "red"}>{v === true ? "是" : "否"}</Tag>,
    },
    {
      title: "Source", dataIndex: "source", key: "source", ellipsis: true,
      sorter: (a, b) => compareRows(a, b, "source"),
      render: (v: unknown, row: CatalogRow) => (
        <span title={row.description !== "—" ? row.description : undefined}>{String(v ?? "—")}</span>
      ),
    },
    ...(!readOnly
      ? [{
          title: "操作", key: "__actions", width: 210,
          render: (_: unknown, row: CatalogRow) => (
            <ActionButtons specs={specs} row={row} ctx={ctx} pending={pending} onRun={(spec, r) => void run(spec, r)} />
          ),
        }]
      : []),
  ] as TableColumnsType<CatalogRow>;

  return (
    <div className="card" style={{ padding: 12 }}>
      {block.title && <h3 style={{ margin: "0 0 8px", fontSize: 15 }}>{block.title}</h3>}
      <Space wrap style={{ marginBottom: 10 }}>
        <Input.Search
          aria-label="搜索目录"
          allowClear
          placeholder="搜索 ID / 名称 / 来源"
          style={{ width: 220 }}
          onSearch={(value) => setFilter((f) => ({ ...f, search: value }))}
          onChange={(e) => { if (!e.target.value) setFilter((f) => ({ ...f, search: "" })); }}
        />
        <Select
          aria-label="后端筛选"
          style={{ width: 140 }}
          placeholder="后端"
          allowClear
          options={backends.map((b) => ({ value: b, label: b }))}
          onChange={(backend) => setFilter((f) => ({ ...f, backend }))}
        />
        <Select
          aria-label="安装状态筛选"
          style={{ width: 140 }}
          placeholder="安装状态"
          allowClear
          options={[
            { value: "installed", label: "已安装" },
            { value: "not-installed", label: "未安装" },
            { value: "compatible", label: "兼容" },
          ]}
          onChange={(value) => setFilter((f) => ({
            ...f,
            installed: value === "installed" ? true : value === "not-installed" ? false : undefined,
            compatible: value === "compatible" ? true : undefined,
          }))}
        />
        <Button size="small" onClick={() => setReload((v) => v + 1)} loading={loading}>刷新</Button>
        {sequence !== null && sequence > 0 && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>目录版本 {sequence}</Typography.Text>
        )}
      </Space>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 10 }} />}
      {readOnly && (
        <Alert
          type="info"
          showIcon
          message="当前目录只读：宿主未提供安装/升级/卸载命令。"
          style={{ marginBottom: 10 }}
        />
      )}
      <Table<CatalogRow>
        size="small"
        rowKey={(_, i) => String(i)}
        loading={loading}
        scroll={{ x: "max-content" }}
        dataSource={visible}
        columns={columns}
        pagination={{ pageSize: block.pageSize ?? 20, hideOnSinglePage: true }}
        locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="目录暂无条目" /> }}
      />
      {hasNative && (
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          native 条目在构建期编译进宿主（build-time only），运行时不可安装/升级/卸载。
        </Typography.Text>
      )}
    </div>
  );
};
