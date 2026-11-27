import { expect, test } from "@playwright/test";

test("80 machines x 10 profiles generates 800 bindings with policy, restart and diagnostics", async ({ page }) => {
  test.setTimeout(240_000);
  // A real template so binding backfill reaches the persisted Elsa run rather
  // than only refreshing a diagnostic cell.
  await page.request.post("/api/command/industrial/save-workflow", { data: {
    workflowId: "scale-template", name: "Scale template", version: 1,
    nodes: [
      { id: "src", type: "industrial.filesystem.local",
        config: { resourceId: "{{SourceResource}}" }, x: 80, y: 120 },
      { id: "read", type: "industrial.reader.csv", config: {}, x: 260, y: 120 },
      { id: "sink", type: "industrial.sink.sqlite",
        config: { targetResourceId: "{{TargetResource}}" }, x: 440, y: 120 },
    ],
    edges: [
      { source: "src", sourcePort: "files", target: "read", targetPort: "input" },
      { source: "read", sourcePort: "records", target: "sink", targetPort: "records" },
    ],
    trigger: { kind: "manual", cron: null, eventName: null, config: {} },
    resourceReferences: ["{{SourceResource}}"], layout: {},
  } });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: "scale-template" },
  });
  for (let i = 0; i < 80; i++) {
    await page.request.post("/api/command/industrial/save-resource", { data: {
      id: `resource-scale-${i}`, name: `Scale source ${i}`, type: "filesystem.local",
      provider: "local", config: { root: "/tmp/gocordis-industrial-browser-e2e/source" }, enabled: true,
    } });
  }
  await page.request.post("/api/command/industrial/save-resource", { data: {
    id: "scale-target", name: "Scale target", type: "sqlite", provider: "local",
    config: { database: "/tmp/scale-target.db" }, enabled: true,
  } });

  await page.goto("/industrial/bindings");
  const machines = Array.from({ length: 80 }, (_, i) =>
    `scale-${i},Scale ${i},10.0.0.${i % 255}`).join("\n");
  expect((await page.request.post("/api/command/industrial/import-machines", {
    data: { csv: `id,name,endpoint\n${machines}` },
  })).ok()).toBe(true);
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/machines")).json()).data;
    return rows.filter((row: { id: string }) => row.id.startsWith("scale-")).length;
  }).toBe(80);

  const profiles = Array.from({ length: 10 }, (_, i) =>
    `profile-scale-${i},Scale profile ${i},*.csv,csv,,true,utf-8,value=reading,value`).join("\n");
  expect((await page.request.post("/api/command/industrial/import-file-profiles", {
    data: {
      csv: `id,name,fileNamePattern,format,delimiter,hasHeader,encoding,mapping,required\n${profiles}`,
    },
  })).ok()).toBe(true);
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/file-profiles")).json()).data;
    return rows.filter((row: { id: string }) => row.id.startsWith("profile-scale-")).length;
  }).toBe(10);

  const choose = async (label: string, option: string) => {
    await page.getByLabel(label).click();
    await page.keyboard.type(option);
    await page.locator(".ant-select-item-option").filter({ hasText: option }).last()
      .evaluate((el: HTMLElement) => el.click());
    await page.keyboard.press("Escape");
  };
  await page.getByLabel("工作流 ID").fill("scale-template");
  // Pin the exact scope: unrelated rows in a shared host must not change the
  // 80 x 10 contract under test.
  await page.getByLabel("机台 ID 列表").fill(
    Array.from({ length: 80 }, (_, index) => `scale-${index}`).join(","));
  await page.getByLabel("文件配置 ID 列表").fill(
    Array.from({ length: 10 }, (_, index) => `profile-scale-${index}`).join(","));
  await page.getByLabel("目标资源 ID 列表").fill("scale-target");
  await page.getByLabel("绑定触发策略选择").selectOption("schedule");
  await page.getByLabel("绑定 Cron").fill("0 2 * * *");
  await page.getByLabel("来源资源模板").fill("resource-{machine}");
  await expect(page.getByText(/预计生成 800 条绑定/)).toBeVisible();
  await page.getByRole("button", { name: "生成绑定" }).click();

  const readBindings = async () =>
    (await (await page.request.get("/api/query/industrial/bindings")).json()).data as any[];
  await expect.poll(async () =>
    (await readBindings()).filter((row) => row.id.startsWith("binding:scale-template:v1:")).length
  ).toBe(800);
  const first = (await readBindings()).find((row) => row.id.startsWith("binding:scale-template:v1:"));
  expect(first.trigger.kind).toBe("schedule");
  expect(first.trigger.cron).toBe("0 2 * * *");

  // Simulate a host restart: a fresh page reload reads the same persisted rows.
  await page.reload();
  await expect.poll(async () =>
    (await readBindings()).filter((row) => row.id.startsWith("binding:scale-template:v1:")).length
  ).toBe(800);

  const workflows = (await (await page.request.get("/api/query/industrial/workflows")).json()).data;
  expect(workflows.filter((row: { workflowId: string }) => row.workflowId === "scale-template").length).toBe(1);

  await page.getByLabel("绑定筛选").fill("scale-0 / profile-scale-0");
  const row = page.getByRole("row").filter({ hasText: "scale-0 / profile-scale-0" }).first();
  await row.getByRole("button", { name: /暂\s*停/ }).click();
  await expect.poll(async () => (await readBindings()).find((item) => item.id === first.id)?.enabled).toBe(false);

  const readStatus = async () =>
    (await (await page.request.get("/api/query/industrial/binding-status")).json()).data;
  const runsBefore = (await readStatus()).runs.length;
  await row.getByRole("button", { name: /补采 7 天/ }).click();
  // "Backfill" must reach the real runtime, not merely paint a diagnostic cell.
  await expect.poll(async () => (await readStatus()).runs.length, { timeout: 30_000 })
    .toBeGreaterThan(runsBefore);
  await expect(page.getByText(/最近运行|暂无运行/)).toBeVisible();
});
