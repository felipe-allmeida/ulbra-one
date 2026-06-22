import { test, expect, type Page } from '@playwright/test';

/**
 * Accounting (núcleo contábil) smoke E2E — a happy-path that exercises the
 * Contábil section end-to-end: dev-auth, the seeded BR chart + posted journal
 * entries, the trial balance / DRE / balance-sheet proofs, and the Draft → Post
 * journal-entry write flow.
 *
 * It mirrors web/e2e/smoke.spec.ts: same dev-login, same EmpresaPicker scoping,
 * pt-BR labels the app actually renders, Radix combobox handling. It is a smoke
 * (happy path) and a regression net for routing/auth/render/write — NOT an
 * exhaustive assertion on business data.
 *
 * Requires the .NET API on :5001 with the accounting endpoints + dev seed
 * (BR chart template + posted journal entries for the ULBRA demo company) and
 * the web dev server on :5173 (see playwright.config.ts).
 */

const EMAIL = 'e2e@ulbra.br';

/** Landing -> login -> dev-token -> dashboard. Mirrors the other specs. */
async function devLogin(page: Page) {
  await page.goto('/');
  await page
    .getByRole('button', { name: /Entrar/i })
    .first()
    .click();
  await expect(page).toHaveURL(/\/login$/);
  await page.locator('input[type="email"]').fill(EMAIL);
  await page.getByRole('button', { name: 'Entrar', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/inicio$/);
}

/** Scope the global EmpresaPicker (first toolbar select) to the seeded ULBRA company. */
async function pickUlbra(page: Page) {
  const picker = page.locator('select').first();
  const ulbra = picker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await picker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }
}

test('login -> plano de contas contábil renders the seeded chart tree', async ({ page }) => {
  await devLogin(page);

  // Navigate to the Contábil "Plano de contas contábil" nav item. (The Financeiro
  // "Plano de Contas" item is distinct; match the accounting label exactly.)
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Plano de contas contábil' }).click();
  await expect(page).toHaveURL(/\/app\/contabil\/plano-de-contas$/);
  await expect(page.getByRole('heading', { name: 'Plano de contas contábil' })).toBeVisible();

  // The chart is company-scoped: pick ULBRA, which the dev seed populated with the
  // BR template (synthetic roots 1..5 + analytical leaves).
  await pickUlbra(page);

  // The tree renders the seeded synthetic roots. Assert the five group roots by
  // their code cell (avoids monetary/data coupling); ".t-mono" holds the code.
  for (const code of ['1', '2', '3', '4', '5']) {
    await expect(page.locator('.t-mono', { hasText: new RegExp(`^${code}$`) }).first()).toBeVisible();
  }
  // And an analytical leaf is present (Caixa under Ativo).
  await expect(page.locator('.t-mono', { hasText: /^1\.01\.01$/ }).first()).toBeVisible();
});

test('login -> balancete proves débito == crédito', async ({ page }) => {
  await devLogin(page);

  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Balancete' }).click();
  await expect(page).toHaveURL(/\/app\/contabil\/balancete$/);
  await expect(page.getByRole('heading', { name: 'Balancete' })).toBeVisible();

  // Default scope is "Consolidado" (no company filter), which includes the seeded
  // posted entries. The trial balance renders rows and the débito==crédito proof
  // badge ("Débitos = Créditos").
  await expect(page.locator('table tbody tr').first()).toBeVisible();
  await expect(page.getByText('Débitos = Créditos')).toBeVisible();
});

test('login -> DRE renders Receitas / Despesas / Resultado', async ({ page }) => {
  await devLogin(page);

  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'DRE' }).click();
  await expect(page).toHaveURL(/\/app\/contabil\/dre$/);
  await expect(page.getByRole('heading', { name: 'DRE' })).toBeVisible();

  // Consolidated DRE over the seeded posted entries: revenue + expense sections
  // and a profit/loss result row render. The seed posts revenue (180k) and
  // expenses (134.3k) so the result is a profit ("Lucro do exercício").
  await expect(page.getByText('Receitas').first()).toBeVisible();
  await expect(page.getByText('(−) Despesas').first()).toBeVisible();
  await expect(page.getByText(/Lucro do exercício|Prejuízo do exercício/)).toBeVisible();
});

