import { expect, test } from "@playwright/test";

const definition = {
  workflowId: "advanced-designer-flow",
  name: "Advanced designer flow",
  version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 100, y: 100 },
    { id: "read", type: "industrial.reader.csv", config: {}, x: 300, y: 100 },
    { id: "store", type: "industrial.sink.sqlite", config: {}, x: 500, y: 300 },
  ],
  edges: [
    { source: "src", sourcePort: "files", target: "read", targetPort: "input" },
    { source: "read", sourcePort: "records", target: "store", targetPort: "records" },
  ],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: ["source-1"],
  layout: { lookbackDays: 1 },
};

const graphState = (page: any) => page.evaluate(() => {
  const el = document.querySelector('[data-testid="industrial-graph"]') as any;
  const graph = el?.__industrialGraph;
  if (!graph) return null;
  const options = graph.getOptions?.() ?? {};
  return {
    nodes: graph.getNodeData().map((node: any) => ({
      id: String(node.id), x: node.style.x, y: node.style.y, config: node.data?.config ?? {},
    })),
    edges: graph.getEdgeData().map((edge: any) => ({
      id: String(edge.id), source: String(edge.source), target: String(edge.target),
    })),
    plugins: (options.plugins ?? []).map((plugin: any) => plugin.type ?? plugin),
    router: options.edge?.style?.router?.type ?? null,
  };
});

test("designer guides, snapping, copy/paste, edge reconnect and deletion", async ({ page }) => {
  test.setTimeout(120_000);
  expect((await page.request.post("/api/command/industrial/save-workflow", { data: definition })).ok()).toBe(true);
  await page.goto("/industrial/designer?workflowId=advanced-designer-flow");
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === 3;
  });

  const initial = await graphState(page);
  expect(initial.plugins).toContain("grid-line");
  expect(initial.plugins).toContain("snapline");
  expect(initial.router).toBe("orth");

  // Native node click + Ctrl+C / Ctrl+V from the keyboard shortcut path.
  const sourcePoint = await page.evaluate(() => {
    const graph = (document.querySelector('[data-testid="industrial-graph"]') as any).__industrialGraph;
    const node = graph.getNodeData().find((item: any) => item.id === "src");
    return graph.getClientByCanvas([node.style.x, node.style.y]);
  });
  await page.mouse.click(sourcePoint[0], sourcePoint[1]);
  await page.keyboard.press("Control+c");
  await page.keyboard.press("Control+v");
  await expect.poll(async () => (await graphState(page))?.nodes.length).toBe(4);
  const pasted = (await graphState(page)).nodes.find((node: any) => node.id !== "src" &&
    node.id !== "read" && node.id !== "store")!;
  expect(pasted.config.resourceId).toBe("source-1");

  // The visible snap command and a native drag both commit grid-aligned
  // positions; the alignment guide is observable in the real canvas.
  await page.getByTestId("designer-snap").click();
  await expect(page.getByTestId("align-guide")).toBeVisible();
  const dragPoints = await page.evaluate(() => {
    const graph = (document.querySelector('[data-testid="industrial-graph"]') as any).__industrialGraph;
    const src = graph.getNodeData().find((item: any) => item.id === "src");
    return {
      from: graph.getClientByCanvas([src.style.x, src.style.y]),
      to: graph.getClientByCanvas([src.style.x + 43, src.style.y + 37]),
    };
  });
  await page.mouse.move(dragPoints.from[0], dragPoints.from[1]);
  await page.mouse.down();
  await page.mouse.move(dragPoints.to[0], dragPoints.to[1], { steps: 8 });
  await page.mouse.up();
  const dragged = await graphState(page);
  for (const node of dragged.nodes) {
    expect(node.x % 20).toBe(0);
    expect(node.y % 20).toBe(0);
  }

  // Edge endpoints can be reconnected from the selected node's property panel.
  const related = page.getByText(/相关连线/);
  if (!(await related.isVisible())) await page.getByTestId("toggle-properties").click();
  await expect(related).toBeVisible();
  const showAll = page.getByRole("button", { name: "显示全部连线" });
  if (await showAll.isVisible()) await showAll.click();
  await page.getByLabel("连线 0 起点节点").selectOption("store");
  await expect.poll(async () => (await graphState(page))?.edges.some((edge) => edge.source === "store")).toBe(true);

  await page.getByLabel("连线 0 起点节点")
    .locator("xpath=ancestor::tr")
    .getByRole("button", { name: "删" }).click();
  await expect.poll(async () => (await graphState(page))?.edges.length).toBe(1);
});

test("node configuration explains fields, shows resource status and locates config errors", async ({ page }) => {
  const configFlow = {
    workflowId: "config-metadata-flow",
    name: "Config metadata flow",
    version: 1,
    nodes: [
      { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 100, y: 100 },
      { id: "bad", type: "industrial.filesystem.local", config: { resourceId: "missing-resource" }, x: 400, y: 100 },
    ],
    edges: [],
    trigger: { kind: "manual", cron: null, eventName: null, config: {} },
    resourceReferences: ["source-1"],
    layout: {},
  };
  expect((await page.request.post("/api/command/industrial/save-workflow", { data: configFlow })).ok()).toBe(true);
  await page.goto("/industrial/designer?workflowId=config-metadata-flow");
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === 2;
  });

  const point = await page.evaluate(() => {
    const graph = (document.querySelector('[data-testid="industrial-graph"]') as any).__industrialGraph;
    const node = graph.getNodeData().find((item: any) => item.id === "src");
    return graph.getClientByCanvas([node.style.x, node.style.y]);
  });
  await page.mouse.click(point[0], point[1]);
  await page.getByTestId("toggle-properties").click();
  await expect(page.getByTestId("field-hint-resourceId"))
    .toContainText(/选择已配置的数据资源.*示例/);
  await expect(page.getByTestId("resource-picker-status"))
    .toContainText(/Default source.*filesystem\.local.*已启用/);
  await expect(page.getByTestId("resource-picker-create")).toBeVisible();

  await expect(page.getByText(/nodes\.bad\.config\.resourceId/)).toBeVisible();
});
