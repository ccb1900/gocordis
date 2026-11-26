// Per-file processing detail: what happened to each discovered file and each
// target delivery, with a user-actionable diagnosis instead of raw exceptions.

import { Alert, Space, Table, Tag, Typography } from "./facade-antd";
import { useCallback, useEffect, useState } from "react";
import { parseFileDetails } from "./dto";
import type { FileDeliveryDetail, FileDetailRow } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
}

export function FileDetails({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [rows, setRows] = useState<FileDetailRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setRows(parseFileDetails(await ctx.hubQuery(block?.query ?? "industrial/file-details")));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, [block?.query, ctx]);

  useEffect(() => { void load(); }, [load]);

  const expanded = (row: FileDetailRow) => (
    <Table
      rowKey="id"
      dataSource={row.deliveries}
      pagination={false}
      size="small"
      columns={[
        { title: "目标", dataIndex: "targetId" },
        { title: "状态", dataIndex: "status", render: (v: string) => <Tag>{v}</Tag> },
        { title: "记录", dataIndex: "records" },
        { title: "接受/拒绝/警告", key: "counts",
          render: (_: unknown, d: FileDeliveryDetail) =>
            `${d.accepted} / ${d.rejected} / ${d.warnings}` },
        {
          title: "原因与下一步", key: "diagnosis",
          render: (_: unknown, d: FileDeliveryDetail) => d.diagnosis ? (
            <Space direction="vertical" size={0}>
              <Typography.Text>{d.diagnosis.message}</Typography.Text>
              <Typography.Text type="secondary">{d.diagnosis.action}</Typography.Text>
              {d.error && <Typography.Text type="secondary">{d.error}</Typography.Text>}
            </Space>
          ) : (d.error ?? "—"),
        },
      ]}
    />
  );

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Typography.Title level={4} style={{ margin: 0 }}>文件处理详情</Typography.Title>
      {error && <Alert type="error" showIcon message={error} />}
      <Table rowKey="id" dataSource={rows} pagination={false}
        expandable={{ expandedRowRender: expanded }}
        columns={[
          { title: "文件", dataIndex: "path" },
          { title: "源日期", dataIndex: "sourceDate" },
          { title: "状态", dataIndex: "status" },
          { title: "大小", dataIndex: "length", render: (v: number) => `${v} B` },
          { title: "目标数", key: "targets",
            render: (_: unknown, row: FileDetailRow) => row.deliveries.length },
        ]}
      />
    </Space>
  );
}
