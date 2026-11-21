// insight-graph: declared vs runtime dependency graph. Both views come from
// one named query; the toggle is local UI state only. Antd Tree (already a
// dependency) is the renderer — no graph library is introduced. All DTO →
// tree mapping lives in lib/insight-graph.ts so it stays node-testable.
import { Alert, Segmented, Space, Tree, Typography } from "antd";
import type { TreeDataNode } from "antd";
import { useEffect, useState } from "react";
import { ALL, useDomainVersion } from "../lib/projections";
import {
  buildTree, type GraphTreeNode, type GraphView, type InsightGraphDTO,
} from "../lib/insight-graph";
import type { BlockRenderer } from "./registry";

function toTreeData(nodes: GraphTreeNode[]): TreeDataNode[] {
  return nodes.map((n) => ({
    key: n.key,
    title: (
      <span style={{ display: "inline-flex", gap: 8, alignItems: "baseline", flexWrap: "wrap" }}>
        <span style={{ fontWeight: 600 }}>{n.label}</span>
        {n.detail && <span style={{ color: "#8a93a6", fontSize: 12 }}>{n.detail}</span>}
      </span>
    ),
    children: n.children ? toTreeData(n.children) : undefined,
  }));
}

export const InsightGraphBlock: BlockRenderer = ({ block, ctx }) => {
  const [data, setData] = useState<InsightGraphDTO | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [view, setView] = useState<GraphView>("declared");
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

  const nodes = buildTree(data, view);
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
      {!loading && !error && nodes.length === 0 && (
        <Typography.Text type="secondary">
          暂无{view === "declared" ? "声明" : "运行时"}图数据。
        </Typography.Text>
      )}
      {nodes.length > 0 && (
        <Tree treeData={toTreeData(nodes)} blockNode defaultExpandAll selectable={false} />
      )}
    </div>
  );
};
