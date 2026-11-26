// Trigger configuration as a user task: choose manual/schedule/event, enter a
// standard 5-field cron or event name, bind the source resource, and see the
// next run plus last result. No scheduler internals leak into the UI.

import { Alert, Button, Input, Modal, Select, Space, Table, Typography } from "./facade-antd";
import { useEffect, useState } from "react";
import { parseTriggerStatus } from "./dto";
import type { TriggerDefinition, TriggerStatusRow } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
}

const KINDS = [
  { value: "manual", label: "手动运行" },
  { value: "schedule", label: "定时运行" },
  { value: "event", label: "事件触发" },
];

const PRESETS = [
  { label: "每天 02:00", cron: "0 2 * * *" },
  { label: "每小时", cron: "0 * * * *" },
  { label: "每 5 分钟", cron: "*/5 * * * *" },
  { label: "工作日 08:30", cron: "30 8 * * 1-5" },
];

export function TriggerEditor({
  open, onClose, trigger, resourceIds, onSave, ctx, workflowId,
}: {
  open: boolean;
  onClose: () => void;
  trigger: TriggerDefinition;
  resourceIds: string[];
  onSave: (trigger: TriggerDefinition) => void;
  ctx: Context;
  workflowId: string;
}) {
  const [draft, setDraft] = useState<TriggerDefinition>(trigger);
  const [status, setStatus] = useState<TriggerStatusRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => { setDraft(trigger); }, [trigger]);
  useEffect(() => {
    if (!open) return;
    ctx.hubQuery("industrial/trigger-status")
      .then((raw) => setStatus(parseTriggerStatus(raw)))
      .catch((cause) => setError(cause instanceof Error ? cause.message : String(cause)));
  }, [ctx, open]);

  const current = status.find((row) => row.workflowId === workflowId);
  const save = () => {
    if (draft.kind === "schedule" && !draft.cron?.trim()) {
      setError("定时运行需要填写 cron 表达式");
      return;
    }
    if (draft.kind === "event" && !draft.eventName?.trim()) {
      setError("事件触发需要填写事件名称");
      return;
    }
    setError(null);
    onSave(draft);
    onClose();
  };

  return (
    <Modal open={open} title="触发器配置" okText="保存触发器" cancelText="取消"
      onCancel={onClose} onOk={save} width={720}>
      <Space direction="vertical" size={12} style={{ width: "100%" }}>
        <Space direction="vertical" style={{ width: "100%" }}>
          <Typography.Text>触发方式</Typography.Text>
          <Space wrap>
            {KINDS.map((kind) => (
              <Button key={kind.value} type={draft.kind === kind.value ? "primary" : "default"}
                onClick={() => setDraft({ ...draft, kind: kind.value })}>
                {kind.label}
              </Button>
            ))}
          </Space>
        </Space>
        <Space wrap>
          <Select aria-label="运行资源" allowClear placeholder="运行资源"
            value={triggerResource(draft)}
            options={resourceIds.map((id) => ({ value: id, label: id }))}
            onChange={(value: string | undefined) => setDraft({
              ...draft,
              config: { ...draft.config, resourceId: value ?? "" },
            })} />
        </Space>
        <Space direction="vertical" style={{ width: "100%" }}>
          <Typography.Text type="secondary">Cron 与事件名称始终可编辑；只有所选触发方式会生效。</Typography.Text>
          <Space wrap>
            <Typography.Text>常用</Typography.Text>
            {PRESETS.map((preset) => (
              <Button key={preset.cron} size="small"
                onClick={() => setDraft({ ...draft, cron: preset.cron })}>
                {preset.label}
              </Button>
            ))}
          </Space>
          <Input aria-label="Cron 表达式" placeholder="分 时 日 月 周，例如 0 2 * * *"
            value={draft.cron ?? ""}
            onChange={(event: any) => setDraft({ ...draft, cron: event.target.value })} />
          <Input aria-label="调度时区" placeholder="时区（默认 UTC，例如 Asia/Shanghai）"
            value={triggerTimezone(draft)}
            onChange={(event: any) => setDraft({
              ...draft,
              config: { ...draft.config, timezone: event.target.value },
            })} />
          <Input aria-label="事件名称" placeholder="事件名称"
            value={draft.eventName ?? ""}
            onChange={(event: any) => setDraft({ ...draft, eventName: event.target.value })} />
          <Typography.Text type="secondary">
            下次运行：{current?.nextRun ?? "尚未发布定时触发器"}
          </Typography.Text>
        </Space>
        {current && (
          <Table rowKey="workflowId" dataSource={[current]} pagination={false}
            columns={[
              { title: "时区", dataIndex: "timezone", render: (v: string | null) => v ?? "UTC" },
              { title: "状态", dataIndex: "lastStatus", render: (v: string | null) => v ?? "未运行" },
              { title: "上次运行", dataIndex: "lastStartedAt", render: (v: string | null) => v ?? "—" },
              { title: "触发点", dataIndex: "lastFireStatus",
                render: (_: string | null, row: TriggerStatusRow) =>
                  row.lastFireStatus
                    ? `${row.lastFireStatus} · ${row.lastFireDueAt ?? "—"} · ${row.lastFireAttempts ?? 0} 次`
                    : "—" },
              { title: "错误", dataIndex: "lastError", render: (v: string | null) => v ?? "—" },
            ]}
          />
        )}
        {error && <Alert type="error" showIcon message={error} />}
      </Space>
    </Modal>
  );
}

function triggerResource(trigger: TriggerDefinition): string | undefined {
  const value = trigger.config.resourceId;
  return typeof value === "string" && value.trim() ? value : undefined;
}

function triggerTimezone(trigger: TriggerDefinition): string {
  const value = trigger.config.timezone;
  return typeof value === "string" && value.trim() ? value : "UTC";
}
