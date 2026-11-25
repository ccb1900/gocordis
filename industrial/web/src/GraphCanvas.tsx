// AntV G6 canvas. THE graph instance is created exactly once and kept stable:
// every refresh goes through `graph.setData(data); graph.render()` on that same
// instance. Destroy only on unmount. The previous designer recreated the graph
// on every data change, which produced an infinite render loop.

import type { Graph } from "@antv/g6";
import { useEffect, useMemo, useRef, type DragEvent } from "react";
import { graphOptions, positionsFromG6, workflowToG6 } from "./graph-model";
import type { NodeDescriptor, WorkflowDefinition } from "./types";

export interface GraphCanvasProps {
  definition: WorkflowDefinition;
  catalog: ReadonlyMap<string, NodeDescriptor>;
  selectedId: string | null;
  onSelect: (id: string | null) => void;
  onDropNode: (nodeTypeId: string, x: number, y: number) => void;
  /** Returns true when the domain model accepted the edge (ports compatible). */
  onCreateEdge: (source: string, target: string) => boolean;
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
  const { definition, catalog, selectedId, onSelect, onDropNode, onCreateEdge, onPositions } = props;
  const containerRef = useRef<HTMLDivElement>(null);
  const graphRef = useRef<Graph | null>(null);
  const readyRef = useRef(false);
  // Refs keep the one-shot G6 event handlers current without re-creating the
  // graph when a parent re-render produces a new callback identity.
  const callbacks = useRef<Callbacks>({ onSelect, onDropNode, onCreateEdge, onPositions });
  callbacks.current = { onSelect, onDropNode, onCreateEdge, onPositions };

  const data = useMemo(
    () => workflowToG6(definition, catalog, selectedId),
    [definition, catalog, selectedId],
  );
  const initialData = useRef(data);
  initialData.current = data;

  useEffect(() => {
    const container = containerRef.current;
    if (!container || readyRef.current) return;
    let disposed = false;
    void import("@antv/g6").then(({ Graph: G6Graph }) => {
      if (disposed || !containerRef.current || readyRef.current) return;
      const base = graphOptions(initialData.current, container);
      const graph = new G6Graph({
        ...base,
        behaviors: [
          "drag-canvas",
          "zoom-canvas",
          {
            type: "drag-element",
            onFinish: () => {
              const current = graphRef.current;
              if (!current) return;
              const allowed = new Set(initialData.current.nodes.map((node) => String(node.id)));
              const nodes = current.getNodeData().map((node) => ({
                id: String(node.id),
                data: (node.data ?? {}) as Record<string, unknown>,
                style: (node.style ?? {}) as Record<string, unknown> & { x?: number; y?: number },
              }));
              callbacks.current.onPositions(positionsFromG6({ nodes, edges: [] }, allowed));
            },
          },
          {
            type: "create-edge",
            trigger: "drag",
            onCreate: (edge: { source?: unknown; target?: unknown }) => {
              const source = String(edge.source ?? "");
              const target = String(edge.target ?? "");
              if (!source || !target || source === target) return undefined;
              return callbacks.current.onCreateEdge(source, target) ? edge : undefined;
            },
          },
        ],
      } as ConstructorParameters<typeof G6Graph>[0]);
      graph.on("node:click", (event: unknown) => {
        const id = nodeIdOf(event);
        if (id) callbacks.current.onSelect(id);
      });
      graph.on("canvas:click", () => callbacks.current.onSelect(null));
      graphRef.current = graph;
      readyRef.current = true;
      void graph.render();
    });
    return () => {
      disposed = true;
    };
    // One-shot creation: data changes are handled by the effect below.
  }, []);

  // Apply each domain refresh to the EXISTING graph instance.
  useEffect(() => {
    const graph = graphRef.current;
    if (!graph || !readyRef.current) return;
    graph.setData(data);
    void graph.render();
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
    <div
      ref={containerRef}
      className="industrial-graph"
      onDragOver={(event) => event.preventDefault()}
      onDrop={drop}
      data-testid="industrial-graph"
    />
  );
}
