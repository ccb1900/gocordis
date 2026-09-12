// Structured plugin config editor: scalar fields become typed controls,
// known declarative shapes (views/actions/columns/filters/...) become
// structured card/row editors, and anything unknown stays editable as JSON.
// Unknown keys are always preserved on save — the editor narrows nothing.
import { Alert, Button, Input, InputNumber, Select, Space, Switch, Typography } from "antd";
import { DeleteOutlined, ArrowUpOutlined, ArrowDownOutlined, PlusOutlined } from "@ant-design/icons";
import React, { useEffect, useMemo, useState } from "react";
import { api } from "../api";
import { registeredBlockKinds } from "../views/registry";

const errText = (e: unknown) => (e instanceof Error ? e.message : String(e));

type Row = Record<string, unknown>;

// ---------------------------------------------------------------- row editor

interface FieldSpec {
  key: string;
  label: string;
  type?: "text" | "number" | "bool" | "select";
  options?: string[];
  width?: number | "flex";
}

function FieldControl({ spec, value, onChange }: {
  spec: FieldSpec;
  value: unknown;
  onChange: (v: unknown) => void;
}) {
  if (spec.type === "bool") {
    return <Switch size="small" checked={Boolean(value)} onChange={(c) => onChange(c)} />;
  }
  if (spec.type === "number") {
    return (
      <InputNumber
        size="small"
        style={{ width: "100%" }}
        value={value === undefined || value === null ? null : Number(value)}
        onChange={(n) => onChange(n ?? null)}
        placeholder={spec.label}
      />
    );
  }
  if (spec.type === "select") {
    return (
      <Select
        size="small"
        style={{ width: "100%" }}
        allowClear
        value={value === undefined || value === null || value === "" ? undefined : String(value)}
        onChange={(v) => onChange(v ?? "")}
        options={(spec.options ?? []).map((o) => ({ value: o, label: o }))}
        placeholder={spec.label}
      />
    );
  }
  return (
    <Input
      size="small"
      value={value === undefined || value === null ? "" : String(value)}
      onChange={(e) => onChange(e.target.value)}
      placeholder={spec.label}
      allowClear
    />
  );
}

function ArrayEditor({ items, spec, addTemplate, addLabel, onChange }: {
  items: Row[];
  spec: FieldSpec[];
  addTemplate: Row;
  addLabel: string;
  onChange: (items: Row[]) => void;
}) {
  const move = (i: number, dir: -1 | 1) => {
    const next = [...items];
    const j = i + dir;
    if (j < 0 || j >= next.length) return;
    [next[i], next[j]] = [next[j], next[i]];
    onChange(next);
  };
  return (
    <Space direction="vertical" style={{ width: "100%" }} size={6}>
      {items.map((item, i) => (
        <div key={i} style={{ display: "flex", gap: 6, alignItems: "center", flexWrap: "wrap" }}>
          {spec.map((f) => (
            <div key={f.key} style={{ width: f.width === "flex" ? 180 : (f.width ?? 130) }}>
              <FieldControl spec={f} value={item[f.key]} onChange={(v) => {
                const next = items.map((it, j) => (j === i ? { ...it, [f.key]: v } : it));
                onChange(next);
              }} />
            </div>
          ))}
          <Button size="small" type="text" icon={<ArrowUpOutlined />} disabled={i === 0} onClick={() => move(i, -1)} />
          <Button size="small" type="text" icon={<ArrowDownOutlined />} disabled={i === items.length - 1} onClick={() => move(i, 1)} />
          <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => onChange(items.filter((_, j) => j !== i))} />
        </div>
      ))}
      <Button size="small" icon={<PlusOutlined />} onClick={() => onChange([...items, { ...addTemplate }])}>
        {addLabel}
      </Button>
    </Space>
  );
}

// ---------------------------------------------------------------- KV editor

