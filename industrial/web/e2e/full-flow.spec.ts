import { expect, test } from "@playwright/test";

const sourceDir = "/tmp/gocordis-industrial-user-flow/source";
const goodDb = "/tmp/gocordis-industrial-user-flow/good.db";
const fixedDb = "/tmp/gocordis-industrial-user-flow/fixed.db";

async function choose(page: any, label: string, option: string) {
  const native = page.locator(`select[aria-label="${label}"]`);
  if ((await native.count()) > 0) {
    await native.selectOption({ label: option });
    return;
  }
  const direct = page.getByLabel(label);
  if ((await direct.count()) > 0 && await direct.first().isVisible()) {
    await direct.first().click();
  } else {
    await page.getByText(label, { exact: true }).locator("..").locator("input").first().click();
  }
  const byRole = page.getByRole("option", { name: option });
  if ((await byRole.count()) > 0) {
    await byRole.last().evaluate((el: HTMLElement) => el.click());
  } else {
    await page.getByText(option, { exact: true }).last().evaluate((el: HTMLElement) => el.click());
  }
}

async function chooseIndex(page: any, label: string, value: string) {
  await page.getByLabel(label).selectOption(value);
}

async function addNode(page: any, title: string, index = 0) {
  await page.locator(".industrial-palette-node")
    .filter({ has: page.locator(".industrial-palette-node-title", { hasText: new RegExp(`^${title}$`) }) })
    .nth(index)
    .click();
  await expect(page.getByText("节点属性", { exact: true })).toBeVisible();
}

async function apply(page: any) {
  await page.getByRole("button", { name: "应用配置" }).click();
}

