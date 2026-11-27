async function pick(page: any, label: string, value: string) {
  await page.getByLabel(label).click();
  const option = page.locator(".ant-select-dropdown:visible .ant-select-item-option").filter({ hasText: value }).last();
  for (let attempt = 0; attempt < 40; attempt++) {
    if ((await option.count()) > 0 && (await option.isVisible())) {
      await option.evaluate((element: HTMLElement) => element.click());
      return;
    }
    await page.mouse.wheel(0, 160);
  }
  throw new Error(`option ${value} not found in ${label}`);
}

export async function chooseAntd(page: any, label: string, value: string) {
  await pick(page, label, value);
  await page.keyboard.press("Escape");
}

export async function chooseAntdMany(page: any, label: string, values: string[]) {
  for (const value of values) await pick(page, label, value);
  await page.keyboard.press("Escape");
}

export async function chooseAntdByFilter(page: any, label: string, filter: string) {
  await page.getByLabel(label).click();
  await page.keyboard.type(filter);
  await page.keyboard.press("ArrowDown");
  await page.keyboard.press("Enter");
  await page.keyboard.press("Escape");
}

export async function chooseAntdByTyping(page: any, label: string, value: string) {
  await page.getByLabel(label).click();
  await page.keyboard.type(value);
  await page.locator(".ant-select-dropdown:visible .ant-select-item-option")
    .filter({ hasText: value }).last()
    .evaluate((element: HTMLElement) => element.click());
  await page.keyboard.press("Escape");
}
