import { expect, test } from "@playwright/test";

// Browser-facing regressions for the audited command/config surfaces. Every
// assertion goes through the real console HTTP commands and queries.

test("a rejected command reaches the component catch and does not report success", async ({ page }) => {
  await page.goto("/industrial/bindings");
  await page.getByLabel("工作流 ID").fill("audit-missing-workflow");
  await page.getByLabel("机台 ID 列表").fill("audit-machine");
  await page.getByLabel("文件配置 ID 列表").fill("audit-profile");
  await page.getByLabel("目标资源 ID 列表").fill("audit-target");
  await page.getByLabel("绑定触发策略").selectOption("schedule");
  await page.getByLabel("绑定 Cron").fill("0 99 * * *"); // five fields, invalid hour
  await page.getByRole("button", { name: "生成绑定" }).click();

  // This text is rendered by BindingConsole's own catch block, not only by
  // the shell's supplementary banner: the command rejection was propagated.
  await expect(page.locator(".ant-alert-error").filter({ hasText: "valid cron expression" }).first())
    .toBeVisible();
  const bindings = (await (await page.request.get("/api/query/industrial/bindings")).json()).data ?? [];
  expect(bindings.some((row: { id: string }) =>
    row.id === "binding:audit-missing-workflow:v1:audit-machine:audit-profile")).toBe(false);
});

test("credential unbind distinguishes an omitted field from an explicit null", async ({ page }) => {
  const save = (body: object) => page.request.post("/api/command/industrial/save-resource", { data: body });
  const read = async () => {
    const rows = (await (await page.request.get("/api/query/industrial/resources")).json()).data;
    return rows.find((row: { id: string }) => row.id === "audit-unbind-source");
  };

  expect((await page.request.post("/api/command/industrial/save-credential", { data: {
    id: "audit-unbind-cred", name: "Audit credential", kind: "username-password", secret: "audit-secret",
  } })).ok()).toBe(true);
  expect((await save({
    id: "audit-unbind-source", name: "Audit source", type: "filesystem.local", provider: "local",
    config: { root: "/tmp" }, credentialId: "audit-unbind-cred", enabled: true,
  })).ok()).toBe(true);
  expect((await read()).credentialId).toBe("audit-unbind-cred");

  // A partial edit that omits credentialId must keep the existing binding.
  expect((await save({ id: "audit-unbind-source", name: "Renamed" })).ok()).toBe(true);
  expect((await read()).credentialId).toBe("audit-unbind-cred");

  // Clearing the Select sends null: the credential is actually removed.
  expect((await save({
    id: "audit-unbind-source", name: "Renamed", type: "filesystem.local",
    config: { root: "/tmp" }, credentialId: null, enabled: true,
  })).ok()).toBe(true);
  expect((await read()).credentialId).toBeNull();
});

test("preview reports are returned per request instead of read from global state", async ({ page }) => {
  const preview = (requestId: string, machineIds: string[]) =>
    page.request.post("/api/command/industrial/preview-bindings", { data: {
      requestId, machineIds, fileProfileIds: ["audit-profile"], workflowId: "audit-workflow",
      workflowVersion: 1, sourceResourcePattern: "resource-{machine}", targetResourceIds: [],
    } });
  expect((await preview("audit-preview-one", ["audit-m1"])).ok()).toBe(true);
  expect((await preview("audit-preview-two", ["audit-m1", "audit-m2", "audit-m3"])).ok()).toBe(true);

  const first = (await (await page.request.get("/api/query/industrial/preview-result/audit-preview-one")).json()).data;
  const second = (await (await page.request.get("/api/query/industrial/preview-result/audit-preview-two")).json()).data;
  expect(first.total).toBe(1);
  expect(second.total).toBe(3);
  expect(first).not.toEqual(second);
});

test("config import rejects invalid binding references and unsatisfied dependencies before writing", async ({ page }) => {
  const raw = await page.request.get("/api/query/industrial/export-config");
  const packageJson = (await raw.json()).data;

  const badReference = JSON.parse(JSON.stringify(packageJson));
  badReference.bindings.push({
    id: "audit-broken-binding", name: "Broken", machineId: "audit-absent-machine-x",
    workflowId: "audit-workflow", workflowVersion: 1, fileProfileId: "audit-profile",
    sourceResourceId: "", targetResourceIds: [],
    trigger: { kind: "manual", cron: null, eventName: null, config: {} },
    enabled: true, createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(),
    parameters: {},
  });
  const rejected = await page.request.post("/api/command/industrial/import-config", { data: badReference });
  expect(rejected.ok()).toBe(false);
  expect(await rejected.text()).toContain("unknown machine 'audit-absent-machine-x'");

  const missingDependency = JSON.parse(JSON.stringify(packageJson));
  missingDependency.dependencies.push({
    kind: "resource", id: "audit-absent-resource-x", required: true,
  });
  const missing = await page.request.post("/api/command/industrial/import-config", { data: missingDependency });
  expect(missing.ok()).toBe(false);
  expect(await missing.text()).toContain("unsatisfied dependencies");
  expect(await missing.text()).toContain("resource:audit-absent-resource-x");
});

test("file profile round-trip accepts comma and semicolon lists and locates bad regexes", async ({ page }) => {
  await page.goto("/industrial/file-profiles");
  await page.getByRole("button", { name: "新建文件配置" }).click();
  await page.getByLabel("配置名称").fill("Audit UI profile");
  await page.getByLabel("字段映射").fill("reading=value,shift=night");
  await page.getByLabel("必填字段").fill("reading,shift");
  await page.getByLabel("元数据规则").fill("filename|(?P<value>[0-9]{8})|date|true");
  await page.getByRole("button", { name: "保存配置" }).click();

  const profile = async (name: string) => {
    const rows = (await (await page.request.get("/api/query/industrial/file-profiles")).json()).data;
    return rows.find((row: { name: string }) => row.name === name);
  };
  await expect.poll(async () => (await profile("Audit UI profile"))?.mapping?.reading).toBe("value");
  const saved = await profile("Audit UI profile");
  expect(saved.mapping.shift).toBe("night");
  expect(saved.requiredFields).toEqual(["reading", "shift"]);
  expect(saved.metadataRules[0].pattern).toBe("(?<value>[0-9]{8})");

  const imported = await page.request.post("/api/command/industrial/import-file-profiles", { data: {
    csv: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required,metadataRules\n" +
         "audit-csv-profile,Audit CSV profile,*.csv,csv,,true,utf-8," +
         "reading=value;shift=night,reading;shift,filename|(?P<value>[0-9]{8})|date|true\n",
  } });
  expect(imported.ok(), await imported.text()).toBe(true);
  const csvProfile = await profile("Audit CSV profile");
  expect(Object.keys(csvProfile.mapping).sort()).toEqual(["reading", "shift"]);
  expect(csvProfile.requiredFields).toEqual(["reading", "shift"]);
  expect(csvProfile.metadataRules).toHaveLength(1);

  const invalid = await page.request.post("/api/command/industrial/import-file-profiles", { data: {
    csv: "id,name,metadataRules\naudit-bad-profile,Bad regex,filename|(|value|true\n",
  } });
  expect(invalid.ok()).toBe(false);
  const detail = await invalid.text();
  expect(detail).toContain("row 2");
  expect(detail).toContain("regex");
});
