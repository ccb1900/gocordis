// insight-graph: declared vs runtime dependency graph, rendered with AntV G6.
// Both views come from one named query; the toggle is local UI state only.
// Pure DTO → G6 data mapping lives in lib/insight-graph-g6.ts (node-testable);
// this component only owns the G6 instance lifecycle against the DOM.
import { Alert, Segmented, Space, Typography } from "antd";
import type { Graph } from "@antv/g6";
import { useEffect, useRef, useState } from "react";
import { ALL, useDomainVersion } from "../lib/projections";
import { buildGraph, type G6GraphData } from "../lib/insight-graph-g6";
import type { GraphView, InsightGraphDTO } from "../lib/insight-graph";
import type { BlockRenderer } from "./registry";

export const InsightGraphBlock: BlockRenderer = ({ block, ctx }) => {
  const [data, setData] = useState<InsightGraphDTO | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<GraphView>("declared");
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<Graph | null>(null);
  const query = block.query || "insight/graph";
  const version = useDomainVersion(block.domain ?? ALL);

  useEffect(() => {
    let alive = true;
    setLoading(true);
    ctx
      .hubQuery<InsightGraphDTO>(query)
      .then((d) => {
        if (alive) {
          setData(d ?? null);
          setError(null);
        }
      })
      .catch((e) => alive && setError(e instanceof Error ? e.message : String(e)))
      .finally(() => alive && setLoading(false));
    return () => {
      alive = false;
    };
  }, [query, version, ctx.hubQuery]);

  const graphData: G6GraphData = buildGraph(data, view);
  const hasData = graphData.nodes.length > 0;

  // One G6 Graph per data/view generation; destroy on change or unmount so no
  // stale edges or canvas handlers survive a query refresh / toggle.
  useEffect(() => {
    const container = containerRef.current;
    if (!container || !hasData) return;
    let disposed = false;
    let graph: Graph | null = null;

    // Load G6 lazily so the ~2 MB graph library is a separate chunk, fetched
    // only when the dependency-graph page is actually opened.
    void import("@antv/g6").then(({ Graph: G6Graph }) => {
      if (disposed || !containerRef.current) return;
      graph = new G6Graph({
        container,
        autoFit: "view",
        data: graphData,
        layout: { type: "dagre", rankdir: "LR", nodesep: 24, ranksep: 60 },
        node: {
          style: {
            labelText: (d: { data?: { label?: string } }) => d?.data?.label ?? "",
            labelPlacement: "bottom",
            size: [116, 36],
            radius: 8,
            fill: "#e6f0ff",
            stroke: "#4d6bfe",
            lineWidth: 1.5,
            labelFill: "#1f1f1f",
            labelFontSize: 11,
          },
        },
        edge: {
          style: {
            labelText: (d: { data?: { label?: string } }) => d?.data?.label ?? "",
            labelFontSize: 10,
            stroke: "#8a93a6",
            endArrow: true,
          },
        },
        behaviors: ["drag-canvas", "zoom-canvas", "drag-element"],
      });
      graphRef.current = graph;
      void graph.render();
    });

    return () => {
      disposed = true;
      graphRef.current = null;
      graph?.destroy();
    };
  }, [graphData, hasData]);

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
        style={{ width: "100%", height: 420, display: hasData ? "block" : "none" }}
      />
    </div>
  );
};
