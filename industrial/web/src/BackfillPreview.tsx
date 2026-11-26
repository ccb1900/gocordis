// Read-only backfill preview: answer what would be scanned, what is already
// complete, what still needs collection and where duplicate delivery could be
// possible — before the user commits a run.

import { Alert, Button, Modal, Space, Table, Typography } from "./facade-antd";
import { useCallback, useState } from "react";
import { parseBackfillPreview } from "./dto";
import type { BackfillPreviewRow, BackfillPreviewView } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

export function BackfillPreview({
  open, onClose, ctx, workflowId, resourceId, lookbackDays,
}: {
  open: boolean;
  onClose: () => void;
  ctx: Context;
  workflowId: string;
  resourceId: string;
  lookbackDays: number;
}) {
  const [preview, setPreview] = useState<BackfillPreviewView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const refresh = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      await ctx.hubCommand("industrial/preview-backfill", {
        workflowId, resourceId, lookbackDays,
      });
      setPreview(parseBackfillPreview(await ctx.hubQuery("industrial/backfill-preview")));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [ctx, lookbackDays, resourceId, workflowId]);

  return (
    <Modal open={open} title="补采预览" onCancel={onClose} width={820}
      footer={[
        <Button key="refresh" loading={busy} onClick={() => void refresh()}>重新扫描</Button>,
        <Button key="close" type="primary" onClick={onClose}>关闭</Button>,
      ]}
    >
      <Space direction="vertical" size={12} style={{ width: "100%" }}>
        <Space>
          <Button type="primary" loading={busy} onClick={() => void refresh()}>扫描最近 {lookbackDays} 天</Button>
          <Typography.Text type="secondary">资源：{resourceId || "未选择"}</Typography.Text>
        </Space>
        {error && <Alert type="error" showIcon message={error} />}
        {preview && (
          <>
            <Typography.Text>
              发现 {preview.summary.discovered} 个文件；已完成 {preview.summary.alreadyComplete}；
              待采集 {preview.summary.needsCollection}；重复风险 {preview.summary.duplicateRisk}。
            </Typography.Text>
            <Table rowKey="path" dataSource={preview.files} pagination={false} size="small"
              columns={[
                { title: "文件", dataIndex: "path" },
                { title: "源日期", dataIndex: "sourceDate" },
                { title: "账本", dataIndex: "inLedger", render: (v: boolean) => v ? "已登记" : "新文件" },
                { title: "当前目标", dataIndex: "expectedTargets" },
                { title: "成功目标", dataIndex: "succeededTargets" },
                { title: "待补目标", dataIndex: "missingTargets", render: (v: string) => v || "—" },
                { title: "未完成目标", dataIndex: "incompleteTargets" },
                { title: "预计动作",
                  render: (_: unknown, row: BackfillPreviewRow) => row.wouldSkip
                    ? "跳过" : row.duplicateRisk ? "仅补缺失目标" : "采集" },
              ]}
            />
          </>
        )}
      </Space>
    </Modal>
  );
}
