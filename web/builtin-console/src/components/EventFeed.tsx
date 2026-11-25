// Event feed panel: the observation journal, refreshed when the shell's
// observation generation changes. This is a product renderer registered by
// the builtin Console client module, not shell infrastructure.
import { useEffect, useState } from "react";
import { List, Space, Tag, Typography } from "antd";
import { facade } from "../facade";
import { observationView, relativeTime } from "../lib/observations";
import type { UIObservation } from "../types";

export function EventFeed({ generation }: { generation: number }) {
  const [history, setHistory] = useState<UIObservation[]>([]);

  useEffect(() => {
    facade().api
      .hubQuery<UIObservation[]>("observations", { limit: 200 })
      .then((recs) => setHistory(recs ?? []))
      .catch(() => setHistory([]));
  }, [generation]);

  if (history.length === 0) {
    return <Typography.Text type="secondary">暂无观察事件——运行时变更上下文后自动填充。</Typography.Text>;
  }
  return (
    <List
      size="small"
      dataSource={[...history].reverse()}
      renderItem={(ev) => {
        const view = observationView(ev.type);
        return (
          <List.Item>
            <Space size={8} wrap>
              <Tag color={view.color}>{view.label}</Tag>
              <Typography.Text code style={{ fontSize: 12 }}>{ev.sourceId || "runtime"}</Typography.Text>
              <Typography.Text type="secondary">{relativeTime(ev.timestamp)}</Typography.Text>
              {ev.message ? <Typography.Text>{ev.message}</Typography.Text> : null}
            </Space>
          </List.Item>
        );
      }}
    />
  );
}
