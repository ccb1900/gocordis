// Event feed panel: persisted observation journal first, then live events;
// merged and deduplicated by timestamp. Observations are console
// infrastructure — this is a built-in renderer, not application code.
// The live tail is a projection: events fold into the read model in the
// projection store, and this component only re-renders when it changed.
import { useEffect, useState } from "react";
import { List, Space, Tag, Typography } from "antd";
import { api } from "../api";
import { projectionState, registerProjection, useProjection } from "../lib/projections";
import { observationView, relativeTime } from "../lib/observations";
import type { UIObservation } from "../types";

registerProjection<UIObservation[]>({
  id: "console.event-tail",
  init: [],
  apply: (prev, ev) => [...prev.slice(-49), ev],
});

export function EventFeed({ generation }: { generation: number }) {
  const [history, setHistory] = useState<UIObservation[]>([]);
  const live = useProjection<UIObservation[]>("console.event-tail") ?? projectionState<UIObservation[]>("console.event-tail") ?? [];

  useEffect(() => {
    api
      .hubQuery<Array<{ type: string; sourceId?: string; timestamp: string }>>("observations", { limit: 200 })
      .then((recs) => setHistory(recs ?? []))
      .catch(() => setHistory([]));
  }, [generation]);
  // Live tail arrives through the projection store — no local event wiring.

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
                {ev.message && <Typography.Text type="danger" style={{ fontSize: 12 }}> {ev.message}</Typography.Text>}
              </span>
              <Typography.Text type="secondary" style={{ fontSize: 11 }}>{relativeTime(ev.timestamp)}</Typography.Text>
            </Space>
          </List.Item>
        );
      }}
    />
  );
}
