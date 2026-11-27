// AntV G6 canvas. THE graph instance is created exactly once and kept stable:
// every refresh goes through `graph.setData(data); graph.draw()` on that same
// instance. Gestures are direct: node drag moves, port drag connects, canvas
// drag pans; Space/middle-button pans over any target.

import type { Graph } from "@antv/g6";
import { useEffect, useMemo, useRef, type DragEvent } from "react";
import { graphBehaviors, graphOptions, workflowToG6 } from "./graph-model";
import type { NodeDescriptor, WorkflowDefinition } from "./types";

export interface GraphCanvasProps {
  definition: WorkflowDefinition;
  catalog: ReadonlyMap<string, NodeDescriptor>;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  onDropNode: (nodeTypeId: string, x: number, y: number) => void;
  onCreateEdge: (source: string, target: string) => { sourcePort: string; targetPort: string } | null;
  onPositions: (positions: Record<string, { x: number; y: number }>) => void;
  onSelectIds?: (ids: string[]) => void;
  onGraphReady?: (graph: any) => void;
}

interface Callbacks {
  onSelect: GraphCanvasProps["onSelect"];
  onDropNode: GraphCanvasProps["onDropNode"];
  onCreateEdge: GraphCanvasProps["onCreateEdge"];
  onPositions: GraphCanvasProps["onPositions"];
  onSelectIds?: GraphCanvasProps["onSelectIds"];
}

const nodeIdOf = (event: unknown): string | undefined => {
  const e = event as { targetType?: string; target?: { id?: string } };
  return e?.targetType === "node" ? e.target?.id : undefined;
};

export function GraphCanvas(props: GraphCanvasProps) {
  const { definition, catalog, selectedId,
    onSelect, onDropNode, onCreateEdge, onPositions, onSelectIds, onGraphReady } = props;
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<Graph | null>(null);
  const readyRef = useRef(false);
  const panModifierRef = useRef(false);
  const connectionStateRef = useRef({ armed: false });
  const gestureOriginRef = useRef<{ x: number; y: number } | null>(null);
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
    const down = (event: KeyboardEvent) => {
      if (event.code === "Space" && !event.repeat) panModifierRef.current = true;
    };
    const up = (event: KeyboardEvent) => {
      if (event.code === "Space") panModifierRef.current = false;
    };
    const blur = () => { panModifierRef.current = false; };
    window.addEventListener("keydown", down);
    window.addEventListener("keyup", up);
    window.addEventListener("blur", blur);
    return () => {
      window.removeEventListener("keydown", down);
      window.removeEventListener("keyup", up);
      window.removeEventListener("blur", blur);
    };
  }, []);

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
            getCanvasByClient: (point) => {
              const p = graphRef.current?.getCanvasByClient(point) ?? point;
              return [p[0], p[1]];
            },
          },
          isPanModifier: () => panModifierRef.current,
          gestureOrigin: () => gestureOriginRef.current,
          connectionState: connectionStateRef.current,
          allowedIds: () => allowedRef.current,
          onPositions: (positions) => callbacks.current.onPositions(positions),
          onSelectIds: (ids) => onSelectIds?.(ids),
          onCreateEdge: (source, target) => callbacks.current.onCreateEdge(source, target),
        }),
      } as ConstructorParameters<typeof G6Graph>[0]);
      graph.on("node:click", (event: unknown) => {
        const id = nodeIdOf(event);
        if (id) callbacks.current.onSelect(id);
      });
      graph.on("edge:click", () => callbacks.current.onSelect(null));
      graph.on("canvas:click", () => callbacks.current.onSelect(null));
      graph.on("pointerdown", (event: unknown) => {
        const e = event as { client?: { x: number; y: number } };
        gestureOriginRef.current = e.client ? { x: e.client.x, y: e.client.y } : null;
      });
      graph.on("pointerup", () => {
        // create-edge finishes its own drop gesture inside this same event;
        // clearing armed synchronously would cancel an in-flight connection.
        queueMicrotask(() => {
          connectionStateRef.current.armed = false;
          gestureOriginRef.current = null;
        });
      });
      graphRef.current = graph;
      // Expose the live instance on its container for real-browser E2E.
      (container as HTMLElement & { __industrialGraph?: Graph }).__industrialGraph = graph;
      onGraphReady?.(graph);
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
      <div className="industrial-graph">
        <div
          ref={containerRef}
          className="industrial-graph-host"
          onDragOver={(event) => event.preventDefault()}
          onDrop={drop}
          data-testid="industrial-graph"
        />
        <div className="industrial-graph-hint">
          Shift+拖拽框选 · 拖节点移动 · 端口拖拽连线 · 滚轮缩放 · 空格/中键平移
        </div>
      </div>
    </div>
  );
}
