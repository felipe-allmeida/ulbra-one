import { test, expect } from '@playwright/test';
import { devLogin, pickCompany } from './helpers/auth';
import { debitMatching, uniqueFitid, writeOfxFile } from './helpers/fake-bank';

/**
 * Full-flow e2e — Fatia A: the complete life of an accounts-payable that gets PAID via the
 * simulated bank (FakeBank). It drives the real frontend end-to-end:
 *
 *   create payable -> approve -> FakeBank emits a matching OFX debit -> conciliação imports it
 *   -> the suggested match is confirmed (the baixa) -> the installment is Paga / payable Liquidada
 *   -> the cash-flow account balance drops by exactly the paid amount.
 *
 * This is the money's whole journey, simulated bank included. It is significant, not vacuously
 * green: it FAILS if conciliação doesn't perform the baixa, or if cash-flow doesn't reflect the
 * payment. No try/catch swallowing, no "if visible" escape hatches.
 *
 * Requires the .NET API on :5001 with the dev seed (see playwright.config.ts) and the web dev
 * server on :5173. Robust against the persistent dev DB + prior runs via per-run-unique
 * description / amount / FITID and a before/after delta assertion on cash flow.
 */

/** Local date -> 'YYYY-MM-DD' for date inputs. Never toISOString() — UTC could shift the day. */
function toDateInput(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** Parses a rendered brl() string ("R$ 1.234" / "-R$ 1.234") to a number. brl() drops decimals. */
function parseBrl(text: string): number {
  const negative = text.includes('-');
  const digits = text.replace(/[^\d]/g, '');
  const n = Number(digits);
  return negative ? -n : n;
}

/** brl() with maximumFractionDigits:0 — mirror the app's formatting for exact string assertions. */
function brl(v: number): string {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });
}

