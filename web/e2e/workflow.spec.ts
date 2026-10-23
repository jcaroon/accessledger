import { test, expect } from '@playwright/test';

test('request, independent decision, and read-only evidence form a complete workflow', async ({ page }) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'Start a private demo' }).click();
  await expect(page.getByRole('heading', { name: 'Your access, in focus.' })).toBeVisible();
  await page.getByRole('button', { name: 'New request' }).click();
  await page.getByLabel('Request title').fill('Review quarterly delivery evidence');
  await page.getByLabel('Resource', { exact: true }).selectOption('project-atlas');
  await page.getByLabel('Business reason').fill('I need read access to prepare the upcoming delivery review with the project team.');
  await page.getByRole('button', { name: 'Submit request' }).click();
  await expect(page.getByRole('dialog')).toBeHidden();
  await expect(page.locator('.request-row').filter({ hasText: 'Review quarterly delivery evidence' })).toBeVisible();
  await page.getByLabel('Demo role').selectOption('Approver');
  const row = page.locator('.request-row').filter({ hasText: 'Review quarterly delivery evidence' });
  await expect(row).toBeVisible(); await row.click();
  await page.getByRole('button', { name: 'Approve request' }).click();
  await page.getByLabel('Approval reason').fill('Read access is appropriate for the documented delivery review.');
  await page.getByRole('button', { name: 'Record decision' }).click();
  await expect(page.locator('.detail-body > .badge')).toHaveText('Approved');
  await expect(page.locator('.timeline')).toContainText('Read access is appropriate');
  await page.getByLabel('Demo role').selectOption('Auditor');
  await expect(page.getByRole('button', { name: 'New request' })).toHaveCount(0);
  await page.getByRole('button', { name: 'Audit trail', exact: true }).click();
  await expect(page.locator('.audit-panel')).toContainText('Read access is appropriate');
  const status = await page.evaluate(async () => {
    const session = await (await fetch('/api/session')).json();
    const response = await fetch('/api/requests', { method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': session.csrfToken }, body: JSON.stringify({ title: 'Forbidden auditor request', resourceKey: 'reporting', scope: 'Read', reason: 'This should be refused by the server even without the button.' }) });
    return response.status;
  });
  expect(status).toBe(403);
});

test('two visitors receive separate workspaces', async ({ browser }) => {
  const a = await browser.newContext(), b = await browser.newContext();
  try {
    const first = await a.newPage(), second = await b.newPage();
    for (const page of [first, second]) { await page.goto('/'); await page.getByRole('button', { name: 'Start a private demo' }).click(); await expect(page.getByRole('heading', { name: 'Your access, in focus.' })).toBeVisible(); }
    await first.getByRole('button', { name: 'New request' }).click();
    await first.getByLabel('Request title').fill('Private workspace isolation check');
    await first.getByLabel('Resource', { exact: true }).selectOption('reporting');
    await first.getByLabel('Business reason').fill('This sample request belongs only to the first browser workspace.');
    await first.getByRole('button', { name: 'Submit request' }).click();
    await expect(first.locator('.request-row').filter({ hasText: 'Private workspace isolation check' })).toBeVisible();
    await second.getByLabel('Search requests').fill('Private workspace isolation check');
    await expect(second.getByRole('heading', { name: 'No matching requests' })).toBeVisible();
  } finally { await a.close(); await b.close(); }
});

test('mobile layout and keyboard-accessible request dialog', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto('/'); await page.getByRole('button', { name: 'Start a private demo' }).click();
  await expect(page.getByRole('heading', { name: 'Your access, in focus.' })).toBeVisible();
  await page.getByRole('button', { name: 'New request' }).click();
  await expect(page.getByLabel('Request title')).toBeFocused();
  await page.keyboard.press('Escape'); await expect(page.getByRole('dialog')).toBeHidden();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
