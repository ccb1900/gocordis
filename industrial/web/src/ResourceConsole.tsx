// Resource management as a user task: create/edit source and target resources,
// bind a credential, test the configuration, then enable it for workflows.

import { Alert, Button, Input, Modal, Select, Space, Switch, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { parseCredentials, parseResourceTests, parseResources } from "./dto";
import type { CredentialRow, ResourceRow, ResourceTestRow } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
  navigate?: (route: string) => void;
}

interface Draft {
  id: string;
  name: string;
  type: string;
  provider: string;
  root: string;
  host: string;
  database: string;
  port: string;
  table: string;
  credentialId: string;
  enabled: boolean;
  config: Record<string, unknown>;
}

const emptyDraft: Draft = {
  id: "", name: "", type: "filesystem.local", provider: "local",
  root: "", host: "", database: "", port: "", table: "", credentialId: "", enabled: true,
  config: {},
};

const TYPES = [
  { value: "filesystem.local", label: "本地目录" },
  { value: "filesystem.unc", label: "Windows UNC 共享" },
  { value: "sqlserver", label: "SQL Server" },
  { value: "postgresql", label: "PostgreSQL" },
  { value: "mysql", label: "MySQL" },
  { value: "sqlite", label: "SQLite" },
];

// The provider is a function of the resource type, never a separate hidden
// choice that can drift out of sync with the connection shape.
const PROVIDER: Record<string, string> = {
  "filesystem.local": "local",
  "filesystem.unc": "local",
  sqlserver: "sqlserver",
  postgresql: "postgresql",
  mysql: "mysql",
  sqlite: "sqlite",
};

// Suggested values are visible in the form (as real input values for a new
// resource), not injected silently into the stored configuration.
const DEFAULTS: Record<string, Partial<Draft>> = {
  sqlserver: { table: "IndustrialRecords" },
  postgresql: { port: "5432" },
  mysql: { port: "3306" },
};

function configOf(draft: Draft): Record<string, unknown> {
  // Include every field of the selected type, including empty inputs: the
  // backend deletes those keys. Keys of another type are retired server-side
  // when the type changes, so switching a resource cannot leak stale fields.
  const config: Record<string, unknown> = { ...draft.config };
  const put = (key: string, value: string) => { config[key] = value.trim(); };
  switch (draft.type) {
    case "filesystem.local":
    case "filesystem.unc":
      put("root", draft.root);
      break;
    case "sqlserver":
      put("server", draft.host);
      put("host", draft.host);
      put("database", draft.database);
      put("table", draft.table);
      break;
    case "postgresql":
    case "mysql":
      put("host", draft.host);
      put("database", draft.database);
      put("port", draft.port);
      break;
    case "sqlite":
      put("database", draft.database);
      break;
  }
  return config;
}

