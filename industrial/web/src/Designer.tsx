// Industrial workflow designer (docs/collector.md §7.2–7.5, §9). Layout:
// toolbar / node palette / G6 canvas / schema-form property editor. The
// WorkflowDefinition is the single editable source; G6 data is a projection.

import {
  Alert, Button, Empty, Input, Layout, Modal, Select, Space, Spin, Table, Tag, Typography, message,
  type TableColumnsType,
} from "antd";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, ApiError } from "./api";
import { GraphCanvas } from "./GraphCanvas";
import { NodePalette } from "./NodePalette";
import { PropertyEditor } from "./PropertyEditor";
import {
  parseCollections, parseCommandResult, parseNodeDescriptors, parseValidationIssues,
  parseWorkflow, parseWorkflowSummaries, unwrapData,
} from "./dto";
import {
  addEdge, applyPositions, descriptorOf, emptyWorkflow, makeNode, nextNodeId,
  removeEdge, removeNode, resolveEdgePorts, setNodeConfig, validateWorkflow,
} from "./workflow-model";
import type {
  CollectionRow, NodeDescriptor, ValidationIssue, WorkflowDefinition, WorkflowSummary,
} from "./types";

const { Header, Content } = Layout;

const localKey = (id: string) => `industrial-designer:workflow:${id}`;

function loadLocal(id: string): WorkflowDefinition | null {
  try {
    const raw = localStorage.getItem(localKey(id));
    if (!raw) return null;
    const parsed = parseWorkflow(JSON.parse(raw));
    return parsed.workflowId ? parsed : null;
  } catch {
    return null;
  }
}

function parseResourceIds(raw: unknown): Set<string> {
  const data = unwrapData(raw);
  const list = (Array.isArray(data) ? data : []) as Array<Record<string, unknown>>;
  return new Set(list.map((item) => String(item?.id ?? "")).filter(Boolean));
}

const commandError = (error: unknown): string =>
  error instanceof Error ? error.message : String(error);

