import { test, expect, type Page } from '@playwright/test';
import { devLogin, pickCompany } from './helpers/auth';
import { ofxStatement, uniqueFitid, writeOfxFile } from './helpers/fake-bank';

/**
 * Full-flow e2e — Fatia C: the conciliação surface itself, the "banking heart". Where Fatia A
 * drove ONE matching debit through to the cash-flow delta, this spec exercises the reconciliation
 * worklist end-to-end with a MULTI-line OFX statement and every action the UI offers:
 *
 *   import (with counts) -> automatic suggestion -> Confirmar (baixa) -> Desfazer (unmatch) ->
 *   re-Confirmar -> manual match via the drawer (pick the installment) -> Ignorar an orphan ->
 *   idempotent reimport (dedup by FITID, no duplicate rows).
 *
 * It drives the REAL frontend (in-app nav, never page.goto('/novo')) and is significant, not
 * vacuously green: it FAILS if matching, confirm, unmatch, manual-match, ignore, or idempotency
 * break. No try/catch swallowing, no "if visible" escape hatches around critical assertions.
 *
 * Determinism on the persistent dev DB: three DISTINCT, non-round, magnitude-separated amounts so
 * each debit's match is unique AND the rendered brl() strings never substring-collide; per-run
 * unique FITIDs (dedup key) and descriptions.
 *
 * Final OFX shape (3 STMTTRN debits, all dated today):
 *   1. -payableMatch  -> UNIQUE match -> Sugerido  (auto-confirm, then unmatch, then re-confirm)
 *   2. -payableManual -> UNIQUE match -> Sugerido  (manually matched via the drawer instead)
 *   3. -orphanAmount  -> matches NO installment    -> Não conciliado (ignored)
 *
 * Note on line 2: the manual-match drawer's baixa enforces |amount| == installment remaining
 * (ConciliationBaixa), so the manual-match LINE must carry payableManual's exact amount — which
 * makes the matcher auto-suggest it. The 'Conciliar manual' action is offered for Sugerido rows
 * too, so we still exercise the genuine manual /match endpoint (drawer -> pick installment ->
 * Conciliar), distinct from the one-click Confirmar path on line 1.
 *
 * Requires the .NET API on :5001 with a FRESH dev seed and the web dev server on :5173.
 */

