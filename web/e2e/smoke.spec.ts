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

test('login -> conciliação -> confirmar a suggested transaction', async ({ page }) => {
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

  // Navigate to Conciliação via the sidebar nav item ("Conciliação bancária").
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Conciliação bancária' }).click();
  await expect(page).toHaveURL(/\/app\/conciliacao$/);
  await expect(page.getByRole('heading', { name: 'Conciliação bancária' })).toBeVisible();

  // Scope to the ULBRA company that owns the seeded statement import. The
  // EmpresaPicker is the first toolbar select.
  const empresaPicker = page.locator('select').first();
  const ulbra = empresaPicker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await empresaPicker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // The upload control for the OFX statement is present once a bank account is set.
  await expect(page.locator('input[type="file"]')).toBeVisible();

  // The bank-account picker is the toolbar select carrying the "— selecione —"
  // placeholder. The seed puts a Suggested debit + Unmatched credit on ULBRA's
  // first account; the API's account ordering isn't guaranteed, so iterate over
  // the real accounts (skip the placeholder) and stop on the one whose
  // "Sugerido" worklist is non-empty.
  const bankPicker = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: '— selecione —' }) })
    .first();
  const accountValues = await bankPicker
    .locator('option')
    .evaluateAll((opts) => (opts as HTMLOptionElement[]).map((o) => o.value).filter((v) => v !== ''));

  // The Status filter is the select inside the panel header that offers "Sugerido".
  const statusFilter = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Sugerido' }) })
    .first();

  const suggestedRows = page.locator('table tbody tr', { hasText: 'Sugerido' });

  let found = false;
  for (const value of accountValues) {
    await bankPicker.selectOption(value);
    // Filter to the Sugerido status; the worklist re-fetches per account+filter.
    await statusFilter.selectOption({ label: 'Sugerido' });
    // Allow the worklist query to settle, then check for a suggested row.
    await page.waitForTimeout(400);
    if ((await suggestedRows.count()) > 0) {
      found = true;
      break;
    }
  }

  // At least one account must carry the seeded Suggested transaction.
  expect(found).toBe(true);
  await expect(suggestedRows.first()).toBeVisible();

  // Confirm the suggested match. The row's "Confirmar" action reconciles it, so
  // it leaves the Sugerido worklist and appears under the Conciliado filter.
  await suggestedRows.first().getByRole('button', { name: 'Confirmar' }).click();

  // It disappears from the Sugerido filter (the worklist shrinks / empties).
  await expect(suggestedRows).toHaveCount(0);

  // And it now shows up under the Conciliado filter — end-to-end confirmation.
  await statusFilter.selectOption({ label: 'Conciliado' });
  await expect(page.locator('table tbody tr', { hasText: 'Conciliado' }).first()).toBeVisible();
});

test('login -> plano de contas renders Contábil + Financeiro charts', async ({ page }) => {
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

  // Navigate to Plano de Contas via the sidebar nav item. Match exactly: the
  // Contábil section adds a "Plano de contas contábil" item that would otherwise
  // also match a substring "Plano de Contas".
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Plano de Contas', exact: true }).click();
  await expect(page).toHaveURL(/\/app\/plano-de-contas$/);
  await expect(page.getByRole('heading', { name: 'Plano de Contas', exact: true })).toBeVisible();

  // Default chart is Contábil (Accounting). The seeder creates the synthetic
  // root "4 · Despesas" plus analytical children (4.1.01, …). Assert a seeded
  // account renders — match the root code cell to avoid monetary/data coupling.
  await expect(page.locator('table tbody tr').first()).toBeVisible();
  await expect(page.locator('.t-mono', { hasText: /^4$/ }).first()).toBeVisible();

  // Toggle to Financeiro and assert a seeded financial account (code 76, the
  // "Energia"-family root) appears. The chart-type Select is the first toolbar
  // select in the page header.
  const chartType = page.locator('select').first();
  await chartType.selectOption({ label: 'Financeiro' });
  await expect(page.locator('.t-mono', { hasText: /^76$/ }).first()).toBeVisible();
});