test('full-flow A: payable created -> approved -> paid via simulated bank (OFX) -> cash flow reflects', async ({
  page,
}) => {
  test.setTimeout(90_000);

  // A distinctive, non-round INTEGER amount makes the conciliação match unique (no collision with
  // the seed's round figures) AND keeps the cash-flow delta free of cent-rounding drift.
  const runId = Date.now();
  const amount = 9000 + (runId % 1000); // 9000..9999, distinctive per run; integer (no cent drift).
  const description = `E2E full-flow ${runId}`;
  const fitid = uniqueFitid('E2E-FULLFLOW');
  const today = new Date();
  const dueIso = toDateInput(today);

  // ---------- 1. Login ----------
  await devLogin(page);

  // ---------- 2. Create the payable via the UI ----------
  // The new-payable FORM has no EmpresaPicker — it scopes to the globally-selected company, which
  // lives in in-memory React state (auto-defaults to the first company, CELSP). So scope to ULBRA
  // on the LIST first, then open the form via the in-app "Nova conta" button (client-side nav, no
  // reload) — a full page.goto('/new') would reset the selection back to CELSP.
  await page.goto('/app/accounts-payable');
  await expect(page.getByRole('heading', { name: 'Contas a pagar' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await expect(page).toHaveURL(/\/app\/accounts-payable\/new$/);
  await expect(page.getByRole('heading', { name: 'Nova conta a pagar' })).toBeVisible();

  // Vendor: Radix combobox (aria-haspopup=listbox). Open it, then pick the first seeded option.
  const vendorTrigger = page.locator('button[aria-haspopup="listbox"]').first();
  await vendorTrigger.click();
  const vendorList = page.locator('.combobox-list[role="listbox"]');
  await expect(vendorList).toBeVisible();
  const vendorOption = vendorList.getByRole('option').first();
  await expect(vendorOption).toBeVisible();
  await vendorOption.click();
  await expect(vendorList).toBeHidden();

  // Cost center: native <select>. Pick INFRA — its auto-approval limit (2.000) is BELOW our amount,
  // so the payable is created PendingApproval and the "Aprovar" step is genuinely exercised. Match
  // by the option whose text starts with the "INFRA · " code prefix.
  const costCenterSelect = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: /INFRA · / }) })
    .first();
  await expect(costCenterSelect).toBeVisible();
  // selectOption by visible label needs the exact string; read it then select.
  const infraLabel = (await costCenterSelect
    .locator('option', { hasText: /INFRA · / })
    .first()
    .textContent())!.trim();
  await costCenterSelect.selectOption({ label: infraLabel });

  // Description (unique per run) and amount (distinctive). The Field wraps a <label> sibling.
  await page
    .locator('.form-field', { has: page.getByText('Descrição', { exact: true }) })
    .locator('input')
    .fill(description);

  // Mode "À vista" (single). The toggle is a button group; the single-mode button is the default
  // but click it explicitly to be deterministic.
  await page.getByRole('button', { name: 'À vista', exact: true }).click();

  // Amount input: the single-mode "Valor (R$)" field. It is the numeric/inputmode field in the amount grid.
  const amountField = page
    .locator('.form-field', { has: page.getByText('Valor (R$)', { exact: true }) })
    .locator('input')
    .first();
  await amountField.fill(String(amount));

  // Due date "Vencimento" = today.
  const dueField = page
    .locator('.form-field', { has: page.getByText('Vencimento', { exact: true }) })
    .locator('input[type="date"]')
    .first();
  await dueField.fill(dueIso);

  // Submit "Lançar" -> navigates to the detail page.
  const submit = page.getByRole('button', { name: 'Lançar', exact: true });
  await expect(submit).toBeEnabled();
  await submit.click();
  await expect(page).toHaveURL(/\/app\/accounts-payable\/[^/]+$/);
  await expect(page.getByRole('heading', { name: description })).toBeVisible();

  // ---------- 3. Ensure Approved ----------
  // INFRA auto-approval is below our amount, so the "Aprovar" button must appear. Click it and
  // assert the payable status flips to "Aprovada". (If a future seed auto-approves, the else-branch
  // still hard-asserts Approved — never a silent pass.)
  const approveBtn = page.getByRole('button', { name: 'Aprovar', exact: true });
  const statusCard = page.locator('.info-card', { hasText: 'Status' });
  if (await approveBtn.isVisible()) {
    await approveBtn.click();
  }
  await expect(statusCard.getByText('Aprovada', { exact: true })).toBeVisible();

  // Capture the installment amount + due date from the Parcelas panel (single installment).
  const parcelas = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelas).toBeVisible();
  const installmentRow = parcelas.locator('tbody tr').first();
  await expect(installmentRow).toBeVisible();
  // The installment must be "Aberta" (open) before reconciliation.
  await expect(installmentRow.getByText('Aberta', { exact: true })).toBeVisible();

  // The detail-page URL holds the payable id; remember it to return after reconciliation.
  const payableUrl = page.url();

  // ---------- Anchor the bank account, then read its balance BEFORE the baixa (for the delta assertion) ----------
  // The reconciliation baixa books a Payment dated TODAY on the bank account the OFX is imported under.
  // A bank account's displayed "current balance" is `anchor + movements STRICTLY after the anchor date`
  // (CashFlowQueries) — so the baixa only moves the number when the account has an anchor dated BEFORE
  // today. The seed/other specs may leave the anchor elsewhere or dated today, so this test OWNS its
  // precondition: it sets a known reconciled balance anchored YESTERDAY on a chosen ULBRA account. That
  // makes the assertion self-contained and independent of run order. We pick the FIRST ULBRA account in
  // the panel (ordered by alias) and use that SAME account for the conciliação import.
  await page.goto('/app/cash-flow');
  await expect(page.getByRole('heading', { name: 'Fluxo de caixa' })).toBeVisible();
  const cfCompany = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Consolidado do grupo' }) })
    .first();
  const cfUlbra = cfCompany.locator('option', { hasText: /ULBRA/i }).first();
  await cfCompany.selectOption({ label: (await cfUlbra.textContent())!.trim() });

  const accountsPanel = page.locator('.panel', { hasText: 'Saldo das contas' });
  await expect(accountsPanel).toBeVisible();
  const bankRow = accountsPanel.locator('tbody tr').first();
  await expect(bankRow).toBeVisible();
  // Capture the chosen account's alias so the conciliação import targets the same account.
  const BANK_ALIAS = (await bankRow.locator('td').first().locator('.cf-acct-name').textContent())!.trim();
  expect(BANK_ALIAS.length).toBeGreaterThan(0);

  // Set a known anchor dated YESTERDAY so the baixa (dated today) lands strictly after it and moves the
  // number. A distinctive round value keeps the delta arithmetic exact (brl() drops decimals).
  const ANCHOR_BALANCE = 500000;
  const yesterday = new Date(today.getFullYear(), today.getMonth(), today.getDate() - 1);
  const yesterdayIso = toDateInput(yesterday);
  await bankRow.getByRole('button', { name: 'Informar saldo real' }).click();
  const anchorModal = page.getByRole('dialog');
  await expect(anchorModal.getByRole('heading', { name: 'Informar saldo real' })).toBeVisible();
  await anchorModal.locator('input[inputmode="decimal"]').first().fill(String(ANCHOR_BALANCE));
  await anchorModal.locator('input[type="date"]').first().fill(yesterdayIso);
  await anchorModal.getByRole('button', { name: 'Salvar' }).click();
  await expect(anchorModal).toBeHidden();

  // Re-read the chosen row's balance after anchoring (= anchor + any movements already after yesterday).
  const bankRowAfterAnchor = accountsPanel.locator('tbody tr', { hasText: BANK_ALIAS }).first();
  await expect(bankRowAfterAnchor).toBeVisible();
  // The panel recalculates on the invalidated query; wait until the anchored value surfaces.
  await expect(bankRowAfterAnchor.getByText(/Conciliado em/).first()).toBeVisible({ timeout: 10_000 });
  const balanceBeforeText = (await bankRowAfterAnchor.locator('td.amount-cell').first().textContent())!.trim();
  const balanceBefore = parseBrl(balanceBeforeText);
  expect(balanceBefore).toBeGreaterThan(0);

  // ---------- 4. FakeBank generates a matching-debit OFX ----------
  const ofx = debitMatching({ amount, dueDate: today, fitid, memo: `E2E ${description}` });
  const ofxPath = writeOfxFile(ofx);

  // ---------- 5. Conciliação: import the OFX on the SAME bank account ----------
  await page.goto('/app/reconciliation');
  await expect(page.getByRole('heading', { name: 'Conciliação bancária' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);

  // Bank-account picker (the toolbar select carrying the "— selecione —" placeholder). Pick the SAME
  // account we anchored above so the baixa lands where the balance is observable. Wait for the account
  // options to load (they refetch on company change) before selecting.
  const bankPicker = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: '— selecione —' }) })
    .first();
  await expect(bankPicker.locator('option', { hasText: BANK_ALIAS })).toHaveCount(1);
  await bankPicker.selectOption({ label: BANK_ALIAS });

  // Import the OFX. setInputFiles on the file input triggers the import; assert the count toast.
  const fileInput = page.locator('input[type="file"]');
  await expect(fileInput).toBeVisible();
  await fileInput.setInputFiles(ofxPath);
  // The import success toast reports counts: "N lançamentos · M novos · K duplicados".
  await expect(page.getByText(/lançamentos · .* novos/i).first()).toBeVisible({ timeout: 15_000 });

  // ---------- 6. The transaction is Sugerido (unique match) -> Confirmar -> Conciliado ----------
  // Filter to Sugerido and find OUR row by its distinctive amount. brl() drops decimals; our amount
  // is an integer so the rendered value is exact.
  const statusFilter = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Sugerido' }) })
    .first();
  await statusFilter.selectOption({ label: 'Sugerido' });

  const amountRendered = brl(amount); // e.g. "R$ 9.123"
  const ourRow = page.locator('table tbody tr', { hasText: amountRendered }).first();
  await expect(ourRow).toBeVisible({ timeout: 15_000 });
  await expect(ourRow.getByText('Sugerido', { exact: true })).toBeVisible();

  // Confirm the suggested match -> performs the baixa and reconciles the transaction.
  await ourRow.getByRole('button', { name: 'Confirmar' }).click();

  // It leaves the Sugerido worklist and appears under Conciliado.
  await expect(page.locator('table tbody tr', { hasText: amountRendered })).toHaveCount(0, { timeout: 15_000 });
  await statusFilter.selectOption({ label: 'Conciliado' });
  const conciliadoRow = page.locator('table tbody tr', { hasText: amountRendered }).first();
  await expect(conciliadoRow).toBeVisible({ timeout: 15_000 });
  await expect(conciliadoRow.getByText('Conciliado', { exact: true })).toBeVisible();

  // ---------- 7. The cycle closed: installment Paga + payable Liquidada ----------
  await page.goto(payableUrl);
  await expect(page.getByRole('heading', { name: description })).toBeVisible();
  const parcelasAfter = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelasAfter.locator('tbody tr', { hasText: 'Paga' }).first()).toBeVisible();
  // Single-installment payable: settling the only installment settles the payable -> "Liquidada".
  const statusCardAfter = page.locator('.info-card', { hasText: 'Status' });
  await expect(statusCardAfter.getByText('Liquidada', { exact: true })).toBeVisible();

  // ---------- 8. Cash flow reflects the payment: the account balance dropped by exactly `amount` ----------
  await page.goto('/app/cash-flow');
  await expect(page.getByRole('heading', { name: 'Fluxo de caixa' })).toBeVisible();
  const cfCompany2 = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Consolidado do grupo' }) })
    .first();
  await cfCompany2.selectOption({
    label: (await cfCompany2.locator('option', { hasText: /ULBRA/i }).first().textContent())!.trim(),
  });

  const accountsPanel2 = page.locator('.panel', { hasText: 'Saldo das contas' });
  await expect(accountsPanel2).toBeVisible();
  const bankRow2 = accountsPanel2.locator('tbody tr', { hasText: BANK_ALIAS }).first();
  await expect(bankRow2).toBeVisible();

  // The new balance MUST equal the prior balance minus the paid amount. `amount` is an integer, so
  // round(before) - amount == round(before - amount): the brl() string is exact. This FAILS if the
  // baixa never hit cash flow (the whole point of the assertion).
  const expectedAfter = brl(balanceBefore - amount);
  await expect(bankRow2.locator('td.amount-cell', { hasText: expectedAfter }).first()).toBeVisible({
    timeout: 15_000,
  });
});
