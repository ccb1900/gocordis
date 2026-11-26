// User-owned workflow creation. The page and query stay backend contributions;
// this client-module block only renders the list and the create action.

import { Button, Input, Modal, Space, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { parseWorkflowSummaries } from "./dto";
import { emptyWorkflow } from "./workflow-model";
import type { WorkflowSummary } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
  navigate?: (route: string) => void;
}

export function WorkflowList({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [items, setItems] = useState<WorkflowSummary[]>([]);
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setItems(parseWorkflowSummaries(await ctx.hubQuery(block?.query ?? "industrial/workflows")));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const create = useCallback(async () => {
    const trimmed = name.trim();
    if (!trimmed) {
      setError("请输入工作流名称");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const suffix = typeof crypto !== "undefined" && "randomUUID" in crypto
        ? crypto.randomUUID().replace(/-/g, "").slice(0, 10)
        : Date.now().toString(36);
      const workflowId = `workflow-${suffix}`;
      await ctx.hubCommand("industrial/save-workflow", {
        ...emptyWorkflow(workflowId),
        name: trimmed,
        layout: { lookbackDays: 1 },
      });
      setOpen(false);
      setName("");
      await load();
      ctx.navigate?.(`/industrial/designer?workflowId=${encodeURIComponent(workflowId)}`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [ctx, load, name]);

  const openWorkflow = useCallback((id: string) => {
    ctx.navigate?.(`/industrial/designer?workflowId=${encodeURIComponent(id)}`);
  }, [ctx]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Space style={{ width: "100%", justifyContent: "space-between" }}>
        <Typography.Title level={4} style={{ margin: 0 }}>采集工作流</Typography.Title>
        <Button type="primary" onClick={() => setOpen(true)}>新建工作流</Button>
      </Space>
      {error && <Typography.Text type="danger">{error}</Typography.Text>}
      <Table
        rowKey="workflowId"
        dataSource={items}
        pagination={false}
        columns={[
          {
            title: "ID",
            dataIndex: "workflowId",
            render: (value: string) => (
              <Button type="link" style={{ padding: 0 }} onClick={() => openWorkflow(value)}>{value}</Button>
            ),
          },
          { title: "名称", dataIndex: "name" },
          { title: "版本", dataIndex: "version" },
        ]}
      />
      <Modal
        open={open}
        title="新建采集工作流"
        okText="创建"
        cancelText="取消"
        confirmLoading={busy}
        onCancel={() => setOpen(false)}
        onOk={() => void create()}
      >
        <Space direction="vertical" style={{ width: "100%" }}>
          <Typography.Text>输入名称后会生成唯一 ID，并直接进入设计器。</Typography.Text>
          <Input
            aria-label="工作流名称"
            placeholder="例如：每日设备数据采集"
            value={name}
            onChange={(event: any) => setName(event.target.value ?? "")}
          />
          {error && <Typography.Text type="danger">{error}</Typography.Text>}
        </Space>
      </Modal>
    </Space>
  );
}
