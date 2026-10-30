import { test, expect } from '@playwright/test';

test('free booking confirms once and a return URL cannot confirm a paid hold', async ({ page }, testInfo) => {
  async function post(path: string, data: unknown) {
    const csrf = await (await page.request.get('/api/auth/csrf')).json();
    return page.request.post(path, { headers: { 'X-CSRF-TOKEN': csrf.token }, data });
  }
  expect((await post('/api/auth/register', {
    email: `checkout-${Date.now()}-${testInfo.project.name}@example.test`, displayName: 'Free attendee', password: 'Local-Test-Password123!',
  })).status()).toBe(201);
  await page.goto('/');
  await page.getByRole('button', { name: 'Community', exact: true }).click();
  await page.getByRole('button', { name: 'View Ideas worth building' }).click();
  await page.getByRole('button', { name: 'Reserve tickets' }).click();
  await page.getByRole('button', { name: 'Confirm free booking' }).click();
  await expect(page).toHaveURL(/\/bookings\/[\da-f-]+$/);
  await expect(page.getByRole('heading', { name: 'You’re on the list.' })).toBeVisible();
  await page.reload();
  await expect(page.getByText('Confirmed', { exact: true })).toBeVisible();

  const held = await post('/api/events/00000000-0000-0000-0000-000000000001/reservations', { quantity: 1, requestId: crypto.randomUUID() });
  expect(held.status()).toBe(201);
  const { bookingId } = await held.json();
  try {
    await page.goto(`/bookings/${bookingId}?checkout=returned`);
    await expect(page.getByText('Held', { exact: true })).toBeVisible();
    await expect(page.getByRole('heading', { name: 'Your place is on hold.' })).toBeVisible();
  } finally { await post(`/api/bookings/${bookingId}/cancel`, {}); }
});
