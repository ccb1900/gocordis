// Credential references are manageable without ever rendering a stored secret.
// Only a new secret is typed in; the vault keeps ciphertext.

import { Alert, Button, Input, Modal, Space, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { parseCredentials } from "./dto";
import type { CredentialRow } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

export function CredentialConsole({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [rows, setRows] = useState<CredentialRow[]>([]);
  const [open, setOpen] = useState(false);
  const [id, setId] = useState("");
  const [name, setName] = useState("");
  const [kind, setKind] = useState("username-password");
  const [secret, setSecret] = useState("");
  const [rotating, setRotating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setRows(parseCredentials(await ctx.hubQuery(block?.query ?? "industrial/credentials")));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const openCreate = useCallback(() => {
    setId(""); setName(""); setKind("username-password"); setSecret(""); setRotating(false); setOpen(true);
  }, []);

  const openRotate = useCallback((row: CredentialRow) => {
    setId(row.id); setName(row.name); setKind(row.kind); setSecret(""); setRotating(true); setOpen(true);
  }, []);

  const save = useCallback(async () => {
    if (!id.trim()) { setError("请输入凭证 ID"); return; }
    if (!secret.trim()) { setError("请输入凭证内容"); return; }
    setBusy(true);
    setError(null);
    try {
      if (rotating) {
        await ctx.hubCommand("industrial/rotate-credential", { id, secret });
      } else {
        await ctx.hubCommand("industrial/save-credential", { id, name: name || id, kind, secret });
      }
      setOpen(false);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [ctx, id, kind, load, name, rotating, secret]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Space style={{ width: "100%", justifyContent: "space-between" }}>
        <Typography.Title level={4} style={{ margin: 0 }}>凭证</Typography.Title>
        <Button type="primary" onClick={openCreate}>新建凭证</Button>
      </Space>
      {error && <Alert type="error" showIcon message={error} />}
      <Table
        rowKey="id"
        dataSource={rows}
        pagination={false}
        columns={[
          { title: "ID", dataIndex: "id" },
          { title: "名称", dataIndex: "name" },
          { title: "类型", dataIndex: "kind" },
          { title: "版本", dataIndex: "version" },
          {
            title: "操作",
            key: "actions",
            render: (_: unknown, row: CredentialRow) => (
              <Button size="small" onClick={() => openRotate(row)}>轮换</Button>
            ),
          },
        ]}
      />
      <Modal
        open={open}
        title={rotating ? "轮换凭证" : "新建凭证"}
        okText={rotating ? "轮换" : "保存凭证"}
        cancelText="取消"
        confirmLoading={busy}
        onCancel={() => setOpen(false)}
        onOk={() => void save()}
      >
        <Space direction="vertical" style={{ width: "100%" }}>
          <Input aria-label="凭证 ID" placeholder="凭证 ID" value={id} disabled={rotating}
            onChange={(e: any) => setId(e.target.value)} />
          {!rotating && (
            <Input aria-label="凭证名称" placeholder="显示名称" value={name}
              onChange={(e: any) => setName(e.target.value)} />
          )}
          <Input aria-label="凭证内容" placeholder={rotating ? "新凭证内容" : "凭证内容"}
            value={secret} onChange={(e: any) => setSecret(e.target.value)} />
          {error && <Typography.Text type="danger">{error}</Typography.Text>}
        </Space>
      </Modal>
    </Space>
  );
}