test('login -> Balanço renders and proves Ativo = Passivo + PL', async ({ page }) => {
  await devLogin(page);

  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Balanço Patrimonial' }).click();
  await expect(page).toHaveURL(/\/app\/contabil\/balanco$/);
  await expect(page.getByRole('heading', { name: 'Balanço Patrimonial' })).toBeVisible();

  // Consolidated balance sheet over the seeded posted entries. The Ativo column
  // panel renders (its title is a .panel-title span) and the closing badge proves
  // it balances ("Ativo = Passivo + PL"). The period result is folded into PL so
  // the invariant holds.
  await expect(page.locator('.panel-title', { hasText: 'Ativo' }).first()).toBeVisible();
  await expect(page.getByText('Ativo = Passivo + PL')).toBeVisible();
});

test('login -> lançamentos: create a balanced Draft, then Post it', async ({ page }) => {
  await devLogin(page);

  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Lançamentos contábeis' }).click();
  await expect(page).toHaveURL(/\/app\/contabil\/lancamentos$/);
  await expect(page.getByRole('heading', { name: 'Lançamentos contábeis' })).toBeVisible();

  // The journal is company-scoped: pick ULBRA so its seeded analytical accounts
  // are available in the line comboboxes.
  await pickUlbra(page);

  // Open the new-entry drawer.
  await page.getByRole('button', { name: 'Novo lançamento' }).click();
  const drawer = page.getByRole('dialog');
  await expect(drawer.getByRole('heading', { name: 'Novo lançamento' })).toBeVisible();

  // Histórico (description) is required. Use a unique value per run so the row this
  // test creates is unambiguous even when the (persistent) dev DB already holds
  // drafts from earlier runs. The Field wraps a <label> as a sibling of the input
  // (no htmlFor), so target the input via the .form-field with the "Histórico" label.
  const description = `Lançamento e2e ${Date.now()}`;
  await drawer
    .locator('.form-field', { has: page.getByText('Histórico', { exact: true }) })
    .locator('input')
    .fill(description);

  // The form starts with two lines (Debit + Credit). Each line's account is a Radix
  // combobox (aria-haspopup=listbox). The cost-center combobox in the same row is
  // also aria-haspopup=listbox, so scope to the row's FIRST trigger (the account
  // cell). Line 1 (Debit) -> Caixa (1.01.01); Line 2 (Credit) -> Receita (4.01.01).
  // Open one at a time so only one listbox is mounted.
  const line1Account = drawer
    .locator('table.journal-lines tbody tr')
    .nth(0)
    .locator('button[aria-haspopup="listbox"]')
    .first();
  const line2Account = drawer
    .locator('table.journal-lines tbody tr')
    .nth(1)
    .locator('button[aria-haspopup="listbox"]')
    .first();

  const pickInRow = async (trigger: ReturnType<Page['locator']>, optionLabel: RegExp) => {
    await trigger.click();
    const listbox = page.locator('.combobox-list[role="listbox"]');
    await expect(listbox).toBeVisible();
    const option = listbox.getByRole('option', { name: optionLabel }).first();
    await expect(option).toBeVisible();
    await option.click();
    await expect(listbox).toBeHidden();
  };

  await pickInRow(line1Account, /1\.01\.01/);
  await pickInRow(line2Account, /4\.01\.01/);

  // Amounts: enter the same value on both lines so Σdébitos = Σcréditos. The amount
  // input is type=number in each line.
  const amounts = drawer.locator('table.journal-lines input[type="number"]');
  await amounts.nth(0).fill('1234.56');
  await amounts.nth(1).fill('1234.56');

  // The balance badge flips to "Balanceado" and Save enables. Save the Draft.
  await expect(drawer.getByText('Balanceado')).toBeVisible();
  const save = drawer.getByRole('button', { name: 'Salvar' });
  await expect(save).toBeEnabled();
  await save.click();

  // Drawer closes; the new Draft appears in the list with the Rascunho badge.
  await expect(drawer).toBeHidden();
  const draftRow = page.locator('table tbody tr').filter({ hasText: description }).first();
  await expect(draftRow).toBeVisible();
  await expect(draftRow.getByText('Rascunho')).toBeVisible();

  // Post it: the Draft row carries a "Contabilizar" action. After posting the row's
  // badge flips to "Contabilizado" (Posted) and it becomes read-only.
  await draftRow.getByRole('button', { name: 'Contabilizar' }).click();
  await expect(draftRow.getByText('Contabilizado')).toBeVisible();
});
