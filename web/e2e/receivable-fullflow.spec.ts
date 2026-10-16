import { test, expect } from '@playwright/test';
import { devLogin, pickCompany } from './helpers/auth';

/**
 * Full-flow e2e — Fatia B: the complete life of an accounts-RECEIVABLE that gets RECEIVED
 * into a bank account. It drives the real frontend end-to-end:
 *
 *   create receivable -> (born "A receber", NO approval step) -> register a receipt (baixa)
 *   into a ULBRA bank account -> the installment becomes Recebida / the receivable Recebido
 *   -> the cash-flow account balance RISES by exactly the received amount.
 *
 * This is the receivable mirror of Fatia A (payable). The decisive difference: receivable
 * conciliation does NOT exist yet — bank-statement reconciliation is payables-only today
 * (conciliacao.tsx matches debits to payable installments). So the inflow enters the system
 * through the receivable's own "registrar recebimento" (baixa) UI, not through OFX import.
 * FakeBank's ofxStatement can already emit CREDIT lines, but nothing consumes them for
 * receivables — that wiring is intentionally out of scope here (see note at the bottom).
 *
 * It is significant, not vacuously green: it FAILS if the receipt is not registered, or if
 * cash-flow doesn't reflect the inflow. No try/catch swallowing, no "if visible" escape
 * hatches around critical assertions.
 *
 * Requires the .NET API on :5001 with the dev seed (see playwright.config.ts) and the web
 * dev server on :5173. Robust against the persistent dev DB + prior runs via a per-run-unique
 * description / amount and a before/after delta assertion on cash flow.
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

test('full-flow B: receivable created -> receipt registered -> cash flow reflects the inflow', async ({ page }) => {
  test.setTimeout(90_000);

  // A distinctive, non-round INTEGER amount keeps the cash-flow delta free of cent-rounding
  // drift and won't collide with the seed's round figures.
  const runId = Date.now();
  const amount = 7000 + (runId % 1000); // 7000..7999, distinctive per run; integer (no cent drift).
  const description = `E2E recv full-flow ${runId}`;
  const today = new Date();
  const dueIso = toDateInput(today);

  // ---------- 1. Login ----------
  await devLogin(page);

  // ---------- 2. Create the receivable via the UI ----------
  // The new-receivable FORM has no EmpresaPicker — it scopes to the globally-selected company,
  // which lives in in-memory React state (auto-defaults to the first company, CELSP). So scope
  // to ULBRA on the LIST first, then open the form via the in-app "Nova conta" button (client-side
  // nav, no reload) — a full page.goto('/new') would reset the selection back to CELSP.
  await page.goto('/app/accounts-receivable');
  await expect(page.getByRole('heading', { name: 'Contas a receber' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);
  await page.getByRole('button', { name: 'Nova conta' }).click();
  await expect(page).toHaveURL(/\/app\/accounts-receivable\/new$/);
  await expect(page.getByRole('heading', { name: 'Nova conta a receber' })).toBeVisible();

  // Payer (Devedor): Radix combobox (aria-haspopup=listbox). Open it, then pick the first seeded
  // customer option (the seed has ULBRA customers).
  const payerTrigger = page.locator('button[aria-haspopup="listbox"]').first();
  await payerTrigger.click();
  const payerList = page.locator('.combobox-list[role="listbox"]');
  await expect(payerList).toBeVisible();
  const payerOption = payerList.getByRole('option').first();
  await expect(payerOption).toBeVisible();
  await payerOption.click();
  await expect(payerList).toBeHidden();

  // Cost center: native <select> listing "CODE · Name". Receivables have NO approval gate, so any
  // cost center works — pick the first real option (skip the "— selecione —" placeholder).
  const costCenterSelect = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: /\s·\s/ }) })
    .first();
  await expect(costCenterSelect).toBeVisible();
  const ccLabel = (await costCenterSelect.locator('option', { hasText: /\s·\s/ }).first().textContent())!.trim();
  await costCenterSelect.selectOption({ label: ccLabel });

  // Description (unique per run). The Field wraps the label as a sibling of the input.
  await page
    .locator('.form-field', { has: page.getByText('Descrição', { exact: true }) })
    .locator('input')
    .fill(description);

  // Mode "À vista" (single installment). It's the default, but click it explicitly to be deterministic.
  await page.getByRole('button', { name: 'À vista', exact: true }).click();

  // Amount input: single-mode "Valor (R$)" field.
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
  await expect(page).toHaveURL(/\/app\/accounts-receivable\/[^/]+$/);
  await expect(page.getByRole('heading', { name: description })).toBeVisible();

  // ---------- 3. Born "A receber" — NO approval step ----------
  // A receivable is created AwaitingReceipt directly; there is no "Aprovar" action. The status
  // card must already read "A receber".
  const statusCard = page.locator('.info-card', { hasText: 'Status' });
  await expect(statusCard.getByText('A receber', { exact: true })).toBeVisible();

  // The single installment must be "Aberta" before the receipt.
  const parcelas = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelas).toBeVisible();
  const installmentRow = parcelas.locator('tbody tr').first();
  await expect(installmentRow).toBeVisible();
  await expect(installmentRow.getByText('Aberta', { exact: true })).toBeVisible();

  // Remember the detail-page URL (holds the receivable id) to return after anchoring the bank.
  const receivableUrl = page.url();

  // ---------- 4. Anchor a ULBRA bank account YESTERDAY, then read its balance BEFORE the baixa ----------
  // A receipt books an inflow dated TODAY on the chosen bank account. A bank account's displayed
  // "current balance" is `anchor + movements STRICTLY after the anchor date` (CashFlowQueries) — so
  // the receipt only moves the number when the account is anchored BEFORE today. The seed/other
  // specs may leave the anchor elsewhere or dated today, so this test OWNS its precondition: it sets
  // a known reconciled balance anchored YESTERDAY on a chosen ULBRA account. We pick the FIRST ULBRA
  // account in the panel (ordered by alias) and receive into that SAME account.
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
  // Capture the chosen account's alias so the receipt drawer targets the same account.
  const BANK_ALIAS = (await bankRow.locator('td').first().locator('.cf-acct-name').textContent())!.trim();
  expect(BANK_ALIAS.length).toBeGreaterThan(0);

  // Set a known anchor dated YESTERDAY so the receipt (dated today) lands strictly after it and moves
  // the number. A distinctive round value keeps the delta arithmetic exact (brl() drops decimals).
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

  // ---------- 5. Register the receipt (baixa) via the receivable detail UI ----------
  await page.goto(receivableUrl);
  await expect(page.getByRole('heading', { name: description })).toBeVisible();

  // Open the "Registrar recebimento" drawer on the open installment.
  const parcelasDetail = page.locator('.panel', { hasText: 'Parcelas' });
  await parcelasDetail.locator('tbody tr').first().getByRole('button', { name: 'Registrar recebimento' }).click();

  const receiptDrawer = page.getByRole('dialog');
  await expect(receiptDrawer.getByText('Registrar recebimento da parcela')).toBeVisible();

  // Date "Data efetiva" = today.
  await receiptDrawer
    .locator('.form-field', { has: page.getByText('Data efetiva', { exact: true }) })
    .locator('input[type="date"]')
    .first()
    .fill(dueIso);

  // Amount "Valor recebido (R$)" = full installment amount.
  await receiptDrawer
    .locator('.form-field', { has: page.getByText('Valor recebido (R$)', { exact: true }) })
    .locator('input')
    .first()
    .fill(String(amount));

  // Bank account "Conta de entrada" = the SAME account we anchored above.
  const receiptBankSelect = receiptDrawer
    .locator('.form-field', { has: page.getByText('Conta de entrada', { exact: true }) })
    .locator('select')
    .first();
  await expect(receiptBankSelect.locator('option', { hasText: BANK_ALIAS })).toHaveCount(1);
  await receiptBankSelect.selectOption({ label: BANK_ALIAS });

  // Method: keep the default (PIX) — the select is pre-populated with a valid value. Submit.
  const registerBtn = receiptDrawer.getByRole('button', { name: 'Registrar recebimento', exact: true });
  await expect(registerBtn).toBeEnabled();
  await registerBtn.click();

  // The drawer closes on success and the data refetches.
  await expect(receiptDrawer).toBeHidden();

  // ---------- 6. The cycle closed: installment Recebida + receivable Recebido ----------
  // Single-installment receivable: receiving the only installment settles the whole receivable.
  const parcelasAfter = page.locator('.panel', { hasText: 'Parcelas' });
  await expect(parcelasAfter.locator('tbody tr', { hasText: 'Recebida' }).first()).toBeVisible({
    timeout: 15_000,
  });
  const statusCardAfter = page.locator('.info-card', { hasText: 'Status' });
  // receivables.status.Received renders as "Recebida" in pt-BR.
  await expect(statusCardAfter.getByText('Recebida', { exact: true })).toBeVisible();

  // ---------- 7. Cash flow reflects the receipt: the account balance ROSE by exactly `amount` ----------
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

  // The new balance MUST equal the prior balance PLUS the received amount (inflow). `amount` is an
  // integer, so round(before) + amount == round(before + amount): the brl() string is exact. This
  // FAILS if the receipt never hit cash flow (the whole point of the assertion).
  const expectedAfter = brl(balanceBefore + amount);
  await expect(bankRow2.locator('td.amount-cell', { hasText: expectedAfter }).first()).toBeVisible({
    timeout: 15_000,
  });
});

/**
 * NOTE (receivable conciliation is out of scope): bank-statement reconciliation today is
 * payables-only — conciliacao.tsx matches OFX DEBITS to payable installments and the manual
 * matcher lists payables. There is no OFX path that recognizes a CREDIT as a receivable receipt.
 * FakeBank (web/e2e/helpers/fake-bank.ts) can already emit positive/CREDIT statement lines, but
 * nothing consumes them for receivables. When receivable conciliation lands, this spec can grow a
 * Fatia-A-style "credit -> conciliação -> baixa" branch reusing ofxStatement({ amount: +N }).
 */
