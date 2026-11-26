// Left palette: node descriptions come from the real hub query
// `industrial/nodes`. Dragging an item drops a typed node onto the G6 canvas.

import { Empty, Spin, Tag, Typography } from "antd";
import type { DragEvent } from "react";
import type { NodeDescriptor } from "./types";

export interface NodePaletteProps {
  descriptors: NodeDescriptor[];
  loading: boolean;
  error?: string | null;
  onAdd?: (nodeTypeId: string) => void;
}

export function NodePalette({ descriptors, loading, error, onAdd }: NodePaletteProps) {
  const groups = new Map<string, NodeDescriptor[]>();
  for (const descriptor of descriptors) {
    const category = descriptor.category.split(".")[0] || "General";
    const group = groups.get(category) ?? [];
    group.push(descriptor);
    groups.set(category, group);
  }

  const start = (event: DragEvent<HTMLDivElement>, descriptor: NodeDescriptor) => {
    event.dataTransfer.setData("application/x-industrial-node", descriptor.nodeTypeId);
    event.dataTransfer.setData("text/plain", descriptor.nodeTypeId);
    event.dataTransfer.effectAllowed = "copy";
  };

  return (
    <div className="industrial-palette-content">
      <Typography.Title level={5} style={{ marginTop: 0 }}>节点面板</Typography.Title>
      <Typography.Text type="secondary" style={{ fontSize: 12 }}>
        拖入画布 · 连线按端口类型校验
      </Typography.Text>
      {loading && <Spin size="small" style={{ display: "block", marginTop: 16 }} />}
      {error && <Typography.Text type="danger" style={{ display: "block", marginTop: 12 }}>{error}</Typography.Text>}
      {!loading && !error && descriptors.length === 0 && (
        <Empty description="industrial/nodes 暂无节点描述" style={{ marginTop: 24 }} />
      )}
      {Array.from(groups.entries()).map(([category, items]) => (
        <section key={category} className="industrial-palette-group">
          <Typography.Text strong style={{ fontSize: 12 }}>{category}</Typography.Text>
          {items.map((descriptor) => (
            <div
              key={descriptor.nodeTypeId}
              className="industrial-palette-node"
              draggable
              role="button"
              tabIndex={0}
              onDragStart={(event) => start(event, descriptor)}
              onClick={() => onAdd?.(descriptor.nodeTypeId)}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") onAdd?.(descriptor.nodeTypeId);
              }}
              title={descriptor.description || descriptor.nodeTypeId}
            >
              <div className="industrial-palette-node-title">{descriptor.name}</div>
              <div className="industrial-palette-node-meta">
                <Tag bordered={false} color="blue">{descriptor.version}</Tag>
                <Typography.Text type="secondary" style={{ fontSize: 11 }}>{descriptor.nodeTypeId}</Typography.Text>
              </div>
              <div className="industrial-palette-node-ports">
                {descriptor.inputs.map((port) => (
                  <Tag key={`in-${port.name}`} bordered={false} color="default">in {port.name}: {port.type}</Tag>
                ))}
                {descriptor.outputs.map((port) => (
                  <Tag key={`out-${port.name}`} bordered={false} color="green">out {port.name}: {port.type}</Tag>
                ))}
              </div>
            </div>
          ))}
        </section>
      ))}
    </div>
  );
}
