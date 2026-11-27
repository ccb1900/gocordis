import { expect, test } from "@playwright/test";

const collapsed = {
  workflowId: "usability-flow",
  name: "Usability flow",
  version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 0, y: 0 },
    { id: "read", type: "industrial.reader.csv", config: {}, x: 0, y: 0 },
    { id: "map", type: "industrial.transform.mapping", config: { mapping: { value: "reading" } }, x: 0, y: 0 },
    { id: "store", type: "industrial.sink.sqlite", config: {}, x: 0, y: 0 },
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

const positions = (page: any) => page.evaluate(() => {
  const el = document.querySelector('[data-testid="industrial-graph"]') as any;
  return Object.fromEntries(el.__industrialGraph.getNodeData().map((n: any) =>
    [n.id, { x: Math.round(n.style.x), y: Math.round(n.style.y) }]));
});

test("designer repairs collapsed layout, auto-arranges and supports history", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: collapsed });
  await page.goto("/industrial/designer?workflowId=usability-flow");
  await expect(page.getByTestId("designer-auto-layout")).toBeVisible();
  await expect(page.getByTestId("designer-fit")).toBeVisible();
  await expect(page.getByTestId("designer-center")).toBeVisible();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === 4;
  });

  const repaired = await positions(page);
  const unique = new Set(Object.values(repaired).map((p: any) => `${p.x}:${p.y}`));
  expect(unique.size).toBe(4);

  const event = page.waitForRequest(request =>
    request.method() === "POST" && request.url().includes("/api/command/industrial/save-workflow"));
  await page.getByTestId("designer-auto-layout").click();
  const arranged = await positions(page);
  expect(arranged.src.x).not.toBe(repaired.src.x);
  await page.getByRole("button", { name: /保\s*存/ }).click();
  const saved = JSON.parse((await event).postData() ?? "{}");
  expect(saved.layout.positions).toBeTruthy();

  await page.getByTestId("designer-undo").click();
  expect(await positions(page)).toEqual(repaired);
  await page.getByTestId("designer-redo").click();
  expect(await positions(page)).toEqual(arranged);
});
