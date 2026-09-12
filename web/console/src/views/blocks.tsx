// Generic view renderers: domain-free block implementations. Data always
// comes from hub named queries, actions go through hub commands; renderers
// never make domain judgments. Every built-in registers into the keyed
// renderer registry — a plugin client module can add its own kinds the same
// way (see views/registry.tsx).
import { Button, DatePicker, Descriptions, Empty, Input, Select, Space, Statistic, Table, Typography } from "antd";
import dayjs from "dayjs";
import type { TableColumnsType } from "antd";
import { useCallback, useEffect, useState } from "react";
import { api } from "../api";
import { ALL, useDomainVersion } from "../lib/projections";
import {
  getBlockRenderer, registerBlockRenderer,
  type BlockRenderer, type ViewContext,
} from "./registry";
import { formatRow, needsFocus, resolveParams, type Focus, type ViewBlock } from "./schema";
import { TrendChart, type TrendPoint } from "./TrendChart";

export type { ViewBlock, ViewContext, BlockRenderer };

type Row = Record<string, unknown>;

function statusNode(status: unknown) {
  const s = status == null ? "" : String(status);
  const color =
    s === "Succeeded" || s === "Active"
      ? "#3ecf8e"
      : s === "Failed"
        ? "#f0655a"
        : s === "Pending"
          ? "#f2b544"
          : "#8a93a6";
  const label =
    s === "Active"
      ? "活跃"
      : s === "Succeeded"
        ? "成功"
        : s === "Failed"
          ? "失败"
          : s === "Pending"
            ? "等待数据"
            : s === "Skipped"
              ? "无数据"
              : s;
  return <span style={{ color }}>{label || "—"}</span>;
}

function cellNode(key: string, format: string | undefined, v: unknown) {
  if (format) return formatRow(format, { [key]: v });
  if (key === "status") return statusNode(v);
  return String(v ?? "—");
}

// Blocked until the referenced focus exists: dependent views stay dormant
// with an explicit hint instead of firing incomplete queries. The hint is
// domain-neutral — the framework never names application pages; the app
// carries the guidance in its own page descriptions.
function FocusHint() {
  return (
    <Empty
      image={Empty.PRESENTED_IMAGE_SIMPLE}
      description="尚未选择数据行 —— 先在列表视图中点选一行，这里将展示它对应的明细。"
    />
  );
}

function useQueryData(block: ViewBlock, ctx: ViewContext) {
  const [data, setData] = useState<unknown>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const dormant = needsFocus(block.params) && !ctx.focus;
  const params = JSON.stringify(dormant ? {} : resolveParams(block.params, ctx.focus));
  // Interest-scoped invalidation: the block re-queries when its declared
  // domain moves, not on every raw observation.
  const version = useDomainVersion(block.domain ?? ALL);

  useEffect(() => {
    if (dormant || !block.query) return;
    let alive = true;
    setLoading(true);
    ctx
      .hubQuery(block.query, JSON.parse(params || "{}"))
      .then((d) => alive && (setData(d), setError(null)))
      .catch((e) => alive && setError(e instanceof Error ? e.message : String(e)))
      .finally(() => alive && setLoading(false));
    return () => {
      alive = false;
    };
  }, [block.query, params, dormant, version, ctx.hubQuery]);

  return { data, error, loading: loading && !dormant, dormant };
}

function rowsOf(data: unknown): Row[] {
  if (Array.isArray(data)) return data as Row[];
  // Columnar page shape ({columns: string[], rows: unknown[][]}) from typed
  // sinks normalizes to object rows.
  const page = data as { columns?: string[]; rows?: unknown[][] } | null;
  if (page && Array.isArray(page.rows) && Array.isArray(page.columns)) {
    return page.rows.map((r) => {
      const obj: Row = {};
      page.columns!.forEach((name, j) => (obj[name] = r[j]));
      return obj;
    });
  }
  return [];
}

function actionButtons(actions: ViewBlock["rowActions"], row: Row, ctx: ViewContext) {
  return (actions ?? []).map((a) => {
    const args: Record<string, unknown> = {};
    for (const [k, ref] of Object.entries(a.args ?? {})) {
      const m = /^\$row\.(.+)$/.exec(ref);
      if (m) args[k] = row[m[1]];
    }
    return (
      <Button key={a.label} size="small" disabled={ctx.busy} onClick={() => void ctx.hubCommand(a.command, args)}>
        {a.label}
      </Button>
    );
  });
}

