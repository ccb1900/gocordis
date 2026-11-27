import { expect, test } from "@playwright/test";
import { chooseAntd } from "./selectors";
import fs from "node:fs";

test("binding run parameters reach the published template at runtime", async ({ page }) => {
  const template = {
    workflowId: "params-e2e-template", name: "Parameter E2E", version: 1,
    nodes: [{ id: "src", type: "industrial.filesystem.local",
      config: { resourceId: "{{SourceResource}}", profile: "{{FileProfile}}",
        target: "{{TargetResource}}", shift: "{{shift}}" }, x: 80, y: 120 }],
    edges: [],
    trigger: { kind: "manual", cron: null, eventName: null, config: {} },
    resourceReferences: ["{{SourceResource}}"], layout: {},
  };
  expect((await page.request.post("/api/command/industrial/save-workflow", { data: template })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: template.workflowId },
  })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/import-machines", { data: {
    csv: "id,name,endpoint\nparams-machine,Param machine,10.0.0.55\n",
  } })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/import-file-profiles", { data: {
    csv: "id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required\n" +
         "p-params,Param profile,*.csv,csv,,true,utf-8,value=reading,value\n",
  } })).ok()).toBe(true);
  const sourceDir = "/tmp/params-e2e-source";
  fs.mkdirSync(sourceDir, { recursive: true });
  fs.writeFileSync(`${sourceDir}/params.csv`, "record_id,reading\n100,7\n");
  expect((await page.request.post("/api/command/industrial/save-resource", { data: {
    id: "params-source", name: "Param source", type: "filesystem.local", provider: "local",
    config: { root: sourceDir }, enabled: true,
  } })).ok()).toBe(true);
  expect((await page.request.post("/api/command/industrial/save-resource", { data: {
    id: "params-target", name: "Param target", type: "sqlite", provider: "local",
    config: { database: "/tmp/params-e2e.db" }, enabled: true,
  } })).ok()).toBe(true);

  await page.goto("/industrial/bindings");
  await chooseAntd(page, "工作流模板", "Parameter E2E");
  await chooseAntd(page, "机台范围", "Param machine");
  await chooseAntd(page, "文件配置范围", "Param profile");
  await chooseAntd(page, "目标资源", "Param target (sqlite)");
  await page.getByLabel("来源资源模板").fill("params-source");
  await page.getByLabel("运行参数").fill("lookbackDays=7;shift=night");
  await page.getByRole("button", { name: "生成绑定" }).click();

  const bindingId = `binding:${template.workflowId}:v1:params-machine:p-params`;
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
    return rows.find((row: { id: string }) => row.id === bindingId)?.parameters?.shift;
  }).toBe("night");

  await page.getByLabel("绑定筛选").fill("params-machine / p-params");
  const cell = page.getByTestId(`binding-parameters-${bindingId}`);
  await expect(cell).toContainText("lookbackDays=7;shift=night");

  await page.getByRole("row").filter({ hasText: "params-machine / p-params" })
    .getByRole("button", { name: /运\s*行/ }).click();
  await expect.poll(async () => {
    const resolved = (await (await page.request.get(
      "/api/query/industrial/binding-run-parameters")).json()).data;
    return resolved?.SourceResource;
  }).toBe("params-source");
  const resolved = (await (await page.request.get(
    "/api/query/industrial/binding-run-parameters")).json()).data;
  expect(resolved.FileProfile).toBe("p-params");
  expect(resolved.TargetResource).toBe("params-target");
  expect(resolved.shift).toBe("night");
});