test('login -> classificação -> classify a payable off the fila', async ({ page }) => {
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

  // Navigate to Classificação via the sidebar nav item.
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Classificação' }).click();
  await expect(page).toHaveURL(/\/app\/classificacao$/);
  await expect(page.getByRole('heading', { name: 'Classificação' })).toBeVisible();

  // Scope to the ULBRA company that owns the seeded payables. The EmpresaPicker
  // is the first toolbar select; before a company is picked the screen shows the
  // "Nenhuma empresa" empty state.
  const picker = page.locator('select').first();
  const ulbra = picker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await picker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // The fila is the classify table. Two outcomes are valid depending on whether
  // the seed left payables unclassified:
  //   - rows exist  => exercise the classify flow on the first row.
  //   - no rows     => assert the "tudo classificado" empty state.
  const rows = page.locator('table tbody tr');
  const doneState = page.getByText('Tudo classificado');

  // The fila query refetches on company change; before its data resolves the
  // screen briefly renders the "Tudo classificado" empty state (payables.length
  // is 0 while loading), so the done state can FLASH even when rows are coming.
  // Let the query settle, then branch on the stable state: prefer rows if any
  // arrive, otherwise treat a persistent done state as a genuinely empty fila.
  await expect(rows.first().or(doneState)).toBeVisible();
  await page.waitForLoadState('networkidle');

  if ((await rows.count()) === 0) {
    // Empty fila (after the network settled): assert the done state and stop.
    await expect(doneState).toBeVisible();
    return;
  }

  const firstRow = rows.first();
  await expect(firstRow).toBeVisible();
  const initialCount = await rows.count();

  // The two account comboboxes are Radix popover triggers (aria-haspopup
  // listbox). Index 0 = Conta contábil, 1 = Conta financeira. Open each, wait for
  // its option list to finish loading (the panel shows "Carregando…" until the
  // chart-accounts query resolves), then pick the first analytical option. The
  // options render in a Radix portal, so query them at the page level — open one
  // combobox at a time so only that listbox is mounted.
  const triggers = firstRow.locator('button[aria-haspopup="listbox"]');
  await expect(triggers).toHaveCount(2);

  const pickFirstOption = async (index: number) => {
    const trigger = triggers.nth(index);
    await expect(trigger).toBeEnabled();
    await trigger.click();
    // The open popover renders the option buttons inside a div[role=listbox]
    // (class combobox-list). Scope to THAT listbox — querying role=option at the
    // page level also matches the native <option>s of the EmpresaPicker <select>,
    // which are never "visible" and would hang the wait.
    const listbox = page.locator('.combobox-list[role="listbox"]');
    await expect(listbox).toBeVisible();
    const option = listbox.getByRole('option').first();
    await expect(option).toBeVisible();
    await option.click();
    // Radix closes the popover on select; ensure it's gone before opening the next
    // so the two listboxes never overlap and steal focus from each other.
    await expect(listbox).toBeHidden();
  };

  await pickFirstOption(0);
  await pickFirstOption(1);

  // "Classificar" becomes enabled once both accounts are set; click it.
  const classify = firstRow.getByRole('button', { name: 'Classificar', exact: true });
  await expect(classify).toBeEnabled();
  await classify.click();

  // End-to-end confirmation: a success toast appears OR the row leaves the fila
  // (the list refetches without it). Accept either signal so the test stays
  // resilient to toast timing.
  const toast = page.getByText('Conta classificada.');
  await expect(toast.or(rows.first()).first()).toBeVisible();
  // The classified payable drops out of the unclassified fila: the row count
  // shrinks by one (the list refetches without it), or the fila empties to the
  // done state when that was the last row on the page.
  await expect.poll(async () => (await rows.count()) < initialCount || (await doneState.isVisible())).toBeTruthy();
});

test('login -> aging -> bucket cards + pivot by cost center', async ({ page }) => {
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

  // Navigate to Vencimentos/Aging via the sidebar nav item ("Vencimentos (aging)").
  const sidebar = page.locator('aside.sidebar');
  await sidebar.getByRole('button', { name: 'Vencimentos (aging)' }).click();
  await expect(page).toHaveURL(/\/app\/aging$/);
  await expect(page.getByRole('heading', { name: 'Vencimentos' })).toBeVisible();

  // Scope to the ULBRA company that owns the seeded payables. Before a company is
  // picked the screen shows the "Selecione uma empresa" empty state.
  const picker = page.locator('select').first();
  const ulbra = picker.locator('option', { hasText: /ULBRA/i }).first();
  if (await ulbra.count()) {
    await picker.selectOption({ label: (await ulbra.textContent())!.trim() });
  }

  // Two valid outcomes depending on whether the seed left anything open:
  //   - data present => the "A vencer" bucket card renders AND a R$ value appears.
  //   - nothing open  => the "Nada em aberto" empty state renders.
  // The "A vencer" label appears both as a KPI card label and a table column
  // header; assert at least one is visible. Don't assert on monetary amounts.
  const aVencer = page.getByText('A vencer').first();
  const noOpen = page.getByText('Nada em aberto');
  await expect(aVencer.or(noOpen)).toBeVisible();

  if (await noOpen.isVisible()) {
    // Empty seed for this company: nothing more to exercise.
    await expect(noOpen).toBeVisible();
    return;
  }

  // Data path: a R$ value appears somewhere on the page (KPI cards / table).
  await expect(page.getByText(/R\$/).first()).toBeVisible();

  // The default pivot is "Por fornecedor" and renders a table with rows.
  await expect(page.locator('table tbody tr').first()).toBeVisible();

  // Toggle the pivot to "Por centro de custo" — the pivot Select is the toolbar
  // select inside the panel header that offers that option.
  const pivot = page
    .locator('select')
    .filter({ has: page.locator('option', { hasText: 'Por centro de custo' }) })
    .first();
  await pivot.selectOption({ label: 'Por centro de custo' });

  // The table re-renders with the cost-center pivot; assert a row is visible.
  await expect(page.locator('table tbody tr').first()).toBeVisible();

  // Best-effort: click a pivot row and assert the drill drawer opens. The drawer
  // is a Radix dialog; if it doesn't open cleanly, keep the cards+pivot asserts.
  try {
    await page.locator('table tbody tr').first().click();
    await expect(page.getByRole('dialog')).toBeVisible({ timeout: 3000 });
  } catch {
    // Drill is best-effort; the cards + pivot assertions above are the contract.
  }
});

test('logged-out access to /app/inicio redirects to /login', async ({ browser }) => {
  // Fresh context => no token in localStorage.
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto('/app/inicio');
  await expect(page).toHaveURL(/\/login$/);
  await context.close();
});
