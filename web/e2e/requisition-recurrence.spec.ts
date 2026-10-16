import { test, expect } from '@playwright/test';
import { devLogin, pickCompany } from './helpers/auth';

/**
 * Full-flow e2e — the whole life of a recurring requisition of type **Conta Fixa** (AEL-877 + AEL-881), driven
 * through the real frontend from creation to termination:
 *
 *   create Conta Fixa (Mensal) -> submit to triage -> approve triage -> send financial complement ->
 *   APPROVE (with no budget ceiling — R1) -> InPayment -> "Lançar fatura do mês" (com boleto) ->
 *   the launched cycle shows up as its own title -> "Encerrar recorrência".
 *
 * It exercises three things unit/integration tests can't reach through the UI:
 *  - R1: a Conta Fixa in an UNBUDGETED cost-center cell must still be approvable — the "Aprovar" button is
 *    ENABLED and the decision panel shows the "no ceiling" note (a real gap this flow caught: the budget-control
 *    read-model used to report breaches for Conta Fixa, disabling the button even though the server allows it).
 *  - R3: the recurrence panel surfaces the "cada fatura exige a Nota Fiscal" lock for Conta Fixa.
 *  - The recurrence engine end-to-end: launch a month's invoice (a new Payable) and terminate the recurrence.
 *
 * The e2e admin (`e2e@ulbra.br`) is provisioned as role Admin, so it holds requisitions:write/triage/approve via
 * the admin bypass AND is NOT a `finalApprover` (that predicate short-circuits on isAdmin) — so the requisition
 * DETAIL page never redirects to the approval screen and the entire esteira is driven from one page.
 *
 * Rerunnable against the persistent dev DB: Conta Fixa is unique per (vendor, unit, cost center) while ACTIVE
 * (R2), so the test CANCELS its requisition at the end to free that slot for the next run. Each run also uses a
 * unique description. Requires the .NET API on :5001 with the dev seed and the web dev server on :5173.
 */

