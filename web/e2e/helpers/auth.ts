import { expect, type Page } from '@playwright/test';

/**
 * Shared e2e helpers extracted from the existing specs (smoke/accounting/cash-flow),
 * so the full-flow spec reuses the exact established flow instead of re-deriving it.
 *
 * These mirror the dev-login + EmpresaPicker scoping the other specs already perform;
 * they intentionally do NOT change those specs — they only give the new spec a single
 * source of truth for the login + company-pick boilerplate.
 */

/** Default dev-login email — the seeded admin used across the e2e suite. */
export const E2E_EMAIL = 'e2e@ulbra.br';

/**
 * Dev-login flow: landing -> "Entrar" CTA -> /login -> fill email -> submit -> dashboard.
 * Lands on /app/inicio. The token is persisted in localStorage by the app.
 */
export async function devLogin(page: Page, email: string = E2E_EMAIL): Promise<void> {
  await page.goto('/');
  // The landing has several "Entrar" buttons (nav + hero + CTA); the first visible is fine.
  await page
    .getByRole('button', { name: /Entrar/i })
    .first()
    .click();
  await expect(page).toHaveURL(/\/login$/);
  await page.locator('input[type="email"]').fill(email);
  // Exact match: the disabled "Entrar com Google" button also starts with "Entrar".
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/inicio$/);
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
