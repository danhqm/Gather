import { test, expect } from '@playwright/test';
import type { APIRequestContext } from '@playwright/test';

test('customer QR tickets admit once through manual entry and a real QR video frame', async ({ page, browser, baseURL }, testInfo) => {
  test.setTimeout(100000);
  test.skip(!process.env.DEMO_ACCOUNT_PASSWORD, 'Set the local demo staff password.');
  async function post(client: APIRequestContext, path: string, data: unknown) {
    const csrf = await (await client.get('/api/auth/csrf')).json();
    let response = await client.post(path, { headers: { 'X-CSRF-TOKEN': csrf.token }, data });
    if (response.status() === 429 && path.startsWith('/api/auth/')) {
      // The complete browser suite deliberately shares the real server's 20/minute login limit.
      await expect(async () => {
        response = await client.post(path, { headers: { 'X-CSRF-TOKEN': csrf.token }, data });
        expect(response.status()).not.toBe(429);
      }).toPass({ intervals: [2000], timeout: 65000 });
    }
    return response;
  }
  expect((await post(page.request, '/api/auth/register', {
    email: `tickets-${Date.now()}-${testInfo.project.name}@example.test`, displayName: 'Ticket attendee', password: 'Local-Test-Password123!',
  })).status()).toBe(201);
  const hold = await post(page.request, '/api/events/00000000-0000-0000-0000-000000000003/reservations', { quantity: 2, requestId: crypto.randomUUID() });
  expect(hold.status()).toBe(201);
  const { bookingId } = await hold.json();
  expect((await post(page.request, `/api/bookings/${bookingId}/checkout`, {})).ok()).toBe(true);
  await page.goto('/tickets');
  await page.getByRole('button', { name: 'Show ticket 1', exact: true }).click();
  const qrImage = page.getByRole('img', { name: 'QR admission code for ticket 1' });
  await expect(qrImage).toBeVisible();
  const firstCode = await page.getByLabel('Manual admission code').inputValue();
  await page.getByRole('button', { name: 'Close ticket' }).click();
  await page.getByRole('button', { name: 'Show ticket 2', exact: true }).click();
  await expect(page.getByRole('img', { name: 'QR admission code for ticket 2' })).toBeVisible();
  const secondImage = await page.getByRole('img', { name: 'QR admission code for ticket 2' }).getAttribute('src');
  const secondCode = await page.getByLabel('Manual admission code').inputValue();

  const staffContext = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 } });
  try {
    expect((await post(staffContext.request, '/api/auth/login', { email: 'staff@gather.test', password: process.env.DEMO_ACCOUNT_PASSWORD })).ok()).toBe(true);
    const staff = await staffContext.newPage();
    await staff.goto('/check-in');
    await expect(staff.getByLabel('Event', { exact: true })).toBeVisible();
    await staff.getByLabel('Event', { exact: true }).selectOption('00000000-0000-0000-0000-000000000003');
    await staff.getByLabel('Admission code', { exact: true }).fill(firstCode);
    await staff.getByRole('button', { name: 'Check ticket', exact: true }).click();
    await expect(staff.getByRole('status')).toHaveText('Admitted. Welcome in!');
    await staff.getByRole('button', { name: 'Check ticket', exact: true }).click();
    await expect(staff.getByRole('alert')).toContainText('Already used.');

    // Supply an actual generated QR image as camera frames; decoding remains the production ZXing path.
    await staff.evaluate(async imageUrl => {
      const image = new Image(); image.src = imageUrl!; await image.decode();
      const canvas = document.createElement('canvas'); canvas.width = image.width; canvas.height = image.height;
      const context = canvas.getContext('2d')!; context.drawImage(image, 0, 0);
      Object.defineProperty(navigator.mediaDevices, 'getUserMedia', { configurable: true,
        value: async () => { const stream = canvas.captureStream(10); setTimeout(() => context.drawImage(image, 0, 0), 100); return stream; } });
    }, secondImage);
    await staff.getByRole('button', { name: 'Scan QR with camera' }).click();
    await expect(staff.getByLabel('Admission code', { exact: true })).toHaveValue(secondCode, { timeout: 15000 });
    await expect(staff.getByRole('status')).toHaveText('Code captured. Choose Check ticket to admit.');
    await staff.getByRole('button', { name: 'Check ticket', exact: true }).click();
    await expect(staff.getByRole('status')).toHaveText('Admitted. Welcome in!');
    await page.getByRole('button', { name: 'Close ticket' }).click();
    await page.getByRole('button', { name: 'Refresh tickets' }).click();
    await expect(page.getByText(/ALREADY USED/)).toHaveCount(2);
  } finally { await staffContext.close(); }
});
