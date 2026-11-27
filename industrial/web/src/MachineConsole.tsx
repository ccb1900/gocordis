// Machine management as a user task: register a machine and bind the
// collection resources that belong to it. Backend owns the page; this block
// only renders it through the console facade.

import { Alert, Button, Input, Select, Space, Table, Typography } from "./facade-antd";
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
  enabled: boolean;
}

interface ResourceRow {
  id: string;
  name: string;
  type: string;
  machineId: string | null;
}

const rows = (raw: unknown): any[] => {
  const data = unwrapData(raw) as unknown;
  return Array.isArray(data) ? data : [];
};

export function MachineConsole({ block, ctx }: {
  block?: { query?: string; resourcesQuery?: string }; ctx: Context;
}) {
  const [machines, setMachines] = useState<MachineRow[]>([]);
  const [resources, setResources] = useState<ResourceRow[]>([]);
  const [name, setName] = useState("");
  const [endpoint, setEndpoint] = useState("");
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setMachines(rows(await ctx.hubQuery(block?.query ?? "industrial/machines")) as MachineRow[]);
      const all = rows(await ctx.hubQuery("industrial/resources")) as ResourceRow[];
      setResources(all);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const save = useCallback(async () => {
    if (!name.trim()) { setError("请输入设备名称"); return; }
    setError(null);
    try {
      await ctx.hubCommand("industrial/save-machine", {
        id: `machine-${name.trim().toLowerCase().replace(/[^a-z0-9]+/g, "-")}`,
        name: name.trim(), endpoint: endpoint.trim(), enabled: true,
      });
      setName(""); setEndpoint(""); await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [ctx, endpoint, load, name]);

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
      <Typography.Title level={4} style={{ margin: 0 }}>设备与资源</Typography.Title>
      {error && <Alert type="error" showIcon message={error} />}
      <Space wrap>
        <Input aria-label="设备名称" placeholder="设备名称" value={name}
          onChange={(event: { target: { value: string } }) => setName(event.target.value)} />
        <Input aria-label="设备地址" placeholder="地址 / IP" value={endpoint}
          onChange={(event: { target: { value: string } }) => setEndpoint(event.target.value)} />
        <Button type="primary" onClick={() => void save()}>新增设备</Button>
      </Space>
      <Table rowKey="id" dataSource={machines} pagination={false}
        columns={[
          { title: "设备", dataIndex: "name" },
          { title: "地址", dataIndex: "endpoint" },
          { title: "状态", dataIndex: "enabled", render: (value: boolean) => value ? "启用" : "停用" },
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
    </Space>
  );
}
