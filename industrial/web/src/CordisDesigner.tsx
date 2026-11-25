// Cordis-native workflow designer. The CONSOLE owns layout, routing, React,
// antd and the API; this module only registers a block renderer with the
// facade. WorkflowDefinition is the single editable source of truth.

import { useCallback, useEffect, useMemo, useState } from "react";
import { Alert, Button, Layout, Select, Space, Spin, Tag, Typography, message } from "antd";
import { GraphCanvas } from "./GraphCanvas";
import { NodePalette } from "./NodePalette";
import { PropertyEditor } from "./PropertyEditor";
import { parseNodeDescriptors, parseWorkflow, parseWorkflowSummaries, unwrapData } from "./dto";
import {
  addEdge, applyPositions, descriptorOf, emptyWorkflow, makeNode, nextNodeId,
  removeEdge, removeNode, resolveEdgePorts, setNodeConfig, validateWorkflow,
} from "./workflow-model";
import type {
  NodeDescriptor, ValidationIssue, WorkflowDefinition, WorkflowEdge, WorkflowSummary,
} from "./types";

export interface DesignerContext {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

const { Header, Content } = Layout;

export function CordisDesigner({ ctx }: { ctx: DesignerContext }) {
  const [messageApi, contextHolder] = message.useMessage();
  const [descriptors, setDescriptors] = useState<NodeDescriptor[]>([]);
  const [summaries, setSummaries] = useState<WorkflowSummary[]>([]);
  const [resourceIds, setResourceIds] = useState<string[]>([]);
  const [resourceId, setResourceId] = useState("");
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [definition, setDefinition] = useState<WorkflowDefinition>(() => emptyWorkflow());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    let alive = true;
    Promise.allSettled([
      ctx.hubQuery("industrial/nodes"),
      ctx.hubQuery("industrial/workflows"),
      ctx.hubQuery("industrial/resources"),
    ]).then(([nodes, workflows, resources]) => {
      if (!alive) return;
      if (nodes.status === "fulfilled") setDescriptors(parseNodeDescriptors(nodes.value));
      else setLoadError(`节点描述加载失败: ${String(nodes.reason)}`);
      if (workflows.status === "fulfilled") setSummaries(parseWorkflowSummaries(workflows.value));
      if (resources.status === "fulfilled") {
        const rows = (unwrapData(resources.value) ?? []) as Array<{ id?: string }>;
        const ids = rows.map((row) => String(row?.id ?? "")).filter(Boolean);
        setResourceIds(ids);
        setResourceId((current) => current || ids[0] || "");
      }
    }).finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, [ctx]);

  const catalog = useMemo(
    () => new Map(descriptors.map((descriptor) => [descriptor.nodeTypeId, descriptor])),
    [descriptors]);
  const selectedNode = definition.nodes.find((node) => node.id === selectedId) ?? null;

  const run = useCallback(async (name: string, body: unknown, ok: string) => {
    setBusy(true);
    try {
      await ctx.hubCommand(name, body);
      messageApi.success(ok);
    } catch (error) {
      messageApi.error(`${name} failed: ${error instanceof Error ? error.message : String(error)}`);
    } finally {
      setBusy(false);
    }
  }, [ctx, messageApi]);

  const load = useCallback(async (workflowId: string) => {
    try {
      const raw = await ctx.hubQuery("industrial/workflow-definitions");
      const rows = (unwrapData(raw) ?? []) as unknown[];
      const found = rows.find((row) => {
        const candidate = row as { workflowId?: string };
        return String(candidate?.workflowId ?? "") === workflowId;
      });
      if (found === undefined) return;
      setDefinition(parseWorkflow(found));
      setSelectedId(null);
      setIssues([]);
    } catch (error) {
      messageApi.error(`加载工作流失败: ${error instanceof Error ? error.message : String(error)}`);
    }
  }, [ctx, messageApi]);

  const dropNode = useCallback((type: string, x: number, y: number) => {
    const descriptor = descriptorOf(catalog, type);
    if (!descriptor) return;
    setDefinition((current) => ({
      ...current,
      nodes: [...current.nodes, makeNode(descriptor, nextNodeId(current.nodes), x, y)],
    }));
    setIssues([]);
  }, [catalog]);