/** Local date -> 'YYYY-MM-DD' for date inputs. Never toISOString() — UTC could shift the day. */
function toDateInput(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

/** brl() with maximumFractionDigits:0 — mirror the app's formatting for exact string assertions. */
function brl(v: number): string {
  return v.toLocaleString('pt-BR', { style: 'currency', currency: 'BRL', maximumFractionDigits: 0 });
}

/**
 * The thousands-grouped digits of an integer amount (e.g. 6904 -> "6\\.904") as an EXACT-anchored
 * regex. Used to find the unique manual-match combobox option by its amount (rendered in the option
 * sub as brl(amount)). A regex sidesteps the non-breaking space in brl()'s "R$ " prefix that
 * makes literal-string matching brittle, and is anchored so 6.904 never matches 16.904 / 6.9040.
 */
function amountDigitsRegex(amount: number): RegExp {
  const grouped = amount.toLocaleString('pt-BR', { useGrouping: true }).replace(/\./g, '\\.');
  return new RegExp(`(^|[^\\d.])${grouped}([^\\d]|$)`);
}

/**
 * Opens the manual-match drawer's "Parcela" combobox and picks the UNIQUE open installment whose
 * amount equals `amount`, then confirms (Conciliar). Hard-asserts exactly one option matches (the
 * per-run-unique amount guarantees uniqueness) so a wrong/ambiguous pick fails loudly instead of
 * silently reconciling the wrong installment. Asserts the drawer closes on success (it only closes
 * onSuccess of the /match call, so a backend rejection would keep it open and fail the test).
 */
async function manualMatchByAmount(page: Page, drawer: ReturnType<Page['getByRole']>, amount: number): Promise<void> {
  await expect(drawer.getByRole('heading', { name: 'Conciliar manualmente' })).toBeVisible();
  await drawer.locator('button[aria-haspopup="listbox"]').first().click();
  const list = page.locator('.combobox-list[role="listbox"]');
  await expect(list).toBeVisible();
  const option = list.getByRole('option').filter({ hasText: amountDigitsRegex(amount) });
  await expect(option).toHaveCount(1);
  await option.first().click();
  await expect(list).toBeHidden();
  await drawer.getByRole('button', { name: 'Conciliar', exact: true }).click();
  await expect(drawer).toBeHidden();
}

/**
 * Creates ONE single-installment payable under the globally-selected company (ULBRA) via the UI:
 * opens the form with the in-app "Nova conta" button, picks the first vendor + the INFRA cost
 * center (auto-approval limit 2.000 — below our amounts, so the payable is born PendingApproval and
 * the Aprovar step is genuinely exercised), fills a distinctive amount due today, submits, then
 * approves. Asserts the final status is "Aprovada" and the single installment is "Aberta", and
 * returns the detail URL. Assumes the list is already scoped to ULBRA (pickCompany done by caller).
 */
async function createApprovedPayable(page: Page, description: string, amount: number, dueIso: string): Promise<string> {
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await expect(page).toHaveURL(/\/app\/contas-a-pagar\/novo$/);
  await expect(page.getByRole('heading', { name: 'Nova conta a pagar' })).toBeVisible();

  // Vendor: Radix combobox (aria-haspopup=listbox). Open it, pick the first seeded option.
  const vendorTrigger = page.locator('button[aria-haspopup="listbox"]').first();
  await vendorTrigger.click();
  const vendorList = page.locator('.combobox-list[role="listbox"]');
  await expect(vendorList).toBeVisible();
  const vendorOption = vendorList.getByRole('option').first();
  await expect(vendorOption).toBeVisible();
  await vendorOption.click();
  await expect(vendorList).toBeHidden();

  // Cost center: native <select>. INFRA's auto-approval limit (2.000) is below our amount, so the
  // payable needs manual approval — exercising the Aprovar step. Match the "INFRA · " prefix.
  const costCenterSelect = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: /INFRA · / }) })
    .first();
  await expect(costCenterSelect).toBeVisible();
  const infraLabel = (await costCenterSelect
    .locator('option', { hasText: /INFRA · / })
    .first()
    .textContent())!.trim();
  await costCenterSelect.selectOption({ label: infraLabel });

  // Description (unique per run).
  await page
    .locator('.form-field', { has: page.getByText('Descrição', { exact: true }) })
    .locator('input')
    .fill(description);

  // Mode "À vista" (single installment) — the default, clicked explicitly for determinism.
  await page.getByRole('button', { name: 'À vista', exact: true }).click();

  // Amount: the single-mode "Valor (R$)" field.
  await page
    .locator('.form-field', { has: page.getByText('Valor (R$)', { exact: true }) })
    .locator('input')
    .first()
    .fill(String(amount));

  // Due date "Vencimento" = today.
  await page
    .locator('.form-field', { has: page.getByText('Vencimento', { exact: true }) })
    .locator('input[type="date"]')
    .first()
    .fill(dueIso);

  // Submit "Lançar" -> detail page.
  const submit = page.getByRole('button', { name: 'Lançar', exact: true });
  await expect(submit).toBeEnabled();
  await submit.click();
  await expect(page).toHaveURL(/\/app\/contas-a-pagar\/[^/]+$/);
  await expect(page.getByRole('heading', { name: description })).toBeVisible();

  // Approve: INFRA auto-approval is below our amount, so the Aprovar button must appear.
  const approveBtn = page.getByRole('button', { name: 'Aprovar', exact: true });
  await expect(approveBtn).toBeVisible();
  await approveBtn.click();
  const statusCard = page.locator('.info-card', { hasText: 'Status' });
  await expect(statusCard.getByText('Aprovada', { exact: true })).toBeVisible();

  // The single installment must be Aberta (open) before reconciliation.
  const parcelas = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelas.locator('tbody tr').first().getByText('Aberta', { exact: true })).toBeVisible();

  return page.url();
}

