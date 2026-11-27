// Cordis-native workflow designer. The CONSOLE owns layout, routing, React,
// antd and the API; this module only registers a block renderer with the
// facade. WorkflowDefinition is the single editable source of truth.

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Alert, Button, Drawer, InputNumber, Layout, Select, Space, Spin, Table, Tag, Typography, message } from "antd";
import { GraphCanvas } from "./GraphCanvas";
import { NodePalette } from "./NodePalette";
import { PropertyEditor } from "./PropertyEditor";
import { TriggerEditor } from "./TriggerEditor";
import { BackfillPreview } from "./BackfillPreview";
import {
  parseExecution, parseNodeDescriptors, parseResourceOptions, parseWorkflow, parseWorkflowSummaries, unwrapData,
} from "./dto";
import { getFacade } from "./facade";
import {
  addEdge, applyPositions, descriptorOf, emptyWorkflow, makeNode,
  removeEdge, removeNode, resolveEdgePorts, setNodeConfig, validateWorkflow,
} from "./workflow-model";
import { autoLayout, freePosition, repairOverlaps } from "./layout-model";
import type {
  ExecutionStateView, NodeDescriptor, ResourceOption, TriggerDefinition, ValidationIssue,
  WorkflowDefinition, WorkflowEdge, WorkflowNode, WorkflowSummary,
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
  const [resources, setResources] = useState<ResourceOption[]>([]);
  const [resourceId, setResourceId] = useState("");
  const [backfillDays, setBackfillDays] = useState(1);
  const [loading, setLoading] = useState(true);
  const [loadErrors, setLoadErrors] = useState<string[]>([]);
  const [definition, setDefinition] = useState<WorkflowDefinition>(() => emptyWorkflow());
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [issues, setIssues] = useState<ValidationIssue[]>([]);
  const [busy, setBusy] = useState(false);
  const [libraryOpen, setLibraryOpen] = useState(true);
  const [propertiesOpen, setPropertiesOpen] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [historyVersion, setHistoryVersion] = useState(0);
  const historyPast = useRef<WorkflowDefinition[]>([]);
  const historyFuture = useRef<WorkflowDefinition[]>([]);
  const graphRef = useRef<any>(null);
  const clipboardRef = useRef<WorkflowNode | null>(null);
  const [execution, setExecution] = useState<ExecutionStateView | null>(null);
  const [triggerOpen, setTriggerOpen] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const idRef = useRef(1);
  const ctxRef = useRef(ctx);
  ctxRef.current = ctx;
  const triggerRef = useRef<TriggerDefinition>(definition.trigger);

  useEffect(() => { triggerRef.current = definition.trigger; }, [definition.trigger]);

  useEffect(() => {
    let alive = true;
    const hub = ctxRef.current.hubQuery;
    Promise.allSettled([
      hub("industrial/nodes"),
      hub("industrial/workflows"),
      hub("industrial/resources"),
    ]).then(([nodes, workflows, resources]) => {
      if (!alive) return;
      const errors: string[] = [];
      if (nodes.status === "fulfilled") setDescriptors(parseNodeDescriptors(nodes.value));
      else errors.push(`节点描述: ${String(nodes.reason)}`);
      if (workflows.status === "fulfilled") {
        const list = parseWorkflowSummaries(workflows.value);
        setSummaries(list);
        const requested = new URLSearchParams(window.location.search).get("workflowId");
        const target = requested && list.some((item) => item.workflowId === requested)
          ? requested
          : list[0]?.workflowId;
        if (target) void loadWorkflow(target);
      } else errors.push(`工作流: ${String(workflows.reason)}`);
      if (resources.status === "fulfilled") {
        const parsed = parseResourceOptions(resources.value);
        setResources(parsed);
        const ids = parsed.map((resource) => resource.id);
        setResourceIds(ids);
        setResourceId((current) => current || ids[0] || "");
      } else errors.push(`资源: ${String(resources.reason)}`);
      setLoadErrors(errors);
    }).finally(() => { if (alive) setLoading(false); });
    return () => { alive = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const catalog = useMemo(
    () => new Map(descriptors.map((descriptor) => [descriptor.nodeTypeId, descriptor])),
    [descriptors]);
  const selectedNode = definition.nodes.find((node) => node.id === selectedId) ?? null;

  const command = useCallback(async (name: string, body: unknown, ok: string) => {
    setBusy(true);
    try {
      // The shell's ViewContext.hubCommand hides transport errors; the facade
      // hub channel preserves them. Rejections are surfaced, never swallowed.
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
      const parsed = found === undefined
        ? { ...emptyWorkflow(workflowId), name: workflowId }
        : parseWorkflow(found);
      const normalized = repairOverlaps(parsed);
      setDefinition(normalized);
      historyPast.current = [];
      historyFuture.current = [];
      setHistoryVersion((v) => v + 1);
      setBackfillDays(Number(normalized.layout.lookbackDays ?? 1) || 1);
      const last = parsed.nodes.map((node) => /^n(\d+)$/.exec(node.id)).filter(Boolean)
        .reduce((max, match) => Math.max(max, Number(match![1])), 0);
      idRef.current = last + 1;
      setSelectedId(null);
      setPropertiesOpen(false);
      setIssues([]);
      setDirty(false);
    } catch (error) {
      messageApi.error(`加载工作流失败: ${error instanceof Error ? error.message : String(error)}`);
    }
  }, [ctx, messageApi]);

  const refreshExecution = useCallback(async () => {
    try {
      const row = parseExecution(await ctx.hubQuery("industrial/execution"));
      if (row === null) return;
      setExecution(row);
    } catch {
      // Execution history is diagnostics; a missing query must not block editing.
    }
  }, [ctx]);

  const save = useCallback(async () => {
    const referenced = new Set<string>(definition.resourceReferences ?? []);
    for (const node of definition.nodes) {
      for (const key of ["resourceId", "targetResourceId"]) {
        const value = node.config?.[key];
        if (typeof value === "string" && value.trim()) referenced.add(value.trim());
      }
    }
    const withPolicy = {
      ...definition,
      trigger: triggerRef.current,
      resourceReferences: [...referenced],
      layout: { ...definition.layout, lookbackDays: backfillDays },
    };
    if (await command("industrial/save-workflow", withPolicy, "已保存")) {
      setDefinition(withPolicy);
      setDirty(false);
    }
  }, [command, definition, backfillDays]);

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

  const commit = useCallback((next: WorkflowDefinition) => {
    if (next === definition) return;
    if (historyPast.current.length >= 50) historyPast.current.shift();
    historyPast.current.push(definition);
    historyFuture.current = [];
    setHistoryVersion((v) => v + 1);
    setDefinition(next);
    setDirty(true);
  }, [definition]);

  const undo = useCallback(() => {
    const previous = historyPast.current.pop();
    if (!previous) return;
    historyFuture.current.push(definition);
    setDefinition(previous);
    setDirty(true);
    setHistoryVersion((v) => v + 1);
  }, [definition]);

  const redo = useCallback(() => {
    const next = historyFuture.current.pop();
    if (!next) return;
    historyPast.current.push(definition);
    setDefinition(next);
    setDirty(true);
    setHistoryVersion((v) => v + 1);
  }, [definition]);

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      const target = event.target as HTMLElement | null;
      if (target && /INPUT|TEXTAREA|SELECT/.test(target.tagName)) return;
      const key = event.key.toLowerCase();
      if ((event.ctrlKey || event.metaKey) && key === "z") {
        event.preventDefault();
        if (event.shiftKey) redo(); else undo();
        return;
      }
      if ((event.ctrlKey || event.metaKey) && key === "y") {
        event.preventDefault(); redo(); return;
      }
      if (event.key === "Delete" && selectedId) {
        event.preventDefault();
        commit(removeNode(definition, selectedId));
        setSelectedId(null);
        return;
      }
      if ((event.ctrlKey || event.metaKey) && key === "c" && selectedId) {
        clipboardRef.current = definition.nodes.find((node) => node.id === selectedId) ?? null;
        return;
      }
      if ((event.ctrlKey || event.metaKey) && key === "v" && clipboardRef.current) {
        event.preventDefault();
        const descriptor = descriptorOf(catalog, clipboardRef.current.type);
        if (!descriptor) return;
        const id = `n${idRef.current++}`;
        const spot = freePosition(definition, clipboardRef.current.x + 40, clipboardRef.current.y + 40);
        commit({ ...definition,
          nodes: [...definition.nodes, makeNode(descriptor, id, spot.x, spot.y)] });
        setSelectedId(id);
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [catalog, commit, definition, redo, selectedId, undo]);

  const autoArrange = useCallback(() => {
    commit(autoLayout(definition));
    requestAnimationFrame(() => graphRef.current?.fitView?.({ when: "always" }));
  }, [commit, definition]);

  const dropNode = useCallback((type: string, x: number, y: number) => {
    const descriptor = descriptorOf(catalog, type);
    if (!descriptor) return;
    const id = `n${idRef.current++}`;
    const spot = freePosition(definition, x, y);
    commit({ ...definition, nodes: [...definition.nodes, makeNode(descriptor, id, spot.x, spot.y)] });
    setSelectedId(id);
    setPropertiesOpen(true);
    setIssues([]);
  }, [catalog, commit, definition]);

  const addNodeFromPalette = useCallback((type: string) => {
    const index = idRef.current - 1;
    dropNode(type, 80 + (index % 3) * 190, 80 + Math.floor(index / 3) * 130);
  }, [dropNode]);

  const createEdge = useCallback((from: string, to: string) => {
    const first = definition.nodes.find((node) => node.id === from);
    const second = definition.nodes.find((node) => node.id === to);
    if (!first || !second) {
      messageApi.warning("连线失败：端点不在画布上");
      return null;
    }

    // A user may drag from an output port OR from an input port. Resolve both
    // directions and keep the domain edge output→input regardless of gesture.
    const firstDescriptor = descriptorOf(catalog, first.type);
    const secondDescriptor = descriptorOf(catalog, second.type);
    if (!firstDescriptor || !secondDescriptor) return null;
    let source = first;
    let target = second;
    let ports = resolveEdgePorts(firstDescriptor, secondDescriptor);
    if (!ports) {
      const reversed = resolveEdgePorts(secondDescriptor, firstDescriptor);
      if (!reversed) {
        messageApi.warning(`端口类型不兼容：${firstDescriptor.name} → ${secondDescriptor.name}`);
        return null;
      }
      source = second;
      target = first;
      ports = reversed;
    }

    const result = addEdge(definition, {
      source: source.id, sourcePort: ports.sourcePort,
      target: target.id, targetPort: ports.targetPort,
    }, catalog);
    if (result.issue) {
      messageApi.warning(`${result.issue.code}: ${result.issue.message}`);
      setIssues((current) => [...current, result.issue!]);
      return null;
    }
    if (result.definition === definition) {
      // Returning ports would let G6 add its temporary edge even though the
      // domain rejected the duplicate; returning null discards that phantom.
      messageApi.info("这条连线已经存在");
      return null;
    }
    commit(result.definition);
    return ports;
  }, [catalog, commit, definition, messageApi]);

  const validate = useCallback(() => {
    const found = validateWorkflow(definition, catalog, new Set(resourceIds));
    setIssues(found);
    if (found.length === 0) messageApi.success("本地校验通过（发布时服务端仍会复核）");
    else {
      messageApi.warning(`发现 ${found.length} 个问题，见属性抽屉定位列表`);
      setPropertiesOpen(true);
    }
  }, [definition, catalog, resourceIds, messageApi]);

  const updateEdge = useCallback((index: number, edge: WorkflowEdge) => {
    commit({ ...definition,
      edges: definition.edges.map((existing, at) => at === index ? edge : existing) });
  }, [commit, definition]);

  if (loading) return <Spin tip="加载节点描述与资源…" />;

  return (
    <div className="industrial-designer">
      {contextHolder}
      <Header className="industrial-header">
        <div className="industrial-title">
          <Typography.Text strong>Industrial Workflow</Typography.Text>
          <Typography.Text type="secondary">{definition.name || definition.workflowId}</Typography.Text>
          {dirty && <Tag color="orange">未保存</Tag>}
          {execution && <Tag color={execution.error ? "error" : "success"}>{execution.status}</Tag>}
        </div>
        <div className="industrial-toolbar">
          <Button
            size="small"
            data-testid="toggle-library"
            aria-expanded={libraryOpen}
            onClick={() => setLibraryOpen((open) => !open)}
          >
            {libraryOpen ? "收起节点库" : "节点库"}
          </Button>
          <Button size="small" data-testid="toggle-properties"
            onClick={() => setPropertiesOpen((open) => !open)}>属性</Button>
          <Button size="small" data-testid="open-trigger" onClick={() => setTriggerOpen(true)}>触发器</Button>
          <Button size="small" data-testid="open-backfill-preview"
            onClick={() => setPreviewOpen(true)}>补采预览</Button>
          <Button size="small" data-testid="designer-undo" disabled={historyPast.current.length === 0}
            onClick={undo}>撤销</Button>
          <Button size="small" data-testid="designer-redo" disabled={historyFuture.current.length === 0}
            onClick={redo}>重做</Button>
          <Button size="small" data-testid="designer-auto-layout" onClick={autoArrange}>自动布局</Button>
          <Button size="small" data-testid="designer-fit" onClick={() => graphRef.current?.fitView?.({ when: "always" })}>适应画布</Button>
          <Button size="small" data-testid="designer-center" onClick={() => graphRef.current?.fitCenter?.()}>居中</Button>
          <span className="industrial-toolbar-divider" />
          <Space size={4}>
            <Button size="small" loading={busy} onClick={() => void save()}>保存</Button>
            <Button size="small" onClick={validate}>校验</Button>
            <Button size="small" loading={busy} onClick={() => void publish()}>发布</Button>
            <Button size="small" type="primary" loading={busy} onClick={() => void run()}>运行</Button>
          </Space>
          <select
            className="industrial-resource-select"
            aria-label="运行资源"
            value={resourceId}
            onChange={(event) => setResourceId(event.target.value)}
          >
            <option value="">运行资源</option>
            {resourceIds.map((id) => <option key={id} value={id}>{id}</option>)}
          </select>
          <InputNumber
            className="industrial-backfill-input"
            size="small"
            min={1}
            max={365}
            value={backfillDays}
            onChange={(value) => {
              setBackfillDays(Number(value) || 1);
              setDirty(true);
            }}
            addonBefore="补采"
          />
        </div>
      </Header>
      <Content className={`industrial-body${libraryOpen ? "" : " industrial-body--collapsed"}`}>
        {libraryOpen ? (
          <aside className="industrial-palette">
            {summaries.length > 0 && (
              <Select
                className="industrial-workflow-select"
                size="small"
                value={definition.workflowId}
                placeholder="选择工作流"
                onChange={(id: string) => void loadWorkflow(id)}
                options={summaries.map((item) => ({ value: item.workflowId, label: item.name }))}
              />
            )}
            <NodePalette descriptors={descriptors} loading={false} onAdd={addNodeFromPalette} />
          </aside>
        ) : (
          <div className="industrial-palette-rail">
            <Button type="text" size="small" onClick={() => setLibraryOpen(true)}>节点库</Button>
          </div>
        )}
        <section className="industrial-center">
          {loadErrors.length > 0 && (
            <Alert type="warning" showIcon style={{ marginBottom: 8 }}
              message="部分设计器数据未加载" description={loadErrors.join("；")} />
          )}
          <GraphCanvas
            definition={definition}
            catalog={catalog}
            selectedId={selectedId}
            onSelect={(id) => {
              setSelectedId(id);
              if (id) setPropertiesOpen(true);
            }}
            onDropNode={dropNode}
            onCreateEdge={createEdge}
            onPositions={(positions) => commit(applyPositions(definition, positions))}
            onSelectIds={(ids) => { if (ids.length) setSelectedId(ids[0]); }}
            onGraphReady={(graph: any) => { graphRef.current = graph; }}
          />
        </section>
      </Content>
      <Drawer
        className="industrial-properties-drawer"
        title="节点属性"
        placement="right"
        width={420}
        open={propertiesOpen}
        mask={false}
        getContainer={false}
        onClose={() => setPropertiesOpen(false)}
      >
        {execution && (
          <Alert
            style={{ marginBottom: 12 }}
            type={execution.error ? "error" : "success"}
            showIcon
            message={`执行状态: ${execution.status}`}
            description={
              <Space direction="vertical" size={8} style={{ width: "100%" }}>
                {execution.error && <span>{execution.error}</span>}
                {execution.executed.length > 0 && (
                  <span>已执行节点: {execution.executed.join(", ")}</span>
                )}
                {execution.nodes.length > 0 && (
                  <Table
                    size="small"
                    pagination={false}
                    rowKey="nodeId"
                    dataSource={execution.nodes}
                    columns={[
                      { title: "节点", dataIndex: "nodeId", key: "nodeId" },
                      { title: "状态", dataIndex: "status", key: "status" },
                      { title: "记录", dataIndex: "records", key: "records" },
                      { title: "文件", dataIndex: "files", key: "files" },
                      {
                        title: "错误 / 样本",
                        key: "detail",
                        render: (_, node) => node.error
                          ?? (node.samples.length > 0 ? node.samples.join(", ") : "—"),
                      },
                    ]}
                  />
                )}
              </Space>
            }
          />
        )}
        <PropertyEditor
          node={selectedNode}
          descriptor={selectedNode ? descriptorOf(catalog, selectedNode.type) ?? null : null}
          nodes={definition.nodes}
          edges={definition.edges}
          catalog={catalog}
          resources={resources}
          issues={issues}
          onApplyConfig={(config) => {
            if (selectedId) commit(setNodeConfig(definition, selectedId, config));
          }}
          onDeleteNode={() => {
            commit(removeNode(definition, selectedId ?? ""));
            setSelectedId(null);
          }}
          onDeleteEdge={(index) => commit(removeEdge(definition, index))}
          onCreateEdge={(from, to) => void createEdge(from, to)}
          onUpdateEdge={updateEdge}
          onSelectNode={(id) => {
            setSelectedId(id);
            setPropertiesOpen(true);
          }}
        />
      </Drawer>
      {triggerOpen && <TriggerEditor
        open={triggerOpen}
        onClose={() => setTriggerOpen(false)}
        trigger={definition.trigger}
        resourceIds={resourceIds}
        workflowId={definition.workflowId}
        ctx={ctx}
        onSave={(trigger: TriggerDefinition) => {
          triggerRef.current = trigger;
          commit({ ...definition, trigger });
        }}
      />}
      {previewOpen && <BackfillPreview
        open={previewOpen}
        onClose={() => setPreviewOpen(false)}
        ctx={ctx}
        workflowId={definition.workflowId}
        resourceId={resourceId}
        lookbackDays={backfillDays}
      />}
    </div>
  );
}
