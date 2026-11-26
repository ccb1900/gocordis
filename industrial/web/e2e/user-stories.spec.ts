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

test("a user configures a schedule and sees its next run", async ({ page }) => {
  const seed = await page.request.post("/api/command/industrial/save-workflow", {
    data: {
      ...flow,
      workflowId: "browser-schedule-flow",
      name: "Browser schedule",
      trigger: { kind: "schedule", cron: "*/5 * * * *", eventName: null, config: { resourceId: "source-1" } },
    },
  });
  expect(seed.ok(), await seed.text()).toBe(true);
  await page.goto("/industrial/designer?workflowId=browser-schedule-flow");
  await expect(page.locator(".industrial-title")).toContainText(/Browser schedule|browser-schedule-flow/);
  await page.getByTestId("open-trigger").click();
  await expect(page.getByLabel("Cron 表达式")).toHaveValue("*/5 * * * *");
  await page.getByRole("button", { name: "保存触发器" }).click();
  await expect(page.getByRole("button", { name: "保存触发器" })).toHaveCount(0);
  const saveRequest = page.waitForRequest(request =>
    request.method() === "POST" &&
    request.url().includes("/api/command/industrial/save-workflow"));
  const saved = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/save-workflow"));
  await page.getByRole("button", { name: /保\s*存/ }).click();
  const payload = JSON.parse((await saveRequest).postData() ?? "{}");
  expect(payload.trigger.kind).toBe("schedule");
  await saved;
  const published = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/publish-workflow"));
  await page.getByRole("button", { name: /发\s*布/ }).click();
  await published;

  const response = await page.request.get("/api/query/industrial/trigger-status");
  const rows = (await response.json()).data.triggers;
  const row = rows.find((item: { workflowId: string }) => item.workflowId === "browser-schedule-flow");
  expect(row.kind).toBe("schedule");
  expect(row.cron).toBe("*/5 * * * *");
  expect(row.nextRun).toBeTruthy();
});

test("a user previews a backfill before running it", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: flow });
  await page.goto("/industrial/designer?workflowId=browser-user-flow");
  await page.getByTestId("open-backfill-preview").click();
  await page.getByRole("button", { name: /扫描最近 1 天/ }).click();
  await expect(page.getByText(/发现 \d+ 个文件/)).toBeVisible();
  await expect(page.getByText("recent.csv")).toBeVisible();
});

test("a user sees per-file processing details and classified failures", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: flow });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: flow.workflowId },
  });
  await page.request.post("/api/command/industrial/run-workflow", {
    data: { workflowId: flow.workflowId, resourceId: "source-1" },
  });

  await page.goto("/industrial/files");
  await expect(page.getByText("文件处理详情", { exact: true })).toBeVisible();
  await expect(page.getByText("recent.csv", { exact: true })).toBeVisible();
  await page.locator(".ant-table-row-expand-icon").first().click();
  await expect(page.getByText("原因与下一步", { exact: true })).toBeVisible();
});

test("a published cron trigger runs without manual action", async ({ page }) => {
  test.setTimeout(120_000);
  const scheduled = {
    ...flow,
    workflowId: "browser-cron-run",
    name: "Browser cron run",
    trigger: { kind: "schedule", cron: "*/1 * * * *", eventName: null, config: { resourceId: "source-1" } },
  };
  await page.request.post("/api/command/industrial/save-workflow", { data: scheduled });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: scheduled.workflowId },
  });

  await expect.poll(async () => {
    const response = await page.request.get("/api/query/industrial/execution-center");
    const data = (await response.json()).data;
    return data.runs.some((run: { workflowId: string }) => run.workflowId === scheduled.workflowId);
  }, { timeout: 90_000 }).toBe(true);
});

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
  await expect(page.getByText(flow.workflowId, { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "重试此目标" }).first()).toBeDisabled();
});
