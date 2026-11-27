import { expect, test } from "@playwright/test";


async function chooseAntd(page: any, label: string, option: string) {
  await page.getByLabel(label).click();
  await page.locator(".ant-select-item-option")
    .filter({ hasText: option }).last().click();
  await page.keyboard.press("Escape");
}

const flow = {
  workflowId: "browser-user-flow",
  name: "Browser user flow",
  version: 1,
  nodes: [
    { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 80, y: 120 },
    { id: "read", type: "industrial.reader.csv", config: {}, x: 280, y: 120 },
    { id: "store", type: "industrial.sink.sqlite", config: {}, x: 480, y: 120 },
  ],
  edges: [
    { source: "src", sourcePort: "files", target: "read", targetPort: "input" },
    { source: "read", sourcePort: "records", target: "store", targetPort: "records" },
  ],
  trigger: { kind: "manual", cron: null, eventName: null, config: {} },
  resourceReferences: ["source-1"],
  layout: { lookbackDays: 1 },
};

test("a user configures a schedule and sees its next run", async ({ page }) => {
  const seed = await page.request.post("/api/command/industrial/save-workflow", {
    data: {
      ...flow,
      workflowId: "browser-schedule-flow",
      name: "Browser schedule",
      trigger: { kind: "schedule", cron: "*/5 * * * *", eventName: null, config: { resourceId: "source-1" } },
    },
  });
  expect(seed.ok(), await seed.text()).toBe(true);
  await page.goto("/industrial/designer?workflowId=browser-schedule-flow");
  await expect(page.locator(".industrial-title")).toContainText(/Browser schedule|browser-schedule-flow/);
  await page.getByTestId("open-trigger").click();
  await expect(page.getByLabel("Cron 表达式")).toHaveValue("*/5 * * * *");
  await page.getByRole("button", { name: "保存触发器" }).click();
  await expect(page.getByRole("button", { name: "保存触发器" })).toHaveCount(0);
  const saveRequest = page.waitForRequest(request =>
    request.method() === "POST" &&
    request.url().includes("/api/command/industrial/save-workflow"));
  const saved = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/save-workflow"));
  await page.getByRole("button", { name: /保\s*存/ }).click();
  const payload = JSON.parse((await saveRequest).postData() ?? "{}");
  expect(payload.trigger.kind).toBe("schedule");
  await saved;
  const published = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/publish-workflow"));
  await page.getByRole("button", { name: /发\s*布/ }).click();
  await published;

  const response = await page.request.get("/api/query/industrial/trigger-status");
  const rows = (await response.json()).data.triggers;
  const row = rows.find((item: { workflowId: string }) => item.workflowId === "browser-schedule-flow");
  expect(row.kind).toBe("schedule");
  expect(row.cron).toBe("*/5 * * * *");
  expect(row.nextRun).toBeTruthy();
});

test("a user previews a backfill before running it", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: flow });
  await page.goto("/industrial/designer?workflowId=browser-user-flow");
  await page.getByTestId("open-backfill-preview").click();
  await page.getByRole("button", { name: /扫描最近 1 天/ }).click();
  await expect(page.getByText(/发现 \d+ 个文件/)).toBeVisible();
  await expect(page.getByText(/recent\.csv/).first()).toBeVisible();
});

test("a user sees per-file processing details and classified failures", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: flow });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: flow.workflowId },
  });
  await page.request.post("/api/command/industrial/run-workflow", {
    data: { workflowId: flow.workflowId, resourceId: "source-1" },
  });

  await page.goto("/industrial/files");
  await expect(page.getByText("文件处理详情", { exact: true })).toBeVisible();
  await expect(page.getByText(/recent\.csv/).first()).toBeVisible();
  await page.locator(".ant-table-row-expand-icon").first().click();
  await expect(page.getByText("原因与下一步", { exact: true })).toBeVisible();
});

test("a published cron trigger runs without manual action", async ({ page }) => {
  test.setTimeout(120_000);
  const scheduled = {
    ...flow,
    workflowId: "browser-cron-run",
    name: "Browser cron run",
    trigger: { kind: "schedule", cron: "*/1 * * * *", eventName: null, config: { resourceId: "source-1" } },
  };
  await page.request.post("/api/command/industrial/save-workflow", { data: scheduled });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: scheduled.workflowId },
  });

  await expect.poll(async () => {
    const response = await page.request.get("/api/query/industrial/execution-center");
    const data = (await response.json()).data;
    return data.runs.some((run: { workflowId: string }) => run.workflowId === scheduled.workflowId);
  }, { timeout: 90_000 }).toBe(true);
});

