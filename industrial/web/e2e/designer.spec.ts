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
