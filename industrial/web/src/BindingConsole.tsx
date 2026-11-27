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

const workbookBase64 = (file: File) => new Promise<string>((resolve, reject) => {
  const reader = new FileReader();
  reader.onload = () => resolve(String(reader.result).split(",")[1] ?? "");
  reader.onerror = () => reject(reader.error ?? new Error("无法读取 Excel 文件"));
  reader.readAsDataURL(file);
});

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
  const [packageMeta, setPackageMeta] = useState<any>(null);
  const [resourceMap, setResourceMap] = useState("");
  const [credentialMap, setCredentialMap] = useState("");
  const [status, setStatus] = useState<any>(null);
  const [triggerKind, setTriggerKind] = useState("manual");
  const [cron, setCron] = useState("");
  const [profileCsv, setProfileCsv] = useState("");
  const [bindingCsv, setBindingCsv] = useState("");
  const [runParameters, setRunParameters] = useState("");
  const [exclusions, setExclusions] = useState("");
  const [bindingPreviewReport, setBindingPreviewReport] = useState<any>(null);
  const [importPreviewReport, setImportPreviewReport] = useState<any>(null);
  const [upgradeVersion, setUpgradeVersion] = useState(1);
  const [filter, setFilter] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    try {
      setBindings(rows(await ctx.hubQuery(block?.query ?? "industrial/bindings")));
      setMachines(rows(await ctx.hubQuery("industrial/machines")));
      setProfiles(rows(await ctx.hubQuery("industrial/file-profiles")));
      setWorkflows(rows(await ctx.hubQuery("industrial/workflows")));
      setResources(rows(await ctx.hubQuery("industrial/resources")));
      try { setStatus(await ctx.hubQuery("industrial/binding-status")); } catch { /* diagnostics only */ }
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
    const payload = {
      machineIds, fileProfileIds: profileIds, workflowId, workflowVersion: version,
      sourceResourcePattern: sourcePattern, targetResourceIds: targetIds,
      triggerKind, cron: cron || null,
      parameters: Object.fromEntries(runParameters.split(";").map((pair) => pair.split("="))
        .filter((parts) => parts.length === 2 && parts[0].trim() && parts[1].trim())
        .map((parts) => [parts[0].trim(), parts[1].trim()])),
      exclusions: exclusions.split(/[,\n;]/).map((item: string) => item.trim()).filter(Boolean),
    };
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/preview-bindings", payload);
      const report = await ctx.hubQuery("industrial/binding-preview") as any;
      setBindingPreviewReport(report);
      if ((report?.errors?.length ?? 0) > 0 || (report?.missing?.length ?? 0) > 0 ||
          (report?.conflicts?.length ?? 0) > 0) {
        setError("服务端预览发现问题，请检查冲突、缺失资源或例外规则后再创建");
        return;
      }
      await ctx.hubCommand("industrial/create-bindings", payload);
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, cron, exclusions, load, profileIds, runParameters, sourcePattern, targetIds, triggerKind, version, workflowId, machineIds]);

  const setEnabled = useCallback(async (binding: any, enabled: boolean) => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/set-binding-enabled", { id: binding.id, enabled });
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, load]);

  const runBinding = useCallback(async (binding: any, lookbackDays: number) => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/run-binding", {
        bindingId: binding.id, lookbackDays,
      });
      setStatus(await ctx.hubQuery("industrial/binding-status"));
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx]);

  const importProfiles = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/import-file-profiles", { csv: profileCsv });
      setProfileCsv(""); await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, load, profileCsv]);

  const importBindingRows = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/import-bindings", { csv: bindingCsv });
      setBindingCsv(""); await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, load, bindingCsv]);

  const importExcel = useCallback(async (
    kind: "machines" | "profiles" | "bindings", file?: File) => {
    if (!file) return;
    setBusy(true); setError(null);
    try {
      const command = kind === "profiles"
        ? "industrial/import-file-profiles"
        : `industrial/import-${kind}`;
      await ctx.hubCommand(command, { xlsx: await workbookBase64(file) });
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, load]);

  const previewBindings = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/preview-bindings", {
        machineIds, fileProfileIds: profileIds, workflowId, workflowVersion: version,
        sourceResourcePattern: sourcePattern, targetResourceIds: targetIds,
        triggerKind, cron: cron || null,
        exclusions: exclusions.split(/[,\n;]/).map((item: string) => item.trim()).filter(Boolean),
      });
      setBindingPreviewReport(await ctx.hubQuery("industrial/binding-preview"));
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, cron, exclusions, machineIds, profileIds, profileIds, sourcePattern, targetIds, triggerKind, version, workflowId]);

  const previewImport = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      await ctx.hubCommand("industrial/preview-import", { csv: bindingCsv });
      setImportPreviewReport(await ctx.hubQuery("industrial/import-preview"));
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, bindingCsv]);

  const upgradeVersionAll = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      const ids = bindings.filter((row) => row.workflowId === workflowId).map((row) => row.id);
      await ctx.hubCommand("industrial/upgrade-binding-version", { ids, version: upgradeVersion });
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [bindings, ctx, load, upgradeVersion, workflowId]);

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
      const text = JSON.stringify(payload, null, 2);
      setPackageText(text);
      setPackageMeta(payload);
      const blob = new Blob([text], { type: "application/json" });
      const url = window.URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `gocordis-industrial-config-v${payload?.formatVersion ?? 1}.json`;
      anchor.click();
      window.URL.revokeObjectURL(url);
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
  }, [ctx]);

  const parseMap = (text: string): Record<string, string> => {
    const result: Record<string, string> = {};
    for (const pair of text.split(/[\n,]/)) {
      const [from, to] = pair.split("=").map((part) => part?.trim());
      if (from && to) result[from] = to;
    }
    return result;
  };

  const loadPackageFile = useCallback(async (file?: File) => {
    if (!file) return;
    setError(null);
    try {
      const text = await file.text();
      setPackageText(text);
      setPackageMeta(JSON.parse(text));
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : String(cause));
    }
  }, []);

  const importConfig = useCallback(async () => {
    setBusy(true); setError(null);
    try {
      const payload = JSON.parse(packageText);
      payload.resourceMap = parseMap(resourceMap);
      payload.credentialMap = parseMap(credentialMap);
      await ctx.hubCommand("industrial/import-config", payload);
      await load();
    } catch (cause) { setError(cause instanceof Error ? cause.message : String(cause)); }
    finally { setBusy(false); }
  }, [ctx, credentialMap, load, packageText, resourceMap]);

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
        <Select aria-label="绑定触发策略" style={{ width: 160 }} value={triggerKind}
          onChange={(value: string) => setTriggerKind(value)}
          options={[{ value: "manual", label: "手动" }, { value: "schedule", label: "定时" }]} />
        <Input aria-label="绑定 Cron" placeholder="cron（定时策略）" value={cron}
          onChange={(e: any) => setCron(e.target.value)} />
        <Input aria-label="来源资源模板" placeholder="来源资源模板，{machine} 占位符" value={sourcePattern}
          onChange={(e: any) => setSourcePattern(e.target.value)} />
        <Input aria-label="运行参数" placeholder="运行参数，如 lookbackDays=7;shift=day" value={runParameters}
          onChange={(e: any) => setRunParameters(e.target.value)} />
        <Input aria-label="例外规则" placeholder="例外规则：机台:文件配置，逗号或换行分隔" value={exclusions}
          onChange={(e: any) => setExclusions(e.target.value)} />
      </Space>
      <Space wrap>
        <Button size="small" onClick={() => setMachineIds(machines.map((item) => item.id))}>全选机台</Button>
        <Button size="small" onClick={() => setProfileIds(profiles.map((item) => item.id))}>全选文件配置</Button>
        <Button size="small" onClick={() => { setMachineIds([]); setProfileIds([]); }}>清空范围</Button>
      </Space>
      <Alert type="info" showIcon
        message={`预计生成 ${preview.total} 条绑定；可创建 ${preview.create} 条`}
        description={`冲突 ${preview.conflicts.length} 条；缺失资源 ${preview.missing.length} 条`} />
      <Space wrap>
        <Button type="primary" loading={busy} onClick={() => void create()}>生成绑定</Button>
        <Button loading={busy} onClick={() => void previewBindings()}>预览绑定</Button>
        <Button loading={busy} onClick={() => void previewImport()}>预览导入</Button>
      </Space>
      {bindingPreviewReport && (
        <Alert type="info" showIcon data-testid="binding-preview-report"
          message={`批量预览：${bindingPreviewReport.total} 组合，创建 ${bindingPreviewReport.create}`}
          description={`冲突 ${bindingPreviewReport.conflicts?.length ?? 0}；缺失 ${bindingPreviewReport.missing?.length ?? 0}；错误 ${bindingPreviewReport.errors?.length ?? 0}`} />
      )}
      {importPreviewReport && (
        <Alert type={importPreviewReport.errors?.length ? "warning" : "success"} showIcon
          data-testid="import-preview-report"
          message={`导入预览：${importPreviewReport.valid ?? 0}/${importPreviewReport.total ?? 0} 行有效`}
          description={(importPreviewReport.errors ?? []).map((error: any, index: number) =>
            <div key={index} data-testid={`import-error-row-${error.row}`}>
              第 {error.row} 行：{error.message}
            </div>)} />
      )}
      <Space wrap>
        <InputNumber aria-label="升级到模板版本" min={1} value={upgradeVersion}
          onChange={(value: number | null) => setUpgradeVersion(Number(value) || 1)} />
        <Button loading={busy} onClick={() => void upgradeVersionAll()}>升级当前工作流绑定版本</Button>
      </Space>
      <Typography.Title level={5} style={{ margin: 0 }}>批量导入机台（CSV）</Typography.Title>
      <Input.TextArea aria-label="机台 CSV" rows={4} value={csv}
        placeholder="id,name,endpoint,credentialId" onChange={(e: any) => setCsv(e.target.value)} />
      <Button loading={busy} onClick={() => void importCsv()}>导入机台</Button>
      <Input.TextArea aria-label="文件配置 CSV" rows={3} value={profileCsv}
        placeholder="id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required"
        onChange={(e: any) => setProfileCsv(e.target.value)} />
      <Button loading={busy} onClick={() => void importProfiles()}>导入文件配置</Button>
      <Input.TextArea aria-label="绑定 CSV" rows={3} value={bindingCsv}
        placeholder="id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron"
        onChange={(e: any) => setBindingCsv(e.target.value)} />
      <Button loading={busy} onClick={() => void importBindingRows()}>导入采集绑定</Button>
      <Typography.Title level={5} style={{ margin: 0 }}>Excel 批量导入（.xlsx，选择即导入）</Typography.Title>
      <Space wrap>
        <label>
          机台 Excel
          <input type="file" accept=".xlsx" aria-label="机台 Excel 文件"
            onChange={(event: any) => void importExcel("machines", event.target.files?.[0])} />
        </label>
        <label>
          文件配置 Excel
          <input type="file" accept=".xlsx" aria-label="文件配置 Excel 文件"
            onChange={(event: any) => void importExcel("profiles", event.target.files?.[0])} />
        </label>
        <label>
          采集绑定 Excel
          <input type="file" accept=".xlsx" aria-label="绑定 Excel 文件"
            onChange={(event: any) => void importExcel("bindings", event.target.files?.[0])} />
        </label>
      </Space>
      <Typography.Title level={5} style={{ margin: 0 }}>配置包（不含凭证）</Typography.Title>
      <Space wrap>
        <Button onClick={() => void exportConfig()}>导出配置</Button>
        <Button loading={busy} onClick={() => void importConfig()}>导入配置</Button>
        <label>
          上传配置文件
          <input type="file" accept=".json,application/json" aria-label="上传配置文件"
            onChange={(event: any) => void loadPackageFile(event.target.files?.[0])} />
        </label>
      </Space>
      {packageMeta?.dependencies && (
        <Alert type="info" showIcon data-testid="package-dependencies"
          message={`配置包 v${packageMeta.formatVersion ?? 1} · ${packageMeta.dependencies.length} 项依赖`}
          description={packageMeta.dependencies.map((item: any, index: number) =>
            <div key={index}>{item.kind}: {item.id}{item.version ? ` @ ${item.version}` : ""}</div>)} />
      )}
      <Input aria-label="资源 ID 映射" placeholder="旧资源ID=新资源ID，逗号或换行分隔" value={resourceMap}
        onChange={(e: any) => setResourceMap(e.target.value)} />
      <Input aria-label="凭证别名映射" placeholder="旧凭证ID=新凭证ID" value={credentialMap}
        onChange={(e: any) => setCredentialMap(e.target.value)} />
      <Input.TextArea aria-label="配置包" rows={5} value={packageText}
        placeholder="导出后可跨环境导入；资源 ID 需自行映射" onChange={(e: any) => setPackageText(e.target.value)} />
      <Input aria-label="绑定筛选" placeholder="按绑定名称/ID筛选" value={filter}
        onChange={(e: any) => setFilter(e.target.value)} />
      <Table rowKey="id" dataSource={bindings.filter((row) =>
        !filter || `${row.id} ${row.name}`.includes(filter))} pagination={{ pageSize: 20 }}
        columns={[
          { title: "绑定", dataIndex: "name" },
          { title: "机台", dataIndex: "machineId" },
          { title: "文件配置", dataIndex: "fileProfileId" },
          { title: "模板版本", dataIndex: "workflowVersion" },
          { title: "运行参数", key: "parameters", render: (_: unknown, row: any) =>
            <span data-testid={`binding-parameters-${row.id}`}>
              {Object.entries(row.parameters ?? {}).map(([key, value]) => `${key}=${value}`).join(";") || "—"}
            </span> },
          { title: "状态", dataIndex: "enabled", render: (value: boolean, row: any) =>
            <Space>
              <Button size="small" onClick={() => void setEnabled(row, !row.enabled)}>
                {row.enabled ? "暂停" : "恢复"}
              </Button>
              <Button size="small" onClick={() => void runBinding(row, 7)}>补采 7 天</Button>
              <Button size="small" onClick={() => void runBinding(row, 1)}>运行</Button>
            </Space> },
          { title: "排障", key: "diagnostics", render: (_: unknown, row: any) => {
            const runs = (status?.runs ?? []).filter((run: any) =>
              run.workflowId === row.workflowId);
            const failed = (status?.deliveries ?? []).filter((delivery: any) =>
              delivery.status === "Failed");
            return <Typography.Text style={{ fontSize: 11 }}>
              {runs.length ? `最近运行 ${runs[0].status}` : "暂无运行"} · 失败交付 {failed.length}
            </Typography.Text>;
          } },
        ]} />
    </Space>
  );
}
