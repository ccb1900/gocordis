// Bulk collection binding console. 80 machines x 10 file profiles creates 800
// binding rows, never 800 workflow definitions.

import { Alert, Button, Input, InputNumber, Select, Space, Table, Typography } from "./facade-antd";
import { useCallback, useEffect, useMemo, useState } from "react";
import { unwrapData } from "./dto";

interface Context {
  hubQuery: <T = unknown>(name: string, params?: Record<string, string>) => Promise<T>;
  hubCommand: (name: string, body: unknown) => Promise<void>;
}

const rows = (raw: unknown): any[] => {
  const data = unwrapData(raw) as unknown;
  return Array.isArray(data) ? data : [];
};

export function BindingConsole({ block, ctx }: { block?: { query?: string }; ctx: Context }) {
  const [bindings, setBindings] = useState<any[]>([]);
  const [machines, setMachines] = useState<any[]>([]);
  const [profiles, setProfiles] = useState<any[]>([]);
  const [workflows, setWorkflows] = useState<any[]>([]);
  const [resources, setResources] = useState<any[]>([]);
  const [machineIds, setMachineIds] = useState<string[]>([]);
  const [profileIds, setProfileIds] = useState<string[]>([]);
  const [workflowId, setWorkflowId] = useState<string | undefined>();
  const [version, setVersion] = useState(1);
  const [sourcePattern, setSourcePattern] = useState("resource-{machine}");
  const [targetIds, setTargetIds] = useState<string[]>([]);
  const [csv, setCsv] = useState("");
  const [packageText, setPackageText] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setBindings(rows(await ctx.hubQuery(block?.query ?? "industrial/bindings")));
      setMachines(rows(await ctx.hubQuery("industrial/machines")));
      setProfiles(rows(await ctx.hubQuery("industrial/file-profiles")));
      setWorkflows(rows(await ctx.hubQuery("industrial/workflows")));
      setResources(rows(await ctx.hubQuery("industrial/resources")));
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
  }, [block?.query, ctx]);
  useEffect(() => { void load(); }, [load]);

  const preview = useMemo(() => {
    const conflicts: string[] = [];
    for (const machine of machineIds) for (const profile of profileIds) {
      const id = `binding:${workflowId ?? ""}:v${version}:${machine}:${profile}`;
      if (bindings.some((binding) => binding.id === id)) conflicts.push(id);
    }
    const missing = targetIds.filter((id) => !resources.some((resource) => resource.id === id));
    const missingSources = machineIds
      .map((machine) => sourcePattern.replace("{machine}", machine))
      .filter((id) => !resources.some((resource) => resource.id === id));
    return { total: machineIds.length * profileIds.length,
      create: machineIds.length * profileIds.length - conflicts.length,
      conflicts, missing: [...missing, ...missingSources] };
  }, [bindings, machineIds, profileIds, resources, sourcePattern, targetIds, version, workflowId]);

  const create = useCallback(async () => {
    if (!workflowId || machineIds.length === 0 || profileIds.length === 0 || targetIds.length === 0) {
      setError("请选择工作流、机台、文件配置和目标资源"); return;
    }
    if (preview.missing.length > 0) { setError("存在缺失资源，请先创建后再导入"); return; }
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/create-bindings", {
        machineIds, fileProfileIds: profileIds, workflowId, workflowVersion: version,
        sourceResourcePattern: sourcePattern, targetResourceIds: targetIds,
      });
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, load, preview.missing.length, profileIds, sourcePattern, targetIds, version, workflowId, machineIds]);

  const importCsv = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/import-machines", { csv });
      setCsv(""); await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, csv, load]);

  const exportConfig = useCallback(async () => {
    setError(null);
    try {
      const raw = await ctx.hubQuery("industrial/export-config");
      const payload = (raw as any)?.data ?? raw;
      setPackageText(JSON.stringify(payload, null, 2));
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
  }, [ctx]);

  const importConfig = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/import-config", JSON.parse(packageText));
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, load, packageText]);

  return (
    <Space direction="vertical" size={12} style={{ width: "100%" }}>
      <Typography.Title level={4} style={{ margin: 0 }}>采集任务绑定</Typography.Title>
      {error && <Alert type="error" showIcon message={error} />}
      <Space wrap>
        <Select aria-label="工作流模板" mode="single" style={{ width: 220 }} value={workflowId}
          onChange={(value: string) => setWorkflowId(value)}
          options={workflows.map((item) => ({ value: item.workflowId, label: item.name }))} />
        <InputNumber aria-label="模板版本" min={1} value={version}
          onChange={(value: number | null) => setVersion(Number(value) || 1)} />
        <Select aria-label="机台范围" mode="multiple" style={{ width: 300 }} value={machineIds}
          onChange={(values: string[]) => setMachineIds(values)}
          options={machines.map((item) => ({ value: item.id, label: item.name }))} />
        <Select aria-label="文件配置范围" mode="multiple" style={{ width: 300 }} value={profileIds}
          onChange={(values: string[]) => setProfileIds(values)}
          options={profiles.map((item) => ({ value: item.id, label: item.name }))} />
        <Select aria-label="目标资源" mode="multiple" style={{ width: 300 }} value={targetIds}
          onChange={(values: string[]) => setTargetIds(values)}
          options={resources.map((item) => ({ value: item.id, label: `${item.name} (${item.type})` }))} />
        <Input aria-label="来源资源模板" placeholder="来源资源模板，{machine} 占位符" value={sourcePattern}
          onChange={(e: any) => setSourcePattern(e.target.value)} />
      </Space>
      <Alert type="info" showIcon
        message={`预计生成 ${preview.total} 条绑定；可创建 ${preview.create} 条`}
        description={`冲突 ${preview.conflicts.length} 条；缺失资源 ${preview.missing.length} 条`} />
      <Button type="primary" loading={busy} onClick={() => void create()}>生成绑定</Button>
      <Typography.Title level={5} style={{ margin: 0 }}>批量导入机台（CSV）</Typography.Title>
      <Input.TextArea aria-label="机台 CSV" rows={4} value={csv}
        placeholder="id,name,endpoint,credentialId" onChange={(e: any) => setCsv(e.target.value)} />
      <Button loading={busy} onClick={() => void importCsv()}>导入机台</Button>
      <Typography.Title level={5} style={{ margin: 0 }}>配置包（不含凭证）</Typography.Title>
      <Space wrap>
        <Button onClick={() => void exportConfig()}>导出配置</Button>
        <Button loading={busy} onClick={() => void importConfig()}>导入配置</Button>
      </Space>
      <Input.TextArea aria-label="配置包" rows={5} value={packageText}
        placeholder="导出后可跨环境导入；资源 ID 需自行映射" onChange={(e: any) => setPackageText(e.target.value)} />
      <Table rowKey="id" dataSource={bindings} pagination={false}
        columns={[
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "状态", dataIndex: "enabled", render: (value: boolean) => value ? "启用" : "停用" },
        ]} />
    </Space>
  );
}