test("a user creates a named workflow from the list and lands in the designer", async ({ page }) => {
  await page.goto("/industrial/workflows");
  await page.getByRole("button", { name: "新建工作流" }).click();
  await page.getByLabel("工作流名称").fill("浏览器新建工作流");
  await page.getByRole("button", { name: /创\s*建/ }).click();

  await expect(page).toHaveURL(/\/industrial\/designer\?workflowId=workflow-/);
  await expect(page.getByText("Industrial Workflow", { exact: true })).toBeVisible();
});

test("a user creates a source resource and tests its connection", async ({ page }) => {
  await page.goto("/industrial/resources");
  await page.getByRole("button", { name: "新建资源" }).click();
  await page.getByLabel("资源 ID").fill("browser-source");
  await page.getByLabel("资源名称").fill("Browser source");
  // The default type is the local directory, so no select interaction is needed.
  await page.getByLabel("目录路径").fill("/tmp/gocordis-industrial-browser-e2e/source");
  await page.getByRole("button", { name: "保存资源" }).click();

  await expect(page.getByText("browser-source", { exact: true })).toBeVisible();
  const row = page.getByText("browser-source", { exact: true }).locator("xpath=ancestor::tr");
  await row.getByRole("button", { name: "测试连接" }).click();
  await expect(page.getByText(/目录可访问|目录存在/)).toBeVisible();
});

test("a user creates a credential without exposing its secret in the list", async ({ page }) => {
  await page.goto("/industrial/credentials");
  await page.getByRole("button", { name: "新建凭证" }).click();
  await page.getByLabel("凭证 ID").fill("browser-credential");
  await page.getByLabel("凭证名称").fill("Browser credential");
  await page.getByLabel("凭证内容").fill("browser-secret-value");
  await page.getByRole("button", { name: "保存凭证" }).click();

  await expect(page.getByText("browser-credential", { exact: true })).toBeVisible();
  await expect(page.getByText("browser-secret-value")).toHaveCount(0);
});

test("the execution center shows real runs and per-target retry actions", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: flow });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: flow.workflowId },
  });
  const run = await page.request.post("/api/command/industrial/run-workflow", {
    data: { workflowId: flow.workflowId, resourceId: "source-1" },
  });
  expect(run.ok()).toBe(true);

  await page.goto("/industrial/executions");
  await expect(page.getByText("采集运行", { exact: true })).toBeVisible();
  await expect(page.getByText("交付目标", { exact: true })).toBeVisible();
  await expect(page.getByText(flow.workflowId, { exact: true }).first()).toBeVisible();
  await expect(page.getByRole("button", { name: "重试此目标" }).first()).toBeDisabled();
});

test("a user edits a resource without losing its connection configuration", async ({ page }) => {
  await page.goto("/industrial/resources");
  await page.getByRole("button", { name: "新建资源" }).click();
  await page.getByLabel("资源 ID").fill("edit-source");
  await page.getByLabel("资源名称").fill("Before rename");
  await page.getByLabel("目录路径").fill("/tmp/gocordis-industrial-browser-e2e/source");
  await page.getByRole("button", { name: "保存资源" }).click();
  await expect(page.getByText("edit-source", { exact: true })).toBeVisible();

  const readConfig = async () => {
    const response = await page.request.get("/api/query/industrial/resources");
    const rows = (await response.json()).data;
    return rows.find((row: { id: string }) => row.id === "edit-source");
  };
  const before = await readConfig();
  expect(before.config.root).toBe("/tmp/gocordis-industrial-browser-e2e/source");

  const row = page.getByRole("row").filter({ hasText: "edit-source" });
  await row.getByRole("button", { name: /编\s*辑/ }).click();
  await page.getByLabel("资源名称").fill("After rename");
  await page.getByRole("button", { name: "保存资源" }).click();
  await expect(page.getByText("After rename", { exact: true })).toBeVisible();

  const after = await readConfig();
  expect(after.name).toBe("After rename");
  expect(after.config.root).toBe(before.config.root);
});

