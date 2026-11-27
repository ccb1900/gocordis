// Machine management as a user task: register/edit/enable/delete machines,
// bind credentials, and bind the collection resources that belong to them.
// Backend owns the page; this block only renders it through the console facade.

import { Alert, Button, Input, Modal, Select, Space, Switch, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { unwrapData } from "./dto";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

interface MachineRow {
  id: string;
  name: string;
  endpoint: string;
  credentialId: string | null;
  enabled: boolean;
}

interface ResourceRow {
  id: string;
  name: string;
  type: string;
  machineId: string | null;
}

interface CredentialRow { id: string; name: string; kind: string; }

const rows = (raw: unknown): any[] => {
  const data = unwrapData(raw) as unknown;
  return Array.isArray(data) ? data : [];
};

const newId = () => `machine-${typeof crypto !== "undefined" && "randomUUID" in crypto
  ? crypto.randomUUID().replace(/-/g, "").slice(0, 12)
  : Date.now().toString(36)}`;

export function MachineConsole({ block, ctx }: {
  block?: { query?: string; resourcesQuery?: string }; ctx: Context;
}) {
  const [machines, setMachines] = useState<MachineRow[]>([]);
  const [resources, setResources] = useState<ResourceRow[]>([]);
  const [credentials, setCredentials] = useState<CredentialRow[]>([]);
  const [editing, setEditing] = useState<MachineRow | null>(null);
  const [name, setName] = useState("");
  const [endpoint, setEndpoint] = useState("");
  const [credentialId, setCredentialId] = useState<string | undefined>();
  const [enabled, setEnabled] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setMachines(rows(await ctx.hubQuery(block?.query ?? "industrial/machines")) as MachineRow[]);
      // Binding UI needs ALL resources; machine-resources only lists already bound ones.
      setResources(rows(await ctx.hubQuery("industrial/resources")) as ResourceRow[]);
      setCredentials(rows(await ctx.hubQuery("industrial/credentials")) as CredentialRow[]);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const openCreate = useCallback(() => {
    setEditing({ id: newId(), name: "", endpoint: "", credentialId: null, enabled: true });
    setName(""); setEndpoint(""); setCredentialId(undefined); setEnabled(true); setError(null);
  }, []);

  const openEdit = useCallback((machine: MachineRow) => {
    setEditing(machine);
    setName(machine.name);
    setEndpoint(machine.endpoint);
    setCredentialId(machine.credentialId ?? undefined);
    setEnabled(machine.enabled);
    setError(null);
  }, []);

  const save = useCallback(async () => {
    if (!editing) return;
    if (!name.trim()) { setError("请输入设备名称"); return; }
    setError(null);
    try {
      await ctx.hubCommand("industrial/save-machine", {
        id: editing.id, name: name.trim(), endpoint: endpoint.trim(),
        credentialId: credentialId ?? null, enabled,
      });
      setEditing(null);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, credentialId, editing, endpoint, enabled, load, name]);

  const remove = useCallback(async (machine: MachineRow) => {
    setError(null);
    try {
      await ctx.hubCommand("industrial/delete-machine", { id: machine.id });
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, load]);

  const toggle = useCallback(async (machine: MachineRow, next: boolean) => {
    setError(null);
    try {
      await ctx.hubCommand("industrial/save-machine", {
        id: machine.id, name: machine.name, endpoint: machine.endpoint,
        credentialId: machine.credentialId, enabled: next,
      });
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, load]);

  const bind = useCallback(async (resource: ResourceRow, machineId: string) => {
    setError(null);
    try {
      await ctx.hubCommand("industrial/save-resource", {
        id: resource.id, machineId: machineId || null,
      });
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, load]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Space style={{ width: "100%", justifyContent: "space-between" }}>
        <Typography.Title level={4} style={{ margin: 0 }}>设备与资源</Typography.Title>
        <Button type="primary" onClick={openCreate}>新增设备</Button>
      </Space>
      <Typography.Text type="secondary">
        设备地址是台账属性；实际连接由数据源资源配置决定。绑定凭证供需要认证的数据源引用。
      </Typography.Text>
      {error && <Alert type="error" showIcon message={error} />}
      <Table rowKey="id" dataSource={machines} pagination={false}
        columns={[
          { title: "设备", dataIndex: "name" },
          { title: "台账地址", dataIndex: "endpoint" },
          { title: "凭证", dataIndex: "credentialId",
            render: (value: string | null) => credentials.find((item) => item.id === value)?.name ?? "—" },
          { title: "启用", dataIndex: "enabled",
            render: (value: boolean, row: MachineRow) =>
              <Switch aria-label={`设备 ${row.name} 启用`} checked={value}
                onChange={(next: boolean) => void toggle(row, next)} /> },
          { title: "操作", key: "actions", render: (_: unknown, row: MachineRow) =>
            <Space>
              <Button size="small" onClick={() => openEdit(row)}>编辑</Button>
              <Button size="small" danger onClick={() => void remove(row)}>删除</Button>
            </Space> },
        ]} />
      <Typography.Title level={5} style={{ margin: 0 }}>资源归属</Typography.Title>
      <Table rowKey="id" dataSource={resources} pagination={false}
        columns={[
          { title: "资源", dataIndex: "name" },
          { title: "类型", dataIndex: "type" },
          {
            title: "所属设备", key: "machineId",
            render: (_: unknown, row: ResourceRow) => (
              <Select aria-label={`资源 ${row.name} 的设备`} style={{ width: 220 }}
                value={row.machineId ?? undefined} allowClear
                onChange={(machineId: string | undefined) => void bind(row, machineId ?? "")}
                options={machines.map((machine) => ({ value: machine.id, label: machine.name }))} />
            ),
          },
        ]} />
      <Modal open={editing !== null} title={editing?.name ? "编辑设备" : "新增设备"}
        okText="保存设备" cancelText="取消"
        onCancel={() => setEditing(null)} onOk={() => void save()}>
        <Space direction="vertical" style={{ width: "100%" }}>
          <Input aria-label="设备名称" placeholder="设备名称" value={name}
            onChange={(event: { target: { value: string } }) => setName(event.target.value)} />
          <Input aria-label="设备地址" placeholder="台账地址 / IP" value={endpoint}
            onChange={(event: { target: { value: string } }) => setEndpoint(event.target.value)} />
          <Select aria-label="设备凭证" style={{ width: "100%" }} allowClear
            placeholder="凭证（可选）" value={credentialId}
            onChange={(value: string | undefined) => setCredentialId(value)}
            options={credentials.map((credential) => ({
              value: credential.id, label: `${credential.name} (${credential.kind})`,
            }))} />
        </Space>
      </Modal>
    </Space>
  );
}
