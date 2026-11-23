// insight-graph: declared vs runtime dependency graph, rendered with AntV G6.
// The G6 instance is created ONCE and kept alive; query/toggle changes call
// graph.setData + render (never destroy/recreate), so canvas/layout state is
// stable across the console's frequent SSE-driven re-renders.
import { Alert, Segmented, Space, Typography } from "antd";
import type { Graph } from "@antv/g6";
import { useEffect, useMemo, useRef, useState } from "react";
import { COMPOSITION, useDomainVersion } from "../lib/projections";
import { buildGraph, graphOptions, type G6GraphData } from "../lib/insight-graph-g6";
import type { GraphView, InsightGraphDTO } from "../lib/insight-graph";
import type { BlockRenderer } from "./registry";



export const InsightGraphBlock: BlockRenderer = ({ block, ctx }) => {
  const [data, setData] = useState<InsightGraphDTO | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<GraphView>("declared");
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<Graph | null>(null);
  const readyRef = useRef(false);
  const query = block.query || "insight/graph";
  // The dependency graph only changes on composition events (enable/disable/
  // install), not on every data observation. Using the COMPOSITION domain keeps
  // the dagre layout stable while the store writer emits data events.
  const version = useDomainVersion(block.domain ?? COMPOSITION);
  // api.hubQuery is a stable module function; capture it once so SSE-driven
  // ctx identity changes cannot restart the query effect.
  const hubQuery = useRef(ctx.hubQuery).current;

  useEffect(() => {
    let alive = true;
    setLoading(true);
    hubQuery<InsightGraphDTO>(query)
      .then((d) => {
        if (alive) { setData(d ?? null); setError(null); }
      })
      .catch((e) => alive && setError(e instanceof Error ? e.message : String(e)))
      .finally(() => alive && setLoading(false));
    return () => { alive = false; };
    // `version` intentionally refreshes the graph when the composition changes
    // (enable/disable/install); hubQuery/query are stable.
  }, [query, version, hubQuery]);

  // Memoize so unrelated re-renders (loading/error/generation) keep the same
  // graphData reference and do not trigger a G6 update.
  const graphData = useMemo(() => buildGraph(data, view), [data, view]);
  const hasData = graphData.nodes.length > 0;

  // Create the graph ONCE (lazy import) when the container first has data.
  useEffect(() => {
    const container = containerRef.current;
    if (!container || readyRef.current || !hasData) return;
    let disposed = false;
    void import("@antv/g6").then(({ Graph: G6Graph }) => {
      if (disposed || !containerRef.current || readyRef.current) return;
      const graph = new G6Graph(graphOptions(graphData, container));
      graphRef.current = graph;
      readyRef.current = true;
      void graph.render();
    });
    return () => { disposed = true; };
    // Creation is intentionally one-shot; data changes are handled below.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hasData]);

  // Apply data/view changes to the EXISTING graph — no destroy/recreate.
  useEffect(() => {
    const graph = graphRef.current;
    if (!graph || !readyRef.current) return;
    graph.setData(graphData);
    void graph.render();
  }, [graphData]);

  // Destroy only on unmount.
  useEffect(() => () => {
    graphRef.current?.destroy();
    graphRef.current = null;
    readyRef.current = false;
  }, []);

  const sequence = data?.sequence ?? data?.eventSequence;
  return (
    <div className="card" style={{ padding: 12 }}>
      <div style={{ display: "flex", justifyContent: "space-between", alignItems: "center", gap: 12, flexWrap: "wrap", marginBottom: 10 }}>
        <Typography.Text strong>{block.title || "依赖图"}</Typography.Text>
        <Space size={10}>
          {typeof sequence === "number" && (
            <Typography.Text type="secondary" style={{ fontSize: 12 }}>序列 {sequence}</Typography.Text>
          )}
          <Segmented
            value={view}
            onChange={(v) => setView(v as GraphView)}
            options={[
              { label: "声明视图", value: "declared" },
              { label: "运行时视图", value: "runtime" },
            ]}
          />
        </Space>
      </div>
      {error && <Alert type="error" showIcon message={error} style={{ marginBottom: 8 }} />}
      {loading && !data && <Typography.Text type="secondary">加载中…</Typography.Text>}
      {!loading && !error && !hasData && (
        <Typography.Text type="secondary">
          暂无{view === "declared" ? "声明" : "运行时"}图数据。
        </Typography.Text>
      )}
      <div
        ref={containerRef}
        style={{ width: "100%", height: 460, display: hasData ? "block" : "none" }}
      />
    </div>
  );
};