test("a user repairs a failed target and retries only that delivery", async ({ page }) => {
  // A directory is not a valid SQLite database file; opening it fails inside
  // the sink after the delivery has been claimed.
  const broken = "/tmp/gocordis-industrial-browser-e2e/source";
  const fixed = "/tmp/gocordis-industrial-browser-e2e/fixed.db";
  await page.goto("/industrial/resources");
  await page.getByRole("button", { name: "新建资源" }).click();
  await page.getByLabel("资源 ID").fill("broken-target");
  await page.getByLabel("资源名称").fill("Broken target");
  await page.getByRole("button", { name: "SQLite", exact: true }).click();
  await page.getByLabel("数据库文件").fill(broken);
  await page.getByRole("button", { name: "保存资源" }).click();
  await expect(page.getByText("broken-target", { exact: true })).toBeVisible();

  const retryFlow = {
    ...flow,
    workflowId: "browser-retry-flow",
    name: "Browser retry flow",
    nodes: [
      { id: "src", type: "industrial.filesystem.local", config: { resourceId: "source-1" }, x: 80, y: 120 },
      { id: "read", type: "industrial.reader.csv", config: {}, x: 280, y: 120 },
      { id: "store", type: "industrial.sink.sqlite",
        config: { targetResourceId: "broken-target" }, x: 480, y: 120 },
    ],
    edges: [
      { source: "src", sourcePort: "files", target: "read", targetPort: "input" },
      { source: "read", sourcePort: "records", target: "store", targetPort: "records" },
    ],
  };
  await page.request.post("/api/command/industrial/save-workflow", { data: retryFlow });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: retryFlow.workflowId },
  });
  await page.request.post("/api/command/industrial/run-workflow", {
    data: { workflowId: retryFlow.workflowId, resourceId: "source-1" },
  });

  await page.goto("/industrial/executions");
  const brokenRow = page.getByRole("row").filter({ hasText: "broken-target" })
    .filter({ hasText: "Failed" });
  const retryButton = brokenRow.getByRole("button", { name: "重试此目标" });
  await expect(retryButton).toBeEnabled();

  await page.goto("/industrial/resources");
  const targetRow = page.getByRole("row").filter({ hasText: "broken-target" });
  await targetRow.getByRole("button", { name: /编\s*辑/ }).click();
  await page.getByLabel("数据库文件").fill(fixed);
  await page.getByRole("button", { name: "保存资源" }).click();
  const repaired = (await (await page.request.get("/api/query/industrial/resources")).json()).data
    .find((row: { id: string }) => row.id === "broken-target");
  expect(repaired.config.database).toBe(fixed);

  await page.goto("/industrial/executions");
  await page.getByRole("row").filter({ hasText: "broken-target" })
    .filter({ hasText: "Failed" })
    .getByRole("button", { name: "重试此目标" }).click();
  await expect.poll(async () => {
    const response = await page.request.get("/api/query/industrial/execution-center");
    const deliveries = (await response.json()).data.deliveries;
    return deliveries.some((delivery: { targetId: string; status: string }) =>
      delivery.targetId === "broken-target" && delivery.status === "Succeeded");
  }, { timeout: 30_000 }).toBe(true);
});

test("a user manages machines and binds resources to a machine", async ({ page }) => {
  await page.goto("/industrial/machines");
  await page.getByRole("button", { name: "新增设备" }).click();
  await page.getByLabel("设备名称").fill("line-1");
  await page.getByLabel("设备地址").fill("10.0.0.1");
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("line-1", { exact: true }).first()).toBeVisible();

  const resources = (await (await page.request.get("/api/query/industrial/resources")).json()).data;
  const source = resources.find((row: { type: string }) => row.type === "filesystem.local");
  expect(source).toBeTruthy();
  const saveRequest = page.waitForRequest(request =>
    request.method() === "POST" && request.url().includes("/api/command/industrial/save-resource"));
  await chooseAntd(page, `资源 ${source.name} 的设备`, "line-1");
  const saved = JSON.parse((await saveRequest).postData() ?? "{}");
  expect(saved.machineId).toMatch(/^machine-/);
  const machines = (await (await page.request.get("/api/query/industrial/machines")).json()).data;
  expect(machines.find((row: { name: string }) => row.name === "line-1")).toBeTruthy();

  // A bound machine cannot be deleted until the resource is unbound.
  const blocked = await page.request.post("/api/command/industrial/delete-machine", {
    data: { id: saved.machineId },
  });
  expect(blocked.ok()).toBe(false);

  // Clearing the binding sends an explicit null, which must remove the link.
  const cleared = await page.request.post("/api/command/industrial/save-resource", {
    data: { id: source.id, machineId: null },
  });
  expect(cleared.ok()).toBe(true);
  const afterUnbind = (await (await page.request.get("/api/query/industrial/resources")).json()).data
    .find((row: { id: string }) => row.id === source.id);
  expect(afterUnbind.machineId).toBeNull();
});

