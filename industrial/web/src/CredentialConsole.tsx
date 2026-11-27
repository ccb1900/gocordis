// Credential references are manageable without ever rendering a stored secret.
// Only a new secret is typed in; the vault keeps ciphertext.

import { Alert, Button, Input, Modal, Select, Space, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { parseCredentials } from "./dto";
import type { CredentialRow } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

const DEFAULT_KINDS = ["username-password", "domain-account", "api-token"];

export function CredentialConsole({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [rows, setRows] = useState<CredentialRow[]>([]);
  const [kinds, setKinds] = useState<string[]>(DEFAULT_KINDS);
  const [open, setOpen] = useState(false);
  const [id, setId] = useState("");
  const [name, setName] = useState("");
  const [kind, setKind] = useState(DEFAULT_KINDS[0]);
  const [username, setUsername] = useState("");
  const [password, setPassword] = useState("");
  const [domain, setDomain] = useState("");
  const [token, setToken] = useState("");
  const [secret, setSecret] = useState("");
  const [rotating, setRotating] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setRows(parseCredentials(await ctx.hubQuery(block?.query ?? "industrial/credentials")));
      const types = await ctx.hubQuery("industrial/credential-types") as any;
      const list = Array.isArray(types) ? types
        : Array.isArray(types?.data) ? types.data : [];
      if (list.length) setKinds(list.map((item: any) => item.kind));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const reset = useCallback(() => {
    setId(""); setName(""); setKind(DEFAULT_KINDS[0]); setUsername(""); setPassword("");
    setDomain(""); setToken(""); setSecret(""); setRotating(false); setError(null);
  }, []);

  const openCreate = useCallback(() => { reset(); setOpen(true); }, [reset]);

  const openRotate = useCallback((row: CredentialRow) => {
    reset();
    setId(row.id); setName(row.name);
    if (kinds.includes(row.kind)) setKind(row.kind);
    setRotating(true); setOpen(true);
  }, [kinds, reset]);

  const composed = useCallback(() => {
    if (secret.trim()) return secret.trim();
    if (kind === "api-token") {
      if (!token.trim()) return null;
      return JSON.stringify({ token: token.trim() });
    }
    if (!username.trim() || !password.trim()) return null;
    return JSON.stringify(kind === "domain-account"
      ? { username: username.trim(), password: password.trim(), domain: domain.trim() }
      : { username: username.trim(), password: password.trim() });
  }, [domain, kind, password, secret, token, username]);

  const save = useCallback(async () => {
    if (!id.trim()) { setError("请输入凭证 ID"); return; }
    const value = composed();
    if (!value) { setError("请填写凭证字段，或在高级内容中提供凭证"); return; }
    setBusy(true); setError(null);
    try {
      if (rotating) {
        await ctx.hubCommand("industrial/rotate-credential", { id, secret: value });
      } else {
        await ctx.hubCommand("industrial/save-credential", { id, name: name || id, kind, secret: value });
      }
      setOpen(false);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [composed, ctx, id, kind, load, name, rotating, secret]);

  const remove = useCallback(async (row: CredentialRow) => {
    setError(null);
    try {
      await ctx.hubCommand("industrial/delete-credential", { id: row.id });
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, load]);

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
              <Space>
                <Button size="small" onClick={() => openRotate(row)}>轮换</Button>
                <Button size="small" danger onClick={() => void remove(row)}>删除</Button>
              </Space>
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
          <Select aria-label="凭证类型" style={{ width: "100%" }} value={kind}
            onChange={(value: string) => setKind(value)}
            options={kinds.map((item) => ({ value: item, label: item }))} />
          {kind === "api-token" ? (
            <Input aria-label="令牌" placeholder="令牌" value={token}
              onChange={(e: any) => setToken(e.target.value)} />
          ) : (
            <>
              <Input aria-label="用户名" placeholder="用户名" value={username}
                onChange={(e: any) => setUsername(e.target.value)} />
              <Input aria-label="密码" placeholder="密码" value={password}
                onChange={(e: any) => setPassword(e.target.value)} />
              {kind === "domain-account" && (
                <Input aria-label="域" placeholder="域（可选）" value={domain}
                  onChange={(e: any) => setDomain(e.target.value)} />
              )}
            </>
          )}
          <Input aria-label="凭证内容" placeholder="高级：直接提供凭证内容（优先）"
            value={secret} onChange={(e: any) => setSecret(e.target.value)} />
          {error && <Typography.Text type="danger">{error}</Typography.Text>}
        </Space>
      </Modal>
    </Space>
  );
}