function KVEditor({ value, onChange }: {
  value: Record<string, unknown>;
  onChange: (v: Record<string, unknown>) => void;
}) {
  const entries = Object.entries(value ?? {});
  const set = (oldKey: string, newKey: string, v: unknown) => {
    const next: Record<string, unknown> = {};
    for (const [k, val] of entries) next[k === oldKey ? newKey : k] = val;
    next[newKey] = v;
    onChange(next);
  };
  const remove = (key: string) => {
    const next = { ...value };
    delete next[key];
    onChange(next);
  };
  return (
    <Space direction="vertical" style={{ width: "100%" }} size={4}>
      {entries.map(([k, v]) => (
        <Space key={k} size={4}>
          <Input size="small" defaultValue={k} style={{ width: 140 }}
            onBlur={(e) => { const nk = e.target.value.trim(); if (nk && nk !== k) set(k, nk, v); }} />
          <Input size="small" defaultValue={String(v ?? "")} style={{ width: 180 }}
            onBlur={(e) => set(k, k, e.target.value)} />
          <Button size="small" type="text" danger icon={<DeleteOutlined />} onClick={() => remove(k)} />
        </Space>
      ))}
      <Button size="small" icon={<PlusOutlined />}
        onClick={() => onChange({ ...value, [`key${entries.length + 1}`]: "" })}>
        添加键值
      </Button>
    </Space>
  );
}

// JSON textarea for values outside the modeled surface.
function JsonField({ label, value, onChange }: { label: string; value: unknown; onChange: (v: unknown) => void; }) {
  const [text, setText] = React.useState(() => JSON.stringify(value ?? null, null, 2));
  const [bad, setBad] = React.useState(false);
  React.useEffect(() => { setText(JSON.stringify(value ?? null, null, 2)); }, [value]);
  return (
    <div>
      <div style={{ marginBottom: 4 }}>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>{label}</Typography.Text>
      </div>
      <Input.TextArea
        value={text}
        onChange={(e) => { setText(e.target.value); setBad(false); }}
        onBlur={() => {
          try { onChange(JSON.parse(text)); } catch { setBad(true); }
        }}
        autoSize={{ minRows: 2, maxRows: 12 }}
        style={{ fontFamily: "monospace", fontSize: 12 }}
        status={bad ? "error" : undefined}
      />
      {bad && <Typography.Text type="danger" style={{ fontSize: 12 }}>JSON 解析失败，未应用</Typography.Text>}
    </div>
  );
}

// ---------------------------------------------------------------- views

// Kind options come from the live renderer registry: built-ins plus any
// custom renderers registered by plugin client modules.
const kindOptions = (): string[] => {
  const kinds = registeredBlockKinds();
  return kinds.length ? kinds : ["table", "kv", "list", "stats", "trend", "query-table"];
};

const COLUMN_SPEC: FieldSpec[] = [
  { key: "key", label: "字段", width: 110 },
  { key: "title", label: "标题", width: 110 },
  { key: "format", label: "格式模板", width: "flex" },
];
const ACTION_SPEC: FieldSpec[] = [
  { key: "label", label: "按钮文字", width: 90 },
  { key: "command", label: "命令", width: 110 },
];
const FILTER_SPEC: FieldSpec[] = [
  { key: "key", label: "参数名", width: 100 },
  { key: "label", label: "标签", width: 90 },
  { key: "optionsQuery", label: "选项查询", width: 120 },
  { key: "type", label: "类型", type: "select", options: ["date"], width: 80 },
  { key: "default", label: "默认值", width: 110 },
  { key: "required", label: "必填", type: "bool", width: 50 },
];
const FIELD_SPEC: FieldSpec[] = [
  { key: "key", label: "字段", width: 130 },
  { key: "label", label: "标签", width: 130 },
];
const SERIES_SPEC: FieldSpec[] = [
  { key: "key", label: "字段", width: 130 },
  { key: "label", label: "名称", width: 130 },
];
const ITEM_SPEC: FieldSpec[] = [
  { key: "label", label: "标签", width: 110 },
  { key: "query", label: "查询", width: 120 },
  { key: "op", label: "聚合", type: "select", options: ["count", "sum"], width: 80 },
  { key: "field", label: "字段", width: 110 },
  { key: "warn", label: "告警", type: "bool", width: 50 },
];

