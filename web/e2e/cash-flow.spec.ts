import { test, expect } from '@playwright/test';

/**
 * Cash-flow (Fluxo de caixa) smoke E2E — mirrors the aging spec: dev-login, navigate
 * via the sidebar, scope to the seeded ULBRA company, then exercise the panel's main
 * read paths plus the single write (reconciled balance).
 *
 * Requires the .NET API on :5001 with seeded data (the dev seeder now seeds
 * receivables + a reconciled-balance anchor on ULBRA's main bank account). UI
 * assertions use the pt-BR labels the app renders.
 */

const EMAIL = 'e2e@ulbra.br';

test('login -> fluxo de caixa -> KPIs, granularity, drill drawer + set real balance', async ({ page }) => {
  // Dev-login flow: landing -> login -> dev-token -> dashboard.
  await page.goto('/');
  await page
    .getByRole('button', { name: /Entrar/i })
    .first()
    .click();
  await expect(page).toHaveURL(/\/login$/);
  await page.locator('input[type="email"]').fill(EMAIL);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/inicio$/);

  // Navigate to Fluxo de caixa via the sidebar nav item.
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Fluxo de caixa' }).click();
  await expect(page).toHaveURL(/\/app\/cash-flow$/);
  await expect(page.getByRole('heading', { name: 'Fluxo de caixa' })).toBeVisible();

  // Scope to the ULBRA company that owns the seeded payables/receivables/anchor.
  // The company control is the first toolbar select (it carries the "Consolidado
  // do grupo" option). Cash-flow defaults to the globally-selected company, but
  // pick ULBRA explicitly so the seeded accounts + anchor are in scope.
  const companyPicker = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Consolidado do grupo' }) })
    .first();
  const ulbra = companyPicker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await companyPicker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // KPI cards render with the seeded data.
  await expect(page.getByText('Saldo hoje')).toBeVisible();
  await expect(page.getByText('Menor saldo projetado')).toBeVisible();
  await expect(page.getByText('Fôlego de caixa')).toBeVisible();
  // At least one currency value is shown somewhere on the panel.
  await expect(page.getByText(/R\$/).first()).toBeVisible();

  // The periods table renders at least one row.
  const periodsPanel = page.locator('.panel', { hasText: 'Períodos' });
  await expect(periodsPanel).toBeVisible();
  await expect(periodsPanel.locator('table tbody tr').first()).toBeVisible();

  // Switch granularity (Mensal -> Semanal). The granularity control is the
  // toolbar select offering "Semanal"; the query refetches and the table re-renders.
  const granularity = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Semanal' }) })
    .first();
  await granularity.selectOption({ label: 'Semanal' });
  await expect(periodsPanel.locator('table tbody tr').first()).toBeVisible();

  // Switch back to Mensal for the drill (fewer, denser buckets).
  await granularity.selectOption({ label: 'Mensal' });
  await expect(periodsPanel.locator('table tbody tr').first()).toBeVisible();

  // Open the drill drawer on a period: the inflow/outflow amount cells are
  // clickable (dotted underline). Click the first row's outflow cell — the
  // seeded payables guarantee outflow buckets. The drawer is a Radix dialog
  // whose sub-title is "Parcelas do período".
  const firstRow = periodsPanel.locator('table tbody tr').first();
  // Columns: período | abertura | entradas | saídas | resultado | fechamento.
  // The outflow cell is the 4th cell (index 3) and carries the click handler.
  await firstRow.locator('td').nth(3).click();
  const drawer = page.getByRole('dialog');
  await expect(drawer).toBeVisible();
  await expect(drawer.getByText('Parcelas do período')).toBeVisible();
  // Close the drill drawer before the next interaction.
  await page.keyboard.press('Escape');
  await expect(drawer).toBeHidden();

  // Exercise the "informar saldo real" form on one account. The accounts panel
  // lists the seeded bank account with an "Informar saldo real" button.
  const accountsPanel = page.locator('.panel', { hasText: 'Saldo das contas' });
  await expect(accountsPanel).toBeVisible();
  const setBalanceBtn = accountsPanel.getByRole('button', { name: 'Informar saldo real' }).first();
  await expect(setBalanceBtn).toBeVisible();
  await setBalanceBtn.click();

  // The reconcile modal opens (Radix dialog) with the balance + date form.
  const modal = page.getByRole('dialog');
  await expect(modal.getByRole('heading', { name: 'Informar saldo real' })).toBeVisible();

  // Set a new real balance. The balance input is the autofocused decimal field.
  const balanceInput = modal.locator('input[inputmode="decimal"]').first();
  await balanceInput.fill('123456');

  // Anchor the balance as of TODAY. The API rejects a future reconciliation date
  // (400, AEL-596), so we can't anchor in the far future. Today works just as well
  // for determinism: the per-account balance is anchor + movements STRICTLY after
  // the anchor date, and the seed's realized payments are dated before today while
  // any same-day baixa (today == anchor) is excluded by the strict comparison — so
  // the recalculated current balance equals the new anchor exactly.
  const dateInput = modal.locator('input[type="date"]').first();
  const now = new Date();
  const todayIso = `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}-${String(now.getDate()).padStart(2, '0')}`;
  await dateInput.fill(todayIso);

  // Submit. On success the modal closes and the cash-flow query is invalidated, so
  // the accounts panel recalculates. The modal closing confirms the write went
  // through end-to-end.
  await modal.getByRole('button', { name: 'Salvar' }).click();
  await expect(modal).toBeHidden();

  // With no movements after the anchor date, current balance == new anchor.
  // brl() drops decimals: 123456 -> "R$ 123.456". Asserting it surfaced in the
  // accounts panel proves the write persisted and the panel recalculated.
  await expect(accountsPanel.getByText(/R\$\s?123\.456/).first()).toBeVisible({ timeout: 10_000 });
});
