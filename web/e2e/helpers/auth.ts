import { expect, type Page } from '@playwright/test';

/**
 * Shared e2e helpers: the dev-login + EmpresaPicker scoping every full-flow spec needs,
 * in one place instead of re-derived per spec.
 */

/** Default dev-login role button — the seeded Admin used across the e2e suite (all permissions). */
export const E2E_ROLE = 'Entrar como Admin';

/**
 * Dev-login flow: the root IS the login (no landing page), so go straight there and click a
 * per-role quick-login button. The login page (Development only) lists one button per seeded
 * local account (`DevDataSeeder`); the default is Admin. The token is persisted in localStorage
 * by the app, so reloads stay authenticated.
 */
export async function devLogin(page: Page, roleButton: string | RegExp = E2E_ROLE): Promise<void> {
  await page.goto('/');
  // Quick-login by role. Each button reads "Entrar como <cargo>".
  await page.getByRole('button', { name: roleButton }).click();
  await expect(page).toHaveURL(/\/app(\/|$)/);
}

/**
 * Scope the global EmpresaPicker (the first toolbar <select>) to the seeded company whose
 * legal name matches `match` (default ULBRA). Returns the chosen option's label so callers
 * can keep using it. Throws if no matching option exists — the new spec depends on the seed.
 */
export async function pickCompany(page: Page, match = /ULBRA/i): Promise<string> {
  const picker = page.locator('select').first();
  const option = picker.locator('option', { hasText: match }).first();
  await expect(option).toHaveCount(1);
  const label = (await option.textContent())!.trim();
  await picker.selectOption({ label });
  return label;
}