function RowActionsEditor({ block, patch }: { block: Row; patch: (p: Row) => void }) {
  const actions = (block.rowActions as Row[]) ?? [];
  return (
    <>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>行动作</Typography.Text>
      {actions.map((a, i) => (
        <div key={i} style={{ border: "1px solid var(--line-soft)", borderRadius: 6, padding: "6px 8px", marginBottom: 6 }}>
          <Space size={4} wrap>
            <Input size="small" value={String(a.label ?? "")} style={{ width: 90 }} placeholder="按钮文字"
              onChange={(e) => patch({ rowActions: actions.map((x, j) => (j === i ? { ...x, label: e.target.value } : x)) })} />
            <Input size="small" value={String(a.command ?? "")} style={{ width: 110 }} placeholder="命令"
              onChange={(e) => patch({ rowActions: actions.map((x, j) => (j === i ? { ...x, command: e.target.value } : x)) })} />
            <Button size="small" type="text" danger icon={<DeleteOutlined />}
              onClick={() => patch({ rowActions: actions.filter((_, j) => j !== i) })} />
          </Space>
          <div style={{ marginTop: 4 }}>
            <Typography.Text type="secondary" style={{ fontSize: 11 }}>参数映射（$row.xxx）</Typography.Text>
            <KVEditor value={(a.args as Record<string, unknown>) ?? {}}
              onChange={(args) => patch({ rowActions: actions.map((x, j) => (j === i ? { ...x, args } : x)) })} />
          </div>
        </div>
      ))}
      <Button size="small" icon={<PlusOutlined />}
        onClick={() => patch({ rowActions: [...actions, { label: "动作", command: "" }] })}>
        添加行动作
      </Button>
    </>
  );
}

function BlockEditor({ block, patch }: { block: Row; patch: (p: Row) => void }) {
  const kind = String(block.kind ?? "table");
  const set = (k: string, v: unknown) => patch({ [k]: v });
  const arr = (k: string) => (Array.isArray(block[k]) ? (block[k] as Row[]) : undefined);
  return (
    <Space direction="vertical" style={{ width: "100%" }} size={8}>
      <Space size={8} wrap>
        <Select size="small" style={{ width: 120 }} value={kind}
          onChange={(v) => patch({ kind: v })}
          options={Array.from(new Set([...kindOptions(), kind])).map((k) => ({ value: k, label: k }))} />
        <Input size="small" style={{ width: 140 }} value={String(block.title ?? "")} placeholder="标题（可选）"
          onChange={(e) => set("title", e.target.value)} allowClear />
        <Input size="small" style={{ width: 160 }} value={String(block.query ?? "")} placeholder="hub 查询名"
          onChange={(e) => set("query", e.target.value)} allowClear />
        {(kind === "table" || kind === "query-table" || kind === "list") && (
          <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>每页</Typography.Text>
            <InputNumber size="small" style={{ width: 70 }} value={Number(block.pageSize ?? 20) || 20}
              onChange={(n) => set("pageSize", n ?? 20)} />
          </span>
        )}
        {kind === "table" && (
          <span style={{ display: "inline-flex", alignItems: "center", gap: 4 }}>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>点选联动</Typography.Text>
            <Switch size="small" checked={Boolean(block.selectFocus)} onChange={(c) => set("selectFocus", c)} />
          </span>
        )}
      </Space>

      {block.params !== undefined || true ? (
        <div>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>查询参数（$focus.sourceId 等令牌可用）</Typography.Text>
          <KVEditor value={(block.params as Record<string, unknown>) ?? {}} onChange={(params) => set("params", params)} />
        </div>
      ) : null}

      {kind === "table" && (
        <>
          {arr("columns") !== undefined && (
            <div>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>列</Typography.Text>
              <ArrayEditor items={arr("columns") ?? []} spec={COLUMN_SPEC} addLabel="添加列"
                addTemplate={{ key: "", title: "" }} onChange={(columns) => set("columns", columns)} />
            </div>
          )}
          <Space size={10}>
            <Input size="small" style={{ width: 160 }} value={String(block.expand ?? "")} placeholder="展开字段（如 metadata）"
              onChange={(e) => set("expand", e.target.value)} allowClear />
          </Space>
        </>
      )}
      {kind === "table" && arr("rowActions") !== undefined && (
        <RowActionsEditor block={block} patch={patch} />
      )}
      {kind === "list" && arr("rowActions") !== undefined && (
        <RowActionsEditor block={block} patch={patch} />
      )}
      {kind === "kv" && (
        <div>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>字段</Typography.Text>
          <ArrayEditor items={arr("fields") ?? []} spec={FIELD_SPEC} addLabel="添加字段"
            addTemplate={{ key: "", label: "" }} onChange={(fields) => set("fields", fields)} />
        </div>
      )}
      {kind === "stats" && (
        <div>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>统计项</Typography.Text>
          <ArrayEditor items={arr("items") ?? []} spec={ITEM_SPEC} addLabel="添加统计项"
            addTemplate={{ label: "", op: "count" }} onChange={(items) => set("items", items)} />
        </div>
      )}
      {kind === "trend" && (
        <>
          <Input size="small" style={{ width: 160 }} value={String(block.dateKey ?? "date")} placeholder="日期字段"
            onChange={(e) => set("dateKey", e.target.value)} />
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>序列（第一个为主序列，第二个为告警序列）</Typography.Text>
            <ArrayEditor items={arr("series") ?? []} spec={SERIES_SPEC} addLabel="添加序列"
              addTemplate={{ key: "", label: "" }} onChange={(series) => set("series", series)} />
          </div>
        </>
      )}
      {kind === "query-table" && (
        <>
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>过滤器</Typography.Text>
            <ArrayEditor items={arr("filters") ?? []} spec={FILTER_SPEC} addLabel="添加过滤器"
              addTemplate={{ key: "", label: "" }} onChange={(filters) => set("filters", filters)} />
          </div>
          {arr("columns") !== undefined && (
            <div>
              <Typography.Text type="secondary" style={{ fontSize: 12 }}>列（留空时由响应列名生成）</Typography.Text>
              <ArrayEditor items={arr("columns") ?? []} spec={COLUMN_SPEC} addLabel="添加列"
                addTemplate={{ key: "", title: "" }} onChange={(columns) => set("columns", columns)} />
            </div>
          )}
        </>
      )}
    </Space>
  );
}

