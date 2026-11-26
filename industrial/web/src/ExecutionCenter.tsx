// Execution history from the ledger, not from in-memory _lastExecution. Runs,
// files and per-target deliveries answer the questions users actually ask.

import { Alert, Button, Space, Table, Tag, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { parseExecutionCenter } from "./dto";
import type { DeliveryRow, ExecutionRunRow } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

const statusColor = (status: string) => status === "Succeeded" || status === "Completed"
  ? "success"
  : status === "Failed" || status === "PartialFailure" ? "error"
    : status === "Running" || status === "Interrupted" ? "processing" : "default";

export function ExecutionCenter({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [runs, setRuns] = useState<ExecutionRunRow[]>([]);
  const [deliveries, setDeliveries] = useState<DeliveryRow[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      const view = parseExecutionCenter(await ctx.hubQuery(block?.query ?? "industrial/execution-center"));
      setRuns(view.runs);
      setDeliveries(view.deliveries);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const retry = useCallback(async (row: DeliveryRow) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await ctx.hubCommand("industrial/retry-failed", {
        deliveryId: row.id, targetId: row.targetId,
      });
      setNotice(`已提交目标 ${row.targetId} 的重试`);
      await load();
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [ctx, load]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Typography.Title level={4} style={{ margin: 0 }}>执行记录</Typography.Title>
      {error && <Alert type="error" showIcon message={error} />}
      {notice && <Alert type="success" showIcon message={notice} />}
      <Table
        rowKey="id"
        dataSource={runs}
        pagination={false}
        title={() => "采集运行"}
        columns={[
          { title: "工作流", dataIndex: "workflowId" },
          { title: "状态", dataIndex: "status", render: (v: string) => <Tag color={statusColor(v)}>{v}</Tag> },
          { title: "开始", dataIndex: "startedAt" },
          { title: "结束", dataIndex: "finishedAt", render: (v: string | null) => v ?? "—" },
          { title: "错误", dataIndex: "error", render: (v: string | null) => v ?? "—" },
        ]}
      />
      <Table
        rowKey="id"
        dataSource={deliveries}
        pagination={false}
        title={() => "交付目标"}
        columns={[
          { title: "目标", dataIndex: "targetId" },
          { title: "状态", dataIndex: "status", render: (v: string) => <Tag color={statusColor(v)}>{v}</Tag> },
          { title: "记录数", dataIndex: "records" },
          { title: "尝试次数", dataIndex: "attempts" },
          {
            title: "原因与下一步",
            key: "diagnosis",
            render: (_: unknown, row: DeliveryRow) => row.diagnosis ? (
              <Space direction="vertical" size={0}>
                <Typography.Text>{row.diagnosis.message}</Typography.Text>
                <Typography.Text type="secondary">{row.diagnosis.action}</Typography.Text>
              </Space>
            ) : (row.error ?? "—"),
          },
          {
            title: "操作",
            key: "actions",
            render: (_: unknown, row: DeliveryRow) => (
              <Button
                size="small"
                disabled={busy || (row.status !== "Failed" && row.status !== "Interrupted")}
                onClick={() => void retry(row)}
              >
                重试此目标
              </Button>
            ),
          },
        ]}
      />
    </Space>
  );
}