test('full-flow C: conciliação surface — import counts, suggest/confirm, unmatch, manual match, ignore, idempotent reimport', async ({
  page,
}) => {
  test.setTimeout(120_000);

  // Three DISTINCT, non-round, magnitude-separated INTEGER amounts: each match is unique AND the
  // rendered brl() strings ("-R$ 6.xxx" / "-R$ 4.xxx" / "-R$ 2.xxx") never substring-collide.
  const runId = Date.now();
  const tail = runId % 1000; // 0..999, distinctive per run.
  const amountMatch = 6000 + tail; // payableMatch — auto-confirmed (then unmatched + re-confirmed).
  const amountManual = 4000 + tail; // payableManual — manually matched via the drawer.
  const amountOrphan = 2000 + tail; // matches NO installment — stays Não conciliado -> ignored.

  const descMatch = `E2E concil match ${runId}`;
  const descManual = `E2E concil manual ${runId}`;
  const today = new Date();
  const dueIso = toDateInput(today);

  const fitidMatch = uniqueFitid('E2E-CONCIL-MATCH');
  const fitidManual = uniqueFitid('E2E-CONCIL-MANUAL');
  const fitidOrphan = uniqueFitid('E2E-CONCIL-ORPHAN');

  // ---------- 1. Login + scope to ULBRA ----------
  await devLogin(page);
  await page.goto('/app/contas-a-pagar');
  await expect(page.getByRole('heading', { name: 'Contas a pagar' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);

  // ---------- 2. Create TWO approved single-installment payables under ULBRA ----------
  await createApprovedPayable(page, descMatch, amountMatch, dueIso);
  // Re-scope to ULBRA on the list before opening the second form (the detail->list nav keeps the
  // global company, but assert it explicitly so the second payable lands under ULBRA too).
  await page.goto('/app/contas-a-pagar');
  await expect(page.getByRole('heading', { name: 'Contas a pagar' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);
  await createApprovedPayable(page, descManual, amountManual, dueIso);

  // ---------- 3. FakeBank: ONE OFX with THREE debits (match, manual-target, orphan) ----------
  const ofx = ofxStatement({
    entries: [
      { fitid: fitidMatch, date: today, amount: -amountMatch, memo: `E2E ${descMatch}` },
      { fitid: fitidManual, date: today, amount: -amountManual, memo: `E2E ${descManual}` },
      { fitid: fitidOrphan, date: today, amount: -amountOrphan, memo: `E2E concil orphan ${runId}` },
    ],
  });
  const ofxPath = writeOfxFile(ofx);

  // ---------- 4. Conciliação: pick ULBRA + a bank account, import the OFX ----------
  await page.goto('/app/conciliacao');
  await expect(page.getByRole('heading', { name: 'Conciliação bancária' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);

  // Bank-account picker (toolbar select with the "— selecione —" placeholder). Pick the first real
  // account (skip the placeholder). The same account scopes the imported transactions.
  const bankPicker = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: '— selecione —' }) })
    .first();
  const accountLabel = (await bankPicker
    .locator('option')
    .filter({ hasNotText: '— selecione —' })
    .first()
    .textContent())!.trim();
  expect(accountLabel.length).toBeGreaterThan(0);
  await bankPicker.selectOption({ label: accountLabel });

  // Import the OFX via the file input. The success toast reports counts:
  // "N lançamentos · M novos · K duplicados". We imported 3 brand-new lines (unique FITIDs) on a
  // fresh seed -> assert 3 lançamentos · 3 novos · 0 duplicados, hard.
  const fileInput = page.locator('input[type="file"]');
  await expect(fileInput).toBeVisible();
  await fileInput.setInputFiles(ofxPath);
  await expect(page.getByText('3 lançamentos · 3 novos · 0 duplicados').first()).toBeVisible({ timeout: 15_000 });

  const renderMatch = brl(-amountMatch); // e.g. "-R$ 6.123"
  const renderManual = brl(-amountManual); // e.g. "-R$ 4.123"
  const renderOrphan = brl(-amountOrphan); // e.g. "-R$ 2.123"

  const statusFilter = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Sugerido' }) })
    .first();
  const rowByAmount = (rendered: string) => page.locator('table tbody tr', { hasText: rendered }).first();

  // ---------- 5. Matched line: Sugerido -> Confirmar -> Conciliado -> Desfazer -> re-Confirmar ----------
  await statusFilter.selectOption({ label: 'Sugerido' });
  const matchRow = rowByAmount(renderMatch);
  await expect(matchRow).toBeVisible({ timeout: 15_000 });
  await expect(matchRow.getByText('Sugerido', { exact: true })).toBeVisible();

  // Confirmar -> baixa -> leaves the Sugerido worklist, appears under Conciliado.
  await matchRow.getByRole('button', { name: 'Confirmar' }).click();
  await expect(page.locator('table tbody tr', { hasText: renderMatch })).toHaveCount(0, { timeout: 15_000 });
  await statusFilter.selectOption({ label: 'Conciliado' });
  const matchReconciled = rowByAmount(renderMatch);
  await expect(matchReconciled).toBeVisible({ timeout: 15_000 });
  await expect(matchReconciled.getByText('Conciliado', { exact: true })).toBeVisible();

  // Desfazer (unmatch) -> reverses the baixa; the txn returns to "Não conciliado" (Unmatched).
  await matchReconciled.getByRole('button', { name: 'Desfazer' }).click();
  await expect(page.locator('table tbody tr', { hasText: renderMatch })).toHaveCount(0, { timeout: 15_000 });
  await statusFilter.selectOption({ label: 'Não conciliado' });
  const matchUnmatched = rowByAmount(renderMatch);
  await expect(matchUnmatched).toBeVisible({ timeout: 15_000 });
  await expect(matchUnmatched.getByText('Não conciliado', { exact: true })).toBeVisible();

  // Re-conciliate via manual match (the unmatched row no longer carries a suggestion, so use the
  // drawer to re-attach it to descMatch's now-Open installment) -> Conciliado again. Final state explicit.
  await matchUnmatched.getByRole('button', { name: 'Conciliar manual' }).click();
  await manualMatchByAmount(page, page.getByRole('dialog'), amountMatch);
  // Back under Conciliado.
  await statusFilter.selectOption({ label: 'Conciliado' });
  const matchFinal = rowByAmount(renderMatch);
  await expect(matchFinal).toBeVisible({ timeout: 15_000 });
  await expect(matchFinal.getByText('Conciliado', { exact: true })).toBeVisible();

  // ---------- 6. Manual-match line: Sugerido -> 'Conciliar manual' drawer -> pick installment ----------
  await statusFilter.selectOption({ label: 'Sugerido' });
  const manualRow = rowByAmount(renderManual);
  await expect(manualRow).toBeVisible({ timeout: 15_000 });
  await expect(manualRow.getByText('Sugerido', { exact: true })).toBeVisible();

  await manualRow.getByRole('button', { name: 'Conciliar manual' }).click();
  await manualMatchByAmount(page, page.getByRole('dialog'), amountManual);

  // The row becomes Conciliado.
  await expect(page.locator('table tbody tr', { hasText: renderManual })).toHaveCount(0, { timeout: 15_000 });
  await statusFilter.selectOption({ label: 'Conciliado' });
  const manualReconciled = rowByAmount(renderManual);
  await expect(manualReconciled).toBeVisible({ timeout: 15_000 });
  await expect(manualReconciled.getByText('Conciliado', { exact: true })).toBeVisible();

  // ---------- 7. Ignore line: the orphan (Não conciliado) -> 'Ignorar' -> Ignorado ----------
  await statusFilter.selectOption({ label: 'Não conciliado' });
  const orphanRow = rowByAmount(renderOrphan);
  await expect(orphanRow).toBeVisible({ timeout: 15_000 });
  await expect(orphanRow.getByText('Não conciliado', { exact: true })).toBeVisible();
  await orphanRow.getByRole('button', { name: 'Ignorar' }).click();
  await expect(page.locator('table tbody tr', { hasText: renderOrphan })).toHaveCount(0, { timeout: 15_000 });
  await statusFilter.selectOption({ label: 'Ignorado' });
  const orphanIgnored = rowByAmount(renderOrphan);
  await expect(orphanIgnored).toBeVisible({ timeout: 15_000 });
  await expect(orphanIgnored.getByText('Ignorado', { exact: true })).toBeVisible();

  // ---------- 8. Idempotent reimport: SAME OFX -> 0 new (dedup by FITID), no duplicate rows ----------
  // Count our three lines across ALL statuses BEFORE the reimport (each must be exactly 1).
  await statusFilter.selectOption({ label: 'Todos' });
  await expect(page.locator('table tbody tr', { hasText: renderMatch })).toHaveCount(1, { timeout: 15_000 });
  await expect(page.locator('table tbody tr', { hasText: renderManual })).toHaveCount(1, { timeout: 15_000 });
  await expect(page.locator('table tbody tr', { hasText: renderOrphan })).toHaveCount(1, { timeout: 15_000 });

  await fileInput.setInputFiles(ofxPath);
  // All three FITIDs already exist -> 3 lançamentos · 0 novos · 3 duplicados. Hard assert.
  await expect(page.getByText('3 lançamentos · 0 novos · 3 duplicados').first()).toBeVisible({ timeout: 15_000 });

  // The table must NOT have grown — still exactly one row per line (no duplicate ingestion).
  await statusFilter.selectOption({ label: 'Todos' });
  await expect(page.locator('table tbody tr', { hasText: renderMatch })).toHaveCount(1, { timeout: 15_000 });
  await expect(page.locator('table tbody tr', { hasText: renderManual })).toHaveCount(1, { timeout: 15_000 });
  await expect(page.locator('table tbody tr', { hasText: renderOrphan })).toHaveCount(1, { timeout: 15_000 });
});
