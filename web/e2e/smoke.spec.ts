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

test('login -> contas a pagar list -> detail with parcelas', async ({ page }) => {
  // Reuse the dev-login flow: landing -> login -> dev-token -> dashboard.
  await page.goto('/');
  await page
    .getByRole('button', { name: /Entrar/i })
    .first()
    .click();
  await expect(page).toHaveURL(/\/login$/);
  await page.locator('input[type="email"]').fill(EMAIL);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/inicio$/);

  // Navigate to Contas a Pagar via the sidebar nav item.
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Contas a pagar' }).click();
  await expect(page).toHaveURL(/\/app\/contas-a-pagar$/);
  await expect(page.getByRole('heading', { name: 'Contas a pagar' })).toBeVisible();

  // The list is scoped to the selected company (EmpresaPicker). Pick the seeded
  // company that owns the payables — match by the "ULBRA" substring in its legal
  // name; if absent keep the default and still assert the table renders.
  const picker = page.locator('select').first();
  const ulbra = picker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await picker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // Data path: the seeder creates 9 payables (incl. parcelado), so multiple
  // installment rows render. Assert at least one clickable row is visible.
  const firstRow = page.locator('table tbody tr.row-click').first();
  await expect(firstRow).toBeVisible();

  // Drill into the detail: clicking a row routes to /app/contas-a-pagar/<id>
  // and the "Parcelas" panel lists at least one installment row.
  await firstRow.click();
  await expect(page).toHaveURL(/\/app\/contas-a-pagar\/[^/]+$/);
  const parcelas = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelas).toBeVisible();
  await expect(parcelas.locator('table tbody tr').first()).toBeVisible();
});

test('login -> contas a pagar -> dar baixa on an open installment', async ({ page }) => {
  // Reuse the dev-login flow: landing -> login -> dev-token -> dashboard.
  await page.goto('/');
  await page
    .getByRole('button', { name: /Entrar/i })
    .first()
    .click();
  await expect(page).toHaveURL(/\/login$/);
  await page.locator('input[type="email"]').fill(EMAIL);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/inicio$/);

  // Navigate to Contas a Pagar.
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Contas a pagar' }).click();
  await expect(page).toHaveURL(/\/app\/contas-a-pagar$/);
  await expect(page.getByRole('heading', { name: 'Contas a pagar' })).toBeVisible();

  // Scope to the ULBRA company that owns the seeded payables.
  const picker = page.locator('select').first();
  const ulbra = picker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await picker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // Find a row for a payable that is Approved with an installment still open: the
  // status cell shows the "Aberta" installment badge, which the list only renders
  // for Approved payables. That's exactly the row a baixa can act on. The list is
  // paginated and ordered by due date, so the row may not be on page 1 — page
  // forward with "Próxima" until it appears.
  await expect(page.locator('table tbody tr.row-click').first()).toBeVisible();
  const openRow = page.locator('table tbody tr.row-click', { hasText: 'Aberta' }).first();
  const nextPage = page.getByRole('button', { name: 'Próxima' });
  for (let i = 0; i < 10 && (await openRow.count()) === 0; i++) {
    if (!(await nextPage.isVisible()) || (await nextPage.isDisabled())) break;
    await nextPage.click();
    await expect(page.locator('table tbody tr.row-click').first()).toBeVisible();
  }
  await expect(openRow).toBeVisible();
  await openRow.click();

  // Detail page for the chosen payable.
  await expect(page).toHaveURL(/\/app\/contas-a-pagar\/[^/]+$/);
  const parcelas = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelas).toBeVisible();

  // The open installment row carries the "Dar baixa" action button.
  const baixaButton = parcelas.getByRole('button', { name: 'Dar baixa' }).first();
  await expect(baixaButton).toBeVisible();
  await baixaButton.click();

  // The baixa drawer (Radix dialog) opens with the payment form.
  const drawer = page.getByRole('dialog');
  await expect(drawer.getByRole('heading', { name: 'Dar baixa na parcela' })).toBeVisible();

  // "Data efetiva" and "Valor pago" come pre-filled (today / remaining). We only
  // need to choose a bank account and submit — no monetary-value assertions.
  const bankSelect = drawer.locator('select').first();
  // Pick the first real account (index 0 is the "— selecione —" placeholder).
  await bankSelect.selectOption({ index: 1 });

  // "Forma de pagamento" defaults to PIX; leave it. Submit the baixa.
  await drawer.getByRole('button', { name: 'Dar baixa', exact: true }).click();

  // After a successful baixa the drawer closes and the installment badge flips to
  // "Paga" — asserting that confirms the write went through end-to-end. For a
  // single-installment payable the payable status also becomes "Liquidada".
  await expect(drawer).toBeHidden();
  await expect(parcelas.locator('tbody tr', { hasText: 'Paga' }).first()).toBeVisible();
});

test('logged-out access to /app/inicio redirects to /login', async ({ browser }) => {
  // Fresh context => no token in localStorage.
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/app/inicio');
  await expect(page).toHaveURL(/\/login$/);
  await context.close();
});