export function ResourceConsole({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [rows, setRows] = useState<ResourceRow[]>([]);
  const [credentials, setCredentials] = useState<CredentialRow[]>([]);
  const [tests, setTests] = useState<ResourceTestRow[]>([]);
  const [draft, setDraft] = useState<Draft>(emptyDraft);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const [resources, credentialRows, testRows] = await Promise.all([
        ctx.hubQuery(block?.query ?? "industrial/resources"),
        ctx.hubQuery("industrial/credentials"),
        ctx.hubQuery("industrial/resource-tests"),
      ]);
      setRows(parseResources(resources));
      setCredentials(parseCredentials(credentialRows));
      setTests(parseResourceTests(testRows));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const edit = useCallback((row: ResourceRow) => {
    const value = (...keys: string[]) => {
      for (const key of keys) {
        const raw = row.config[key];
        if (raw !== undefined && raw !== null && String(raw).trim()) return String(raw);
      }
      return "";
    };
    setDraft({
      id: row.id,
      name: row.name,
      type: row.type,
      provider: PROVIDER[row.type] ?? row.provider,
      root: value("root", "path"),
      host: value("server", "host", "dataSource", "data_source"),
      database: value("database", "initialCatalog", "initial_catalog", "path"),
      port: value("port"),
      table: value("table"),
      credentialId: row.credentialId ?? "",
      enabled: row.enabled,
      config: row.config,
    });
    setOpen(true);
  }, []);

  const save = useCallback(async () => {
    const id = draft.id.trim();
    if (!id) { setError("请输入资源 ID"); return; }
    setBusy(true);
    setError(null);
    try {
      await ctx.hubCommand("industrial/save-resource", {
        id,
        name: draft.name || id,
        type: draft.type,
        provider: PROVIDER[draft.type] ?? "manual",
        config: configOf(draft),
        // An empty Select is an explicit unbind: null is sent, not omitted.
        credentialId: draft.credentialId || null,
        enabled: draft.enabled,
      });
      // A saved connection is untested until the user tests it again.
      setTests((current) => current.filter((test) => test.resourceId !== id));
      setOpen(false);
      setDraft(emptyDraft);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [ctx, draft, load]);

  const test = useCallback(async (id: string) => {
    setError(null);
    try {
      await ctx.hubCommand("industrial/test-resource", { id });
    } catch (cause) {
      // The server records the classified failure in industrial/resource-tests.
      setError(cause instanceof Error ? cause.message : String(cause));
    }
    await load();
  }, [ctx, load]);

  const remove = useCallback(async (id: string) => {
    setError(null);
    try {
      await ctx.hubCommand("industrial/delete-resource", { id });
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, load]);

  const testsById = new Map<string, ResourceTestRow>(tests.map((test) => [test.resourceId, test]));

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Space style={{ width: "100%", justifyContent: "space-between" }}>
        <Typography.Title level={4} style={{ margin: 0 }}>数据源与目标</Typography.Title>
        <Space>
          <Button onClick={() => ctx.navigate?.("/industrial/designer")}>返回设计器</Button>
          <Button type="primary" onClick={() => {
            const defaults = DEFAULTS[emptyDraft.type] ?? {};
            setDraft({ ...emptyDraft, ...defaults, provider: PROVIDER[emptyDraft.type] ?? "manual" });
            setOpen(true);
          }}>新建资源</Button>
        </Space>
      </Space>
      {error && <Alert type="error" showIcon message={error} />}
      <Table
        rowKey="id"
        dataSource={rows}
        pagination={false}
        columns={[
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          { title: "凭证", dataIndex: "credentialName", render: (v: string | null) => v ?? "—" },
          {
            title: "连接测试",
            key: "test",
            render: (_: unknown, row: ResourceRow) => {
              const result = testsById.get(row.id);
              return result
                ? <Typography.Text type={result.ok ? "success" : "danger"}>{result.detail}</Typography.Text>
                : "未测试";
            },
          },
          {
            title: "操作",
            key: "actions",
            render: (_: unknown, row: ResourceRow) => (
              <Space size={4}>
                <Button size="small" onClick={() => void test(row.id)}>测试连接</Button>
                <Button size="small" onClick={() => edit(row)}>编辑</Button>
                <Button size="small" danger onClick={() => void remove(row.id)}>删除</Button>
              </Space>
            ),
          },
        ]}
      />
      <Modal
        open={open}
        title={draft.id ? "编辑资源" : "新建资源"}
        okText="保存资源"
        cancelText="取消"
        confirmLoading={busy}
        onCancel={() => setOpen(false)}
        onOk={() => void save()}
      >
        <Space direction="vertical" style={{ width: "100%" }}>
          <Input aria-label="资源 ID" placeholder="资源 ID（唯一）" value={draft.id}
            onChange={(e: any) => setDraft({ ...draft, id: e.target.value })} />
          <Input aria-label="资源名称" placeholder="显示名称" value={draft.name}
            onChange={(e: any) => setDraft({ ...draft, name: e.target.value })} />
          <Typography.Text>资源类型</Typography.Text>
          <Space wrap>
            {TYPES.map((type) => (
              <Button key={type.value} size="small"
                type={draft.type === type.value ? "primary" : "default"}
                onClick={() => {
                  const defaults = DEFAULTS[type.value] ?? {};
                  // Show suggested fields for the new type; stale fields of
                  // the previous type are retired by the backend on save.
                  setDraft({ ...draft, type: type.value, provider: PROVIDER[type.value] ?? "manual",
                    root: "", host: "", database: "", port: "", table: "", ...defaults });
                }}>
                {type.label}
              </Button>
            ))}
          </Space>
          {(draft.type === "filesystem.local" || draft.type === "filesystem.unc") && (
            <Input aria-label="目录路径" placeholder={draft.type === "filesystem.unc" ? "\\\\server\\share" : "/data/csv"}
              value={draft.root} onChange={(e: any) => setDraft({ ...draft, root: e.target.value })} />
          )}
          {(draft.type === "sqlserver" || draft.type === "postgresql" || draft.type === "mysql") && (
            <Space>
              <Input aria-label="主机或服务器" placeholder="主机 / 服务器" value={draft.host}
                onChange={(e: any) => setDraft({ ...draft, host: e.target.value })} />
              <Input aria-label="数据库" placeholder="数据库" value={draft.database}
                onChange={(e: any) => setDraft({ ...draft, database: e.target.value })} />
              <Input aria-label="端口" placeholder={draft.type === "mysql" ? "默认 3306" : "默认 5432"}
                value={draft.port} onChange={(e: any) => setDraft({ ...draft, port: e.target.value })} />
            </Space>
          )}
          {draft.type === "sqlite" && (
            <Input aria-label="数据库文件" placeholder="数据库文件路径" value={draft.database}
              onChange={(e: any) => setDraft({ ...draft, database: e.target.value })} />
          )}
          <Select
            aria-label="凭证"
            allowClear
            placeholder="凭证（可选）"
            value={draft.credentialId || undefined}
            options={credentials.map((credential: CredentialRow) => ({ value: credential.id, label: credential.name }))}
            onChange={(value: string | undefined) => setDraft({ ...draft, credentialId: value ?? "" })}
          />
          <Space>
            <Switch aria-label="启用资源" checked={draft.enabled}
              onChange={(value: boolean) => setDraft({ ...draft, enabled: value })} />
            <Typography.Text>启用</Typography.Text>
          </Space>
        </Space>
      </Modal>
    </Space>
  );
}
