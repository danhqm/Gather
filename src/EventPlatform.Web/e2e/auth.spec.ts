import { test, expect } from '@playwright/test';

test('customer can register, refresh, sign out and sign in', async ({ page }, testInfo) => {
  const email = `attendee-${Date.now()}-${testInfo.project.name}@example.test`;
  const password = 'Local-Test-Password123!';
  await page.goto('/');
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  await page.getByRole('button', { name: 'New here? Create an account' }).click();
  const dialog = page.getByRole('dialog', { name: 'Make yourself at home.' });
  await dialog.getByLabel('Your name').fill('Test Attendee');
  await dialog.getByLabel('Email address').fill(email);
  await dialog.getByLabel('Password', { exact: true }).fill(password);
  await dialog.getByRole('button', { name: 'Create account', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
  await page.reload();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
  await page.getByRole('button', { name: 'Sign out', exact: true }).click();
  await page.getByRole('button', { name: 'Sign in', exact: true }).click();
  const login = page.getByRole('dialog', { name: 'Good to see you.' });
  await login.getByLabel('Email address').fill(email);
  await login.getByLabel('Password', { exact: true }).fill(password);
  await login.getByRole('button', { name: 'Sign in', exact: true }).click();
  await expect(page.getByRole('button', { name: 'Sign out', exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBe(true);
});
