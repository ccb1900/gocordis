// framework-dashboard: framework health at a glance. Fetches the aggregate
// `framework/dashboard` query and the canonical `runtime/events` timeline,
// spotlights failed/pending counts, lists diagnostics and links to the
// Runtime Inspector. All parsing/aggregation is in ./framework-dashboard.
import { Alert, Button, Card, Col, Empty, Row, Space, Statistic, Table, Tag, Timeline, Typography } from "antd";
import type { TableColumnsType } from "antd";
import { useEffect, useState } from "react";
import { ALL, useDomainVersion } from "../lib/projections";
import { navigate } from "../router";
import type { FrameworkDashboardDTO, RuntimeEventsDTO } from "../types";
import type { BlockRenderer } from "./registry";
import {
  parseDashboard, parseEvents, resolveEvents,
  type DiagnosticRow, type EventRow,
} from "./framework-dashboard";

const DASHBOARD_QUERY = "framework/dashboard";
const EVENTS_QUERY = "runtime/events";
const DEFAULT_INSPECTOR_ROUTE = "/inspector";

function eventTone(type: string): string {
  const t = type.toLowerCase();
  if (t.includes("fail")) return "red";
  if (t.includes("active") || t.includes("committed")) return "green";
  if (t.includes("undo") || t.includes("withdraw") || t.includes("unload")) return "orange";
  if (t.includes("loading") || t.includes("pending")) return "gold";
  return "blue";
}