export function Designer() {
  const [messageApi, contextHolder] = message.useMessage();
  const [descriptors, setDescriptors] = useState<NodeDescriptor[]>([]);
  const [summaries, setSummaries] = useState<WorkflowSummary[]>([]);
  const [collections, setCollections] = useState<CollectionRow[]>([]);
  const [versions, setVersions] = useState<Array<Record<string, unknown>>>([]);
  const [resourceIds, setResourceIds] = useState<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [definition, setDefinition] = useState<WorkflowDefinition>(() => loadLocal("daily-collection") ?? emptyWorkflow());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [busy, setBusy] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const [resourceId, setResourceId] = useState("source-1");

  const catalog = useMemo(
    () => new Map(descriptors.map((descriptor) => [descriptor.nodeTypeId, descriptor])),
    [descriptors],
  );

  useEffect(() => {
    let alive = true;
    Promise.allSettled([api.nodes(), api.workflows(), api.collections(), api.resources(), api.query<Array<Record<string, unknown>>>("industrial/workflow-versions")])
      .then(([nodes, workflows, rows, resources, versions]) => {
        if (!alive) return;
        if (nodes.status === "fulfilled") setDescriptors(parseNodeDescriptors(nodes.value));
        else setLoadError(`节点描述加载失败: ${commandError(nodes.reason)}`);
        if (workflows.status === "fulfilled") setSummaries(parseWorkflowSummaries(workflows.value));
        if (rows.status === "fulfilled") setCollections(parseCollections(rows.value));
        if (resources.status === "fulfilled") setResourceIds(parseResourceIds(resources.value));
        if (versions.status === "fulfilled") setVersions(versions.value);
      })
      .finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
  }, []);

  const selectedNode = definition.nodes.find((node) => node.id === selectedId) ?? null;
  const selectedDescriptor = selectedNode ? descriptorOf(catalog, selectedNode.type) ?? null : null;

  const dropNode = useCallback((nodeTypeId: string, x: number, y: number) => {
    const descriptor = descriptorOf(catalog, nodeTypeId);
    if (!descriptor) {
      messageApi.warning(`未知节点类型: ${nodeTypeId}`);
      return;
    }
    const id = nextNodeId(definition.nodes);
    setDefinition({ ...definition, nodes: [...definition.nodes, makeNode(descriptor, id, x, y)] });
    setSelectedId(id);
    setIssues([]);
  }, [definition, catalog, messageApi]);

  // Decide synchronously: G6's `onCreate` must receive the verdict in the same
  // tick, and a rejected edge must never survive as a canvas-only artifact.
  const createEdge = useCallback((source: string, target: string): boolean => {
    const sourceNode = definition.nodes.find((node) => node.id === source);
    const targetNode = definition.nodes.find((node) => node.id === target);
    if (!sourceNode || !targetNode) return false;
    const sourceDescriptor = descriptorOf(catalog, sourceNode.type);
    const targetDescriptor = descriptorOf(catalog, targetNode.type);
    if (!sourceDescriptor || !targetDescriptor) return false;
    const ports = resolveEdgePorts(sourceDescriptor, targetDescriptor);
    if (!ports) {
      messageApi.warning("端口类型不兼容，无法连线");
      return false;
    }
    const result = addEdge(definition, { source, target, ...ports }, catalog);
    if (result.issue) {
      messageApi.warning(result.issue.message);
      return false;
    }
    setDefinition(result.definition);
    setIssues([]);
    return true;
  }, [definition, catalog, messageApi]);

  const positions = useCallback((next: Record<string, { x: number; y: number }>) => {
    setDefinition((current) => {
      const changed = current.nodes.some((node) => {
        const pos = next[node.id];
        return pos && (pos.x !== node.x || pos.y !== node.y);
      });
      return changed ? applyPositions(current, next) : current;
    });
  }, []);

  const applyConfig = useCallback((config: Record<string, unknown>) => {
    if (!selectedId) return;
    setDefinition((current) => setNodeConfig(current, selectedId, config));
    setIssues([]);
    messageApi.success("配置已应用");
  }, [selectedId, messageApi]);

  const updateEdge = useCallback((index: number, edge: WorkflowDefinition["edges"][number]) => {
    setDefinition((current) => ({
      ...current,
      edges: current.edges.map((existing, i) => i === index ? edge : existing),
    }));
    setIssues([]);
  }, []);

  const runCommand = async (name: string, payload: unknown, success: string): Promise<boolean> => {
    setBusy(true);
    try {
      const result = parseCommandResult(await api.command<unknown>(name, payload));
      if (!result.accepted) {
        messageApi.warning(`${name} 未被接受: ${result.error ?? result.code ?? "unknown"}`);
        return false;
      }
      messageApi.success(success);
      return true;
    } catch (error) {
      if (error instanceof ApiError && error.code === "not_found") {
        messageApi.warning(`后端未注册命令 ${name}`);
      } else {
        messageApi.error(commandError(error));
      }
      return false;
    } finally {
      setBusy(false);
    }
  };

  const save = async () => {
    try {
      const result = parseCommandResult(await api.command<unknown>("industrial/save-workflow", definition));
      if (!result.accepted) {
        messageApi.warning(`保存未被接受: ${result.error ?? result.code ?? "unknown"}`);
        return;
      }
      messageApi.success("工作流已保存");
    } catch (error) {
      if (error instanceof ApiError && error.code === "not_found") {
        // "if available": the backend may not publish a save command yet.
        localStorage.setItem(localKey(definition.workflowId), JSON.stringify(definition));
        messageApi.info("后端未注册 industrial/save-workflow，已保存到本地草稿");
      } else {
        messageApi.error(commandError(error));
      }
    }
  };

  const validate = () => {
    const found = validateWorkflow(definition, catalog, resourceIds.size > 0 ? resourceIds : undefined);
    setIssues(found);
    if (found.length === 0) messageApi.success("校验通过");
    else messageApi.warning(`发现 ${found.length} 个问题，见右侧定位列表`);
  };

  const publish = () => void runCommand(
    "industrial/publish-workflow",
    { workflowId: definition.workflowId, version: definition.version },
    "工作流已发布",
  );

  const run = () => void runCommand(
    "industrial/run-workflow",
    { workflowId: definition.workflowId, resourceId },
    "采集任务已启动",
  );

  const chooseWorkflow = (workflowId: string) => {
    const summary = summaries.find((item) => item.workflowId === workflowId);
    const loaded = loadLocal(workflowId);
    const next = loaded ?? {
      ...emptyWorkflow(workflowId),
      name: summary?.name ?? workflowId,
      version: summary?.version ?? 1,
    };
    setDefinition(next);
    setSelectedId(null);
    setIssues([]);
    if (!loaded) messageApi.info("后端仅发布工作流摘要；开始编辑一个空定义");
  };

  const collectionColumns: TableColumnsType<CollectionRow> = [
    { title: "Id", dataIndex: "id", key: "id" },
    { title: "路径", dataIndex: "path", key: "path" },
    { title: "源日期", dataIndex: "sourceDate", key: "sourceDate" },
    { title: "状态", dataIndex: "status", key: "status", render: (value: string) => <Tag>{value}</Tag> },
  ];

  return (
    <Layout className="industrial-designer">
      {contextHolder}
      <Header className="industrial-header">
        <Typography.Text strong style={{ color: "#fff", marginRight: 16 }}>Industrial Designer</Typography.Text>
        <Input
          style={{ width: 180 }}
          value={definition.name}
          placeholder="工作流名称"
          onChange={(event) => setDefinition((current) => ({ ...current, name: event.target.value }))}
        />
        <Select
          style={{ width: 200 }}
          value={definition.workflowId || undefined}
          placeholder="选择工作流"
          showSearch
          optionFilterProp="label"
          options={summaries.map((summary) => ({
            label: `${summary.name} (${summary.workflowId} v${summary.version})`,
            value: summary.workflowId,
          }))}
          onChange={chooseWorkflow}
        />
        <Input
          style={{ width: 140 }}
          value={resourceId}
          placeholder="resourceId"
          onChange={(event) => setResourceId(event.target.value)}
        />
        <Space size={8}>
          <Button onClick={() => void save()} disabled={busy}>保存</Button>
          <Button onClick={validate} disabled={busy}>校验</Button>
          <Button onClick={publish} disabled={busy}>发布</Button>
          <Button type="primary" onClick={run} disabled={busy}>运行</Button>
          <Button onClick={() => setHistoryOpen(true)}>执行记录</Button>
        </Space>
      </Header>
      <Content className="industrial-body">
        <NodePalette descriptors={descriptors} loading={loading} error={loadError} />
        <main className="industrial-canvas-wrap">
          {loading && <Spin style={{ position: "absolute", top: 12, right: 12 }} />}
          {definition.nodes.length === 0 && (
            <Alert
              type="info"
              showIcon
              closable
              message="从左侧拖入节点开始编排"
              description="拖动节点边缘的连线手柄创建边；端口类型不兼容时连线会被拒绝。"
              style={{ position: "absolute", top: 12, left: 12, zIndex: 2, maxWidth: 420 }}
            />
          )}
          <GraphCanvas
            definition={definition}
            catalog={catalog}
            selectedId={selectedId}
            onSelect={setSelectedId}
            onDropNode={dropNode}
            onCreateEdge={createEdge}
            onPositions={positions}
          />
        </main>
        <PropertyEditor
          node={selectedNode}
          descriptor={selectedDescriptor}
          edges={definition.edges}
          catalog={catalog}
          issues={issues}
          onApplyConfig={applyConfig}
          onDeleteNode={() => {
            if (!selectedId) return;
            setDefinition((current) => removeNode(current, selectedId));
            setSelectedId(null);
            setIssues([]);
          }}
          onDeleteEdge={(index) => {
            setDefinition((current) => removeEdge(current, index));
            setIssues([]);
          }}
          onUpdateEdge={updateEdge}
          onSelectNode={setSelectedId}
        />
      </Content>
      <Modal
        title="执行记录 (industrial/collections)"
        open={historyOpen}
        onCancel={() => setHistoryOpen(false)}
        footer={null}
        width={760}
      >
        {collections.length === 0
          ? <Empty description="暂无采集记录" />
          : <Table rowKey="id" size="small" columns={collectionColumns} dataSource={collections} />}
        <Table
          title={() => "发布版本 (industrial/workflow-versions)"}
          rowKey={(row) => `${String(row.workflowId ?? "")}:${String(row.version ?? "")}:${String(row.publishedAt ?? "")}`}
          size="small"
          style={{ marginTop: 16 }}
          dataSource={versions}
          columns={[
            { title: "Workflow", dataIndex: "workflowId", key: "workflowId" },
            { title: "Version", dataIndex: "version", key: "version" },
            { title: "Published", dataIndex: "publishedAt", key: "publishedAt" },
          ]}
        />
      </Modal>
    </Layout>
  );
}
