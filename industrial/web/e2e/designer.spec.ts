import { expect, test } from "@playwright/test";

const validDefinition = {
  workflowId: "browser-flow",
  name: "Browser flow",
  version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 80, y: 120 },
    { id: "read", type: "industrial.reader.csv", config: {}, x: 280, y: 120 },
    { id: "map", type: "industrial.transform.mapping", config: { mapping: { value: "reading" } }, x: 480, y: 120 },
    { id: "store", type: "industrial.sink.sqlite", config: {}, x: 680, y: 120 },
  ],
  edges: [
    { source: "src", sourcePort: "files", target: "read", targetPort: "input" },
    { source: "read", sourcePort: "records", target: "map", targetPort: "input" },
    { source: "map", sourcePort: "output", target: "store", targetPort: "records" },
  ],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: ["source-1"],
  layout: { lookbackDays: 1 },
};

test("real browser edits and saves a complete workflow", async ({ page }) => {
  const seeded = await page.request.post("/api/command/industrial/save-workflow", { data: validDefinition });
  expect(seeded.ok()).toBe(true);

  await page.goto("/industrial/designer");
  await expect(page.getByText("Industrial Workflow", { exact: true })).toBeVisible();
  await expect(page.locator(".industrial-backfill-input input")).toHaveValue("1");
  await expect(page.locator(".industrial-graph-host")).toBeVisible();
  expect(await page.getByRole("button", { name: /选\s*择|连\s*线|平\s*移/ }).count()).toBe(0);

  const canvasWidth = () => page.locator(".industrial-graph-host")
    .evaluate((el) => el.getBoundingClientRect().width);
  const beforeCollapse = await canvasWidth();
  await page.getByTestId("toggle-library").click();
  expect(await canvasWidth()).toBeGreaterThan(beforeCollapse + 100);
  await page.getByTestId("toggle-library").click();

  await page.getByTestId("toggle-properties").click();
  await expect(page.getByText("节点属性", { exact: true })).toBeVisible();
  await page.getByTestId("toggle-properties").click();

  const backfill = page.locator(".industrial-backfill-input input");
  await backfill.click();
  await backfill.fill("");
  await backfill.pressSequentially("7");
  await backfill.press("Tab");

  const saved = page.waitForRequest(request =>
    request.method() === "POST" &&
    request.url().includes("/api/command/industrial/save-workflow"), { timeout: 15_000 });
  const savedResponse = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/save-workflow"), { timeout: 15_000 });
  await page.getByRole("button", { name: /保\s*存/ }).click();

  expect((await savedResponse).ok()).toBe(true);
  const payload = JSON.parse((await saved).postData() ?? "{}");
  expect(payload.layout.lookbackDays).toBe(7);
  expect(payload.nodes.find((node: { id: string }) => node.id === "map").config.mapping)
    .toEqual({ value: "reading" });
});

test("industrial product opens on the workflow list and links into a designer", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/industrial\/workflows$/);
  const link = page.getByRole("button", { name: "browser-flow" });
  await expect(link).toBeVisible();
  await link.click();
  await expect(page).toHaveURL(/\/industrial\/designer\?workflowId=browser-flow$/);
  await expect(page.getByText("Industrial Workflow", { exact: true })).toBeVisible();
});

test("node configuration explains business meaning without exposing runtime details", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: validDefinition });
  await page.goto("/industrial/designer?workflowId=browser-flow");
  await expect(page.getByTestId("industrial-graph")).toBeVisible();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return (el?.__industrialGraph?.getNodeData()?.length ?? 0) >= 3;
  });
  await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    el.__industrialGraph.emit("node:click", { targetType: "node", target: { id: "map" } });
  });
  if (!(await page.getByText("节点属性", { exact: true }).isVisible())) {
    await page.getByTestId("toggle-properties").click();
  }
  await expect(page.getByText("Field Mapping", { exact: true }).first()).toBeVisible();
  await expect(page.getByText("输入", { exact: false }).first()).toBeVisible();
  await expect(page.getByText("Advanced", { exact: true })).toHaveCount(0);
  await expect(page.getByText("高级配置", { exact: true })).toBeVisible();
  await page.getByText("高级配置", { exact: true }).click();
  await expect(page.getByText("运行时 Activity", { exact: false }).first()).toBeVisible();
});
