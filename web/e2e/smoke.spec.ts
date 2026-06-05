import { test, expect } from '@playwright/test';

/**
 * Smoke E2E — a single resilient happy-path that exercises routing, dev-auth and
 * data render. It's a regression net: it should catch routing/auth/render
 * breakage before refactors, NOT assert on specific business data.
 *
 * Requires the .NET API on :5001 with seeded data (see playwright.config.ts).
 * UI assertions use the pt-BR labels the app actually renders.
 */

const EMAIL = 'e2e@ulbra.br';

test('landing -> login -> app dashboard -> fornecedores', async ({ page }) => {
  // 1. Landing hero renders.
  await page.goto('/');
  await expect(page.getByRole('heading', { level: 1 })).toBeVisible();

  // 2. "Entrar" CTA goes to /login. There are several "Entrar" buttons on the
  // landing (nav + hero + CTA); the first visible one is fine.
  await page
    .getByRole('button', { name: /Entrar/i })
    .first()
    .click();
  await expect(page).toHaveURL(/\/login$/);

  // 3. Dev login: fill email + submit. On success the app lands on the dashboard.
  await page.locator('input[type="email"]').fill(EMAIL);
  // Exact match: the disabled "Entrar com Google" button also starts with "Entrar".
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/inicio$/);

  // Logged-in marker: the sidebar nav with registry items is visible.
  const sidebar = page.locator('aside.sidebar');
  await expect(sidebar.getByRole('button', { name: 'Fornecedores' })).toBeVisible();

  // 4. Navigate to Fornecedores; the seeded list renders at least one row.
  await sidebar.getByRole('button', { name: 'Fornecedores' }).click();
  await expect(page).toHaveURL(/\/app\/fornecedores$/);
  await expect(page.getByRole('heading', { name: 'Fornecedores' })).toBeVisible();

  // The list is scoped to the selected company (EmpresaPicker). Pick the seeded
  // company that owns the vendors so we assert on a populated list. Match by the
  // "ULBRA" substring in its legal name; fall back to keeping the default if
  // absent, then just assert the table rendered without crashing.
  const picker = page.locator('select').first();
  const ulbra = picker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await picker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // Data path rendered: at least one seeded row is visible.
  await expect(page.locator('table tbody tr.row-click').first()).toBeVisible();
});

test('logged-out access to /app/inicio redirects to /login', async ({ browser }) => {
  // Fresh context => no token in localStorage.
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/app/inicio');
  await expect(page).toHaveURL(/\/login$/);
  await context.close();
});