  const createEdge = useCallback((source: string, target: string) => {
    const sourceNode = definition.nodes.find((node) => node.id === source);
    const targetNode = definition.nodes.find((node) => node.id === target);
    if (!sourceNode || !targetNode) return false;
    const sourceDescriptor = descriptorOf(catalog, sourceNode.type);
    const targetDescriptor = descriptorOf(catalog, targetNode.type);
    if (!sourceDescriptor || !targetDescriptor) return false;
    const ports = resolveEdgePorts(sourceDescriptor, targetDescriptor);
    if (!ports) return false;
    const result = addEdge(definition, {
      source, sourcePort: ports.sourcePort, target, targetPort: ports.targetPort,
    }, catalog);
    if (result.issue) setIssues((currentIssues) => [...currentIssues, result.issue!]);
    else setDefinition(result.definition);
    return !result.issue;
  }, [definition, catalog]);

  const validate = useCallback(() => {
    const found = validateWorkflow(definition, catalog, new Set(resourceIds));
    setIssues(found);
    if (found.length === 0) messageApi.success("校验通过");
    else messageApi.warning(`发现 ${found.length} 个问题，见右侧定位列表`);
  }, [definition, catalog, resourceIds, messageApi]);

  const updateEdge = useCallback((index: number, edge: WorkflowEdge) => {
    setDefinition((current) => ({
      ...current,
      edges: current.edges.map((existing, at) => at === index ? edge : existing),
    }));
  }, []);

  if (loading) return <Spin tip="加载节点描述与资源…" />;
  if (loadError) return <Alert type="error" message={loadError} />;

  return (
    <Layout style={{ minHeight: 520, border: "1px solid #f0f0f0" }}>
      {contextHolder}
      <Header style={{ background: "#001529", display: "flex", alignItems: "center", gap: 12 }}>
        <Typography.Text strong style={{ color: "#fff" }}>Industrial Designer</Typography.Text>
        <Tag color="blue">Cordis block renderer</Tag>
        <Space>
          <Button loading={busy} onClick={() => void run("industrial/save-workflow", definition, "已保存")}>保存</Button>
          <Button onClick={validate}>校验</Button>
          <Button loading={busy} onClick={() => void run("industrial/publish-workflow",
            { workflowId: definition.workflowId }, "已发布")}>发布</Button>
          <Button type="primary" loading={busy} onClick={() => void run("industrial/run-workflow",
            { workflowId: definition.workflowId, resourceId }, "采集已启动")}>运行</Button>
          <Select
            style={{ width: 180 }}
            value={resourceId}
            onChange={setResourceId}
            options={resourceIds.map((id) => ({ value: id, label: id }))}
          />
        </Space>
      </Header>
      <Content style={{ display: "grid", gridTemplateColumns: "220px 1fr 300px", gap: 12, padding: 12 }}>
        <div style={{ borderRight: "1px solid #f0f0f0", paddingRight: 12 }}>
          <NodePalette descriptors={descriptors} loading={false} />
        </div>
        <GraphCanvas
          definition={definition}
          catalog={catalog}
          selectedId={selectedId}
          onSelect={setSelectedId}
          onDropNode={dropNode}
          onCreateEdge={createEdge}
          onPositions={(positions) => setDefinition((current) => applyPositions(current, positions))}
        />
        <div style={{ borderLeft: "1px solid #f0f0f0", paddingLeft: 12 }}>
          {summaries.length > 0 && (
            <Select
              style={{ width: "100%", marginBottom: 12 }}
              placeholder="选择工作流"
              onChange={(id: string) => void load(id)}
              options={summaries.map((item) => ({ value: item.workflowId, label: item.name }))}
            />
          )}
          <PropertyEditor
            node={selectedNode}
            descriptor={selectedNode ? descriptorOf(catalog, selectedNode.type) ?? null : null}
            edges={definition.edges}
            catalog={catalog}
            issues={issues}
            onApplyConfig={(config) => {
              if (selectedId) setDefinition((current) => setNodeConfig(current, selectedId, config));
            }}
            onDeleteNode={() => { setDefinition((current) => removeNode(current, selectedId ?? "")); setSelectedId(null); }}
            onDeleteEdge={(index) => setDefinition((current) => removeEdge(current, index))}
            onUpdateEdge={updateEdge}
            onSelectNode={setSelectedId}
          />
        </div>
      </Content>
    </Layout>
  );
}
