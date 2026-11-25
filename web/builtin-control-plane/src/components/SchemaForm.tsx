// SchemaForm: renders FieldSpecs with the console's existing antd controls.
// FieldControl is shared with ConfigEditor's structured editors (views,
// columns, filters, …) so a schema-driven form and the legacy editors use
// exactly the same widgets and value conventions.
import { Input, InputNumber, Select, Switch, Typography } from "antd";
import React from "react";
import type { FieldSpec } from "../lib/schema-form";

// JSON editing keeps its own component: the hooks below must belong to a
// component that always mounts with the same hook order. FieldControl only
// branches on which component to render, so it stays hook-free.
function JsonControl({ value, onChange }: { value: unknown; onChange: (v: unknown) => void }) {
  const [text, setText] = React.useState(() => JSON.stringify(value ?? null, null, 2));
  const [bad, setBad] = React.useState(false);
  React.useEffect(() => {
    setText(JSON.stringify(value ?? null, null, 2));
  }, [value]);
  return (
    <div>
      <Input.TextArea
        value={text}
        autoSize={{ minRows: 2, maxRows: 8 }}
        style={{ fontFamily: "monospace", fontSize: 12 }}
        status={bad ? "error" : undefined}
        onChange={(e) => { setText(e.target.value); setBad(false); }}
        onBlur={() => {
          try { onChange(JSON.parse(text)); } catch { setBad(true); }
        }}
      />
      {bad && <Typography.Text type="danger" style={{ fontSize: 11 }}>JSON 解析失败，未应用</Typography.Text>}
    </div>
  );
}

export function FieldControl({ spec, value, onChange }: {
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
  // Array/object fields stay editable as structured JSON: keep the raw text
  // while typing, parse on blur, and never write an invalid value back.
  if (spec.type === "json") return <JsonControl value={value} onChange={onChange} />;
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

/** Render schema-derived (or fallback) fields into one responsive grid. */
export function SchemaForm({ specs, value, onChange }: {
  specs: FieldSpec[];
  value: Record<string, unknown>;
  onChange: (key: string, v: unknown) => void;
}) {
  if (specs.length === 0) return null;
  return (
    <div style={{ display: "grid", gridTemplateColumns: "repeat(auto-fill, minmax(230px, 1fr))", gap: "10px 14px" }}>
      {specs.map((f) => (
        <div key={f.key}>
          <div style={{ marginBottom: 4, display: "flex", gap: 4 }}>
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>{f.label}</Typography.Text>
            {f.required && <Typography.Text type="danger" style={{ fontSize: 12 }}>*</Typography.Text>}
          </div>
          <FieldControl spec={f} value={value[f.key]} onChange={(v) => onChange(f.key, v)} />
          {f.description && (
            <div style={{ fontSize: 11, color: "#8a93a6", marginTop: 2 }}>{f.description}</div>
          )}
        </div>
      ))}
    </div>
  );
}
