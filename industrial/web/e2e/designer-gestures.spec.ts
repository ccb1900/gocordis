import { expect, test } from "@playwright/test";

/** Seed a valid multi-node workflow, then remove one edge in the UI so the
 * port-drag gesture has an unambiguous gap to close. */
const gestureDefinition = {
  workflowId: "gesture-flow",
  name: "Gesture flow",
  version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 120, y: 160 },
    { id: "read", type: "industrial.reader.csv", config: {}, x: 420, y: 160 },
    { id: "map", type: "industrial.transform.mapping", config: { mapping: { value: "reading" } }, x: 620, y: 160 },
    { id: "store", type: "industrial.sink.sqlite", config: {}, x: 820, y: 160 },
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

async function graphState(page: any) {
  return page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el?.__industrialGraph;
    if (!graph) return null;
    return {
      nodes: graph.getNodeData().map((n: any) => ({ id: n.id, x: n.style.x, y: n.style.y })),
      edges: graph.getEdgeData().map((e: any) => e.id),
      viewport: graph.getViewportByCanvas([0, 0])[0],
    };
  });
}

async function waitForGraph(page: any) {
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return !!el?.__industrialGraph?.getNodeData;
  });
}

test("G6 native gestures: port drag connects, node drag moves, canvas drag pans, selection persists", async ({ page }) => {
  test.setTimeout(120_000);
  const seeded = await page.request.post("/api/command/industrial/save-workflow", { data: gestureDefinition });
  expect(seeded.ok()).toBe(true);

  await page.goto("/industrial/designer?workflowId=gesture-flow");
  await waitForGraph(page);
  await expect(page.locator(".industrial-graph-host")).toBeVisible();

  // Remove the src→read edge through the property panel so the real port drag
  // below must create it again.
  await page.getByTestId("toggle-properties").click();
  await expect(page.getByText("连线 (3)", { exact: true })).toBeVisible();
  await page.getByRole("row").filter({ hasText: "src → read" })
    .getByRole("button", { name: "删" }).click();
  await expect(page.getByText("连线 (2)", { exact: true })).toBeVisible();
  await page.getByTestId("toggle-properties").click();

  // --- native port drag: output port of src → body of read ---
  const points = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const a = graph.getNodeData().find((n: any) => n.id === "src") as any;
    const b = graph.getNodeData().find((n: any) => n.id === "read") as any;
    const [sx, sy] = graph.getClientByCanvas([a.style.x + 74, a.style.y]);
    const [tx, ty] = graph.getClientByCanvas([b.style.x, b.style.y]);
    return { sx, sy, tx, ty };
  });
  await page.mouse.move(points.sx, points.sy);
  await page.mouse.down();
  await page.mouse.move(points.tx, points.ty, { steps: 12 });
  await page.mouse.up();
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getEdgeData()?.length === 3;
  });

  // --- selection: clicking a node opens its property drawer ---
  const clicked = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const node = graph.getNodeData().find((n: any) => n.id === "src") as any;
    const [x, y] = graph.getClientByCanvas([node.style.x, node.style.y]);
    return { x, y };
  });
  await page.mouse.click(clicked.x, clicked.y);
  await expect(page.getByText("节点属性", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: /属\s*性/ }).click();

  // --- node drag: a body drag moves the node, it does not create an edge ---
  const before = await graphState(page);
  const moveFrom = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const graph = el.__industrialGraph;
    const node = graph.getNodeData().find((n: any) => n.id === "src") as any;
    const [x, y] = graph.getClientByCanvas([node.style.x, node.style.y]);
    return { x, y };
  });
  await page.mouse.move(moveFrom.x, moveFrom.y);
  await page.mouse.down();
  await page.mouse.move(moveFrom.x + 60, moveFrom.y + 40, { steps: 8 });
  await page.mouse.move(moveFrom.x + 140, moveFrom.y + 80, { steps: 8 });
  await page.mouse.up();
  await page.waitForFunction((x: number) => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const node = el.__industrialGraph.getNodeData().find((n: any) => n.id === "src");
    return Math.abs(node.style.x - x) > 20;
  }, before!.nodes.find((node) => node.id === "src")!.x);
  expect((await graphState(page))!.edges).toHaveLength(3);

  // --- canvas pan: a background drag changes the viewport, not the nodes ---
  const panBefore = await graphState(page);
  const background = await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    const rect = el.getBoundingClientRect();
    return { x: rect.left + rect.width / 2, y: rect.top + rect.height - 40 };
  });
  await page.mouse.move(background.x, background.y);
  await page.mouse.down();
  await page.mouse.move(background.x + 130, background.y, { steps: 10 });
  await page.mouse.up();
  await page.waitForFunction((viewport: number) => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return Math.abs(el.__industrialGraph.getViewportByCanvas([0, 0])[0] - viewport) > 20;
  }, panBefore!.viewport);

  // --- save, reload and verify the gesture-created edge survived ---
  const saveRequest = page.waitForRequest(request =>
    request.method() === "POST" && request.url().includes("/api/command/industrial/save-workflow"));
  await page.getByRole("button", { name: /保\s*存/ }).click();
  const saved = JSON.parse((await saveRequest).postData() ?? "{}");
  expect(saved.edges).toHaveLength(3);
  expect(saved.edges).toContainEqual({
    source: "src", sourcePort: "files", target: "read", targetPort: "input",
  });

  await page.reload();
  await waitForGraph(page);
  await page.waitForFunction(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getEdgeData()?.length === 3;
  });
  const after = await graphState(page);
  expect(after!.edges).toHaveLength(3);

  // --- palette → canvas HTML5 drag/drop adds a node (no click-to-add) ---
  const beforeDrop = (await graphState(page))!.nodes.length;
  const paletteItem = page.locator(".industrial-palette-node")
    .filter({ has: page.locator(".industrial-palette-node-title", { hasText: /^Local Files$/ }) })
    .first();
  await paletteItem.scrollIntoViewIfNeeded();
  expect(await paletteItem.locator(".industrial-palette-node-title").textContent()).toBe("Local Files");
  await page.evaluate(() => new Promise<void>((resolve) =>
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))));
  expect(await paletteItem.locator(".industrial-palette-node-title").textContent()).toBe("Local Files");
  await paletteItem.dragTo(page.getByTestId("industrial-graph"), { targetPosition: { x: 500, y: 420 } });
  await page.waitForFunction((count: number) => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getNodeData()?.length === count + 1;
  }, beforeDrop);
});
