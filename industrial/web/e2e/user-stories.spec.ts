import { expect, test } from "@playwright/test";

const flow = {
  workflowId: "browser-user-flow",
  name: "Browser user flow",
  version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 80, y: 120 },
    { id: "read", type: "industrial.reader.csv", config: {}, x: 280, y: 120 },
    { id: "store", type: "industrial.sink.sqlite", config: {}, x: 480, y: 120 },
  ],
  edges: [
    { source: "src", sourcePort: "files", target: "read", targetPort: "input" },
    { source: "read", sourcePort: "records", target: "store", targetPort: "records" },
  ],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: ["source-1"],
  layout: { lookbackDays: 1 },
};

test("a user creates a named workflow from the list and lands in the designer", async ({ page }) => {
  await page.goto("/industrial/workflows");
  await page.getByRole("button", { name: "新建工作流" }).click();
  await page.getByLabel("工作流名称").fill("浏览器新建工作流");
  await page.getByRole("button", { name: /创\s*建/ }).click();

  await expect(page).toHaveURL(/\/industrial\/designer\?workflowId=workflow-/);
  await expect(page.getByText("Industrial Workflow", { exact: true })).toBeVisible();
});

test("a user creates a source resource and tests its connection", async ({ page }) => {
  await page.goto("/industrial/resources");
  await page.getByRole("button", { name: "新建资源" }).click();
  await page.getByLabel("资源 ID").fill("browser-source");
  await page.getByLabel("资源名称").fill("Browser source");
  // The default type is the local directory, so no select interaction is needed.
  await page.getByLabel("目录路径").fill("/tmp/gocordis-industrial-browser-e2e/source");
  await page.getByRole("button", { name: "保存资源" }).click();

  await expect(page.getByText("browser-source", { exact: true })).toBeVisible();
  const row = page.getByText("browser-source", { exact: true }).locator("xpath=ancestor::tr");
  await row.getByRole("button", { name: "测试连接" }).click();
  await expect(page.getByText(/目录可访问|目录存在/)).toBeVisible();
});

test("a user creates a credential without exposing its secret in the list", async ({ page }) => {
  await page.goto("/industrial/credentials");
  await page.getByRole("button", { name: "新建凭证" }).click();
  await page.getByLabel("凭证 ID").fill("browser-credential");
  await page.getByLabel("凭证名称").fill("Browser credential");
  await page.getByLabel("凭证内容").fill("browser-secret-value");
  await page.getByRole("button", { name: "保存凭证" }).click();

  await expect(page.getByText("browser-credential", { exact: true })).toBeVisible();
  await expect(page.getByText("browser-secret-value")).toHaveCount(0);
});

test("the execution center shows real runs and per-target retry actions", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: flow });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: flow.workflowId },
  });
  const run = await page.request.post("/api/command/industrial/run-workflow", {
    data: { workflowId: flow.workflowId, resourceId: "source-1" },
  });
  expect(run.ok()).toBe(true);

  await page.goto("/industrial/executions");
  await expect(page.getByText("采集运行", { exact: true })).toBeVisible();
  await expect(page.getByText("交付目标", { exact: true })).toBeVisible();
  await expect(page.getByText(flow.workflowId, { exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "重试此目标" }).first()).toBeDisabled();
});
