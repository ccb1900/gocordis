// AntV G6 canvas. THE graph instance is created exactly once and kept stable:
// every refresh goes through `graph.setData(data); graph.draw()` on that same
// instance. Destroy only on unmount. The previous designer recreated the graph
// on every data change, which produced an infinite render loop.

import type { Graph } from "@antv/g6";
import { useEffect, useMemo, useRef, type DragEvent } from "react";
import { graphBehaviors, graphOptions, workflowToG6 } from "./graph-model";
import type { NodeDescriptor, WorkflowDefinition } from "./types";

export interface GraphCanvasProps {
  definition: WorkflowDefinition;
  catalog: ReadonlyMap<string, NodeDescriptor>;
  selectedId: string | null;
  /** Drag-to-connect and drag-to-move share the pointer: exactly one at a time. */
  connectMode: boolean;
  onSelect: (id: string | null) => void;
  onDropNode: (nodeTypeId: string, x: number, y: number) => void;
  /** Returns the resolved domain port pair, or null when incompatible. */
  onCreateEdge: (source: string, target: string) => { sourcePort: string; targetPort: string } | null;
  onPositions: (positions: Record<string, { x: number; y: number }>) => void;
}

interface Callbacks {
  onSelect: GraphCanvasProps["onSelect"];
  onDropNode: GraphCanvasProps["onDropNode"];
  onCreateEdge: GraphCanvasProps["onCreateEdge"];
  onPositions: GraphCanvasProps["onPositions"];
}

const nodeIdOf = (event: unknown): string | undefined => {
  const e = event as { targetType?: string; target?: { id?: string } };
  return e?.targetType === "node" ? e.target?.id : undefined;
};

export function GraphCanvas(props: GraphCanvasProps) {
  const { definition, catalog, selectedId, connectMode,
    onSelect, onDropNode, onCreateEdge, onPositions } = props;
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<Graph | null>(null);
  const readyRef = useRef(false);
  const connectRef = useRef(connectMode);
  connectRef.current = connectMode;
  // Refs keep the one-shot G6 event handlers current without re-creating the
  // graph when a parent re-render produces a new callback identity.
  const callbacks = useRef<Callbacks>({ onSelect, onDropNode, onCreateEdge, onPositions });
  callbacks.current = { onSelect, onDropNode, onCreateEdge, onPositions };

  const data = useMemo(
    () => workflowToG6(definition, catalog, selectedId),
    [definition, catalog, selectedId]);
  const initialData = useRef(data);
  initialData.current = data;
  const allowedRef = useRef<ReadonlySet<string>>(new Set());
  allowedRef.current = new Set(definition.nodes.map((node) => node.id));

  useEffect(() => {
    const container = containerRef.current;
    if (!container || readyRef.current) return;
    let disposed = false;
    void import("@antv/g6").then(({ Graph: G6Graph }) => {
      if (disposed || !containerRef.current || readyRef.current) return;
      const base = graphOptions(initialData.current, container);
      const graph = new G6Graph({
        ...base,
        behaviors: graphBehaviors({
          graph: {
            getNodeData: () => graphRef.current?.getNodeData() ?? [],
          },
          allowedIds: () => allowedRef.current,
          connectMode: () => connectRef.current,
          onSelect: (id) => callbacks.current.onSelect(id),
          onDropNode: (type, x, y) => callbacks.current.onDropNode(type, x, y),
          onCreateEdge: (source, target) => callbacks.current.onCreateEdge(source, target),
          onPositions: (positions) => callbacks.current.onPositions(positions),
        }),
      } as ConstructorParameters<typeof G6Graph>[0]);
      graph.on("node:click", (event: unknown) => {
        const id = nodeIdOf(event);
        if (id && !connectRef.current) callbacks.current.onSelect(id);
      });
      graph.on("edge:click", (event: unknown) => {
        const e = event as { target?: { id?: string } };
        if (e?.target?.id) callbacks.current.onSelect(e.target.id);
      });
      graph.on("canvas:click", () => callbacks.current.onSelect(null));
      graphRef.current = graph;
      readyRef.current = true;
      void graph.render();
    });
    return () => { disposed = true; };
    // One-shot creation: data changes are handled by the effect below.
  }, []);

  // Apply each domain refresh to the EXISTING graph instance. `draw` does not
  // re-run layout, so node positions from the domain stay authoritative.
  useEffect(() => {
    const graph = graphRef.current;
    if (!graph || !readyRef.current) return;
    graph.setData(data);
    void graph.draw();
  }, [data]);

  useEffect(() => () => {
    graphRef.current?.destroy();
    graphRef.current = null;
    readyRef.current = false;
  }, []);

  const drop = (event: DragEvent<HTMLDivElement>) => {
    event.preventDefault();
    const typeId = event.dataTransfer.getData("application/x-industrial-node") ||
      event.dataTransfer.getData("text/plain");
    if (!typeId) return;
    const graph = graphRef.current;
    const container = containerRef.current;
    if (!graph || !container) return;
    const rect = container.getBoundingClientRect();
    // G6 Point is a [x, y] tuple, not an object.
    const point = graph.getCanvasByViewport([
      event.clientX - rect.left,
      event.clientY - rect.top,
    ]);
    callbacks.current.onDropNode(typeId, point[0], point[1]);
  };

  return (
    <div style={{ position: "relative", height: "100%", minHeight: 360 }}>
      <div
        ref={containerRef}
        className="industrial-graph"
        onDragOver={(event) => event.preventDefault()}
        onDrop={drop}
        data-testid="industrial-graph"
        style={{
          height: "100%", minHeight: 360, border: connectMode ? "1px dashed #1677ff" : "1px solid transparent",
          cursor: connectMode ? "crosshair" : "default",
        }}
      />
      {connectMode && (
        <div style={{ position: "absolute", left: 8, top: 8, pointerEvents: "none" }}>
          <span style={{ background: "#1677ff", color: "#fff", borderRadius: 4, padding: "2px 8px", fontSize: 12 }}>
            连线模式：从一个节点拖到另一个节点
          </span>
        </div>
      )}
    </div>
  );
}
