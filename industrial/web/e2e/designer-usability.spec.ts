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

test("designer context menus, edge selection and sample preview work through real gestures", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: collapsed });
  await page.goto("/industrial/designer?workflowId=usability-flow");
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === 4;
  });

  const nodePoint = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const node = graph.getNodeData().find((item: any) => item.id === "src") as any;
    const [x, y] = graph.getClientByCanvas([node.style.x, node.style.y]);
    return { x, y };
  });
  await page.evaluate((point) => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    el.__industrialGraph.emit("node:contextmenu", {
      targetType: "node", target: { id: "src" }, client: point,
    });
  }, nodePoint);
  await expect(page.getByText("复制节点", { exact: true })).toBeVisible();
  await page.getByText("复制节点", { exact: true }).click();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === 5;
  });
  const duplicateId = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return (el.__industrialGraph.getNodeData() as any[])
      .find((item) => /^n\d+$/.test(item.id))?.id;
  });
  const duplicatePoint = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const node = graph.getNodeData().find((item: any) => /^n\d+$/.test(item.id)) as any;
    const [x, y] = graph.getClientByCanvas([node.style.x, node.style.y]);
    return { x, y };
  });
  await page.evaluate(({ point, id }) => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    el.__industrialGraph.emit("node:contextmenu", {
      targetType: "node", target: { id }, client: point,
    });
  }, { point: duplicatePoint, id: duplicateId });
  await page.getByText("删除节点", { exact: true }).click();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === 4;
  });

  if (!(await page.getByText("节点属性", { exact: true }).isVisible())) {
    await page.getByTestId("toggle-properties").click();
  }
  await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const edge = graph.getEdgeData()[0] as any;
    graph.emit("edge:click", { targetType: "edge", target: { id: edge.id } });
  });
  await expect(page.getByText(/当前连线/)).toBeVisible();
  await page.getByRole("button", { name: "删除此连线" }).click();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getEdgeData()?.length === 2;
  });

  await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const node = graph.getNodeData().find((item: any) => item.id === "src") as any;
    const [x, y] = graph.getClientByCanvas([node.style.x, node.style.y]);
    graph.emit("node:click", { targetType: "node", target: { id: node.id }, client: { x, y } });
  });
  if (!(await page.getByText("节点属性", { exact: true }).isVisible())) {
    await page.getByTestId("toggle-properties").click();
  }
  await expect(page.getByText("样本预览", { exact: true })).toBeVisible();
});