test("Chinese machine names get distinct ids", async ({ page }) => {
  await page.goto("/industrial/machines");
  await page.getByRole("button", { name: "新增设备" }).click();
  await page.getByLabel("设备名称").fill("一号机台");
  await page.getByLabel("设备地址").fill("10.0.0.2");
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("一号机台", { exact: true }).first()).toBeVisible();
  await page.getByRole("button", { name: "新增设备" }).click();
  await page.getByLabel("设备名称").fill("二号机台");
  await page.getByLabel("设备地址").fill("10.0.0.3");
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("二号机台", { exact: true }).first()).toBeVisible();
  const machines = (await (await page.request.get("/api/query/industrial/machines")).json()).data;
  expect(machines.filter((row: { name: string }) => row.name.startsWith("号")).length).toBe(0);
  expect(machines.filter((row: { name: string }) => row.name.includes("机台")).length).toBe(2);
});

test("a quick wizard creates and publishes a standard workflow", async ({ page }) => {
  await page.goto("/industrial/wizard");
  await expect(page.getByText("快速创建采集任务", { exact: true })).toBeVisible();
  const resources = (await (await page.request.get("/api/query/industrial/resources")).json()).data;
  const source = resources.find((row: { type: string }) => row.type === "filesystem.local");
  const target = resources.find((row: { type: string }) => row.type === "sqlite");
  expect(source && target).toBeTruthy();
  await page.getByLabel("任务名称").fill("Wizard flow");
  await chooseAntd(page, "数据来源", `${source.name} (${source.type})`);
  await chooseAntd(page, "目标资源", `${target.name} (${target.type})`);
  const savedRequest = page.waitForRequest(request =>
    request.method() === "POST" && request.url().includes("/api/command/industrial/save-workflow"));
  const publishedResponse = page.waitForResponse(response =>
    response.request().method() === "POST" &&
    response.url().includes("/api/command/industrial/publish-workflow"));
  await page.getByRole("button", { name: "创建并发布" }).click();
  const saved = JSON.parse((await savedRequest).postData() ?? "{}");
  expect(saved.name).toBe("Wizard flow");
  expect(saved.nodes).toHaveLength(3);
  expect(saved.edges).toHaveLength(2);
  expect((await publishedResponse).ok()).toBe(true);
  await expect(page).toHaveURL(/\/industrial\/designer\?workflowId=quick-/);
});

test("a user manages machines end to end and credentials by type", async ({ page }) => {
  await page.goto("/industrial/credentials");
  await page.getByRole("button", { name: "新建凭证" }).click();
  await page.getByLabel("凭证 ID").fill("machine-credential");
  await page.getByLabel("凭证名称").fill("Machine credential");
  await chooseAntd(page, "凭证类型", "username-password");
  await page.getByLabel("用户名").fill("operator");
  await page.getByLabel("密码").fill("structured-secret");
  await page.getByRole("button", { name: "保存凭证" }).click();
  await expect(page.getByText("machine-credential", { exact: true })).toBeVisible();
  await expect(page.getByText("structured-secret")).toHaveCount(0);

  await page.goto("/industrial/machines");
  await page.getByRole("button", { name: "新增设备" }).click();
  await page.getByLabel("设备名称").fill("managed-line");
  await page.getByLabel("设备地址").fill("10.1.2.3");
  await chooseAntd(page, "设备凭证", "Machine credential (username-password)");
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("managed-line", { exact: true }).first()).toBeVisible();

  const row = page.getByRole("row").filter({ hasText: "managed-line" });
  await row.getByRole("button", { name: /编\s*辑/ }).click();
  await page.getByLabel("设备名称").fill("managed-line-2");
  await page.getByRole("button", { name: "保存设备" }).click();
  await expect(page.getByText("managed-line-2", { exact: true }).first()).toBeVisible();

  const machinesBefore = (await (await page.request.get("/api/query/industrial/machines")).json()).data;
  expect(machinesBefore.some((machine: { name: string }) => machine.name === "managed-line-2")).toBe(true);

  const credentials = (await (await page.request.get("/api/query/industrial/credentials")).json()).data;
  const credential = credentials.find((row: { id: string }) => row.id === "machine-credential");
  expect(credential.version).toBe(1);
  await page.goto("/industrial/credentials");
  const credentialRow = page.getByRole("row").filter({ hasText: "machine-credential" });
  await credentialRow.getByRole("button", { name: /轮\s*换/ }).click();
  await page.getByLabel("用户名").fill("operator2");
  await page.getByLabel("密码").fill("rotated-secret");
  await page.getByRole("dialog").getByRole("button", { name: /轮\s*换/ }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/credentials")).json()).data;
    return rows.find((row: { id: string }) => row.id === "machine-credential")?.version;
  }).toBe(2);
  await expect(page.getByText("rotated-secret")).toHaveCount(0);

  await page.goto("/industrial/machines");
  await page.getByRole("row").filter({ hasText: "managed-line-2" })
    .getByRole("button", { name: /删\s*除/ }).click();
  await expect(page.getByText("managed-line-2", { exact: true })).toHaveCount(0);
});

