import { expect, test } from "@playwright/test";
import { chooseAntd, chooseAntdMany } from "./selectors";

test("import preview reports row errors and bulk exclusions skip invalid combinations", async ({ page }) => {
  const template = {
    workflowId: "preview-template", name: "Preview template", version: 1,
    nodes: [], edges: [],
    trigger: { kind: "manual", cron: null, eventName: null, config: {} },
    resourceReferences: [], layout: {},
  };
  expect((await page.request.post("/api/command/industrial/save-workflow", { data: template })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: template.workflowId },
  })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/import-machines", { data: {
    csv: "id,name,endpoint\npv-m1,M1,10.0.0.1\npv-m2,M2,10.0.0.2\n",
  } })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/import-file-profiles", { data: {
    csv: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required\n" +
         "pv-p1,P1,*.csv,csv,,true,utf-8,value=reading,value\n" +
         "pv-p2,P2,*.csv,csv,,true,utf-8,value=reading,value\n",
  } })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/save-resource", { data: {
    id: "pv-target", name: "Preview target", type: "sqlite", provider: "local",
    config: { database: "/tmp/preview-e2e.db" }, enabled: true,
  } })).ok()).toBe(true);

  await page.goto("/industrial/bindings");
  await chooseAntd(page, "工作流模板", "Preview template");
  await chooseAntdMany(page, "机台范围", ["M1", "M2"]);
  await chooseAntdMany(page, "文件配置范围", ["P1", "P2"]);
  await chooseAntd(page, "目标资源", "Preview target (sqlite)");
  await page.getByLabel("来源资源模板").fill("source-1");
  await page.getByLabel("例外规则").fill("pv-m2:pv-p2");
  await page.getByRole("button", { name: "预览绑定" }).click();
  await expect(page.getByTestId("binding-preview-report")).toContainText("3 组合，创建 3");

  await page.getByLabel("绑定 CSV").fill([
    "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId,targetResourceIds,triggerKind,cron",
    "pv-b1,Valid,pv-m1,preview-template,1,pv-p1,source-1,pv-target,manual,",
    "pv-b1,Duplicate,pv-m1,preview-template,1,pv-p1,source-1,pv-target,manual,",
    "pv-b3,Bad trigger,pv-m1,preview-template,1,pv-p1,source-1,pv-target,nightly,",
  ].join("\n"));
  await page.getByRole("button", { name: "预览导入" }).click();
  await expect(page.getByTestId("import-preview-report")).toContainText("1/3 行有效");
  await expect(page.getByTestId("import-error-row-2")).toContainText("duplicate id");
  await expect(page.getByTestId("import-error-row-3")).toContainText("unknown triggerKind");

  await page.getByRole("button", { name: "生成绑定" }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
    return rows.filter((row: { id: string }) => row.id.startsWith("binding:preview-template:v1:")).length;
  }).toBe(3);
  const bindings = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
  expect(bindings.some((row: { id: string }) => row.id === "binding:preview-template:v1:pv-m2:pv-p2")).toBe(false);
});
