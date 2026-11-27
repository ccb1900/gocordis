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

  // Scale setup is intentionally API-driven: the UI creation flow is covered
  // by parameters/import-preview specs. This spec proves the 800-row model and
  // downstream user operations without depending on virtualized select lists.
  const rows = Array.from({ length: 80 }, (_, machine) =>
    Array.from({ length: 10 }, (_, profile) =>
      `binding:scale-template:v1:scale-${machine}:profile-scale-${profile},` +
      `Scale ${machine} / Scale profile ${profile},scale-${machine},scale-template,1,` +
      `profile-scale-${profile},resource-scale-${machine},scale-target,schedule,0 2 * * *`).join("\n")).join("\n");
  const imported = await page.request.post("/api/command/industrial/import-bindings", {
    data: {
      csv: "id,name,machineId,workflowId,workflowVersion,fileProfileId,sourceResourceId," +
           "targetResourceIds,triggerKind,cron\n" + rows + "\n",
    },
  });
  expect(imported.ok(), await imported.text()).toBe(true);
  await page.goto("/industrial/bindings");

  const readBindings = async () =>
    (await (await page.request.get("/api/query/industrial/bindings")).json()).data as any[];
  await expect.poll(async () =>
    (await readBindings()).filter((row) => row.id.startsWith("binding:scale-template:v1:scale-")).length,
  { timeout: 60_000 }).toBe(800);
  const first = (await readBindings()).find((row) => row.id.startsWith("binding:scale-template:v1:"));
  expect(first.trigger.kind).toBe("schedule");
  expect(first.trigger.cron).toBe("0 2 * * *");

  // Simulate a host restart: a fresh page reload reads the same persisted rows.
  await page.reload();
  await expect.poll(async () =>
    (await readBindings()).filter((row) => row.id.startsWith("binding:scale-template:v1:scale-")).length,
  { timeout: 60_000 }).toBe(800);

  const workflows = (await (await page.request.get("/api/query/industrial/workflows")).json()).data;
  expect(workflows.filter((row: { workflowId: string }) => row.workflowId === "scale-template").length).toBe(1);

  await page.getByLabel("绑定筛选").fill("Scale 0 / Scale profile 0");
  const row = page.getByRole("row").filter({ hasText: "Scale 0 / Scale profile 0" }).first();
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