test("blank environment: browser-only create, configure, run, fail, repair, retry", async ({ page }) => {
  test.setTimeout(240_000);

  await page.goto("/industrial/credentials");
  await page.getByRole("button", { name: "新建凭证" }).click();
  await page.getByLabel("凭证 ID").fill("flow-credential");
  await page.getByLabel("凭证名称").fill("Flow credential");
  await page.getByLabel("凭证内容").fill("blank-secret");
  await page.getByRole("button", { name: "保存凭证" }).click();
  await expect(page.getByText("flow-credential", { exact: true })).toBeVisible();
  await expect(page.getByText("blank-secret")).toHaveCount(0);

  // The operator also registers the machine itself before any resource exists.
  await page.goto("/industrial/machines");
  await page.getByRole("button", { name: "新增设备" }).click();
  await page.getByLabel("设备名称").fill("Flow machine");
  await page.getByLabel("设备地址").fill("10.0.0.7");
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("Flow machine", { exact: true }).first()).toBeVisible();

  await page.goto("/industrial/resources");
  const createResource = async (id: string, name: string, sqlite: boolean, database?: string) => {
    await page.getByRole("button", { name: "新建资源" }).click();
    await page.getByLabel("资源 ID").fill(id);
    await page.getByLabel("资源名称").fill(name);
    if (sqlite) {
      await page.getByRole("button", { name: "SQLite", exact: true }).click();
      await page.getByLabel("数据库文件").fill(database ?? "");
      await choose(page, "凭证", "Flow credential");
    } else {
      await page.getByLabel("目录路径").fill(sourceDir);
    }
    await page.getByRole("button", { name: "保存资源" }).click();
    await expect(page.getByText(id, { exact: true })).toBeVisible();
  };
  await createResource("flow-source", "Flow source", false);
  await createResource("flow-good", "Flow good", true, goodDb);
  await createResource("flow-bad", "Flow bad", true, sourceDir);

  const row = page.getByRole("row").filter({ hasText: "flow-source" });
  await row.getByRole("button", { name: "测试连接" }).click();
  await expect(page.getByText(/目录可访问|目录存在/)).toBeVisible();

  await page.goto("/industrial/workflows");
  await page.getByRole("button", { name: "新建工作流" }).click();
  await page.getByLabel("工作流名称").fill("Blank environment flow");
  await page.getByRole("button", { name: /创\s*建/ }).click();
  await page.waitForURL(/\/industrial\/designer\?workflowId=/);
  await expect(page.locator(".industrial-title")).toContainText("Blank environment flow");
  await expect(page.locator(".industrial-palette-node").first()).toBeVisible();

  await addNode(page, "Local Files");
  await choose(page, "Source resource", "Flow source (filesystem.local)");
  await apply(page);

  await addNode(page, "CSV Reader");
  await apply(page);

  await addNode(page, "Field Mapping");
  await page.locator('[data-testid="field-mapping"]').getByRole("button", { name: "添加字段" }).click();
  await page.getByLabel("目标字段 1").fill("value");
  await page.getByLabel("来源字段 1").fill("reading");
  await apply(page);

  await addNode(page, "Validation");
  await page.locator('[data-testid="field-required"]').getByRole("button", { name: "添加字段" }).click();
  await page.getByLabel("字段 1").fill("value");
  await apply(page);

  await addNode(page, "SQLite");
  await choose(page, "Target resource", "Flow good (sqlite)");
  await apply(page);

  await addNode(page, "SQLite");
  await choose(page, "Target resource", "Flow bad (sqlite)");
  await apply(page);

  const pairs = [["n1", "n2"], ["n2", "n3"], ["n3", "n4"], ["n4", "n5"], ["n4", "n6"]];
  for (let index = 0; index < pairs.length; index++) {
    const [from, to] = pairs[index];
    await chooseIndex(page, "连线起点", from);
    await chooseIndex(page, "连线终点", to);
    await page.getByRole("button", { name: "添加连线" }).click();
    await page.waitForFunction((count: number) => {
      const el = document.querySelector('[data-testid="industrial-graph"]') as any;
      return el?.__industrialGraph?.getEdgeData()?.length === count;
    }, index + 1);
  }
  expect(await page.evaluate(() => {
    const el = document.querySelector('[data-testid="industrial-graph"]') as any;
    return el?.__industrialGraph?.getEdgeData()?.length ?? 0;
  })).toBe(5);

  await page.getByRole("button", { name: /属\s*性/ }).click();
  await page.getByLabel("运行资源").selectOption("flow-source");
  const saveRequest = page.waitForRequest(request =>
    request.method() === "POST" && request.url().includes("/api/command/industrial/save-workflow"));
  await page.getByRole("button", { name: /保\s*存/ }).click();
  const saved = JSON.parse((await saveRequest).postData() ?? "{}");
  expect(saved.nodes).toHaveLength(6);
  expect(saved.edges).toHaveLength(5);
  expect(saved.nodes.some((node: any) => node.config.targetResourceId === "flow-bad")).toBe(true);

  await page.getByRole("button", { name: /发\s*布/ }).click();
  await expect(page.getByText("未保存", { exact: true })).toHaveCount(0);
  await page.getByRole("button", { name: /运\s*行/ }).click();

  await expect.poll(async () => {
    const response = await page.request.get("/api/query/industrial/execution-center");
    const deliveries = (await response.json()).data.deliveries;
    return deliveries.some((delivery: any) =>
      delivery.targetId === "flow-bad" && delivery.status === "Failed");
  }, { timeout: 30_000 }).toBe(true);

  await page.goto("/industrial/executions");
  await expect(page.getByText("采集运行", { exact: true })).toBeVisible();
  await expect(page.getByRole("row").filter({ hasText: "flow-bad" })
    .getByRole("button", { name: "重试此目标" })).toBeEnabled();

  await page.goto("/industrial/resources");
  await page.getByRole("row").filter({ hasText: "flow-bad" })
    .getByRole("button", { name: /编\s*辑/ }).click();
  const dialog = page.getByRole("dialog");
  await dialog.getByLabel("数据库文件").fill(fixedDb);
  const repairRequest = page.waitForRequest(request =>
    request.method() === "POST" && request.url().includes("/api/command/industrial/save-resource"));
  const repairResponse = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/save-resource"));
  await dialog.getByRole("button", { name: "保存资源" }).click();
  expect((await repairResponse).ok()).toBe(true);
  const repaired = (await (await page.request.get("/api/query/industrial/resources")).json()).data
    .find((resource: any) => resource.id === "flow-bad");
  expect(repaired.config.database).toBe(fixedDb);

  await page.goto("/industrial/executions");
  await page.getByRole("row").filter({ hasText: "flow-bad" })
    .getByRole("button", { name: "重试此目标" }).click();
  await expect.poll(async () => {
    const response = await page.request.get("/api/query/industrial/execution-center");
    const deliveries = (await response.json()).data.deliveries;
    return deliveries.some((delivery: any) =>
      delivery.targetId === "flow-bad" && delivery.status === "Succeeded" && delivery.records === 3);
  }, { timeout: 30_000 }).toBe(true);

  const good = (await (await page.request.get("/api/query/industrial/execution-center")).json())
    .data.deliveries.find((delivery: any) => delivery.targetId === "flow-good");
  expect(good.status).toBe("Succeeded");
  expect(good.records).toBe(3);
});