function ViewsEditor({ views, onChange }: { views: Row[]; onChange: (v: Row[]) => void }) {
  const add = () => onChange([...views, { kind: "table", query: "" }]);
  return (
    <Space direction="vertical" style={{ width: "100%" }} size={10}>
      {views.map((block, i) => (
        <div key={i} style={{ border: "1px solid var(--line-soft)", borderRadius: 8, padding: "10px 12px" }}>
          <div style={{ display: "flex", justifyContent: "space-between", marginBottom: 8 }}>
            <Typography.Text strong style={{ fontSize: 12 }}>
              视图块 {i + 1} · {String(block.kind ?? "?")}
            </Typography.Text>
            <Space size={0}>
              <Button size="small" type="text" icon={<ArrowUpOutlined />} disabled={i === 0}
                onClick={() => { const n = [...views]; [n[i], n[i - 1]] = [n[i - 1], n[i]]; onChange(n); }} />
              <Button size="small" type="text" icon={<ArrowDownOutlined />} disabled={i === views.length - 1}
                onClick={() => { const n = [...views]; [n[i], n[i + 1]] = [n[i + 1], n[i]]; onChange(n); }} />
              <Button size="small" type="text" danger icon={<DeleteOutlined />}
                onClick={() => onChange(views.filter((_, j) => j !== i))} />
            </Space>
          </div>
          <BlockEditor block={block} patch={(p) => onChange(views.map((b, j) => (j === i ? { ...b, ...p } : b)))} />
        </div>
      ))}
      <Button size="small" icon={<PlusOutlined />} onClick={add}>添加视图块</Button>
    </Space>
  );
}

// ---------------------------------------------------------------- main editor