/** Local date -> 'YYYY-MM-DD' for date inputs. Never toISOString() — UTC could shift the day. */
function toDateInput(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

test('full-flow: Conta Fixa created -> esteira -> approved without ceiling -> invoice launched -> terminated', async ({
  page,
}) => {
  test.setTimeout(120_000);

  const runId = Date.now();
  const description = `E2E Conta Fixa energia ${runId}`;
  const today = new Date();
  const y = today.getFullYear();
  const mo = today.getMonth();
  // Decoupled from the run date so the test is valid any day: due dates in future months, emission = today.
  const firstDueIso = toDateInput(new Date(y, mo + 1, 20)); // 1st invoice due next month
  const issueIso = toDateInput(today); // emission today (<= first due)
  const cycleDueIso = toDateInput(new Date(y, mo + 1, 25)); // launched cycle's due date
  const nextMonth = new Date(y, mo + 1, 1);
  // Competency (YYYY-MM) of the launched invoice — must be after the start competency (current month).
  const cycleCompetency = `${nextMonth.getFullYear()}-${String(nextMonth.getMonth() + 1).padStart(2, '0')}`;
  const cycleAmount = '120'; // distinct from the base (estimate = 100) so the cycle row is identifiable
  // A 47-digit "bancário" digitable line (valid length; not a 48-digit convênio).
  const boleto = '34191790010104351004791020150008291070026000123';

  // ---------- 1. Login ----------
  await devLogin(page);

  // ---------- 2. Open the new-requisition form scoped to ULBRA ----------
  // Scope the global company on the LIST first, then open the form via the in-app button (client-side nav) so
  // the selection carries into the form (it reads the globally-selected company).
  await page.goto('/app/requisitions');
  await expect(page.getByRole('heading', { name: 'Solicitações de compra' })).toBeVisible();
  await pickCompany(page, /ULBRA/i);
  await page.getByRole('button', { name: 'Nova solicitação' }).click();
  await expect(page).toHaveURL(/\/app\/requisitions\/new$/);
  await expect(page.getByRole('heading', { name: 'Nova solicitação' })).toBeVisible();

  // ---------- 3. Fill the Conta Fixa form ----------
  // Type: "Conta fixa" (segmented pill). This coerces the estimate to Mensal (no installments) and reveals the
  // Conta-Fixa hint. Assert the hint so we know the type actually took.
  await page.getByRole('button', { name: 'Conta fixa', exact: true }).click();
  await expect(page.getByText(/pode passar do teto do orçamento.*exige a Nota Fiscal/i)).toBeVisible();
  // Only the Conta-Fixa hint shows — the generic "Recorrente:" hint must NOT appear for a Conta Fixa.
  await expect(page.getByText(/^Recorrente: informe o valor/i)).toBeHidden();

  // Vendor: the shared Combobox (aria-haspopup=listbox). Search the electric utility and pick it.
  await page.locator('button[aria-haspopup="listbox"]').first().click();
  const vendorList = page.locator('.combobox-list[role="listbox"]');
  await expect(vendorList).toBeVisible();
  await page.locator('.combobox-input').fill('Equatorial');
  const vendorOption = vendorList.getByRole('option', { hasText: /Equatorial/i }).first();
  await expect(vendorOption).toBeVisible();
  await vendorOption.click();
  await expect(vendorList).toBeHidden();

  // Cost center FIRST (it filters the unit options), then Unit. Both are native <select> — locate each by an
  // option it uniquely contains (robust against the required-mark in the label text).
  await page
    .locator('select', { has: page.locator('option', { hasText: 'Suporte' }) })
    .first()
    .selectOption({ label: 'Suporte' });
  await page
    .locator('select', { has: page.locator('option', { hasText: 'CAN — Canoas/RS' }) })
    .first()
    .selectOption({ label: 'CAN — Canoas/RS' });

  // Estimate value (per month for a recurring requisition) — the only field with placeholder "0,00" on this form.
  await page.locator('input[placeholder="0,00"]').first().fill('100');

  // First due date — the only date input on the create form.
  await page.locator('input[type="date"]').first().fill(firstDueIso);

  // Description (unique per run) — the form's only textarea.
  await page.locator('textarea').first().fill(description);

  // Send straight to triage. Assert we land on the created requisition's detail (id is `req_…`) — NOT still on
  // `/new` (a failed create, e.g. an R2 duplicate, would keep us there; `[^/]+` alone would match "new").
  await page.getByRole('button', { name: 'Enviar para triagem' }).click();
  await expect(page).toHaveURL(/\/app\/requisitions\/req_[a-z0-9]+$/);
  const detailUrl = page.url();

  // ---------- 4. Triage -> approve triage ----------
  const approveTriage = page.getByRole('button', { name: 'Aprovar triagem', exact: true });
  await expect(approveTriage).toBeVisible();
  await approveTriage.click();

  // ---------- 5. Financial complement -> send ----------
  // The complement form appears once the status flips to FinancialComplement (requester = the admin). Its
  // emission-date field is a unique, stable sync point (Panel titles render as text, not a heading role).
  await expect(page.locator('#fc-issue-date')).toBeVisible();
  await page.locator('#fc-issue-date').fill(issueIso);
  // The single installment row is pre-seeded (due = first due, amount = estimate, Pix) for a recurring req.
  await page.getByRole('button', { name: 'Enviar informações financeiras' }).click();

  // ---------- 6. Approval — R1: no budget ceiling for Conta Fixa ----------
  // The decision panel renders and the "Aprovar" button must be ENABLED even though the cell is unbudgeted.
  const approve = page.getByRole('button', { name: 'Aprovar', exact: true });
  await expect(approve).toBeVisible();
  // Conta Fixa PARTICIPA do orçamento: a "Decisão orçamentária" aparece, mostrando o impacto…
  await expect(page.getByText('Decisão orçamentária')).toBeVisible();
  // …com a nota de que pode ser aprovada acima do teto (R1) — e o botão fica habilitado.
  await expect(page.getByText(/pode ser aprovada mesmo acima do teto/i)).toBeVisible();
  await expect(approve).toBeEnabled();
  await approve.click();

  // ---------- 7. InPayment — the recurrence panel + the Conta-Fixa NF lock (R3) ----------
  // Panel title renders as text (`.panel-title`), not a heading role.
  await expect(page.getByText('Recorrência — faturas do mês')).toBeVisible();
  await expect(page.getByText(/cada fatura exige a Nota Fiscal/i)).toBeVisible(); // R3 hint

  // Only the base (approval) title exists so far.
  const rows = page.locator('.rc-row');
  await expect(rows).toHaveCount(1);

  // ---------- 8. Launch the month's invoice (com boleto) ----------
  const launch = page.locator('.rc-launch');
  await launch.locator('input[type="month"]').first().fill(cycleCompetency);
  await launch.locator('input[type="date"]').first().fill(cycleDueIso);
  await launch.locator('input[inputmode="decimal"]').first().fill(cycleAmount);
  await launch.locator('select').first().selectOption({ label: 'Boleto' });
  await launch.locator('input[inputmode="numeric"]').first().fill(boleto);
  await page.getByRole('button', { name: 'Lançar fatura', exact: true }).click();

  // A new cycle row appears (base + the launched invoice = 2), showing the launched amount and the Boleto method.
  await expect(rows).toHaveCount(2);
  const cycleRow = rows.filter({ hasText: 'R$ 120,00' }).first();
  await expect(cycleRow).toBeVisible();
  await expect(cycleRow.getByText('Boleto', { exact: true })).toBeVisible();

  // ---------- 9. Terminate the recurrence ----------
  // ConfirmDialog is a Radix AlertDialog (role "alertdialog"). Its confirm button is "Encerrar".
  await page.getByRole('button', { name: 'Encerrar recorrência', exact: true }).click();
  const terminateDialog = page.getByRole('alertdialog');
  await expect(terminateDialog).toBeVisible();
  // Terminate AT the launched cycle's competency (the dialog's default can lag behind a just-launched cycle;
  // ending before an existing invoice is rejected by the server). Set it explicitly for determinism.
  await terminateDialog.locator('input[type="month"]').first().fill(cycleCompetency);
  await terminateDialog.getByRole('button', { name: 'Encerrar', exact: true }).click();
  await expect(page.getByText('Recorrência encerrada.')).toBeVisible();

  // ---------- 10. Cleanup: cancel to free the (vendor, unit, cost center) Conta-Fixa slot for reruns ----------
  await page.goto(detailUrl);
  await page.getByRole('button', { name: 'Cancelar', exact: true }).click();
  const cancelDialog = page.getByRole('alertdialog');
  await expect(cancelDialog).toBeVisible();
  // The justificativa field is a multi-line textarea (AEL-881 UX) — not a single-line input.
  await cancelDialog.locator('textarea').first().fill('E2E cleanup');
  await cancelDialog.getByRole('button', { name: 'Cancelar solicitação', exact: true }).click();
  await expect(page.getByText('Solicitação cancelada.')).toBeVisible();
  // The recurrence panel is gone once the requisition leaves InPayment.
  await expect(page.getByText('Recorrência — faturas do mês')).toBeHidden();
});
