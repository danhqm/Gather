import { test, expect } from '@playwright/test';
import type { APIRequestContext, Page } from '@playwright/test';

async function post(client: APIRequestContext, path: string, data: unknown) {
  const csrfResponse = await client.get('/api/auth/csrf');
  expect(csrfResponse.ok()).toBe(true);
  const csrf = await csrfResponse.json();
  return client.post(path, { headers: { 'X-CSRF-TOKEN': csrf.token }, data });
}

test('two browsers cannot hold the last ticket and the countdown ignores a wrong clock', async ({ page, browser, baseURL }, testInfo) => {
  test.skip(!process.env.DEMO_ACCOUNT_PASSWORD, 'Set the local demo organiser password.');
  const admin = await browser.newContext({ baseURL });
  const other = await browser.newContext({ baseURL, viewport: { width: 390, height: 844 } });
  const second = await other.newPage();
  const title = `Last admission ${testInfo.project.name} ${Date.now()}`;
  let eventId: string | undefined;
  try {
    expect((await post(admin.request, '/api/auth/login', { email: 'organiser@gather.test', password: process.env.DEMO_ACCOUNT_PASSWORD })).ok()).toBe(true);
    const start = new Date(Date.now() + 14 * 86400000);
    const created = await post(admin.request, '/api/organiser/events', {
      organisationId: '00000000-0000-0000-0000-000000000101', event: {
        title, description: 'A one-person event for the concurrency demonstration.', category: 'workshop', city: 'Kuala Lumpur', venue: 'Demo studio',
        timezone: 'Asia/Kuala_Lumpur', startsAt: start.toISOString(), endsAt: new Date(start.getTime() + 3600000).toISOString(), currency: 'MYR', priceMinor: 2500, capacity: 1,
      },
    });
    expect(created.status()).toBe(201); eventId = (await created.json()).id;
    expect((await post(admin.request, `/api/organiser/events/${eventId}/publish`, {})).ok()).toBe(true);
    for (const [index, target] of [page, second].entries()) {
      expect((await post(target.request, '/api/auth/register', {
        email: `race-${Date.now()}-${testInfo.project.name}-${index}@example.test`, password: 'Local-Test-Password123!', displayName: 'Race attendee',
      })).status()).toBe(201);
      await target.clock.setFixedTime(new Date('2035-01-01T00:00:00Z'));
      await target.goto('/');
      await target.getByRole('button', { name: 'Workshops', exact: true }).click();
      await target.getByRole('button', { name: `View ${title}`, exact: true }).click();
    }
    await Promise.all([page.getByRole('button', { name: 'Reserve tickets' }).click(), second.getByRole('button', { name: 'Reserve tickets' }).click()]);
    await expect.poll(async () => (await Promise.all([page, second].map(target => target.getByText('1 ticket · Held', { exact: true }).count()))).reduce((a, b) => a + b, 0)).toBe(1);
    const winner = await page.getByText('1 ticket · Held', { exact: true }).isVisible() ? page : second;
    const loser = winner === page ? second : page;
    await expect(loser.getByRole('alert')).toContainText('not enough tickets');
    await expect(winner.getByRole('timer')).toHaveText(/^(34:\d{2}|35:00)$/);
    await winner.reload();
    await winner.getByRole('button', { name: 'Workshops', exact: true }).click();
    await winner.getByRole('button', { name: `View ${title}`, exact: true }).click();
    await expect(winner.getByText('1 ticket · Held', { exact: true })).toBeVisible();
    await winner.getByRole('button', { name: 'Cancel reservation', exact: true }).click();
    await expect(winner.getByText('1 ticket · Cancelled', { exact: true })).toBeVisible();
    await loser.getByRole('button', { name: 'Reserve tickets' }).click();
    await expect(loser.getByText('1 ticket · Held', { exact: true })).toBeVisible();
  } finally {
    for (const target of [page, second]) {
      if (eventId) {
        const cached = await target.evaluate(id => sessionStorage.getItem(`gather.reservation.${id}`), eventId).catch(() => null);
        const bookingId = cached && JSON.parse(cached).bookingId;
        if (bookingId) await post(target.request, `/api/bookings/${bookingId}/cancel`, {});
      }
    }
    if (eventId) await post(admin.request, `/api/organiser/events/${eventId}/unpublish`, {});
    await other.close(); await admin.close();
  }
});

test('a reservation prompts sign-in and returns to the event', async ({ page }, testInfo) => {
  await page.goto('/');
  await page.getByRole('button', { name: 'View Make something tangible' }).click();
  await page.getByRole('button', { name: 'Reserve tickets' }).click();
  await expect(page.getByRole('dialog', { name: 'Good to see you.' })).toBeVisible();
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  const signup = page.getByRole('dialog', { name: 'Make yourself at home.' });
  await signup.getByLabel('Your name').fill('Returning attendee');
  await signup.getByLabel('Email address').fill(`return-${Date.now()}-${testInfo.project.name}@example.test`);
  await signup.getByLabel('Password', { exact: true }).fill('Local-Test-Password123!');
  await signup.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('dialog', { name: 'Make something tangible' })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Reserve tickets' })).toBeVisible();
});
