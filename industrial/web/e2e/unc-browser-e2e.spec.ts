import { expect, test } from "@playwright/test";

// Windows-only E2E: the SMB share, accounts and packaged host are provisioned
// by e2e-unc-browser.ps1. Nothing here uses a seeded resource or a fake native
// seam; every action starts in the browser.
const path = process.env.GOCORDIS_UNC_PATH ?? "";
const userA = process.env.GOCORDIS_UNC_USER ?? "";
const passwordA = process.env.GOCORDIS_UNC_PASSWORD ?? "";
const userB = process.env.GOCORDIS_UNC_ROTATED_USER ?? "";
const passwordB = process.env.GOCORDIS_UNC_ROTATED_PASSWORD ?? "";
const wrongPassword = process.env.GOCORDIS_UNC_WRONG_PASSWORD ?? "";
const database = process.env.GOCORDIS_UNC_BROWSER_DB ?? "";

test.skip(!path || !userA || !userB || !database, "Windows UNC browser fixture is not configured");

async function choose(page: any, label: string, option: string) {
  await page.getByLabel(label).click();
  await page.locator(".ant-select-item-option").filter({ hasText: option }).last()
    .evaluate((element: HTMLElement) => element.click());
  await page.keyboard.press("Escape");
}

test("blank environment: UI creates UNC machine, credential and resource, reads a file and rotates the credential", async ({ page }) => {
  test.setTimeout(180_000);

  await page.goto("/industrial/credentials");
  await page.getByRole("button", { name: "新建凭证" }).click();
  await page.getByLabel("凭证 ID").fill("unc-credential");
  await page.getByLabel("凭证名称").fill("UNC credential");
  await choose(page, "凭证类型", "domain-account");
  await page.getByLabel("用户名").fill(userA);
  await page.getByLabel("密码").fill(passwordA);
  await page.getByRole("button", { name: "保存凭证" }).click();
  await expect(page.getByText("unc-credential", { exact: true })).toBeVisible();
  await expect(page.getByText(passwordA)).toHaveCount(0);

  await page.goto("/industrial/machines");
  await page.getByRole("button", { name: "新增设备" }).click();
  await page.getByLabel("设备名称").fill("UNC machine");
  await page.getByLabel("设备地址").fill(path);
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("UNC machine", { exact: true }).first()).toBeVisible();

  await page.goto("/industrial/resources");
  await page.getByRole("button", { name: "新建资源" }).click();
  await page.getByLabel("资源 ID").fill("unc-source");
  await page.getByLabel("资源名称").fill("UNC source");
  await page.getByRole("button", { name: "Windows UNC 共享", exact: true }).click();
  await page.getByLabel("目录路径").fill(path);
  await choose(page, "凭证", "UNC credential");
  await page.getByRole("button", { name: "保存资源" }).click();
  await expect(page.getByText("unc-source", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "新建资源" }).click();
  await page.getByLabel("资源 ID").fill("unc-target");
  await page.getByLabel("资源名称").fill("UNC target");
  await page.getByRole("button", { name: "SQLite", exact: true }).click();
  await page.getByLabel("数据库文件").fill(database);
  await page.getByRole("button", { name: "保存资源" }).click();
  await expect(page.getByText("unc-target", { exact: true })).toBeVisible();

  const sourceRow = page.getByRole("row").filter({ hasText: "unc-source" });
  await sourceRow.getByRole("button", { name: "测试连接" }).click();
  await expect(sourceRow).toContainText(/目录可访问|目录存在/);

  await page.goto("/industrial/wizard");
  await page.getByLabel("任务名称").fill("UNC browser flow");
  await choose(page, "数据来源", `UNC source (filesystem.unc)`);
  await choose(page, "目标资源", `UNC target (sqlite)`);
  await page.getByRole("button", { name: "创建并发布" }).click();
  await expect(page).toHaveURL(/\/industrial\/designer\?workflowId=quick-/);
  await page.getByRole("button", { name: /运\s*行/ }).click();

  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/collections")).json()).data;
    return rows.filter((row: { sourceResourceId: string; path: string }) =>
      row.sourceResourceId === "unc-source" && row.path.includes("recent.csv")).length;
  }).toBeGreaterThan(0);
  const deliveries = (await (await page.request.get("/api/query/industrial/execution-center")).json())
    .data.deliveries;
  expect(deliveries.some((delivery: { targetId: string; status: string; records: number }) =>
    delivery.targetId === "unc-target" && delivery.status === "Succeeded" && delivery.records >= 1))
    .toBe(true);

  // Rotate to a wrong password: the next connection must fail with the NEW
  // credential, proving the vault rotation reached the real SMB session.
  await page.goto("/industrial/credentials");
  const credentialRow = page.getByRole("row").filter({ hasText: "unc-credential" });
  await credentialRow.getByRole("button", { name: /轮\s*换/ }).click();
  await page.getByLabel("用户名").fill(userB);
  await page.getByLabel("密码").fill(wrongPassword);
  await page.getByRole("dialog").getByRole("button", { name: /轮\s*换/ }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/credentials")).json()).data;
    return rows.find((row: { id: string }) => row.id === "unc-credential")?.version;
  }).toBe(2);

  await page.goto("/industrial/resources");
  await sourceRow.getByRole("button", { name: "测试连接" }).click();
  const failed = (await (await page.request.get("/api/query/industrial/resource-tests")).json()).data
    .find((row: { resourceId: string }) => row.resourceId === "unc-source");
  expect(failed.ok).toBe(false);

  // Correct secret for the same rotated identity: the next connection succeeds.
  await page.goto("/industrial/credentials");
  await credentialRow.getByRole("button", { name: /轮\s*换/ }).click();
  await page.getByLabel("用户名").fill(userB);
  await page.getByLabel("密码").fill(passwordB);
  await page.getByRole("dialog").getByRole("button", { name: /轮\s*换/ }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/credentials")).json()).data;
    return rows.find((row: { id: string }) => row.id === "unc-credential")?.version;
  }).toBe(3);
  await expect(page.getByText(passwordB)).toHaveCount(0);

  await page.goto("/industrial/resources");
  await sourceRow.getByRole("button", { name: "测试连接" }).click();
  await expect(sourceRow).toContainText(/目录可访问|目录存在/);

  // The workflow still references this resource: deletion must be refused.
  await sourceRow.getByRole("button", { name: "删除" }).click();
  const stillThere = (await (await page.request.get("/api/query/industrial/resources")).json()).data;
  expect(stillThere.some((row: { id: string }) => row.id === "unc-source")).toBe(true);
});
