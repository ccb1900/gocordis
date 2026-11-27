// Quick collection wizard. It produces the SAME WorkflowDefinition the designer
// edits and delegates persistence/execution to the existing commands; there is
// no second workflow model or execution path.

import { Alert, Button, Input, Select, Space, Typography } from "./facade-antd";
import { useCallback, useEffect, useMemo, useState } from "react";
import { parseNodeDescriptors, parseResourceOptions } from "./dto";
import { emptyWorkflow } from "./workflow-model";
import type { NodeDescriptor, ResourceOption, WorkflowDefinition, WorkflowEdge, WorkflowNode } from "./types";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
  navigate?: (route: string) => void;
}

const sourceTypes = ["filesystem.local", "filesystem.unc"];
const readerTypes = ["reader.csv", "reader.excel", "reader.json"];
const sinkTypes = ["sink.sqlite", "sink.sqlserver", "sink.postgresql", "sink.mysql"];

export function CollectionWizard({ block, ctx }: {
  block?: { query?: string; nodesQuery?: string }; ctx: Context;
}) {
  const [resources, setResources] = useState<ResourceOption[]>([]);
  const [descriptors, setDescriptors] = useState<NodeDescriptor[]>([]);
  const [name, setName] = useState("");
  const [sourceId, setSourceId] = useState("");
  const [format, setFormat] = useState("csv");
  const [targetIds, setTargetIds] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    void (async () => {
      try {
        setResources(parseResourceOptions(await ctx.hubQuery(block?.query ?? "industrial/resources")));
        setDescriptors(parseNodeDescriptors(await ctx.hubQuery(block?.nodesQuery ?? "industrial/nodes")));
      } catch (cause) {
        setError(cause instanceof Error ? cause.message : String(cause));
      }
    })();
  }, [block?.nodesQuery, block?.query, ctx]);

  const sources = resources.filter((resource) => sourceTypes.includes(resource.type));
  const targets = resources.filter((resource) => sinkTypes.some((sink) => resource.type === sink.split(".")[1]));
  const lookup = useMemo(
    () => new Map(descriptors.map((descriptor) => [descriptor.nodeTypeId, descriptor])),
    [descriptors]);

  const create = useCallback(async () => {
    if (!name.trim()) { setError("请输入任务名称"); return; }
    if (!sourceId) { setError("请选择数据来源"); return; }
    if (targetIds.length === 0) { setError("请至少选择一个目标"); return; }
    setBusy(true); setError(null);
    try {
      const suffix = typeof crypto !== "undefined" && "randomUUID" in crypto
        ? crypto.randomUUID().replace(/-/g, "").slice(0, 10)
        : Date.now().toString(36);
      const workflowId = `quick-${suffix}`;
      const sourceResource = resources.find((resource) => resource.id === sourceId)!;
      const sourceType = `industrial.${sourceResource.type}`;
      const readerType = `industrial.reader.${format}`;
      const node = (id: string, type: string, x: number, y: number, config: Record<string, unknown>): WorkflowNode =>
        ({ id, type, config, x, y });
      const edge = (from: string, to: string): WorkflowEdge => {
        const fromDescriptor = lookup.get(nodeMap[from])!;
        const toDescriptor = lookup.get(nodeMap[to])!;
        return { source: from, sourcePort: fromDescriptor.outputs[0]?.name ?? "output",
          target: to, targetPort: toDescriptor.inputs[0]?.name ?? "input" };
      };
      const nodes: WorkflowNode[] = [
        node("src", sourceType, 80, 120, { resourceId: sourceId }),
        node("read", readerType, 280, 120, {}),
        ...targetIds.map((targetId, index) =>
          node(`sink${index + 1}`, `industrial.sink.${resources.find((r) => r.id === targetId)!.type}`,
            500, 80 + index * 120, { targetResourceId: targetId })),
      ];
      const nodeMap: Record<string, string> = Object.fromEntries(nodes.map((item) => [item.id, item.type]));
      const edges: WorkflowEdge[] = [edge("src", "read"), ...targetIds.map((_, index) => edge("read", `sink${index + 1}`))];
      const definition: WorkflowDefinition = {
        ...emptyWorkflow(workflowId),
        name: name.trim(),
        nodes, edges,
        resourceReferences: [sourceId, ...targetIds],
        layout: { lookbackDays: 1 },
      };
      await ctx.hubCommand("industrial/save-workflow", definition);
      await ctx.hubCommand("industrial/publish-workflow", { workflowId });
      ctx.navigate?.(`/industrial/designer?workflowId=${encodeURIComponent(workflowId)}`);
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    } finally {
      setBusy(false);
    }
  }, [ctx, format, lookup, name, resources, sourceId, targetIds]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Typography.Title level={4} style={{ margin: 0 }}>快速创建采集任务</Typography.Title>
      <Typography.Text type="secondary">生成标准工作流，可在设计器中继续修改。</Typography.Text>
      {error && <Alert type="error" showIcon message={error} />}
      <Input aria-label="任务名称" placeholder="任务名称" value={name}
        onChange={(event: { target: { value: string } }) => setName(event.target.value)} />
      <Space wrap>
        <Select aria-label="数据来源" style={{ width: 260 }} placeholder="数据来源" value={sourceId || undefined}
          onChange={(value: string) => setSourceId(value)}
          options={sources.map((resource) => ({ value: resource.id, label: `${resource.name} (${resource.type})` }))} />
        <Select aria-label="数据格式" style={{ width: 160 }} value={format}
          onChange={(value: string) => setFormat(value)}
          options={["csv", "excel", "json"].map((value) => ({ value, label: value.toUpperCase() }))} />
      </Space>
      <Select aria-label="目标资源" mode="multiple" style={{ width: 420 }} placeholder="目标资源"
        value={targetIds} onChange={(value: string[]) => setTargetIds(value)}
        options={targets.map((resource) => ({ value: resource.id, label: `${resource.name} (${resource.type})` }))} />
      <Button type="primary" loading={busy} onClick={() => void create()}>创建并发布</Button>
    </Space>
  );
}
