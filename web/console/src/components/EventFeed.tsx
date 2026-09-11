// Event feed panel: persisted observation journal first, then live events;
// merged and deduplicated by timestamp. Observations are console
// infrastructure — this is a built-in renderer, not application code.
import { useEffect, useState } from "react";
import { List, Space, Tag, Typography } from "antd";
import { api } from "../api";
import { observationView, relativeTime } from "../lib/observations";
import { onObservation } from "../stream";
import type { UIObservation } from "../types";

export function EventFeed({ generation }: { generation: number }) {
  const [history, setHistory] = useState<UIObservation[]>([]);
  const [live, setLive] = useState<UIObservation[]>([]);

  useEffect(() => {
    api
      .hubQuery<Array<{ type: string; sourceId?: string; timestamp: string }>>("observations", { limit: 200 })
      .then((recs) => setHistory(recs ?? []))
      .catch(() => setHistory([]));
  }, [generation]);

  useEffect(() => onObservation((ev) => setLive((prev) => [...prev.slice(-49), ev])), []);

  const seen = new Set<string>();
  const merged: UIObservation[] = [];
  for (const ev of [...history, ...live]) {
    const k = `${ev.timestamp}|${ev.type}|${ev.sourceId ?? ""}`;
    if (seen.has(k)) continue;
    seen.add(k);
    merged.push(ev);
  }
  if (merged.length === 0) {
    return <Typography.Text type="secondary">暂无观察事件——运行时变更上下文后自动填充。</Typography.Text>;
  }
  return (
    <List
      size="small"
      dataSource={[...merged].reverse()}
      renderItem={(ev) => {
        const view = observationView(ev.type);
        return (
          <List.Item style={{ padding: "4px 0" }}>
            <Space style={{ width: "100%", justifyContent: "space-between" }}>
              <span>
                <Tag color={view.tone === "ok" ? "success" : view.tone === "danger" ? "error" : view.tone === "warn" ? "warning" : view.tone === "accent" ? "processing" : "default"}>{view.label}</Tag>
                {ev.sourceId && <Typography.Text type="secondary" style={{ fontSize: 12 }}>{ev.sourceId}</Typography.Text>}
              </span>
              <Typography.Text type="secondary" style={{ fontSize: 11 }}>{relativeTime(ev.timestamp)}</Typography.Text>
            </Space>
          </List.Item>
        );
      }}
    />
  );
}
