import { expect, test } from "@playwright/test";
import { bindingsXlsx, machinesXlsx, profilesXlsx } from "./excel-fixtures";

const file = (name: string, base64: string) => ({
  name,
  mimeType: "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
  buffer: Buffer.from(base64, "base64"),
});

test("a user imports machines, file profiles and bindings from real Excel workbooks", async ({ page }) => {
  await page.goto("/industrial/bindings");

  await page.getByLabel("机台 Excel 文件").setInputFiles(
    file("machines.xlsx", machinesXlsx));
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/machines")).json()).data;
    return rows.some((row: { id: string }) => row.id === "e2e-excel-m-1");
  }).toBe(true);

  await page.getByLabel("文件配置 Excel 文件").setInputFiles(
    file("profiles.xlsx", profilesXlsx));
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/file-profiles")).json()).data;
    return rows.some((row: { id: string }) => row.id === "e2e-excel-p-1");
  }).toBe(true);

  await page.getByLabel("绑定 Excel 文件").setInputFiles(
    file("bindings.xlsx", bindingsXlsx));
  const binding = async () => {
    const rows = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
    return rows.find((row: { id: string }) => row.id === "e2e-excel-b-1");
  };
  await expect.poll(async () => (await binding())?.name).toBe("Excel binding");
  expect((await binding()).trigger.kind).toBe("schedule");
  expect((await binding()).trigger.cron).toBe("0 5 * * *");
  expect((await binding()).workflowVersion).toBe(1);

  // The imported row is visible in the user's table after a fresh navigation.
  await page.reload();
  await page.getByLabel("绑定筛选").fill("Excel binding");
  await expect(page.getByRole("row").filter({ hasText: "Excel binding" }).first())
    .toContainText("e2e-excel-p-1");
});
