// AntV G6 canvas. THE graph instance is created exactly once and kept stable:
// every refresh goes through `graph.setData(data); graph.draw()` on that same
// instance. Destroy only on unmount. Pointer ownership is exclusive: select
// moves nodes, connect creates typed edges, pan moves the viewport.

import type { Graph } from "@antv/g6";
import { useEffect, useMemo, useRef, type DragEvent } from "react";
import {
  graphBehaviors, graphOptions, workflowToG6, type DesignerMode,
} from "./graph-model";
import type { NodeDescriptor, WorkflowDefinition } from "./types";

export interface GraphCanvasProps {
  definition: WorkflowDefinition;
  catalog: ReadonlyMap<string, NodeDescriptor>;
  selectedId: string | null;
  mode: DesignerMode;
  onSelect: (id: string | null) => void;
  onDropNode: (nodeTypeId: string, x: number, y: number) => void;
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

const MODE_HINT: Record<DesignerMode, string> = {
  select: "选择：拖动节点改变位置，点击节点编辑属性",
  connect: "连线：从节点拖到另一个节点，自动匹配兼容端口",
  pan: "平移：拖动画布移动视口（不移动节点）",
};

export function GraphCanvas(props: GraphCanvasProps) {
  const { definition, catalog, selectedId, mode,
    onSelect, onDropNode, onCreateEdge, onPositions } = props;
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<Graph | null>(null);
  const readyRef = useRef(false);
  const modeRef = useRef(mode);
  modeRef.current = mode;
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
          graph: { getNodeData: () => graphRef.current?.getNodeData() ?? [] },
          mode: () => modeRef.current,
          allowedIds: () => allowedRef.current,
          onPositions: (positions) => callbacks.current.onPositions(positions),
          onCreateEdge: (source, target) => callbacks.current.onCreateEdge(source, target),
        }),
      } as ConstructorParameters<typeof G6Graph>[0]);
      graph.on("node:click", (event: unknown) => {
        const id = nodeIdOf(event);
        if (id && modeRef.current === "select") callbacks.current.onSelect(id);
      });
      graph.on("edge:click", () => {
        // Edge details are edited in the edge table; selecting an edge as a
        // node used to leave the property panel in an impossible state.
        callbacks.current.onSelect(null);
      });
      graph.on("canvas:click", () => callbacks.current.onSelect(null));
      graphRef.current = graph;
      // Expose the live instance on its container for real-browser E2E. The
      // test emits the same G6 event a mouse click emits; no mock graph.
      (container as HTMLElement & { __industrialGraph?: Graph }).__industrialGraph = graph;
      readyRef.current = true;
      void graph.render();
    });
    return () => { disposed = true; };
  }, []);

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
    if (modeRef.current !== "select") return;
    const typeId = event.dataTransfer.getData("application/x-industrial-node") ||
      event.dataTransfer.getData("text/plain");
    if (!typeId) return;
    const graph = graphRef.current;
    const container = containerRef.current;
    if (!graph || !container) return;
    const rect = container.getBoundingClientRect();
    const point = graph.getCanvasByViewport([
      event.clientX - rect.left,
      event.clientY - rect.top,
    ]);
    callbacks.current.onDropNode(typeId, point[0], point[1]);
  };

  return (
    <div className="industrial-canvas-wrap">
      <div className={`industrial-graph industrial-graph--${mode}`}>
        <div
          ref={containerRef}
          className="industrial-graph-host"
          onDragOver={(event) => event.preventDefault()}
          onDrop={drop}
          data-testid="industrial-graph"
        />
        <div className="industrial-graph-hint">{MODE_HINT[mode]}</div>
      </div>
    </div>
  );
}