export const FrameworkDashboardBlock: BlockRenderer = ({ block, ctx }) => {
  const [dashboard, setDashboard] = useState(() => parseDashboard(null));
  const [events, setEvents] = useState<EventRow[]>([]);
  const [eventsSequence, setEventsSequence] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reload, setReload] = useState(0);
  const version = useDomainVersion(ALL);
  const dashboardQuery = block.query || DASHBOARD_QUERY;
  const eventsQuery = block.params?.eventsQuery || EVENTS_QUERY;
  const inspectorRoute = block.params?.inspectorRoute || DEFAULT_INSPECTOR_ROUTE;

  useEffect(() => {
    let alive = true;
    setLoading(true);
    Promise.all([
      ctx.hubQuery<FrameworkDashboardDTO>(dashboardQuery).then((d) => d).catch((e) => ({ __error: e })),
      ctx.hubQuery<RuntimeEventsDTO>(eventsQuery).then((d) => d).catch((e) => ({ __error: e })),
    ]).then(([rawDashboard, rawEvents]) => {
      if (!alive) return;
      const dErr = (rawDashboard as { __error?: unknown }).__error;
      const eErr = (rawEvents as { __error?: unknown }).__error;
      if (dErr) setError(dErr instanceof Error ? dErr.message : String(dErr));
      else {
        setError(null);
        setDashboard(parseDashboard(rawDashboard));
      }
      if (!eErr) {
        const parsed = parseEvents(rawEvents);
        setEvents(parsed.events);
        setEventsSequence(parsed.sequence);
      }
    }).finally(() => {
      if (alive) setLoading(false);
    });
    return () => {
      alive = false;
    };
  }, [ctx.hubQuery, dashboardQuery, eventsQuery, version, reload]);

  const counts = dashboard.counts;
  const shownEvents = resolveEvents(events, dashboard.recentEvents).slice(0, 30);

  const diagnosticColumns: TableColumnsType<DiagnosticRow> = [
    { title: "Level", dataIndex: "level", key: "level", width: 110, render: (v: string) => <Tag color={v.toLowerCase().includes("err") ? "red" : v.toLowerCase().includes("warn") ? "gold" : "default"}>{v}</Tag> },
    { title: "Code", dataIndex: "code", key: "code", width: 120 },
    { title: "Source", dataIndex: "source", key: "source", width: 140 },
    { title: "Fiber", dataIndex: "fiberID", key: "fiberID", width: 100 },
    { title: "Message", dataIndex: "message", key: "message", ellipsis: true },
    { title: "Detail", dataIndex: "detail", key: "detail", ellipsis: true },
  ];

  return (
    <div className="card" style={{ padding: 12 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12, flexWrap: "wrap", marginBottom: 12 }}>
        <div>
          <Typography.Text strong style={{ fontSize: 15 }}>{block.title || "框架概览"}</Typography.Text>
          <Typography.Text type="secondary" style={{ fontSize: 12, marginLeft: 10 }}>
            {dashboard.state} · 序列 {dashboard.sequence}
            {eventsSequence !== null ? ` · 事件 ${eventsSequence}` : ""}
          </Typography.Text>
        </div>
        <Space size={8}>
          <Button
            type="link"
            size="small"
            href={inspectorRoute}
            onClick={(e) => { e.preventDefault(); navigate(inspectorRoute); }}
          >
            运行时检视 →
          </Button>
          <Button size="small" loading={loading} onClick={() => setReload((v) => v + 1)}>刷新</Button>
        </Space>
      </div>

      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 10 }} />}

      <Row gutter={[12, 12]} style={{ marginBottom: 8 }}>
        <Col xs={12} md={6}>
          <Card size="small" styles={{ body: { padding: "12px 16px" } }}>
            <Statistic title="Failed" value={counts.failed} valueStyle={{ color: "#cf3b3b", fontWeight: 700 }} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small" styles={{ body: { padding: "12px 16px" } }}>
            <Statistic title="Pending" value={counts.pending} valueStyle={{ color: "#d48806", fontWeight: 700 }} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small" styles={{ body: { padding: "12px 16px" } }}>
            <Statistic title="Active" value={counts.active} />
          </Card>
        </Col>
        <Col xs={12} md={6}>
          <Card size="small" styles={{ body: { padding: "12px 16px" } }}>
            <Statistic title="Total" value={counts.total} />
          </Card>
        </Col>
      </Row>
      {counts.items.length > 0 && (
        <Space size={[6, 6]} wrap style={{ marginBottom: 12 }}>
          {counts.items.map((i) => <Tag key={i.key}>{`${i.label}: ${i.value}`}</Tag>)}
        </Space>
      )}

      <div style={{ marginBottom: 14 }}>
        <Typography.Text strong>最近生命周期事件</Typography.Text>
        {shownEvents.length > 0 ? (
          <Timeline
            style={{ marginTop: 10 }}
            items={shownEvents.map((e) => ({
              key: e.key,
              color: eventTone(e.type),
              title: (
                <Space size={8} wrap>
                  <Typography.Text code>{`#${e.sequence}`}</Typography.Text>
                  <Typography.Text strong>{e.type}</Typography.Text>
                  <Typography.Text type="secondary" style={{ fontSize: 12 }}>{e.timestamp}</Typography.Text>
                </Space>
              ),
              content: (
                <Typography.Text type="secondary" style={{ fontSize: 12 }}>
                  fiber {e.fiberID} · activation {e.activationID}
                  {e.data ? ` · ${e.data.length > 120 ? `${e.data.slice(0, 120)}…` : e.data}` : ""}
                </Typography.Text>
              ),
            }))}
          />
        ) : (
          <div style={{ marginTop: 8 }}><Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无生命周期事件" /></div>
        )}
      </div>

      <div>
        <Typography.Text strong>诊断</Typography.Text>
        <div style={{ marginTop: 8 }}>
          <Table<DiagnosticRow>
            size="small"
            rowKey="key"
            dataSource={dashboard.diagnostics}
            columns={diagnosticColumns}
            pagination={{ pageSize: 10, hideOnSinglePage: true }}
            scroll={{ x: "max-content" }}
            locale={{ emptyText: <Empty image={Empty.PRESENTED_IMAGE_SIMPLE} description="暂无诊断" /> }}
          />
        </div>
      </div>
    </div>
  );
};