export function ConfigEditor({ pluginId }: { pluginId: string }) {
  const [draft, setDraft] = useState<Record<string, unknown> | null>(null);
  const [raw, setRaw] = useState("");
  const [tab, setTab] = useState("form");
  const [loadError, setLoadError] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [savedAt, setSavedAt] = useState<string | null>(null);

  useEffect(() => {
    setDraft(null); setRaw(""); setTab("form"); setSavedAt(null); setSaveError(null); setLoadError(null);
    api.pluginConfig(pluginId)
      .then((cfg) => {
        const obj = cfg ?? {};
        setDraft(obj);
        setRaw(JSON.stringify(obj, null, 2));
      })
      .catch((e) => setLoadError(errText(e)));
  }, [pluginId]);

  const setField = (k: string, v: unknown) => setDraft((d) => ({ ...d!, [k]: v }));
  const setViews = (k: "views" | "actions") => (v: Row[]) => setDraft((d) => ({ ...d!, [k]: v }));

  const switchTab = (next: string) => {
    if (next === "json") { setRaw(JSON.stringify(draft ?? {}, null, 2)); setSaveError(null); setTab(next); return; }
    try {
      const parsed = JSON.parse(raw);
      if (parsed == null || typeof parsed !== "object" || Array.isArray(parsed)) throw new Error("配置必须是 JSON 对象");
      setDraft(parsed); setSaveError(null); setTab(next);
    } catch (e) { setSaveError(errText(e)); }
  };

  const save = async () => {
    setSaving(true); setSaveError(null);
    try {
      let cfg = draft;
      if (tab === "json") {
        const parsed = JSON.parse(raw);
        if (parsed == null || typeof parsed !== "object" || Array.isArray(parsed)) throw new Error("配置必须是 JSON 对象");
        cfg = parsed; setDraft(parsed);
      }
      await api.setPluginConfig(pluginId, cfg as Record<string, unknown>);
      setSavedAt(new Date().toLocaleTimeString());
    } catch (e) { setSaveError(errText(e)); } finally { setSaving(false); }
  };

  const isComposition = /(^|:)(ui-page|ui-panel|ui-contribution)/.test(pluginId) ||
    (draft !== null && ("views" in draft || "actions" in draft));

  const scalarField = (k: string, v: unknown) => {
    if (typeof v === "boolean") {
      return (
        <Space size={6} key={k}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{k}</Typography.Text>
          <Switch size="small" checked={v} onChange={(c) => setField(k, c)} />
        </Space>
      );
    }
    if (typeof v === "number") {
      return (
        <span key={k} style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
          <Typography.Text type="secondary" style={{ fontSize: 12 }}>{k}</Typography.Text>
          <InputNumber size="small" style={{ width: 100 }} value={v} onChange={(n) => setField(k, n ?? 0)} />
        </span>
      );
    }
    return (
      <span key={k} style={{ display: "inline-flex", alignItems: "center", gap: 6 }}>
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>{k}</Typography.Text>
        <Input size="small" style={{ width: 220 }} value={String(v)} onChange={(e) => setField(k, e.target.value)} allowClear />
      </span>
    );
  };

  const body = useMemo(() => {
    if (!draft) return null;
    const scalars = Object.keys(draft).filter((k) => {
      const v = draft[k];
      if (k === "views" || k === "actions") return false;
      return typeof v !== "object" || v === null;
    }).sort();
    return (
      <Space direction="vertical" style={{ width: "100%" }} size={10}>
        <div style={{ display: "flex", gap: 14, flexWrap: "wrap" }}>{scalars.map((k) => scalarField(k, draft[k]))}</div>
        {Array.isArray(draft.actions) && (
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>页面动作</Typography.Text>
            <ArrayEditor items={draft.actions as Row[]} addLabel="添加动作" addTemplate={{ label: "", command: "" }}
              spec={[
                { key: "label", label: "按钮文字", width: 120 },
                { key: "command", label: "命令", width: 120 },
                { key: "datePicker", label: "日期选择", type: "bool", width: 80 },
              ]}
              onChange={setViews("actions")} />
          </div>
        )}
        {Array.isArray(draft.views) && (
          <div>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>视图块</Typography.Text>
            <ViewsEditor views={draft.views as Row[]} onChange={setViews("views")} />
          </div>
        )}
        {Object.keys(draft).filter((k) => {
          const v = draft[k];
          return k !== "views" && k !== "actions" && v !== null && typeof v === "object";
        }).map((k) => (
          <JsonField key={k} label={k} value={draft[k]} onChange={(v) => setField(k, v)} />
        ))}
      </Space>
    );
  }, [draft]);

  return (
    <>
      {isComposition && (
        <Alert
          type="warning" showIcon style={{ marginBottom: 10 }}
          message="组合声明组件"
          description="此组件的配置属于页面组合声明。在此保存的改动会持久化为期望态覆盖，并覆盖配置文件中的同名设置；结构性调整建议在配置文件中完成。"
        />
      )}
      <Space size={4} style={{ marginBottom: 8 }}>
        <Button size="small" type={tab === "form" ? "primary" : "default"} onClick={() => switchTab("form")}>表单</Button>
        <Button size="small" type={tab === "json" ? "primary" : "default"} onClick={() => switchTab("json")}>JSON</Button>
      </Space>
      {tab === "json" ? (
        <Input.TextArea value={raw} onChange={(e) => setRaw(e.target.value)}
          rows={Math.min(18, Math.max(6, raw.split("\n").length + 1))}
          style={{ fontFamily: "monospace", fontSize: 12 }} />
      ) : body}
      <Space align="center" style={{ marginTop: 10 }}>
        <Button size="small" type="primary" loading={saving} onClick={() => void save()}>保存配置</Button>
        {savedAt && <Typography.Text type="secondary" style={{ fontSize: 12 }}>已保存 {savedAt}（持久化为期望态覆盖）</Typography.Text>}
      </Space>
      {saveError && <Alert style={{ marginTop: 8 }} type="error" showIcon message={saveError} />}
    </>
  );
}
