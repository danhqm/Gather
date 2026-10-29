import { test, expect } from '@playwright/test';

test('real catalogue filters, details and empty state', async ({ page }) => {
  const errors: string[] = [];
  page.on('pageerror', error => errors.push(error.message));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Find your next good plan.' })).toBeVisible();
  await expect(page.getByRole('status')).toHaveText('6 events to make time for');
  await expect(page.locator('.event-card')).toHaveCount(6);
  await page.getByRole('button', { name: 'Workshops', exact: true }).click();
  await page.getByLabel('Price', { exact: true }).selectOption('3000');
  await page.getByRole('button', { name: 'Find events' }).click();
  await expect(page.locator('.event-card')).toHaveCount(1);
  await page.getByRole('button', { name: 'View Make something tangible' }).click();
  await expect(page.getByRole('dialog')).toBeVisible();
  await expect(page.getByRole('dialog')).toContainText('Materials and firing included');
  await expect(page.getByRole('dialog')).toContainText('Asia/Kuala_Lumpur');
  await page.getByRole('button', { name: 'Close event details' }).click();
  await expect(page.getByRole('dialog')).not.toBeVisible();
  await page.getByLabel('City', { exact: true }).selectOption('George Town');
  await page.getByRole('button', { name: 'Find events' }).click();
  await expect(page.getByRole('heading', { name: 'No plans here. Yet.' })).toBeVisible();
  await page.getByRole('button', { name: 'See all events' }).click();
  await expect(page.locator('.event-card')).toHaveCount(6);
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
  expect(errors).toEqual([]);
});

test('calendar filter uses the selected local day', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('From', { exact: true }).fill('2026-11-07');
  await page.getByLabel('Through', { exact: true }).fill('2026-11-07');
  await page.getByRole('button', { name: 'Find events' }).click();
  await expect(page.getByRole('status')).toHaveText('1 events to make time for');
  await expect(page.getByRole('button', { name: 'View Make something tangible' })).toBeVisible();
});

test('API failure shows a recoverable error', async ({ page }) => {
  await page.route('**/api/events?**', route => route.fulfill({ status: 503 }));
  await page.goto('/');
  await expect(page.getByRole('alert')).toContainText('We couldn’t load events');
  await page.unroute('**/api/events?**');
  await page.getByRole('button', { name: 'Try again' }).click();
  await expect(page.locator('.event-card')).toHaveCount(6);
});

test('one-sided dates leave the other boundary open', async ({ page }) => {
  await page.goto('/');
  await page.getByLabel('From', { exact: true }).fill('2026-11-14');
  await page.getByRole('button', { name: 'Find events' }).click();
  await expect(page.getByRole('status')).toHaveText('4 events to make time for');
  await page.getByLabel('From', { exact: true }).fill('');
  await page.getByLabel('Through', { exact: true }).fill('2026-11-14');
  await page.getByRole('button', { name: 'Find events' }).click();
  await expect(page.getByRole('status')).toHaveText('3 events to make time for');
});
