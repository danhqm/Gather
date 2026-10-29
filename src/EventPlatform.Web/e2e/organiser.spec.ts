import { test, expect } from '@playwright/test';

test('organiser creates, edits, publishes and unpublishes an event', async ({ page }, testInfo) => {
  const password = process.env.DEMO_ACCOUNT_PASSWORD;
  test.skip(!password, 'Set DEMO_ACCOUNT_PASSWORD for the seeded local organiser.');
  const title = `Browser gathering ${testInfo.project.name} ${Date.now()}`;
  const day = new Date(Date.now() + 14 * 86400000).toISOString().slice(0, 10);
  let eventId: string | undefined;
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.getByLabel('Email address').fill('organiser@gather.test');
  await page.getByLabel('Password', { exact: true }).fill(password!);
  await page.getByRole('dialog').getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.getByRole('link', { name: 'Manage events', exact: true }).click();
  try {
    await page.getByRole('button', { name: 'Create event' }).click();
    const editor = page.getByRole('dialog', { name: 'Create an event.' });
    await editor.getByLabel('Event title').fill(title);
    await editor.getByLabel('Description').fill('A hands-on gathering created through the organiser dashboard.');
    await editor.getByLabel('Venue', { exact: true }).fill('Demo Studio');
    await editor.getByLabel('Starts at').fill(`${day}T18:00`);
    await editor.getByLabel('Ends at').fill(`${day}T20:00`);
    await editor.getByLabel('Ticket price (RM)').fill('25.50');
    const creation = page.waitForResponse(response => response.request().method() === 'POST' && response.url().endsWith('/api/organiser/events'));
    await editor.getByRole('button', { name: 'Create draft' }).click();
    const created = await creation;
    expect(created.status()).toBe(201);
    eventId = (await created.json()).id;
    let row = page.locator('.management-row').filter({ hasText: title });
    await expect(row).toContainText('Draft');
    await row.getByRole('button', { name: `Edit ${title}`, exact: true }).click();
    await page.getByRole('dialog').getByLabel('Description').fill('Updated event details, saved to PostgreSQL.');
    await page.getByRole('button', { name: 'Save changes' }).click();
    await expect(page.getByRole('dialog')).not.toBeVisible();
    await row.getByRole('button', { name: `Publish ${title}`, exact: true }).click();
    await expect(row.locator('.publication-state')).toHaveText('Published');
    await page.goto('/');
    await page.getByRole('button', { name: 'Community', exact: true }).click();
    await page.getByRole('button', { name: `View ${title}`, exact: true }).click();
    await expect(page.getByRole('dialog')).toContainText('Updated event details, saved to PostgreSQL.');
    await expect(page.getByRole('dialog')).toContainText('25.50');
    await page.goto('/organiser');
    row = page.locator('.management-row').filter({ hasText: title });
    await row.getByRole('button', { name: `Unpublish ${title}`, exact: true }).click();
    await expect(row.locator('.publication-state')).toHaveText('Draft');
    expect((await page.request.get(`/api/events/${eventId}`)).status()).toBe(404);
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
  } finally {
    if (eventId) {
      const csrf = await (await page.request.get('/api/auth/csrf')).json();
      await page.request.post(`/api/organiser/events/${eventId}/unpublish`, { headers: { 'X-CSRF-TOKEN': csrf.token }, data: {} });
    }
  }
});