function expandRenderer(block: ViewBlock) {
  if (!block.expand) return undefined;
  return (row: Row) => {
    const meta = row[block.expand!];
    if (!meta || typeof meta !== "object") {
      return <Typography.Text type="secondary">暂无{block.expand}。</Typography.Text>;
    }
    return (
      <Descriptions size="small" column={1} bordered>
        {Object.entries(meta as Record<string, unknown>).map(([k, v]) => (
          <Descriptions.Item key={k} label={k}>
            {String(v)}
          </Descriptions.Item>
        ))}
      </Descriptions>
    );
  };
}

function TableBlock({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const { data, error, loading, dormant } = useQueryData(block, ctx);
  if (dormant) return <FocusHint />;
  if (error) return <p style={{ color: "#f0655a" }}>{error}</p>;
  const columns = (block.columns ?? []).map((c) => ({
    title: c.title,
    dataIndex: c.key,
    key: c.key,
    render: (v: unknown, row: Row) => cellNode(c.key, c.format, c.format ? row : v),
  })) as TableColumnsType<Row>;
  const actions = block.rowActions ?? [];
  const cols: TableColumnsType<Row> = [
    ...columns,
    ...(actions.length
      ? [{ title: "操作", key: "__actions", render: (_: unknown, row: Row) => (
          <span style={{ display: "flex", gap: 6 }}>{actionButtons(actions, row, ctx)}</span>
        ) }]
      : []),
  ];
  const expand = expandRenderer(block);
  return (
    <Table<Row>
      size="small"
      rowKey={(_, i) => String(i)}
      loading={loading}
      dataSource={rowsOf(data)}
      pagination={{ pageSize: block.pageSize ?? 20, hideOnSinglePage: true }}
      columns={cols}
      expandable={expand ? { expandedRowRender: expand, rowExpandable: () => true } : undefined}
      rowClassName={(record) =>
        ctx.focus && record["sourceId"] === ctx.focus.sourceId && record["date"] === ctx.focus.date
          ? "ant-table-row-selected"
          : ""
      }
      onRow={(record) => ({
        onClick: () => {
          if (block.selectFocus && record["sourceId"] && record["date"]) {
            ctx.onFocus(String(record["sourceId"]), String(record["date"]));
          }
        },
        style: block.selectFocus ? { cursor: "pointer" } : undefined,
      })}
    />
  );
}

function KVBlock({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const { data, error, loading, dormant } = useQueryData(block, ctx);
  if (dormant) return <FocusHint />;
  if (error) return <p style={{ color: "#f0655a" }}>{error}</p>;
  const obj = (data ?? {}) as Record<string, unknown>;
  return (
    <Descriptions size="small" column={1} bordered title={block.title}>
      {(block.fields ?? []).map((f) => (
        <Descriptions.Item key={f.key} label={f.label}>
          {loading ? "…" : String(obj[f.key] ?? "—")}
        </Descriptions.Item>
      ))}
    </Descriptions>
  );
}

function ListBlock({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const { data, error, loading, dormant } = useQueryData(block, ctx);
  if (dormant) return <FocusHint />;
  if (error) return <p style={{ color: "#f0655a" }}>{error}</p>;
  const entries = rowsOf(data);
  const titleKey = block.titleKey ?? "sourceId";
  if (entries.length === 0 && !loading) {
    return <Typography.Text type="secondary">暂无条目。</Typography.Text>;
  }
  return (
    <Space direction="vertical" style={{ width: "100%" }} size={4}>
      {entries.map((entry, i) => (
        <div key={i} style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 8 }}>
          <Typography.Text>
            {String(entry[titleKey] ?? "")}
            {entry["date"] ? ` · ${String(entry["date"])}` : ""}
          </Typography.Text>
          <span style={{ display: "flex", gap: 6 }}>{actionButtons(block.rowActions, entry, ctx)}</span>
        </div>
      ))}
    </Space>
  );
}

// Stats: per-item aggregation (count / sum) over a named query's rows.
function StatsBlock({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const [values, setValues] = useState<Array<{ item: NonNullable<ViewBlock["items"]>[number]; value: number }>>([]);
  const [error, setError] = useState<string | null>(null);
  const version = useDomainVersion(block.domain ?? ALL);

  useEffect(() => {
    if (!block.items?.length) return;
    let alive = true;
    Promise.all(
      block.items.map(async (item) => {
        const rows = rowsOf(await ctx.hubQuery(item.query ?? block.query ?? ""));
        let value = 0;
        const field = item.op === "sum" ? item.field : undefined;
        if (field) {
          value = rows.reduce((s, r) => s + (Number(r[field]) || 0), 0);
        } else {
          value = rows.length;
        }
        return { item, value };
      })
    )
      .then((out) => alive && (setValues(out), setError(null)))
      .catch((e) => alive && setError(e instanceof Error ? e.message : String(e)));
    return () => {
      alive = false;
    };
  }, [block.items, block.query, version, ctx.hubQuery]);

  if (error) return <p style={{ color: "#f0655a" }}>{error}</p>;
  return (
    <div style={{ display: "flex", gap: 18, flexWrap: "wrap" }}>
      {values.map(({ item, value }) => (
        <div key={item.label} style={{ flex: "1 1 140px", minWidth: 130 }}>
          <Statistic
            title={item.label}
            value={value}
            valueStyle={item.warn && value > 0 ? { color: "#f0655a" } : undefined}
          />
        </div>
      ))}
    </div>
  );
}

// Trend: aggregate rows by dateKey, summing each series key per bucket.
function TrendBlock({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const { data, error, loading, dormant } = useQueryData(block, ctx);
  if (dormant) return <FocusHint />;
  if (error) return <p style={{ color: "#f0655a" }}>{error}</p>;
  const dateKey = block.dateKey ?? "date";
  const byDate = new Map<string, TrendPoint>();
  for (const row of rowsOf(data)) {
    const label = String(row[dateKey] ?? "").slice(5);
    if (!label) continue;
    const point = byDate.get(label) ?? { label, primary: 0, warning: 0 };
    const primary = block.series?.[0];
    const warning = block.series?.[1];
    point.primary += Number(primary ? row[primary.key] : 0) || 0;
    point.warning += Number(warning ? row[warning.key] : 0) || 0;
    byDate.set(label, point);
  }
  const points = Array.from(byDate.values()).sort((a, b) => (a.label < b.label ? -1 : 1));
  return (
    <>
      {block.title && <h3 style={{ margin: "4px 0 8px", fontSize: 15 }}>{block.title}</h3>}
      {loading ? (
        <Typography.Text type="secondary">加载中…</Typography.Text>
      ) : (
        <TrendChart
          points={points}
          primaryLabel={block.series?.[0]?.label ?? ""}
          warningLabel={block.series?.[1]?.label ?? ""}
        />
      )}
    </>
  );
}

// QueryTable: a query with declared, data-driven filters — pick a source
// from one named query, pick a date, filter exact-match columns.
function QueryTableBlock({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const [filters, setFilters] = useState<Record<string, string>>(() => {
    const init: Record<string, string> = {};
    const token = (v: string) => {
      const day = 86400000;
      const offset = v === "$yesterday" ? 1 : v === "$today" ? 0 : null;
      if (offset === null) return v;
      return new Date(Date.now() - offset * day).toISOString().slice(0, 10);
    };
    for (const f of block.filters ?? []) {
      if (f.default) init[f.key] = token(f.default);
    }
    return init;
  });
  const [options, setOptions] = useState<Record<string, Array<{ value: string; label: string }>>>({});
  const [rows, setRows] = useState<Row[]>([]);
  const [total, setTotal] = useState<number | null>(null);
  const [columns, setColumns] = useState<TableColumnsType<Row>>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const version = useDomainVersion(block.domain ?? ALL);

  useEffect(() => {
    for (const f of block.filters ?? []) {
      if (!f.optionsQuery) continue;
      ctx
        .hubQuery(f.optionsQuery)
        .then((data) => {
          const opts = rowsOf(data).map((r) => ({
            value: String(r[f.optionKey ?? "id"] ?? ""),
            label: String(r[f.optionLabel ?? f.optionKey ?? "id"] ?? ""),
          }));
          setOptions((m) => ({ ...m, [f.key]: opts }));
          setFilters((prev) => {
            if (prev[f.key] || !(f.required ?? false) || opts.length === 0) return prev;
            return { ...prev, [f.key]: opts[0].value };
          });
        })
        .catch(() => setOptions((m) => ({ ...m, [f.key]: [] })));
    }
    // Options load once per block identity.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [block.filters]);

  const load = useCallback(() => {
    if (!block.query) return;
    setLoading(true);
    ctx
      .hubQuery<{ columns?: string[]; rows?: unknown[][] } | unknown[]>(block.query, {
        ...filters,
        limit: "200",
      })
      .then((data) => {
        let out: Row[] = [];
        let names: string[] = [];
        let matched: number | null = null;
        if (Array.isArray(data)) {
          out = rowsOf(data);
          names = Object.keys(out[0] ?? {});
          matched = out.length;
        } else {
          const page = data as { columns?: string[]; rows?: unknown[][]; total?: number };
          names = page.columns ?? [];
          out = (page.rows ?? []).map((r) => {
            const obj: Row = {};
            names.forEach((name, j) => (obj[name] = r[j]));
            return obj;
          });
          matched = typeof page.total === "number" ? page.total : out.length;
        }
        setRows(out);
        setTotal(matched);
        // Declared columns win; without them the response's own column
        // names become the header (typed sinks answer columnar pages).
        const declared = block.columns ?? [];
        const cols: TableColumnsType<Row> = declared.length
          ? declared.map((c) => ({
              title: c.title,
              dataIndex: c.key,
              key: c.key,
              render: (v: unknown) => (v === null || v === undefined ? "—" : String(v)),
            }))
          : names.map((n) => ({
              title: n,
              dataIndex: n,
              key: n,
              render: (v: unknown) => (v === null || v === undefined ? "—" : String(v)),
            }));
        setColumns(cols);
        setError(null);
      })
      .catch((e) => setError(e instanceof Error ? e.message : String(e)))
      .finally(() => setLoading(false));
  }, [block.query, block.columns, filters, version, ctx.hubQuery]);

  useEffect(() => {
    load();
  }, [load]);

  const ready = (block.filters ?? []).every((f) => !f.required || filters[f.key]);
  return (
    <>
      {block.title && <h3 style={{ margin: "4px 0 8px", fontSize: 15 }}>{block.title}</h3>}
      <Space wrap style={{ marginBottom: 12 }}>
        {(block.filters ?? []).map((f) =>
          f.optionsQuery ? (
            <Select
              key={f.key}
              aria-label={f.label}
              style={{ minWidth: 160 }}
              value={filters[f.key]}
              onChange={(v) => setFilters((m) => ({ ...m, [f.key]: v }))}
              options={options[f.key] ?? []}
              placeholder={f.label}
            />
          ) : f.type === "date" ? (
            <DatePicker
              key={f.key}
              aria-label={f.label}
              style={{ width: 140 }}
              value={filters[f.key] ? dayjs(filters[f.key]) : null}
              onChange={(d) => setFilters((m) => ({ ...m, [f.key]: d ? d.format("YYYY-MM-DD") : "" }))}
              placeholder={f.label}
              allowClear={!f.required}
            />
          ) : (
            <Input
              key={f.key}
              aria-label={f.label}
              style={{ width: 140 }}
              value={filters[f.key]}
              onChange={(e) => setFilters((m) => ({ ...m, [f.key]: e.target.value }))}
              placeholder={f.label}
            />
          )
        )}
        <Button onClick={load} loading={loading} disabled={!ready}>
          查询
        </Button>
        {total !== null && (
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>
            共 {total} 行{rows.length < total ? `（当前页 ${rows.length}）` : ""}
          </Typography.Text>
        )}
      </Space>
      {error && <p style={{ color: "#f0655a" }}>{error}</p>}
      <Table<Row>
        size="small"
        rowKey={(_, i) => String(i)}
        loading={loading}
        dataSource={ready ? rows : []}
        pagination={{ pageSize: block.pageSize ?? 20, hideOnSinglePage: true }}
        columns={columns}
      />
    </>
  );
}

// Graceful fallback for kinds this console build does not render: dynamic
// composition may declare a view authored against a newer renderer set. The
// declaration is shown, never silently dropped, and the page keeps working.
function UnknownBlock({ block }: { block: ViewBlock }) {
  return (
    <div style={{ color: "#8a93a6", padding: 8 }}>
      <Typography.Text type="secondary">
        视图类型 “{block.kind}” 未在此控制台注册。
      </Typography.Text>
      <details style={{ marginTop: 6 }}>
        <summary style={{ cursor: "pointer", fontSize: 12, color: "#8a93a6" }}>原始声明</summary>
        <pre style={{ fontSize: 11, whiteSpace: "pre-wrap", margin: "6px 0 0" }}>
          {JSON.stringify(block, null, 2)}
        </pre>
      </details>
    </div>
  );
}

export function ViewBlockRenderer({ block, ctx }: { block: ViewBlock; ctx: ViewContext }) {
  const Renderer = getBlockRenderer(block.kind);
  if (!Renderer) return <UnknownBlock block={block} />;
  return <Renderer block={block} ctx={ctx} />;
}

// Built-in palette. Registration (not a switch statement) is what keeps the
// palette open: kind → renderer is data, extensible at runtime.
registerBlockRenderer("table", TableBlock);
registerBlockRenderer("kv", KVBlock);
registerBlockRenderer("list", ListBlock);
registerBlockRenderer("stats", StatsBlock);
registerBlockRenderer("trend", TrendBlock);
registerBlockRenderer("query-table", QueryTableBlock);
