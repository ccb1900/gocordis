// Cordis-native workflow designer. The CONSOLE owns layout, routing, React,
// antd and the API; this module only registers a block renderer with the
// facade. WorkflowDefinition is the single editable source of truth.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, Button, Layout, Select, Space, Spin, Tag, Typography, message } from "antd";
import { GraphCanvas } from "./GraphCanvas";
import { NodePalette } from "./NodePalette";
import { PropertyEditor } from "./PropertyEditor";
import { parseNodeDescriptors, parseWorkflow, parseWorkflowSummaries, unwrapData } from "./dto";
import { getFacade } from "./facade";
import {
  addEdge, applyPositions, descriptorOf, emptyWorkflow, makeNode,
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

interface ExecutionState {
  status: string;
  error?: string | null;
  executed?: string[];
}

export function CordisDesigner({ ctx }: { ctx: DesignerContext }) {
  const [messageApi, contextHolder] = message.useMessage();
  const [descriptors, setDescriptors] = useState<NodeDescriptor[]>([]);
  const [summaries, setSummaries] = useState<WorkflowSummary[]>([]);
  const [resourceIds, setResourceIds] = useState<string[]>([]);
  const [resourceId, setResourceId] = useState("");
  const [loading, setLoading] = useState(true);
  const [loadErrors, setLoadErrors] = useState<string[]>([]);
  const [definition, setDefinition] = useState<WorkflowDefinition>(() => emptyWorkflow());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [busy, setBusy] = useState(false);
  const [connectMode, setConnectMode] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [execution, setExecution] = useState<ExecutionState | null>(null);
  const idRef = useRef(1);

  useEffect(() => {
    let alive = true;
    Promise.allSettled([
      ctx.hubQuery("industrial/nodes"),
      ctx.hubQuery("industrial/workflows"),
      ctx.hubQuery("industrial/resources"),
    ]).then(([nodes, workflows, resources]) => {
      if (!alive) return;
      const errors: string[] = [];
      if (nodes.status === "fulfilled") setDescriptors(parseNodeDescriptors(nodes.value));
      else errors.push(`节点描述: ${String(nodes.reason)}`);
      if (workflows.status === "fulfilled") {
        const list = parseWorkflowSummaries(workflows.value);
        setSummaries(list);
        if (list.length > 0) void loadWorkflow(list[0].workflowId);
      } else errors.push(`工作流: ${String(workflows.reason)}`);
      if (resources.status === "fulfilled") {
        const rows = (unwrapData(resources.value) ?? []) as Array<{ id?: string }>;
        const ids = rows.map((row) => String(row?.id ?? "")).filter(Boolean);
        setResourceIds(ids);
        setResourceId((current) => current || ids[0] || "");
      } else errors.push(`资源: ${String(resources.reason)}`);
      setLoadErrors(errors);
    }).finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [ctx]);

  const catalog = useMemo(
    () => new Map(descriptors.map((descriptor) => [descriptor.nodeTypeId, descriptor])),
    [descriptors]);
  const selectedNode = definition.nodes.find((node) => node.id === selectedId) ?? null;

  const command = useCallback(async (name: string, body: unknown, ok: string) => {
    setBusy(true);
    try {
      // The shell's ViewContext.hubCommand converts transport failures into a
      // page banner, so a block cannot distinguish them from success. The
      // facade's api.hubCommand is the SAME hub channel but preserves errors.
      await (getFacade().api.hubCommand as (name: string, body: unknown) => Promise<void>)(name, body);
      messageApi.success(ok);
      return true;
    } catch (error) {
      messageApi.error(`${name} failed: ${error instanceof Error ? error.message : String(error)}`);
      return false;
    } finally {
      setBusy(false);
    }
  }, [messageApi]);

  const loadWorkflow = useCallback(async (workflowId: string) => {
    try {
      const raw = await ctx.hubQuery("industrial/workflow-definitions");
      const rows = (unwrapData(raw) ?? []) as unknown[];
      const found = rows.find((row) => {
        const candidate = row as { workflowId?: string };
        return String(candidate?.workflowId ?? "") === workflowId;
      });
      if (found === undefined) {
        setDefinition({ ...emptyWorkflow(workflowId), name: workflowId });
        return;
      }
      setDefinition(parseWorkflow(found));
      const used = parseWorkflow(found).nodes.map((node) => node.id);
      const last = used.map((id) => /^n(\d+)$/.exec(id)).filter(Boolean)
        .reduce((max, match) => Math.max(max, Number(match![1])), 0);
      idRef.current = last + 1;
      setSelectedId(null);
      setIssues([]);
      setDirty(false);
    } catch (error) {
      messageApi.error(`加载工作流失败: ${error instanceof Error ? error.message : String(error)}`);
    }
  }, [ctx, messageApi]);

  const refreshExecution = useCallback(async () => {
    try {
      const raw = await ctx.hubQuery("industrial/execution");
      if (raw === null || raw === undefined) return;
      const row = raw as ExecutionState;
      setExecution({ status: String(row.status ?? ""), error: row.error, executed: row.executed ?? [] });
    } catch {
      // Execution history is diagnostics; a missing query must not block editing.
    }
  }, [ctx]);

  const save = useCallback(async () => {
    if (await command("industrial/save-workflow", definition, "已保存")) setDirty(false);
  }, [command, definition]);

  const publish = useCallback(async () => {
    if (dirty) {
      messageApi.warning("草稿尚未保存，请先保存再发布");
      return;
    }
    if (await command("industrial/publish-workflow", { workflowId: definition.workflowId }, "已发布")) {
      await refreshExecution();
    }
  }, [command, definition.workflowId, dirty, messageApi, refreshExecution]);

  const run = useCallback(async () => {
    if (dirty) {
      messageApi.warning("草稿尚未保存，请先保存再运行");
      return;
    }
    if (await command("industrial/run-workflow",
      { workflowId: definition.workflowId, resourceId }, "采集已启动")) {
      await refreshExecution();
    }
  }, [command, definition.workflowId, dirty, messageApi, refreshExecution, resourceId]);

  const dropNode = useCallback((type: string, x: number, y: number) => {
    const descriptor = descriptorOf(catalog, type);
    if (!descriptor) return;
    const id = `n${idRef.current++}`;
    setDefinition((current) => ({
      ...current,
      nodes: [...current.nodes, makeNode(descriptor, id, x, y)],
    }));
    setSelectedId(id);
    setIssues([]);
    setDirty(true);
  }, [catalog]);

  const createEdge = useCallback((source: string, target: string) => {
    const sourceNode = definition.nodes.find((node) => node.id === source);
    const targetNode = definition.nodes.find((node) => node.id === target);
    if (!sourceNode || !targetNode) {
      messageApi.warning("连线失败：端点不在画布上");
      return null;
    }
    const sourceDescriptor = descriptorOf(catalog, sourceNode.type);
    const targetDescriptor = descriptorOf(catalog, targetNode.type);
    if (!sourceDescriptor || !targetDescriptor) return null;
    const ports = resolveEdgePorts(sourceDescriptor, targetDescriptor);
    if (!ports) {
      messageApi.warning(`端口类型不兼容：${sourceDescriptor.name} → ${targetDescriptor.name}`);
      return null;
    }
    const result = addEdge(definition, {
      source, sourcePort: ports.sourcePort, target, targetPort: ports.targetPort,
    }, catalog);
    if (result.issue) {
      messageApi.warning(`${result.issue.code}: ${result.issue.message}`);
      setIssues((current) => [...current, result.issue!]);
      return null;
    }
    if (result.definition === definition) {
      messageApi.info("这条连线已经存在");
      return ports;
    }
    setDefinition(result.definition);
    setDirty(true);
    return ports;
  }, [catalog, definition, messageApi]);

  const validate = useCallback(() => {
    const found = validateWorkflow(definition, catalog, new Set(resourceIds));
    setIssues(found);
    if (found.length === 0) messageApi.success("本地校验通过（发布时服务端仍会复核）");
    else messageApi.warning(`发现 ${found.length} 个问题，见右侧定位列表`);
  }, [definition, catalog, resourceIds, messageApi]);

  const updateEdge = useCallback((index: number, edge: WorkflowEdge) => {
    setDefinition((current) => ({
      ...current,
      edges: current.edges.map((existing, at) => at === index ? edge : existing),
    }));
    setDirty(true);
  }, []);

  if (loading) return <Spin tip="加载节点描述与资源…" />;

  return (
    <Layout style={{ minHeight: 520, border: "1px solid #f0f0f0" }}>
      {contextHolder}
      <Header style={{ background: "#001529", display: "flex", alignItems: "center", gap: 12, flexWrap: "wrap" }}>
        <Typography.Text strong style={{ color: "#fff" }}>Industrial Designer</Typography.Text>
        <Tag color="blue">Cordis block renderer</Tag>
        {dirty && <Tag color="orange">未保存</Tag>}
        <Space>
          <Button type={connectMode ? "primary" : "default"} onClick={() => setConnectMode((on) => !on)}>
            {connectMode ? "退出连线" : "连线模式"}
          </Button>
          <Button loading={busy} onClick={() => void save()}>保存</Button>
          <Button onClick={validate}>校验</Button>
          <Button loading={busy} onClick={() => void publish()}>发布</Button>
          <Button type="primary" loading={busy} onClick={() => void run()}>运行</Button>
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
          connectMode={connectMode}
          onSelect={setSelectedId}
          onDropNode={dropNode}
          onCreateEdge={createEdge}
          onPositions={(positions) => {
            setDefinition((current) => applyPositions(current, positions));
            setDirty(true);
          }}
        />
        <div style={{ borderLeft: "1px solid #f0f0f0", paddingLeft: 12 }}>
          {loadErrors.length > 0 && (
            <Alert type="warning" showIcon style={{ marginBottom: 12 }}
              message="部分设计器数据未加载"
              description={loadErrors.join("；")} />
          )}
          {summaries.length > 0 && (
            <Select
              style={{ width: "100%", marginBottom: 12 }}
              value={definition.workflowId}
              placeholder="选择工作流"
              onChange={(id: string) => void loadWorkflow(id)}
              options={summaries.map((item) => ({ value: item.workflowId, label: item.name }))}
            />
          )}
          {execution && (
            <Alert
              style={{ marginBottom: 12 }}
              type={execution.error ? "error" : "success"}
              showIcon
              message={`执行状态: ${execution.status}`}
              description={execution.error ?? (execution.executed?.length
                ? `已执行节点: ${execution.executed.join(", ")}` : undefined)}
            />
          )}
          <PropertyEditor
            node={selectedNode}
            descriptor={selectedNode ? descriptorOf(catalog, selectedNode.type) ?? null : null}
            edges={definition.edges}
            catalog={catalog}
            issues={issues}
            onApplyConfig={(config) => {
              if (selectedId) {
                setDefinition((current) => setNodeConfig(current, selectedId, config));
                setDirty(true);
              }
            }}
            onDeleteNode={() => {
              setDefinition((current) => removeNode(current, selectedId ?? ""));
              setSelectedId(null);
              setDirty(true);
            }}
            onDeleteEdge={(index) => {
              setDefinition((current) => removeEdge(current, index));
              setDirty(true);
            }}
            onUpdateEdge={updateEdge}
            onSelectNode={setSelectedId}
          />
        </div>
      </Content>
    </Layout>
  );
}
