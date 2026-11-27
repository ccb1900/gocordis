// Reusable file format configuration. Normal users configure parsing, mapping
// and validation through business fields; no workflow JSON is exposed.

import { Alert, Button, Input, Modal, Space, Switch, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { unwrapData } from "./dto";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

interface ProfileRow {
  id: string; name: string; version: number;
  fileNamePattern: string; format: string; delimiter: string;
  hasHeader: boolean; encoding: string; enabled: boolean;
  mapping: Record<string, string>; requiredFields: string[];
  directoryPattern: string; datePattern: string; flat: boolean;
  metadataRules: Array<{ from: string; pattern: string; key: string; required: boolean }>;
}

const rows = (raw: unknown): any[] => {
  const data = unwrapData(raw) as unknown;
  return Array.isArray(data) ? data : [];
};

const empty = (): ProfileRow => ({
  id: `profile-${typeof crypto !== "undefined" && "randomUUID" in crypto
    ? crypto.randomUUID().replace(/-/g, "").slice(0, 10) : Date.now().toString(36)}`,
  name: "", version: 0, fileNamePattern: "*.csv", format: "csv", delimiter: ",",
  hasHeader: true, encoding: "utf-8", enabled: true, mapping: {}, requiredFields: [],
  directoryPattern: "", datePattern: "", flat: false, metadataRules: [],
});

export function FileProfileConsole({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [items, setItems] = useState<ProfileRow[]>([]);
  const [draft, setDraft] = useState<ProfileRow | null>(null);
  const [mapping, setMapping] = useState("");
  const [required, setRequired] = useState("");
  const [metadataText, setMetadataText] = useState("");
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try { setItems(rows(await ctx.hubQuery(block?.query ?? "industrial/file-profiles")) as ProfileRow[]); }
    catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
  }, [block?.query, ctx]);
  useEffect(() => { void load(); }, [load]);

  const open = useCallback((row?: ProfileRow) => {
    const value = row ? { ...row } : empty();
    setDraft(value);
    setMapping(Object.entries(value.mapping ?? {}).map(([target, source]) => `${target}=${source}`).join(","));
    setRequired((value.requiredFields ?? []).join(","));
    setMetadataText((value.metadataRules ?? []).map((rule) =>
      `${rule.from}|${rule.pattern}|${rule.key}|${rule.required}`).join("\n"));
    setError(null);
  }, []);

  const save = useCallback(async () => {
    if (!draft) return;
    if (!draft.name.trim()) { setError("请输入配置名称"); return; }
    setError(null);
    try {
      const map: Record<string, string> = {};
      for (const pair of mapping.split(",")) {
        const [target, source] = pair.split("=").map((part) => part?.trim());
        if (target && source) map[target] = source;
      }
      const metadataRules = metadataText.split("\n").map((line) => line.split("|"))
        .filter((parts) => parts.length >= 3 && parts[0].trim() && parts[1].trim() && parts[2].trim())
        .map((parts) => ({ from: parts[0].trim(), pattern: parts[1].trim(),
          key: parts[2].trim(), required: parts[3]?.trim() !== "false" }));
      await ctx.hubCommand("industrial/save-file-profile", {
        id: draft.id, name: draft.name.trim(), fileNamePattern: draft.fileNamePattern,
        format: draft.format, delimiter: draft.delimiter, hasHeader: draft.hasHeader,
        encoding: draft.encoding, enabled: draft.enabled, mapping: map,
        required: required.split(",").map((part) => part.trim()).filter(Boolean),
        directoryPattern: draft.directoryPattern, datePattern: draft.datePattern,
        flat: draft.flat, metadataRules,
      });
      setDraft(null); await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
  }, [ctx, draft, load, mapping, metadataText, required]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Space style={{ width: "100%", justifyContent: "space-between" }}>
        <Typography.Title level={4} style={{ margin: 0 }}>文件格式配置</Typography.Title>
        <Button type="primary" onClick={() => open()}>新建文件配置</Button>
      </Space>
      {error && <Alert type="error" showIcon message={error} />}
      <Table rowKey="id" dataSource={items} pagination={false}
        columns={[
          { title: "名称", dataIndex: "name" },
          { title: "文件匹配", dataIndex: "fileNamePattern" },
          { title: "格式", dataIndex: "format" },
          { title: "版本", dataIndex: "version" },
          { title: "操作", key: "actions", render: (_: unknown, row: ProfileRow) =>
            <Button size="small" onClick={() => open(row)}>编辑</Button> },
        ]} />
      <Modal open={draft !== null} title={draft?.version ? "编辑文件配置" : "新建文件配置"}
        okText="保存配置" cancelText="取消" onCancel={() => setDraft(null)} onOk={() => void save()}>
        {draft && <Space direction="vertical" style={{ width: "100%" }}>
          <Input aria-label="配置名称" placeholder="配置名称" value={draft.name}
            onChange={(e: any) => setDraft({ ...draft, name: e.target.value })} />
          <Input aria-label="文件匹配规则" placeholder="文件匹配，例如 *.csv" value={draft.fileNamePattern}
            onChange={(e: any) => setDraft({ ...draft, fileNamePattern: e.target.value })} />
          <Input aria-label="文件格式" placeholder="csv / excel / json" value={draft.format}
            onChange={(e: any) => setDraft({ ...draft, format: e.target.value })} />
          <Input aria-label="分隔符" placeholder="分隔符" value={draft.delimiter}
            onChange={(e: any) => setDraft({ ...draft, delimiter: e.target.value })} />
          <Input aria-label="编码" placeholder="编码" value={draft.encoding}
            onChange={(e: any) => setDraft({ ...draft, encoding: e.target.value })} />
          <Switch aria-label="首行为表头" checked={draft.hasHeader}
            onChange={(next: boolean) => setDraft({ ...draft, hasHeader: next })} />
          <Input aria-label="字段映射" placeholder="目标字段=来源字段，逗号分隔" value={mapping}
            onChange={(e: any) => setMapping(e.target.value)} />
          <Input aria-label="必填字段" placeholder="必填字段，逗号分隔" value={required}
            onChange={(e: any) => setRequired(e.target.value)} />
          <Input aria-label="日期目录模板" placeholder="YYYY-MM-DD / 留空按递归扫描" value={draft.directoryPattern}
            onChange={(e: any) => setDraft({ ...draft, directoryPattern: e.target.value })} />
          <Input aria-label="日期文件模板" placeholder="yyyyMMdd-*.csv / 留空按文件名解析" value={draft.datePattern}
            onChange={(e: any) => setDraft({ ...draft, datePattern: e.target.value })} />
          <Switch aria-label="单文件模式" checked={draft.flat}
            onChange={(next: boolean) => setDraft({ ...draft, flat: next })} />
          <Input.TextArea aria-label="元数据规则" rows={3}
            placeholder="每行：filename|正则(?P&lt;value&gt;...)|字段名|required"
            value={metadataText} onChange={(e: any) => setMetadataText(e.target.value)} />
        </Space>}
      </Modal>
    </Space>
  );
}