test("a user generates bulk bindings instead of copying workflows", async ({ page }) => {
  await page.request.post("/api/command/industrial/save-workflow", { data: {
    workflowId: "bulk-template", name: "Bulk template", version: 1, nodes: [], edges: [],
    trigger: { kind: "manual", cron: null, eventName: null, config: {} },
    resourceReferences: [], layout: {},
  } });
  await page.request.post("/api/command/industrial/publish-workflow", {
    data: { workflowId: "bulk-template" },
  });
  await page.goto("/industrial/bindings");
  const csv = ["id,name,endpoint", "bulk-1,Bulk 1,10.0.0.1", "bulk-2,Bulk 2,10.0.0.2"].join("\n");
  await page.getByLabel("机台 CSV").fill(csv);
  await page.getByRole("button", { name: "导入机台" }).click();
  for (const machine of ["bulk-1", "bulk-2"]) {
    await page.request.post("/api/command/industrial/save-resource", { data: {
      id: `resource-${machine}`, name: `Source ${machine}`, type: "filesystem.local",
      provider: "local", config: { root: "/tmp" }, enabled: true,
    } });
  }
  await expect(page.getByText(/预计生成/)).toBeVisible();

  for (const profile of ["Profile A", "Profile B"]) {
    await page.goto("/industrial/file-profiles");
    await page.getByRole("button", { name: "新建文件配置" }).click();
    await page.getByLabel("配置名称").fill(profile);
    await page.getByRole("button", { name: "保存配置" }).click();
    await expect(page.getByText(profile, { exact: true }).first()).toBeVisible();
  }

  await page.goto("/industrial/bindings");
  const chooseMulti = async (label: string, names: string[]) => {
    await page.getByLabel(label).click();
    for (const name of names) {
      await page.locator(".ant-select-item-option").filter({ hasText: name }).last().click();
    }
    await page.keyboard.press("Escape");
  };
  await chooseMulti("工作流模板", ["Bulk template"]);
  await chooseMulti("机台范围", ["Bulk 1", "Bulk 2"]);
  await chooseMulti("文件配置范围", ["Profile A", "Profile B"]);
  const available = (await (await page.request.get("/api/query/industrial/resources")).json()).data;
  const target = available.find((row: { type: string }) => row.type === "sqlite") ?? available[0];
  await chooseMulti("目标资源", [`${target.name} (${target.type})`]);
  await expect(page.getByText(/预计生成 4 条绑定/)).toBeVisible();
  await page.getByLabel("资源 ID 映射").fill("old-source=resource-bulk-1");
  await page.getByLabel("凭证别名映射").fill("old-cred=machine-credential");
  await page.getByRole("button", { name: "生成绑定" }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
    return rows.filter((row: { id: string }) => row.id.startsWith("binding:bulk-template:v1:")).length;
  }).toBe(4);
  const workflows = (await (await page.request.get("/api/query/industrial/workflows")).json()).data;
  expect(workflows.length).toBeLessThan(20);

  const bindingsNow = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
  const firstBinding = bindingsNow.find((row: { id: string }) =>
    row.id.startsWith("binding:bulk-template:v1:bulk-1:"));
  await page.getByRole("row").filter({ hasText: "bulk-1 /" }).first()
    .getByRole("button", { name: /暂\s*停/ }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
    return rows.find((row: { id: string }) => row.id === firstBinding.id)?.enabled;
  }).toBe(false);
  await page.getByRole("row").filter({ hasText: "bulk-1 /" }).first()
    .getByRole("button", { name: /恢\s*复/ }).click();
  await expect.poll(async () => {
    const rows = (await (await page.request.get("/api/query/industrial/bindings")).json()).data;
    return rows.find((row: { id: string }) => row.id === firstBinding.id)?.enabled;
  }).toBe(true);

  const exported = await (await page.request.get("/api/query/industrial/export-config")).text();
  expect(exported).toContain("gocordis-industrial-config-v1");
  expect(exported).not.toContain("blank-secret");
  await page.getByLabel("配置包").fill(exported);
  await page.getByRole("button", { name: "导入配置" }).click();
  await expect(page.getByText("采集任务绑定", { exact: true })).toBeVisible();
});
