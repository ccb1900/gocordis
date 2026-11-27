function option(page: any, text: string) {
  return page.locator(".ant-select-item-option").filter({ hasText: text }).last();
}

export async function chooseAntd(page: any, label: string, value: string) {
  await page.getByLabel(label).click();
  let match = option(page, value);
  if ((await match.count()) === 0 || !(await match.isVisible())) {
    await page.keyboard.press("Control+a");
    await page.keyboard.type(value);
    match = option(page, value);
  }
  await match.evaluate((element: HTMLElement) => element.click());
  await page.keyboard.press("Escape");
}

export async function chooseAntdMany(page: any, label: string, values: string[]) {
  for (const value of values) await chooseAntd(page, label, value);
}

export async function chooseAntdByFilter(page: any, label: string, filter: string) {
  await page.getByLabel(label).click();
  await page.keyboard.press("Control+a");
  await page.keyboard.type(filter);
  await page.locator(".ant-select-item-option").first()
    .evaluate((element: HTMLElement) => element.click());
  await page.keyboard.press("Escape");
}

export async function chooseAntdByTyping(page: any, label: string, value: string) {
  await page.getByLabel(label).click();
  await page.keyboard.type(value);
  await page.locator(".ant-select-item-option").filter({ hasText: value }).last()
    .evaluate((element: HTMLElement) => element.click());
  await page.keyboard.press("Escape");
}
