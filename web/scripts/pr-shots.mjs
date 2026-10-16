// PR screenshots — boots against a running vite dev server and mocks the API
// (/auth/me per cargo + an empty-array catch-all) so authenticated views render
// without the .NET backend. Usage: `node scripts/pr-shots.mjs` (vite on :5199).
import { chromium } from '@playwright/test';
import { existsSync, mkdirSync } from 'node:fs';

const BASE = process.env.SHOT_BASE ?? 'http://localhost:5199';
const OUT = process.env.SHOT_OUT ?? 'pr-shots'; // gitignored; anexe estes PNGs ao PR
mkdirSync(OUT, { recursive: true });

const PERMS = {
  Finance: [
    'payables:read',
    'payables:write',
    'payables:pay',
    'receivables:read',
    'receivables:write',
    'receivables:receive',
    'treasury:read',
    'treasury:write',
    'registry:read',
    'registry:write',
    'core:read',
    'reports:read',
  ],
  Controllership: [
    'payables:read',
    'payables:approve',
    'receivables:read',
    'treasury:read',
    'treasury:approve',
    'accounting:read',
    'fiscal:read',
    'registry:read',
    'core:read',
    'core:write',
    'reports:read',
    'audit:read',
    'projects:read',
    'projects:write',
    'requisitions:read',
    'requisitions:triage',
  ],
  Approver: ['requisitions:read', 'requisitions:approve', 'core:read', 'registry:read', 'projects:read'],
  Requester: [
    'core:read',
    'registry:read',
    'registry:write',
    'projects:read',
    'requisitions:read',
    'requisitions:write',
  ],
  Accounting: [
    'accounting:read',
    'accounting:write',
    'accounting:post',
    'fiscal:read',
    'fiscal:write',
    'reports:read',
    'payables:read',
    'payables:write',
    'receivables:read',
    'registry:read',
    'core:read',
  ],
  Tax: ['payables:tax', 'payables:read', 'fiscal:read', 'registry:read', 'core:read', 'reports:read'],
  Admin: [],
};
// A foto do perfil vem da Google (lh3.googleusercontent.com) e os shots rodam sem rede — um data:
// URI rende a mesma coisa na tela sem depender do host externo.
const AVATAR_1 =
  'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCA2NCA2NCI+PHJlY3Qgd2lkdGg9IjY0IiBoZWlnaHQ9IjY0IiBmaWxsPSIjMmY2ZjhmIi8+PGNpcmNsZSBjeD0iMzIiIGN5PSIyNCIgcj0iMTIiIGZpbGw9IiNjZmU0ZWUiLz48cGF0aCBkPSJNOCA2NGMwLTEzIDExLTIwIDI0LTIwczI0IDcgMjQgMjB6IiBmaWxsPSIjY2ZlNGVlIi8+PC9zdmc+';
const AVATAR_2 =
  'data:image/svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCA2NCA2NCI+PHJlY3Qgd2lkdGg9IjY0IiBoZWlnaHQ9IjY0IiBmaWxsPSIjOGE1YTJiIi8+PGNpcmNsZSBjeD0iMzIiIGN5PSIyNCIgcj0iMTIiIGZpbGw9IiNmMGRjYzQiLz48cGF0aCBkPSJNOCA2NGMwLTEzIDExLTIwIDI0LTIwczI0IDcgMjQgMjB6IiBmaWxsPSIjZjBkY2M0Ii8+PC9zdmc+';

const meFor = (role) => ({
  userId: 'user_00000000000000000000000000000001',
  name: role === 'User' ? 'Novo Usuário' : `${role} Dev`,
  email: `${role === 'User' ? 'novo' : role.toLowerCase()}@ulbra.local`,
  role,
  isAdmin: role === 'Admin',
  // Sem foto para o cargo "User": é o estado de quem nunca entrou pela Google, que cai nas iniciais.
  pictureUrl: role === 'User' ? null : AVATAR_1,
  permissions: PERMS[role] ?? [],
});

// `main.tsx` purges the stored token when `auth.hasValidToken()` is false (it decodes the JWT and
// checks `exp`), so a placeholder string gets cleared before the app renders and the shot lands on
// /login. Seed a real-shaped unsigned JWT with a far-future exp instead — the API is mocked, so the
// signature is never verified.
const FAKE_JWT = (() => {
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const exp = Math.floor(Date.now() / 1000) + 60 * 60 * 24 * 365;
  return `${b64({ alg: 'none', typ: 'JWT' })}.${b64({ sub: 'pr-shots', exp })}.sig`;
})();
const seedToken = (token) => localStorage.setItem('ulbra-one.token', token);

// External hosts (fonts/GIS/clarity) would hang networkidle — abort them.
async function blockExternal(ctx) {
  await ctx.route(/googleapis|gstatic|accounts\.google|clarity\.ms/, (r) => r.abort());
}
async function mockApi(ctx, role) {
  await ctx.route('**/api/**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await ctx.route('**/auth/me', (r) =>
    r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(meFor(role)) }),
  );
}
const shot = async (page, name) => {
  await page.screenshot({ path: `${OUT}/${name}.png` });
  console.log('saved', name);
};

// PW_CHROME (CI) aponta um chromium específico; localmente (sem a env ou com caminho inexistente, ex.:
// Windows) deixa o Playwright resolver o chromium que ele próprio instalou.
const pwChrome = process.env.PW_CHROME ?? '/opt/pw-browsers/chromium';
const browser = await chromium.launch({
  ...(existsSync(pwChrome) ? { executablePath: pwChrome } : {}),
  headless: true,
});

// 1) Login (dev) — botões por cargo (estático).
{
  const ctx = await browser.newContext({ viewport: { width: 1180, height: 1024 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  const page = await ctx.newPage();
  await page.goto(`${BASE}/login`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Entrar como Admin')
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(400);
  await shot(page, '01-login-dev');
  await ctx.close();
}

// 1b) Login no celular — a raiz `/` é o login (não há landing) e o painel de marca sai abaixo de 900px.
{
  const ctx = await browser.newContext({ viewport: { width: 390, height: 844 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  const page = await ctx.newPage();
  await page.goto(`${BASE}/`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Entrar como Admin')
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(400);
  await shot(page, '01b-login-mobile');
  await ctx.close();
}

// 2) Estado "sem cargo".
{
  const ctx = await browser.newContext({ viewport: { width: 1180, height: 820 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'User');
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText(/acesso está sendo configurado/i)
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(400);
  await shot(page, '02-sem-cargo');
  await ctx.close();
}

// 3) Menu por cargo — entra em /app e deixa o RoleLanding decidir a tela (o SideNav difere por
// permissão). Antes apontava para /app/home, rota que deixou de existir com a remoção do Início.
for (const role of ['Finance', 'Controllership', 'Accounting', 'Tax', 'Admin']) {
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, role);
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app`, { waitUntil: 'domcontentloaded' });
  await page
    .locator('aside.side-nav')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(800);
  await shot(page, `03-menu-${role.toLowerCase()}`);
  await ctx.close();
}

// 3b) Balancete por período (Accounting) — colunas saldo anterior / débitos / créditos / saldo final.
{
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Accounting');
  // Rota específica registrada por último → precede o catch-all `[]`. Escopada a /api para não
  // capturar a rota do SPA (ver memória pr-shots-mock-api-path-scope).
  await ctx.route('**/api/accounting/trial-balance*', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        companyId: null,
        from: '2026-01-01',
        to: '2026-01-31',
        costCenterId: null,
        rows: [
          {
            ledgerAccountId: 'ledgacct_1',
            accountCode: '1.2.05.15.015',
            reducedCode: '11515',
            accountName: 'Banco Conta Movimento',
            accountGroup: 'Asset',
            normalBalance: 'Debit',
            openingBalance: 50000,
            totalDebit: 0,
            totalCredit: 30000,
            balance: 20000,
          },
          {
            ledgerAccountId: 'ledgacct_2',
            accountCode: '4.2.05.05.020',
            reducedCode: '42505',
            accountName: 'Despesas com Pessoal',
            accountGroup: 'Expense',
            normalBalance: 'Debit',
            openingBalance: 0,
            totalDebit: 30000,
            totalCredit: 0,
            balance: 30000,
          },
        ],
        totalDebit: 30000,
        totalCredit: 30000,
        isBalanced: true,
      }),
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/accounting/trial-balance`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Saldo anterior')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(600);
  await shot(page, '05-balancete-periodo');
  await ctx.close();
}

// 3c) DRE estruturada (Accounting) — cascata Receita Bruta → … → Lucro Líquido, com subtotais.
{
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Accounting');
  // Rota específica escopada a /api (precede o catch-all) — ver memória pr-shots-mock-api-path-scope.
  await ctx.route('**/api/accounting/income-statement*', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        companyId: null,
        costCenterId: null,
        from: '2026-01-01',
        to: '2026-12-31',
        sections: [
          {
            line: 'GrossRevenue',
            total: 100000,
            lines: [
              {
                ledgerAccountId: 'ledgacct_1',
                accountCode: '3.2.05.10.020',
                accountName: 'Receita de serviços educacionais',
                dreLine: 'GrossRevenue',
                amount: 100000,
              },
            ],
          },
          {
            line: 'RevenueDeductions',
            total: -8000,
            lines: [
              {
                ledgerAccountId: 'ledgacct_2',
                accountCode: '3.2.10.05.020',
                accountName: '(-) Bolsas e descontos',
                dreLine: 'RevenueDeductions',
                amount: -8000,
              },
            ],
          },
          {
            line: 'CostOfSales',
            total: -35000,
            lines: [
              {
                ledgerAccountId: 'ledgacct_3',
                accountCode: '5.2.05.05.020',
                accountName: 'Custo com mão de obra',
                dreLine: 'CostOfSales',
                amount: -35000,
              },
            ],
          },
          {
            line: 'OperatingExpenses',
            total: -30000,
            lines: [
              {
                ledgerAccountId: 'ledgacct_4',
                accountCode: '4.2.05.05.020',
                accountName: 'Despesas com pessoal',
                dreLine: 'OperatingExpenses',
                amount: -30000,
              },
            ],
          },
          {
            line: 'FinancialExpense',
            total: -2000,
            lines: [
              {
                ledgerAccountId: 'ledgacct_5',
                accountCode: '4.2.30.05.020',
                accountName: 'Juros e encargos',
                dreLine: 'FinancialExpense',
                amount: -2000,
              },
            ],
          },
          {
            line: 'IncomeTax',
            total: -4000,
            lines: [
              {
                ledgerAccountId: 'ledgacct_6',
                accountCode: '4.2.35.05.040',
                accountName: 'Provisão para IRPJ',
                dreLine: 'IncomeTax',
                amount: -4000,
              },
            ],
          },
          {
            line: 'SocialContribution',
            total: -1500,
            lines: [
              {
                ledgerAccountId: 'ledgacct_7',
                accountCode: '4.2.35.05.020',
                accountName: 'Provisão para CSLL',
                dreLine: 'SocialContribution',
                amount: -1500,
              },
            ],
          },
        ],
        grossRevenue: 100000,
        revenueDeductions: -8000,
        netRevenue: 92000,
        costOfSales: -35000,
        grossProfit: 57000,
        otherOperatingIncome: 0,
        operatingExpenses: -30000,
        operatingResult: 27000,
        financialRevenue: 0,
        financialExpense: -2000,
        financialResult: -2000,
        otherResult: 0,
        preTaxResult: 25000,
        incomeTax: -4000,
        socialContribution: -1500,
        netIncome: 19500,
      }),
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/accounting/income-statement`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Receita Operacional Líquida')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(600);
  await shot(page, '06-dre-cascata');
  await ctx.close();
}

// 3c-2) Accounting home (AEL-865) — "Revisão de Contabilidade": RoleHome + stage tabs + the
// "A caminho do Financeiro" aside + KPI tiles + the "Precisa de você" classification-review queue.
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const bucket = (count, totalAmount, netTotalAmount = null) => ({ count, totalAmount, netTotalAmount });
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 940 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Accounting');
  // Routes scoped to /api (must precede the catch-all `[]`) — see the pr-shots-mock-api-path-scope memory.
  await ctx.route('**/api/companies*', (r) =>
    json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
  );
  await ctx.route('**/api/units*', (r) =>
    json(r, [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }]),
  );
  await ctx.route('**/api/ledger-accounts*', (r) =>
    json(r, [
      {
        id: 'ledgacct_1',
        code: '3.1.4.07',
        name: 'Serviços de TI',
        kind: 'Analytical',
        accountGroup: 'Expense',
        normalBalance: 'Debit',
        isActive: true,
        parentId: null,
      },
      {
        id: 'ledgacct_2',
        code: '3.1.2.01',
        name: 'Material de Consumo',
        kind: 'Analytical',
        accountGroup: 'Expense',
        normalBalance: 'Debit',
        isActive: true,
        parentId: null,
      },
    ]),
  );
  // Pipeline summary: payment (finance-scheduled queue, with net total) + fiscalDocumentPending (missing NF).
  await ctx.route('**/api/payables/stage-summary*', (r) =>
    json(r, {
      entry: bucket(0, 0),
      classification: bucket(2, 8600),
      approval: bucket(0, 0),
      accounting: bucket(3, 27300),
      payment: bucket(4, 47700, 44700),
      reconciled: bucket(0, 0),
      fiscalDocumentPending: bucket(2, 9050),
    }),
  );
  // Classification-review queue (reviewPending): scheduled payables with no account, carrying an AI suggestion.
  await ctx.route('**/api/payables/stage-items*', (r) =>
    json(r, {
      items: [
        {
          payableId: 'pay_1',
          counterpartyName: 'TI Soluções ME',
          description: 'Licenças anuais de software',
          costCenterId: 'cc_1',
          categoryId: null,
          firstDueDate: '2026-08-10',
          installmentCount: 1,
          totalAmount: 18900,
          totalWithheld: 945,
          netTotal: 17955,
          status: 'Scheduled',
          origin: 'Purchase',
          hasFiscalDocument: true,
          suggestedAccountingAccountId: 'ledgacct_1',
          accountingAccountId: null,
          classificationConfidence: 0.82,
          classificationRationale: 'Histórico de lançamentos de serviços de TI.',
        },
        {
          payableId: 'pay_2',
          counterpartyName: 'Papelaria Central Ltda',
          description: 'Material de escritório',
          costCenterId: 'cc_1',
          categoryId: null,
          firstDueDate: '2026-08-12',
          installmentCount: 1,
          totalAmount: 3480,
          totalWithheld: 0,
          netTotal: 3480,
          status: 'Scheduled',
          origin: 'Purchase',
          hasFiscalDocument: false,
          suggestedAccountingAccountId: 'ledgacct_2',
          accountingAccountId: null,
          classificationConfidence: 0.64,
          classificationRationale: 'Similaridade com compras de material de consumo.',
        },
      ],
      page: 1,
      pageSize: 20,
      total: 5,
      totalPages: 1,
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/accounting/review`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('A caminho do Financeiro')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(700);
  await shot(page, '20-contabil-home-revisao');
  // Despesas tab in the same compact format (detail + "Registrar despesa").
  await page.goto(`${BASE}/app/accounting/review?tab=review`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Registrar despesa')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(500);
  await shot(page, '21-contabil-despesas');
  await ctx.close();
}

// 4) Tela "Cargos & permissões" (Admin) — matriz cargo × permissão.
{
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 1000 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Admin');
  // Rotas específicas registradas por último têm precedência sobre o catch-all `[]`.
  await ctx.route('**/api/permissions', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        'core:read',
        'core:write',
        'registry:read',
        'registry:write',
        'payables:read',
        'payables:write',
        'payables:approve',
        'payables:pay',
        'payables:tax',
        'receivables:read',
        'receivables:write',
        'receivables:receive',
        'treasury:read',
        'treasury:write',
        'treasury:approve',
        'accounting:read',
        'accounting:write',
        'accounting:post',
        'fiscal:read',
        'fiscal:write',
        'reports:read',
        'admin:users',
        'audit:read',
      ]),
    }),
  );
  await ctx.route('**/api/roles', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        { id: 'role_admin', name: 'Admin', permissions: [] },
        {
          id: 'role_finance',
          name: 'Finance',
          permissions: ['payables:read', 'payables:write', 'payables:pay', 'receivables:read', 'treasury:read'],
        },
        {
          id: 'role_controllership',
          name: 'Controllership',
          permissions: ['payables:read', 'payables:approve', 'treasury:approve', 'accounting:read', 'audit:read'],
        },
        {
          id: 'role_accounting',
          name: 'Accounting',
          permissions: ['accounting:read', 'accounting:write', 'accounting:post', 'fiscal:read'],
        },
        {
          id: 'role_tax',
          name: 'Tax',
          permissions: ['payables:tax', 'payables:read', 'fiscal:read', 'registry:read', 'core:read', 'reports:read'],
        },
        { id: 'role_user', name: 'User', permissions: [] },
      ]),
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/settings/roles`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Matriz de acesso')
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(600);
  await shot(page, '04-cargos-permissoes');
  await ctx.close();
}

// 3d) Projetos (AEL-792) — lista e detalhe com acompanhamento do teto acumulado.
{
  const PROJ_ID = 'proj_11111111111111111111111111111111';
  const projetos = [
    {
      id: PROJ_ID,
      companyId: 'comp_1',
      unitId: 'unit_1',
      name: 'Obra Bloco A',
      description: 'Reforma das salas do prédio 11',
      budgetLimit: 500000,
      initialConsumedValue: 40000,
      status: 'Active',
    },
    {
      id: 'proj_2',
      companyId: 'comp_1',
      unitId: 'unit_1',
      name: 'Laboratório Ulbra Tech',
      description: 'Montagem dos laboratórios',
      budgetLimit: 250000,
      initialConsumedValue: 0,
      status: 'Active',
    },
    {
      id: 'proj_3',
      companyId: 'comp_1',
      unitId: 'unit_1',
      name: 'Modernização da Biblioteca',
      description: null,
      budgetLimit: 120000,
      initialConsumedValue: 0,
      status: 'Finished',
    },
  ];

  // Rotas específicas escopadas a /api (precedem o catch-all) — ver memória pr-shots-mock-api-path-scope.
  const withProjectMocks = async (ctx) => {
    // A lista exige uma empresa selecionada (senão cai no EmptyState "Nenhuma empresa cadastrada").
    await ctx.route('**/api/companies*', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([{ id: 'comp_1', legalName: 'ULBRA S.A.', tradeName: 'ULBRA', taxId: '92741990000199' }]),
      }),
    );
    await ctx.route('**/api/units*', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN' }]),
      }),
    );
    await ctx.route('**/api/projects/*/metrics', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/json',
        // Previsto (560k) estoura o teto (500k) → saldo negativo, alerta vermelho visível.
        body: JSON.stringify({
          id: PROJ_ID,
          name: 'Obra Bloco A',
          status: 'Active',
          budgetLimit: 500000,
          previsto: 560000,
          realizado: 210000,
          saldo: -60000,
          percentPrevisto: 112,
          percentRealizado: 42,
          isOverCeiling: true,
          requisitionCount: 7,
        }),
      }),
    );
    await ctx.route(/\/api\/projects\/[^/]+$/, (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(projetos[0]) }),
    );
    await ctx.route('**/api/projects?*', (r) =>
      r.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ items: projetos, page: 1, pageSize: 200, total: projetos.length, totalPages: 1 }),
      }),
    );
  };

  for (const [route, name, anchor] of [
    ['/app/projects', '06-projetos-lista', 'Teto total'],
    [`/app/projects/${PROJ_ID}`, '07-projeto-detalhe-estouro', 'Uso do orçamento'],
  ]) {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Controllership');
    await withProjectMocks(ctx);
    const page = await ctx.newPage();
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText(anchor)
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, name);
    await ctx.close();
  }
}

// 3e) Orçamento & Solicitações (AEL-790) — lista de solicitações, detalhe na aprovação
// (card de previsão + card de projeto), modal de estouro, e lista de orçamentos.
{
  const REQ_ID = 'req_11111111111111111111111111111111';
  const PROJ_ID = 'proj_22222222222222222222222222222222';
  const companies = [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }];
  const units = [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }];
  const costCenters = [
    {
      id: 'cc_1',
      companyId: 'comp_1',
      code: '100',
      name: 'Tecnologia',
      annualBudget: 0,
      autoApprovalLimit: 0,
      active: true,
      unitIds: ['unit_1'],
    },
  ];
  const budgetControl = {
    months: [
      {
        competency: '2026-07',
        orcado: 50000,
        realized: 0,
        committed: 0,
        forecast: 0,
        consumed: 0,
        available: 50000,
        budgeted: true,
        cost: 20000,
      },
    ],
    breaches: [],
    unbudgeted: [],
    candidateExceeds: false,
  };
  const actor = 'user_00000000000000000000000000000001';
  const req = {
    id: REQ_ID,
    code: 42,
    companyId: 'comp_1',
    unitId: 'unit_1',
    costCenterId: 'cc_1',
    projectId: PROJ_ID,
    type: 'Purchase',
    description: 'Servidores para o laboratório',
    observation: null,
    estimateValue: 20000,
    estimateType: 'Full',
    startCompetency: '2026-07',
    endCompetency: null,
    paymentFrequency: null,
    paymentInstallmentsCount: null,
    firstDueDate: '2026-07-10',
    totalCost: 20000,
    realizedValue: 0,
    status: 'Approval',
    installments: [],
    attachments: [],
    transitions: [
      {
        action: 'Submit',
        fromStatus: 'Draft',
        toStatus: 'Triage',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: '2026-07-20T12:00:00Z',
        reason: null,
      },
      {
        action: 'ApproveTriage',
        fromStatus: 'Triage',
        toStatus: 'FinancialComplement',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: '2026-07-21T09:00:00Z',
        reason: null,
      },
      {
        action: 'SubmitFinancialComplement',
        fromStatus: 'FinancialComplement',
        toStatus: 'Approval',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: '2026-07-21T10:00:00Z',
        reason: null,
      },
    ],
    createdAt: '2026-07-20T12:00:00Z',
    createdBy: actor,
  };
  const listReqs = [
    req,
    {
      ...req,
      id: 'req_a',
      code: 41,
      estimateValue: 30000,
      totalCost: 30000,
      projectId: null,
      description: 'Consultoria de rede',
      transitions: [],
      attachments: [],
    },
    {
      ...req,
      id: 'req_b',
      code: 40,
      estimateValue: 12000,
      totalCost: 12000,
      status: 'InPayment',
      projectId: null,
      description: 'Notebooks para laboratório',
      transitions: [],
      attachments: [],
    },
  ];
  const metrics = {
    id: PROJ_ID,
    name: 'Obra Bloco A',
    status: 'Active',
    budgetLimit: 100000,
    previsto: 80000,
    realizado: 20000,
    saldo: 20000,
    percentPrevisto: 80,
    percentRealizado: 20,
    isOverCeiling: false,
    requisitionCount: 4,
  };
  const paginated = (items) => ({ items, page: 1, pageSize: 20, total: items.length, totalPages: 1 });
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

  // Rotas escopadas a /api (precedem o catch-all `[]`) — ver memória pr-shots-mock-api-path-scope.
  const withBudgetingMocks = async (ctx) => {
    await ctx.route('**/api/companies*', (r) => json(r, companies));
    await ctx.route('**/api/units*', (r) => json(r, units));
    await ctx.route('**/api/cost-centers*', (r) => json(r, costCenters));
    await ctx.route('**/api/projects/*/metrics', (r) => json(r, metrics));
    await ctx.route(/\/api\/projects\?/, (r) =>
      json(
        r,
        paginated([
          {
            id: PROJ_ID,
            companyId: 'comp_1',
            unitId: 'unit_1',
            name: 'Obra Bloco A',
            description: null,
            budgetLimit: 100000,
            initialConsumedValue: 0,
            status: 'Active',
          },
        ]),
      ),
    );
    await ctx.route(/\/api\/requisitions\/req_[^/?]+$/, (r) => json(r, req)); // detalhe (GET /{id})
    await ctx.route('**/api/requisitions?*', (r) => json(r, paginated(listReqs))); // listas + contagens
    // Painel de controle orçamentário (AEL-830) — registrado por último p/ ter precedência sobre a lista.
    await ctx.route(/\/api\/requisitions\/(req_[^/?]+\/)?budget-control/, (r) => json(r, budgetControl));
  };

  const newCtx = async (role, w = 1360, h = 940) => {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, role);
    await withBudgetingMocks(ctx);
    return ctx;
  };

  // Lista de solicitações (abas de estágio).
  {
    const ctx = await newCtx('Admin');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Rascunhos')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(700);
    await shot(page, '08-requisicoes-lista');
    await ctx.close();
  }

  // Detalhe na aprovação — card de previsão + card de projeto.
  {
    const ctx = await newCtx('Admin', 1360, 1200);
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions/${REQ_ID}`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Previsão de saldo em tempo real')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(800);
    await shot(page, '09-requisicao-aprovacao');

    // A timeline fica abaixo da dobra; recorta só ela para o avatar do ator ficar legível.
    const timeline = page.locator('.audit-timeline').first();
    if (await timeline.count()) {
      await timeline.scrollIntoViewIfNeeded();
      await page.waitForTimeout(300);
      await timeline.screenshot({ path: `${OUT}/09c-requisicao-timeline.png` });
      console.log('saved 09c-requisicao-timeline');
    }
    await ctx.close();
  }
}

// 3f) Budget adjustment (AEL-791) — list, form (type-first + context) and detail
// (budget context of the affected competencies + history + workflow actions).
{
  const ADEQ_ID = 'adeq_11111111111111111111111111111111';
  const actor = 'user_00000000000000000000000000000001';
  const paginated = (items) => ({ items, page: 1, pageSize: 20, total: items.length, totalPages: 1 });
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

  const adequacaoDetail = {
    id: ADEQ_ID,
    code: 7,
    companyId: 'comp_1',
    unitId: 'unit_1',
    costCenterId: 'cc_1',
    type: 'Transfer',
    competency: '2026-08',
    sourceCompetency: '2026-07',
    value: 15000,
    justification: 'Remanejar verba de julho para agosto para cobrir a compra de servidores.',
    counterparts: 'Entrega dos servidores em agosto.',
    status: 'AwaitingApproval',
    transitions: [
      {
        action: 'SendToApproval',
        fromStatus: 'AwaitingTriage',
        toStatus: 'AwaitingApproval',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: '2026-07-22T10:00:00Z',
        reason: null,
      },
    ],
    createdAt: '2026-07-22T09:00:00Z',
    createdBy: actor,
    statusChangedAt: '2026-07-22T10:00:00Z',
  };
  const list = [
    adequacaoDetail,
    {
      ...adequacaoDetail,
      id: 'adeq_2',
      code: 6,
      type: 'Supplementation',
      sourceCompetency: null,
      competency: '2026-07',
      value: 8000,
      status: 'AwaitingTriage',
      transitions: [],
    },
    {
      ...adequacaoDetail,
      id: 'adeq_3',
      code: 5,
      type: 'Reduction',
      sourceCompetency: null,
      competency: '2026-06',
      value: 5000,
      status: 'Approved',
      counterparts: null,
      transitions: [],
    },
  ];

  // Routes scoped to /api (precede the `[]` catch-all) — see memory pr-shots-mock-api-path-scope.
  const withAdequacaoMocks = async (ctx) => {
    await ctx.route('**/api/companies*', (r) =>
      json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
    );
    await ctx.route('**/api/units*', (r) =>
      json(r, [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }]),
    );
    await ctx.route('**/api/cost-centers*', (r) =>
      json(r, [
        {
          id: 'cc_1',
          companyId: 'comp_1',
          code: '100',
          name: 'Tecnologia',
          annualBudget: 0,
          autoApprovalLimit: 0,
          active: true,
          unitIds: ['unit_1'],
        },
      ]),
    );
    await ctx.route('**/api/requisitions?*', (r) => json(r, paginated([])));
    await ctx.route(/\/api\/budget-adjustments\/adeq_[^/?]+$/, (r) => json(r, adequacaoDetail)); // detail (GET /{id})
    await ctx.route('**/api/budget-adjustments?*', (r) => json(r, paginated(list))); // list
    // Painel de controle orçamentário (AEL-830) que o contexto da adequação usa — precedência por último.
    await ctx.route(/\/api\/requisitions\/(req_[^/?]+\/)?budget-control/, (r) =>
      json(r, {
        months: [
          {
            competency: '2026-08',
            orcado: 50000,
            realized: 0,
            committed: 0,
            forecast: 0,
            consumed: 0,
            available: 50000,
            budgeted: true,
          },
        ],
        breaches: [],
        unbudgeted: [],
        candidateExceeds: false,
      }),
    );
  };

  const newCtx = async (w = 1360, h = 1040) => {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Admin');
    await withAdequacaoMocks(ctx);
    return ctx;
  };

  for (const [route, name, anchor] of [
    ['/app/adequacoes', '12-adequacoes-lista', 'Transferência'],
    ['/app/adequacoes/new', '13-adequacao-form', 'Dados da adequação'],
    [`/app/adequacoes/${ADEQ_ID}`, '14-adequacao-detalhe', 'Histórico'],
  ]) {
    const ctx = await newCtx();
    const page = await ctx.newPage();
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText(anchor)
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(700);
    await shot(page, name);
    await ctx.close();
  }
}

// 3g) Remap da esteira (AEL-843/844) — inbox do Aprovador, decisão orçamentária mês a mês
// (com estouro → Aprovar bloqueado + Adequação) e painel de verificação de fornecedor na triagem.
{
  const APR_ID = 'req_33333333333333333333333333333333';
  const TRI_ID = 'req_44444444444444444444444444444444';
  const actor = 'user_00000000000000000000000000000001';
  const paginated = (items) => ({ items, page: 1, pageSize: 20, total: items.length, totalPages: 1 });
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const daysAgo = (n) => new Date(Date.now() - n * 86_400_000).toISOString();

  const companies = [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }];
  const units = [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }];
  const costCenters = [
    {
      id: 'cc_1',
      companyId: 'comp_1',
      code: '300',
      name: 'Manutenção predial',
      autoApprovalLimit: 0,
      active: true,
      unitIds: ['unit_1'],
    },
  ];
  const vendor = {
    id: 'vendor_1',
    companyId: 'comp_1',
    personType: 'Company',
    name: 'Construtora Alfa Ltda',
    tradeName: 'Alfa',
    document: '12.345.678/0001-90',
    email: null,
    phone: null,
    address: null,
    paymentTerms: '30/60 dias',
    bankInfo: { bankCode: '341', branch: '1234', account: '56789', accountDigit: '0', pixKey: '' },
    taxRegime: 'SimplesNacional',
    paymentScheduleMode: 'OnDueDate',
    paymentScheduleDay: null,
    active: true,
    cprbForeseen: null,
    simplesAnexo: 'III',
    acceptedPaymentMethods: [],
    withholdingRates: [{ taxType: 'Iss', rate: 0.05 }],
    associatedLedgerAccountIds: [],
    createdAt: daysAgo(8), // recente → selo "Novo fornecedor"
  };
  // Estouro autoritativo: o pedido (42,5k) não cabe no disponível (20k) da competência.
  const overBudgetControl = {
    months: [
      {
        competency: '2026-07',
        orcado: 50000,
        realized: 20000,
        committed: 10000,
        forecast: 30000,
        consumed: 30000,
        available: 20000,
        budgeted: true,
        cost: 42500,
      },
    ],
    breaches: [{ competency: '2026-07', orcado: 50000, available: 20000, cost: 42500 }],
    unbudgeted: [],
    candidateExceeds: true,
  };
  const okBudgetControl = {
    months: [
      {
        competency: '2026-07',
        orcado: 50000,
        realized: 0,
        committed: 0,
        forecast: 0,
        consumed: 0,
        available: 50000,
        budgeted: true,
        cost: 8200,
      },
    ],
    breaches: [],
    unbudgeted: [],
    candidateExceeds: false,
  };
  const baseReq = {
    id: APR_ID,
    code: 55,
    companyId: 'comp_1',
    unitId: 'unit_1',
    costCenterId: 'cc_1',
    projectId: null,
    vendorId: 'vendor_1',
    type: 'Purchase',
    description: 'Reforma da quadra coberta',
    observation: null,
    estimateValue: 42500,
    estimateType: 'Full',
    startCompetency: '2026-07',
    endCompetency: null,
    paymentFrequency: null,
    paymentInstallmentsCount: null,
    firstDueDate: '2026-07-30',
    totalCost: 42500,
    realizedValue: 0,
    status: 'Approval',
    installments: [],
    attachments: [],
    transitions: [
      {
        action: 'Submit',
        fromStatus: 'Draft',
        toStatus: 'Triage',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(6),
        reason: null,
      },
      {
        action: 'ApproveTriage',
        fromStatus: 'Triage',
        toStatus: 'FinancialComplement',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(5),
        reason: null,
      },
      {
        action: 'SubmitFinancialComplement',
        fromStatus: 'FinancialComplement',
        toStatus: 'Approval',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(4),
        reason: null,
      },
    ],
    createdAt: daysAgo(6),
    createdBy: actor,
    statusChangedAt: daysAgo(4), // >24h úteis → flag "atrasado"
  };
  const approvalList = [
    baseReq,
    {
      ...baseReq,
      id: 'req_a5',
      code: 54,
      vendorId: 'vendor_1',
      estimateValue: 8200,
      totalCost: 8200,
      description: 'Materiais de limpeza',
      statusChangedAt: daysAgo(0),
    },
  ];
  const triageReq = {
    ...baseReq,
    id: TRI_ID,
    code: 56,
    status: 'Triage',
    estimateValue: 8200,
    totalCost: 8200,
    description: 'Serviço de pintura',
    statusChangedAt: daysAgo(0),
    transitions: [
      {
        action: 'Submit',
        fromStatus: 'Draft',
        toStatus: 'Triage',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(1),
        reason: null,
      },
    ],
  };

  const withRemapMocks = async (ctx, { budgetControl, detail }) => {
    await ctx.route('**/api/companies*', (r) => json(r, companies));
    await ctx.route('**/api/units*', (r) => json(r, units));
    await ctx.route('**/api/cost-centers*', (r) => json(r, costCenters));
    await ctx.route(/\/api\/projects\?/, (r) => json(r, paginated([])));
    await ctx.route('**/api/vendors/vendor_*', (r) => json(r, vendor));
    await ctx.route(/\/api\/requisitions\/req_[^/?]+$/, (r) => json(r, detail)); // detalhe (GET /{id})
    await ctx.route('**/api/requisitions?*', (r) => json(r, paginated(approvalList))); // listas + contagens
    await ctx.route(/\/api\/requisitions\/(req_[^/?]+\/)?budget-control/, (r) => json(r, budgetControl));
    // Preview de retenções (regras do sistema) do painel de verificação do fornecedor na triagem.
    await ctx.route('**/api/withholding-rules/preview*', (r) =>
      json(r, [
        { taxType: 'Iss', retains: true, reason: 'None', rateSource: 'FromNota', rate: null, invoiceDependent: true },
        { taxType: 'Inss', retains: true, reason: 'None', rateSource: 'Fixed', rate: 0.11, invoiceDependent: false },
      ]),
    );
    // Orçamento por natureza do aside (Fila + Orçamento) — registrado por último p/ preceder o catch-all `requisitions?*`.
    await ctx.route('**/api/requisitions/budget-summary*', (r) =>
      json(r, [
        {
          costCenterId: 'cc_1',
          costCenterName: 'Manutenção predial',
          orcado: 50000,
          realized: 20000,
          committed: 10000,
          forecast: 30000,
          consumed: 30000,
          available: 20000,
          budgeted: true,
        },
        {
          costCenterId: 'cc_2',
          costCenterName: 'Serviços gerais',
          orcado: 18000,
          realized: 4000,
          committed: 2000,
          forecast: 6000,
          consumed: 6000,
          available: 12000,
          budgeted: true,
        },
      ]),
    );
  };

  const newCtx = async (role, mocks, w = 1360, h = 1100) => {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, role);
    await withRemapMocks(ctx, mocks);
    return ctx;
  };

  // Inbox do Aprovador — fila "Precisa de você" com flags atrasado + estoura orçamento.
  {
    const ctx = await newCtx('Approver', { budgetControl: overBudgetControl, detail: baseReq });
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions/approval`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Precisa de você')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(800);
    await shot(page, '15-aprovador-inbox');
    await ctx.close();
  }

  // Detalhe / decisão orçamentária (Approval, estoura) — Aprovar bloqueado + link p/ Adequação.
  {
    const ctx = await newCtx('Approver', { budgetControl: overBudgetControl, detail: baseReq }, 1360, 1400);
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions/${APR_ID}`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Decisão orçamentária')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(800);
    await shot(page, '16-aprovador-decisao');
    await ctx.close();
  }

  // Verificação de fornecedor na triagem (Controladoria) — selo "Novo fornecedor".
  {
    const ctx = await newCtx('Controllership', { budgetControl: okBudgetControl, detail: triageReq }, 1360, 1200);
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions/${TRI_ID}`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Fornecedor')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(800);
    await shot(page, '17-triagem-verificacao-fornecedor');
    await ctx.close();
  }
}

// 3h) Detalhe do Solicitante — solicitação devolvida (Complement): callout do motivo, dados
// consolidados (lista, não cards soltos), anexos e histórico.
{
  const CMP_ID = 'req_66666666666666666666666666666666';
  const actor = 'user_00000000000000000000000000000001';
  const paginated = (items) => ({ items, page: 1, pageSize: 20, total: items.length, totalPages: 1 });
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const daysAgo = (n) => new Date(Date.now() - n * 86_400_000).toISOString();

  const vendor = {
    id: 'vendor_2',
    companyId: 'comp_1',
    personType: 'Company',
    name: 'Livraria Saber Ltda',
    tradeName: 'Saber',
    document: '98.765.432/0001-10',
    email: null,
    phone: null,
    address: null,
    paymentTerms: '28 dias',
    bankInfo: { bankCode: '033', branch: '4567', account: '11223', accountDigit: '4', pixKey: '' },
    taxRegime: 'SimplesNacional',
    paymentScheduleMode: 'OnDueDate',
    paymentScheduleDay: null,
    active: true,
    cprbForeseen: null,
    simplesAnexo: null,
    acceptedPaymentMethods: [],
    withholdingRates: [],
    associatedLedgerAccountIds: [],
    createdAt: daysAgo(400),
  };
  const req = {
    id: CMP_ID,
    code: 57,
    companyId: 'comp_1',
    unitId: 'unit_1',
    costCenterId: 'cc_1',
    projectId: null,
    vendorId: 'vendor_2',
    type: 'Purchase',
    description: 'Compra de material didático para o laboratório de química (reagentes, vidraria e EPIs).',
    observation: 'Preferência por fornecedor com entrega em até 10 dias úteis.',
    estimateValue: 2650,
    estimateType: 'Full',
    startCompetency: '2026-07',
    endCompetency: null,
    paymentFrequency: null,
    paymentInstallmentsCount: null,
    firstDueDate: '2026-08-05',
    totalCost: 2650,
    realizedValue: 0,
    status: 'Complement',
    installments: [],
    attachments: [
      {
        id: 'att_1',
        fileName: 'nota-fiscal-saber.pdf',
        contentType: 'application/pdf',
        sizeBytes: 184320,
        kind: 'FiscalDocument',
        createdAt: daysAgo(3),
      },
      {
        id: 'att_2',
        fileName: 'cotacao-concorrente.pdf',
        contentType: 'application/pdf',
        sizeBytes: 201400,
        kind: 'Other',
        createdAt: daysAgo(3),
      },
    ],
    transitions: [
      {
        action: 'Submit',
        fromStatus: 'Draft',
        toStatus: 'Triage',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(3),
        reason: null,
      },
      {
        action: 'ReturnForComplement',
        fromStatus: 'Triage',
        toStatus: 'Complement',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(1),
        reason: 'Anexar 3 cotações de fornecedores e a justificativa da escolha.',
      },
    ],
    createdAt: daysAgo(3),
    createdBy: actor,
    statusChangedAt: daysAgo(1),
  };
  const budgetControl = {
    months: [
      {
        competency: '2026-07',
        orcado: 40000,
        realized: 12000,
        committed: 5000,
        forecast: 17000,
        consumed: 17000,
        available: 23000,
        budgeted: true,
        cost: 2650,
      },
    ],
    breaches: [],
    unbudgeted: [],
    candidateExceeds: false,
  };

  const ctx = await browser.newContext({ viewport: { width: 1360, height: 1300 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Requester');
  await ctx.route('**/api/companies*', (r) =>
    json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
  );
  await ctx.route('**/api/units*', (r) =>
    json(r, [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }]),
  );
  await ctx.route('**/api/cost-centers*', (r) =>
    json(r, [
      {
        id: 'cc_1',
        companyId: 'comp_1',
        code: '200',
        name: 'Material didático',
        autoApprovalLimit: 0,
        active: true,
        unitIds: ['unit_1'],
      },
    ]),
  );
  await ctx.route(/\/api\/projects\?/, (r) => json(r, paginated([])));
  await ctx.route('**/api/vendors/vendor_*', (r) => json(r, vendor));
  await ctx.route(/\/api\/requisitions\/req_[^/?]+$/, (r) => json(r, req));
  await ctx.route('**/api/requisitions?*', (r) => json(r, paginated([req])));
  await ctx.route(/\/api\/requisitions\/(req_[^/?]+\/)?budget-control/, (r) => json(r, budgetControl));
  // Resumo de orçamento por natureza (rail persistente do Solicitante: "Sua fila" + "Orçamentos da unidade").
  // Registrado por último → precede o catch-all `**/api/requisitions?*`.
  await ctx.route('**/api/requisitions/budget-summary*', (r) =>
    json(r, [
      {
        costCenterId: 'cc_1',
        costCenterName: 'Material didático',
        orcado: 40000,
        realized: 12000,
        committed: 5000,
        forecast: 17000,
        consumed: 17000,
        available: 23000,
        budgeted: true,
      },
      {
        costCenterId: 'cc_2',
        costCenterName: 'Serviços gráficos',
        orcado: 18000,
        realized: 4000,
        committed: 2000,
        forecast: 6000,
        consumed: 6000,
        available: 12000,
        budgeted: true,
      },
    ]),
  );

  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/requisitions/${CMP_ID}`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Classificação')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(800);
  await shot(page, '18-solicitante-detalhe-complemento');
  // AEL-959: scroll to the attachments panel to show the per-file type badges + the "Selecione o tipo" picker.
  await page
    .getByText('Anexos')
    .first()
    .scrollIntoViewIfNeeded()
    .catch(() => {});
  await page.waitForTimeout(400);
  await shot(page, '18b-solicitante-anexos-tipo');
  await ctx.close();
}

// 3i) Complemento financeiro (AEL-844) — o Solicitante autora as parcelas (vencimento/valor/forma de
// pagamento), informa a data de emissão e vê o Resumo do faturamento (aprovado × programado × diferença).
{
  const FC_ID = 'req_77777777777777777777777777777777';
  const actor = 'user_00000000000000000000000000000001';
  const paginated = (items) => ({ items, page: 1, pageSize: 20, total: items.length, totalPages: 1 });
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const daysAgo = (n) => new Date(Date.now() - n * 86_400_000).toISOString();

  const vendor = {
    id: 'vendor_3',
    companyId: 'comp_1',
    personType: 'Company',
    name: 'TechServ Manutenção Ltda',
    tradeName: 'TechServ',
    document: '11.222.333/0001-44',
    email: null,
    phone: null,
    address: null,
    paymentTerms: '30 dias',
    bankInfo: { bankCode: '341', branch: '1234', account: '55667', accountDigit: '8', pixKey: '' },
    taxRegime: 'SimplesNacional',
    paymentScheduleMode: 'OnDueDate',
    paymentScheduleDay: null,
    active: true,
    cprbForeseen: null,
    simplesAnexo: null,
    acceptedPaymentMethods: [],
    withholdingRates: [],
    associatedLedgerAccountIds: [],
    createdAt: daysAgo(400),
  };
  const req = {
    id: FC_ID,
    code: 58,
    companyId: 'comp_1',
    unitId: 'unit_1',
    costCenterId: 'cc_1',
    projectId: null,
    vendorId: 'vendor_3',
    type: 'ServiceProvision',
    description: 'Manutenção preventiva dos climatizadores (3 parcelas mensais).',
    observation: null,
    estimateValue: 6000,
    estimateType: 'Full',
    startCompetency: '2026-07',
    endCompetency: null,
    paymentFrequency: 'Monthly',
    paymentInstallmentsCount: 3,
    firstDueDate: '2026-08-05',
    totalCost: 6000,
    realizedValue: 0,
    status: 'FinancialComplement',
    issueDate: null,
    installments: [],
    attachments: [
      {
        id: 'att_1',
        fileName: 'NF-e 14731 climatizadores.pdf',
        contentType: 'application/pdf',
        sizeBytes: 84210,
        createdAt: daysAgo(1),
      },
    ],
    transitions: [
      {
        action: 'Submit',
        fromStatus: 'Draft',
        toStatus: 'Triage',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(3),
        reason: null,
      },
      {
        action: 'ApproveTriage',
        fromStatus: 'Triage',
        toStatus: 'FinancialComplement',
        actorId: actor,
        actorName: 'Ana Contábil',
        actorPictureUrl: AVATAR_2,
        occurredAt: daysAgo(1),
        reason: null,
      },
    ],
    createdAt: daysAgo(3),
    createdBy: actor,
    statusChangedAt: daysAgo(1),
  };
  const budgetControl = {
    months: [
      {
        competency: '2026-07',
        orcado: 40000,
        realized: 12000,
        committed: 5000,
        forecast: 17000,
        consumed: 17000,
        available: 23000,
        budgeted: true,
        cost: 6000,
      },
    ],
    breaches: [],
    unbudgeted: [],
    candidateExceeds: false,
  };

  const ctx = await browser.newContext({ viewport: { width: 1360, height: 1520 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Requester');
  await ctx.route('**/api/companies*', (r) =>
    json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
  );
  await ctx.route('**/api/units*', (r) =>
    json(r, [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }]),
  );
  await ctx.route('**/api/cost-centers*', (r) =>
    json(r, [
      {
        id: 'cc_1',
        companyId: 'comp_1',
        code: '300',
        name: 'Manutenção predial',
        autoApprovalLimit: 0,
        active: true,
        unitIds: ['unit_1'],
      },
    ]),
  );
  await ctx.route(/\/api\/projects\?/, (r) => json(r, paginated([])));
  await ctx.route('**/api/vendors/vendor_*', (r) => json(r, vendor));
  await ctx.route(/\/api\/requisitions\/req_[^/?]+$/, (r) => json(r, req));
  await ctx.route('**/api/requisitions?*', (r) => json(r, paginated([req])));
  await ctx.route(/\/api\/requisitions\/(req_[^/?]+\/)?budget-control/, (r) => json(r, budgetControl));
  await ctx.route('**/api/requisitions/budget-summary*', (r) =>
    json(r, [
      {
        costCenterId: 'cc_1',
        costCenterName: 'Manutenção predial',
        orcado: 40000,
        realized: 12000,
        committed: 5000,
        forecast: 17000,
        consumed: 17000,
        available: 23000,
        budgeted: true,
      },
    ]),
  );

  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/requisitions/${FC_ID}`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Data de emissão')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(800);
  await shot(page, '19-solicitante-complemento-financeiro');
  // Tela estreita: as linhas de parcela (.fc-head/.fc-row) devem manter os rótulos alinhados com os campos.
  await page.setViewportSize({ width: 720, height: 720 });
  await page
    .getByText('Adicionar parcela')
    .first()
    .scrollIntoViewIfNeeded()
    .catch(() => {});
  await page.waitForTimeout(300);
  await shot(page, '19b-complemento-financeiro-estreito');
  await ctx.close();
}

// 3j) Gestão Financeira (Financeiro home) — fila por vencimento (Vencidos/Vence hoje + seleção p/
// remessa), aba Remessas e Histórico de baixas. Rotas escopadas a /api (ver pr-shots-mock-api-path-scope).
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const isoDay = (offset) => new Date(Date.now() + offset * 86_400_000).toISOString().slice(0, 10);
  const paginated = (items, total = items.length) => ({ items, page: 1, pageSize: 50, total, totalPages: 1 });

  const queueItem = (over) => ({
    payableId: 'pay_1',
    installmentId: `ins_${Math.random().toString(36).slice(2, 8)}`,
    sequence: 1,
    installmentCount: 1,
    counterpartyName: 'Fornecedor',
    description: '',
    costCenterId: 'cc_1',
    categoryId: null,
    dueDate: isoDay(0),
    amount: 1000,
    netAmount: 1000,
    installmentStatus: 'Open',
    payableStatus: 'Scheduled',
    scheduledPaymentDate: isoDay(30),
    isHeld: false,
    holdReason: null,
    remittanceId: null,
    remittanceSequence: null,
    rail: 'Remittance',
    manualReason: null,
    vendorDocument: '92802784000190',
    vendorId: 'vendor_1',
    effectiveMethod: 'Boleto',
    boletoDigitableLine: null,
    documentNumber: null,
    issueDate: isoDay(-20),
    originUnitId: 'unit_1',
    hasFiscalDocument: true,
    ...over,
  });
  const queueItems = [
    queueItem({
      payableId: 'pay_o1',
      counterpartyName: 'RGE Sul Distribuidora',
      dueDate: isoDay(-6),
      documentNumber: '88123401',
    }),
    queueItem({
      payableId: 'pay_h1',
      counterpartyName: 'Companhia Riograndense de Saneamento',
      dueDate: isoDay(0),
      documentNumber: '31854541',
      boletoDigitableLine: '8' + '4'.repeat(47),
      netAmount: 528.4,
      amount: 528.4,
    }),
    queueItem({
      payableId: 'pay_h2',
      counterpartyName: 'Fundo Especial de Modernização',
      dueDate: isoDay(0),
      documentNumber: '10491143',
      boletoDigitableLine: '0' + '4'.repeat(46),
      netAmount: 1548.89,
      amount: 1548.89,
    }),
    queueItem({
      payableId: 'pay_m1',
      counterpartyName: 'Serviços Manuais ME',
      dueDate: isoDay(1),
      rail: 'Manual',
      manualReason: 'VendorBankInfoMissing',
      effectiveMethod: 'Pix',
      netAmount: 890.5,
      amount: 890.5,
      hasFiscalDocument: false,
    }),
    queueItem({
      payableId: 'pay_f1',
      counterpartyName: 'TI Soluções ME',
      dueDate: isoDay(2),
      documentNumber: '77120',
      netAmount: 18900,
      amount: 19845,
    }),
  ];
  const summary = {
    asOf: isoDay(0),
    overdueCount: 73,
    overdueNetTotal: 311969.73,
    groups: [
      { dueDate: isoDay(0), count: 5, netTotal: 5388.27 },
      { dueDate: isoDay(1), count: 3, netTotal: 59539.26 },
      { dueDate: isoDay(2), count: 9, netTotal: 40210.11 },
    ],
    totalCount: 117,
    totalNetTotal: 417107.37,
  };
  const history = [
    {
      paymentId: 'pmt_1',
      payableId: 'pay_1',
      installmentId: 'ins_1',
      installmentSequence: 1,
      installmentCount: 2,
      counterpartyName: 'Construtora Alfa Ltda',
      vendorDocument: '12345678000195',
      documentNumber: 'NF-4521',
      paymentDate: isoDay(-2),
      amount: 21250,
      interest: 0,
      fine: 0,
      discount: 0,
      method: 'Ted',
      bankAccountId: 'ba_1',
      isReversed: false,
      reversalReason: null,
      hasProof: true,
      createdAt: new Date().toISOString(),
      createdById: 'user-1',
    },
    {
      paymentId: 'pmt_2',
      payableId: 'pay_2',
      installmentId: 'ins_2',
      installmentSequence: 1,
      installmentCount: 1,
      counterpartyName: 'TI Soluções ME',
      vendorDocument: '11222333000144',
      documentNumber: 'NF-77120',
      paymentDate: isoDay(-40),
      amount: 18900,
      interest: 0,
      fine: 0,
      discount: 0,
      method: 'Pix',
      bankAccountId: 'ba_1',
      isReversed: false,
      reversalReason: null,
      hasProof: false,
      createdAt: new Date().toISOString(),
      createdById: '00000000-0000-0000-0000-0000000005ed',
    },
  ];

  const withTreasuryMocks = async (ctx) => {
    await ctx.route('**/api/companies*', (r) =>
      json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
    );
    await ctx.route('**/api/units*', (r) =>
      json(r, [{ id: 'unit_1', companyId: 'comp_1', name: 'Mantenedora', acronym: 'Ulbra S.A', active: true }]),
    );
    await ctx.route('**/api/bank-accounts*', (r) =>
      json(r, [
        {
          id: 'ba_1',
          companyId: 'comp_1',
          bankCode: '033',
          bankName: 'Santander',
          agency: '1234',
          accountNumber: '41221',
          accountDigit: '0',
          alias: 'Banrisul c/c 41.221',
          cnabAgreementCode: 'CONV123',
          active: true,
          isDefaultForPayments: true,
        },
      ]),
    );
    await ctx.route('**/api/bank-transactions/period-summary*', (r) =>
      json(r, {
        from: null,
        to: null,
        reconciled: { count: 10, total: 0 },
        suggested: { count: 40, total: 0 },
        unmatched: { count: 5, total: 0 },
        ignored: { count: 0, total: 0 },
        debitTotal: 0,
        creditTotal: 0,
      }),
    );
    await ctx.route('**/api/remittances?*', (r) =>
      json(
        r,
        paginated(
          [
            {
              id: 'rem_442',
              sequenceNumber: 442,
              status: 'Generated',
              lineCount: 12,
              totalAmount: 88400,
              generatedAt: new Date().toISOString(),
            },
            {
              id: 'rem_441',
              sequenceNumber: 441,
              status: 'Confirmed',
              lineCount: 5,
              totalAmount: 23100,
              generatedAt: new Date(Date.now() - 86_400_000).toISOString(),
            },
          ],
          83,
        ),
      ),
    );
    await ctx.route('**/api/payables/payments*', (r) => json(r, paginated(history, 215)));
    await ctx.route('**/api/payables/payment-queue-summary*', (r) => json(r, summary));
    // Registrada por último → precede o catch-all; só a lista (com `?`) para não engolir as rotas acima.
    await ctx.route('**/api/payables?*', (r) => json(r, paginated(queueItems, 117)));
  };

  const newCtx = async (w = 1360, h = 1080) => {
    const ctx = await browser.newContext({ viewport: { width: w, height: h }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Finance');
    await withTreasuryMocks(ctx);
    return ctx;
  };

  // The queue is a flex list (not a table) so it reflows on any width — 3 tabs (Conciliação is a
  // separate screen). 23b captures the narrow reflow; the history filters are compact date/method fields.
  for (const [route, name, anchor, width] of [
    ['/app/treasury', '23-financeiro-fila', 'Vencidos', 1360],
    ['/app/treasury', '23b-financeiro-fila-estreita', 'Vencidos', 820],
    ['/app/treasury?tab=remittances', '24-financeiro-remessas', 'Remessas CNAB240', 1360],
    ['/app/treasury?tab=history', '25-financeiro-historico', 'Construtora Alfa Ltda', 1360],
  ]) {
    const ctx = await newCtx(width);
    const page = await ctx.newPage();
    await page.goto(`${BASE}${route}`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText(anchor)
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(800);
    await shot(page, name);
    await ctx.close();
  }
}

// 26/27) AEL-877 — boleto no complemento financeiro + painel de recorrência (Solicitante).
{
  const reqBase = {
    id: 'req_1',
    code: 4210,
    companyId: 'company_1',
    unitId: 'unit_1',
    costCenterId: 'cost_center_1',
    projectId: null,
    vendorId: 'vendor_1',
    type: 'FixedExpense',
    description: 'Energia elétrica — Campus Canoas',
    observation: null,
    estimateValue: 3200,
    estimateType: 'Monthly',
    startCompetency: '2026-07',
    endCompetency: null,
    paymentFrequency: null,
    paymentInstallmentsCount: null,
    firstDueDate: '2026-07-10',
    issueDate: null,
    totalCost: 3200,
    realizedValue: 0,
    status: 'FinancialComplement',
    attachments: [],
    installments: [],
    cycles: [],
    basePayableId: null,
    transitions: [],
    createdAt: '2026-07-01T12:00:00Z',
    createdBy: 'user_1',
    statusChangedAt: '2026-07-02T12:00:00Z',
    fiscalDocumentPending: false,
  };
  const routeReq = (ctx, obj) =>
    ctx.route(`**/api/requisitions/${obj.id}`, (r) =>
      r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(obj) }),
    );

  // 26) Complemento financeiro com uma parcela paga por Boleto → campo de dígitos + badge Convênio.
  {
    const ctx = await browser.newContext({ viewport: { width: 1180, height: 1180 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Requester');
    await routeReq(ctx, reqBase);
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions/req_1`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Enviar informações financeiras')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page
      .locator('select')
      .first()
      .selectOption('Boleto')
      .catch(() => {});
    await page
      .getByLabel('Linha digitável do boleto')
      .fill('846700000017599200241114202410000000000000000000')
      .catch(() => {});
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${OUT}/26-complemento-boleto-convenio.png`, fullPage: true });
    console.log('saved 26');
    await ctx.close();
  }

  // 27) Recorrência em pagamento: títulos gerados (base + fatura de agosto por convênio) + lançar/encerrar.
  {
    const inPayment = {
      ...reqBase,
      status: 'InPayment',
      estimateValue: 3200,
      basePayableId: 'payable-base-0001',
      cycles: [
        {
          sequence: 1,
          competency: '2026-08',
          amount: 3410.55,
          dueDate: '2026-08-10',
          paymentMethod: 'Boleto',
          boletoDigitableLine: '846700000017599200241114202410000000000000000000',
          payableId: 'payable-cycle-0008',
          createdAt: '2026-08-01T12:00:00Z',
        },
      ],
    };
    const ctx = await browser.newContext({ viewport: { width: 1180, height: 1220 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Requester');
    await routeReq(ctx, inPayment);
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/requisitions/req_1`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Recorrência — faturas do mês')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(500);
    await page.screenshot({ path: `${OUT}/27-recorrencia-ciclos.png`, fullPage: true });
    console.log('saved 27');
    await ctx.close();
  }
}

// 30) Documentação (AEL-923) — seção geral + manual do Financeiro restrito (Remessa/Retorno) com os
// previews das telas embutidos (barra de abas real + "Selecione uma empresa"), e o contraste do gate.
{
  // Geral (Admin): rail com o botão "Docs" + a página de manuais (Visão Geral do Fluxo).
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1200 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Admin');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/documentation`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Visão Geral do Fluxo')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, '30-documentacao-geral');
    await ctx.close();
  }

  // Financeiro (Finance): item restrito "Remessa e Retorno" — mostra os previews das telas (abas + empresa).
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1700 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Finance');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/documentation/financeiro-remessa-retorno`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Remessa e Retorno — Financeiro')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, '31-documentacao-financeiro');
    await ctx.close();
  }

  // Contraste do gate: Contábil (excluído) em /app/documentation — sem o item "Remessa e Retorno".
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1000 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Accounting');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/documentation`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Visão Geral do Fluxo')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, '32-documentacao-contabil-sem-financeiro');
    await ctx.close();
  }

  // Guia do Solicitante (Requester) — item sem gate, visível a todos os cargos.
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1500 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Requester');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/documentation/guia-solicitante`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Guia do Solicitante')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, '33-documentacao-solicitante');
    await ctx.close();
  }

  // Guia do Contábil (Accounting) — item sem gate, visível a todos os cargos.
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1500 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Accounting');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/documentation/guia-contabil`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Guia do Contábil')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, '34-documentacao-contabil-guia');
    await ctx.close();
  }

  // Guia da Controladoria (Controllership) — item sem gate; a Controladoria também vê o manual do Financeiro.
  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 1500 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Controllership');
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/documentation/guia-controladoria`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Guia da Controladoria')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, '35-documentacao-controladoria');
    await ctx.close();
  }
}

// 3z) Estrutura — Empresas: excluir (admin, soft-delete) e inativar/reativar (core:write).
// Admin vê Editar + Inativar/Reativar + Excluir; Controladoria (core:write) vê Editar + Inativar/Reativar
// sem Excluir. Lista com uma empresa ativa e uma inativa para mostrar o badge de status.
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const companies = [
    { id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199', active: true },
    { id: 'comp_2', legalName: 'Colégio Luterano Bom Jesus', cnpj: '90120000000110', active: false },
  ];
  const units = [
    {
      id: 'unit_1',
      companyId: 'comp_1',
      name: 'Campus Canoas',
      acronym: 'CAN',
      code: null,
      cnpj: null,
      isMatriz: true,
      active: true,
    },
  ];
  // Seed a concrete company selection so the page is not in consolidated "Todas" mode (which hides write actions).
  const seedSelection = () => localStorage.setItem('ulbra-one.empresaId', 'comp_1');

  for (const [role, name] of [
    ['Admin', '36-estrutura-empresas-admin'],
    ['Controllership', '37-estrutura-empresas-controladoria'],
  ]) {
    const ctx = await browser.newContext({ viewport: { width: 1180, height: 720 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await ctx.addInitScript(seedSelection);
    await mockApi(ctx, role);
    // Routes scoped to /api (must precede the catch-all `[]`) — see the pr-shots-mock-api-path-scope memory.
    await ctx.route('**/api/companies*', (r) => json(r, companies));
    await ctx.route('**/api/units*', (r) => json(r, units));
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/structure`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Empresas e unidades')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    // Consolidated roles (Admin/Controllership) default to "Todas as empresas", which hides write actions —
    // pick a concrete company so the Editar/Inativar/Excluir controls render.
    await page.selectOption('select.emp-select', 'comp_1').catch(() => {});
    await page.waitForTimeout(500);
    await shot(page, name);
    await ctx.close();
  }
}

// 3aa) Usuários & acessos — cadastro por convite: a tabela com o badge "Aguardando 1º acesso" e o
// diálogo de cadastro (e-mail + cargo + escopo) que cria o usuário antes do primeiro login Google.
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const companies = [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199', active: true }];
  const units = [
    {
      id: 'unit_1',
      companyId: 'comp_1',
      name: 'Campus Canoas',
      acronym: 'CAN',
      code: null,
      cnpj: null,
      isMatriz: true,
      active: true,
    },
    {
      id: 'unit_2',
      companyId: 'comp_1',
      name: 'Campus Torres',
      acronym: 'TOR',
      code: null,
      cnpj: null,
      isMatriz: false,
      active: true,
    },
  ];
  const costCenters = [
    {
      id: 'cc_1',
      companyId: 'comp_1',
      code: '100',
      name: 'Tecnologia',
      annualBudget: 0,
      autoApprovalLimit: 0,
      active: true,
      unitIds: [],
    },
    {
      id: 'cc_2',
      companyId: 'comp_1',
      code: '200',
      name: 'Recursos Humanos',
      annualBudget: 0,
      autoApprovalLimit: 0,
      active: true,
      unitIds: [],
    },
  ];
  const roles = ['Admin', 'Presidency', 'Finance', 'Controllership', 'Accounting', 'Approver', 'Requester', 'User'].map(
    (name, i) => ({ id: `role_${i}`, name, permissions: [] }),
  );
  const users = [
    {
      id: 'user_1',
      email: 'carla@ulbra.br',
      name: 'Carla Controladoria',
      role: 'Controllership',
      isAdmin: false,
      active: true,
      pendingFirstLogin: false,
      canApproveBudgetAdjustment: true,
      companyIds: [],
      units: [],
      costCenters: [],
    },
    {
      id: 'user_2',
      email: 'rita@ulbra.br',
      name: 'Rita Solicitante',
      role: 'Requester',
      isAdmin: false,
      active: true,
      pendingFirstLogin: false,
      canApproveBudgetAdjustment: false,
      companyIds: ['comp_1'],
      units: [{ companyId: 'comp_1', unitId: 'unit_1' }],
      costCenters: [{ companyId: 'comp_1', costCenterId: 'cc_1' }],
    },
    {
      id: 'user_3',
      email: 'novato@aelbra.com.br',
      name: '',
      role: 'Accounting',
      isAdmin: false,
      active: true,
      pendingFirstLogin: true,
      canApproveBudgetAdjustment: false,
      companyIds: [],
      units: [],
      costCenters: [],
    },
  ];

  const openUsers = async () => {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 820 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Admin');
    // Routes scoped to /api (must precede the catch-all `[]`) — see the pr-shots-mock-api-path-scope memory.
    await ctx.route('**/api/users*', (r) => json(r, users));
    await ctx.route('**/api/roles*', (r) => json(r, roles));
    await ctx.route('**/api/companies*', (r) => json(r, companies));
    await ctx.route('**/api/units*', (r) => json(r, units));
    await ctx.route('**/api/cost-centers*', (r) => json(r, costCenters));
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/settings/users`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Usuários & acessos')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(500);
    return { ctx, page };
  };

  {
    const { ctx, page } = await openUsers();
    await shot(page, '38-usuarios-lista-pendente');
    await ctx.close();
  }

  {
    const { ctx, page } = await openUsers();
    await page.setViewportSize({ width: 1360, height: 1100 });
    await page
      .getByRole('button', { name: 'Cadastrar usuário' })
      .click()
      .catch(() => {});
    await page
      .getByLabel('E-mail')
      .fill('consultor@parceiro.com')
      .catch(() => {});
    await page
      .getByLabel('Nome', { exact: true })
      .fill('Consultor Externo')
      .catch(() => {});
    // `exact` matters: the table rows carry "Cargo de <nome>" selects, which a substring match would also hit.
    await page
      .getByLabel('Cargo', { exact: true })
      .selectOption('Requester')
      .catch(() => {});
    await page
      .getByLabel('Empresa ULBRA S.A. para o novo usuário')
      .check()
      .catch(() => {});
    await page.waitForTimeout(500);
    await shot(page, '39-usuarios-cadastro-dialogo');
    await ctx.close();
  }
}

// 6) Retorno CNAB — painel "Retornos processados" (histórico dos .RET armazenados, com download).
{
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 980 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  const companyId = 'company_00000000000000000000000000000001';
  await ctx.addInitScript(seedToken, FAKE_JWT);
  // A aba é por empresa (some no modo consolidado "Todas"), então a seleção precisa ser uma empresa real.
  await ctx.addInitScript((id) => localStorage.setItem('ulbra-one.empresaId', id), companyId);
  await mockApi(ctx, 'Finance');
  await ctx.route('**/api/companies*', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([{ id: companyId, legalName: 'ULBRA Demo S.A.', cnpj: '92802784000190', active: true }]),
    }),
  );
  await ctx.route('**/api/bank-accounts*', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify([
        {
          id: 'bank_account_00000000000000000000000000000009',
          companyId,
          bankCode: '033',
          bankName: 'Santander',
          agency: '1234',
          accountNumber: '41221',
          accountDigit: '0',
          accountType: 'Checking',
          alias: 'Santander c/c 41.221',
          openingBalance: 0,
          active: true,
          isDefaultForPayments: true,
          cnabAgreementCode: null,
        },
      ]),
    }),
  );
  // O aside do Financeiro lê objetos (não listas): com o catch-all `[]` ele quebra em `summary.groups`.
  await ctx.route('**/api/payables/payment-queue-summary*', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        asOf: '2026-08-20',
        overdueCount: 0,
        overdueNetTotal: 0,
        groups: [],
        totalCount: 0,
        totalNetTotal: 0,
      }),
    }),
  );
  await ctx.route('**/api/bank-transactions/period-summary*', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        from: null,
        to: null,
        reconciled: { count: 0, total: 0 },
        suggested: { count: 0, total: 0 },
        unmatched: { count: 0, total: 0 },
        ignored: { count: 0, total: 0 },
        debitTotal: 0,
        creditTotal: 0,
      }),
    }),
  );
  await ctx.route('**/api/remittances/return/history/**', (r) =>
    r.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify({
        items: [
          {
            id: 'remittance_return_00000000000000000000000000000001',
            fileName: 'RETORNO_20260820.RET',
            processedAt: '2026-08-20T13:45:00Z',
            processedBy: 'maria.silva',
            applied: true,
            matched: 12,
            paid: 10,
            returned: 1,
            unmatched: 0,
            divergent: 1,
          },
          {
            id: 'remittance_return_00000000000000000000000000000002',
            fileName: 'RETORNO_20260819_TRUNCADO.RET',
            processedAt: '2026-08-19T09:12:00Z',
            processedBy: 'joao.souza',
            applied: false,
            matched: 0,
            paid: 0,
            returned: 0,
            unmatched: 3,
            divergent: 0,
          },
        ],
        page: 1,
        pageSize: 20,
        total: 2,
        totalPages: 1,
      }),
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/treasury?tab=cnabReturn`, { waitUntil: 'domcontentloaded' });
  // Semear `ulbra-one.empresaId` não basta: o cargo Finance enxerga o consolidado, e o efeito de
  // normalização do store roda antes de /companies resolver, com a lista vazia — a seleção volta pra
  // "Todas" e a aba some (AEL-906 tornou remessas/retorno por empresa). Escolher no seletor é determinístico.
  await page.selectOption('.emp-select', companyId).catch(() => {});
  await page
    .getByText('Retornos processados')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(800);
  await shot(page, '06-retorno-cnab-historico');
  await ctx.close();
}

// 4) Auditoria (Configurações) — lista da trilha e o drawer com o diff campo a campo.
// Admin resolve o autor pelo nome (tem admin:users); Controladoria só tem audit:read e vê o id carimbado.
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const users = [
    {
      id: 'user_3fa85f6457174219973000000000000a',
      email: 'ana@ulbra.local',
      name: 'Ana Contábil',
      role: 'Accounting',
      isAdmin: false,
      active: true,
      canApproveBudgetAdjustment: false,
      companyIds: [],
      units: [],
      costCenters: [],
    },
  ];
  const entry = (over) => ({
    id: 'audit_log_1',
    occurredAt: '2026-08-20T14:03:11-03:00',
    traceId: '4bf92f3577b34da6a3ce929d0e0e4736',
    actorUserId: '3fa85f64-5717-4219-9730-00000000000a',
    action: 'Update',
    entityType: 'Payable',
    schema: 'finance',
    tableName: 'payables',
    primaryKey: 'pay_8f21c4',
    oldValues: JSON.stringify({ Amount: 12500.0, Status: 'Pending', DueDate: '2026-09-05' }),
    newValues: JSON.stringify({ Amount: 13750.5, Status: 'Approved', DueDate: '2026-09-12' }),
    affectedColumns: JSON.stringify(['Amount', 'Status', 'DueDate']),
    ...over,
  });
  const items = [
    entry({}),
    entry({
      id: 'audit_log_2',
      occurredAt: '2026-08-20T11:47:02-03:00',
      action: 'Create',
      entityType: 'Requisition',
      schema: 'budgeting',
      tableName: 'requisitions',
      primaryKey: 'req_5510',
      oldValues: null,
      newValues: JSON.stringify({ Description: 'Compra de notebooks', TotalValue: 24800, Status: 'Draft' }),
      affectedColumns: null,
    }),
    entry({
      id: 'audit_log_3',
      occurredAt: '2026-08-19T18:22:40-03:00',
      actorUserId: 'system',
      action: 'Delete',
      entityType: 'WithholdingRule',
      schema: 'fiscal',
      tableName: 'withholding_rules',
      primaryKey: 'whrule_12',
      oldValues: JSON.stringify({ Code: 'IRRF-1708', Active: true }),
      newValues: null,
      affectedColumns: null,
    }),
  ];
  const trail = { items, page: 1, pageSize: 20, total: items.length, totalPages: 1 };

  for (const [role, name] of [
    ['Admin', '40-auditoria-lista'],
    ['Controllership', '41-auditoria-controladoria'],
  ]) {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 820 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, role);
    // Routes scoped to /api (must precede the catch-all `[]`) — see the pr-shots-mock-api-path-scope memory.
    await ctx.route('**/api/audit-logs*', (r) => json(r, trail));
    await ctx.route('**/api/users*', (r) => json(r, users));
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/settings/audit`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Auditoria')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(600);
    await shot(page, name);

    // Drawer: the same entry read field by field (only worth one shot, on the Admin pass).
    if (role === 'Admin') {
      await page.getByText('pay_8f21c4').first().click();
      await page.waitForTimeout(500);
      await shot(page, '42-auditoria-detalhe');
    }
    await ctx.close();
  }
}

// 50/51) Pagamentos Programados e Realizados (AEL-958) — aba Relação (filtros + tabela + Exportar PDF /
// Copiar Resumo) e aba Remessas / Títulos (grade filtrável + lista plana de títulos).
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const paginated = (items) => ({ items, page: 1, pageSize: 20, total: items.length, totalPages: 1 });
  const companies = [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }];
  const units = [
    { id: 'unit_1', companyId: 'comp_1', name: 'Ulbra S.A', acronym: 'ULB', active: true },
    { id: 'unit_2', companyId: 'comp_1', name: 'Canoas/RS', acronym: 'CAN', active: true },
  ];
  const costCenters = [
    {
      id: 'cc_1',
      companyId: 'comp_1',
      code: '6060',
      name: 'ENSINO',
      autoApprovalLimit: 0,
      active: true,
      unitIds: ['unit_1'],
    },
    {
      id: 'cc_2',
      companyId: 'comp_1',
      code: '6061',
      name: 'SUPORTE',
      autoApprovalLimit: 0,
      active: true,
      unitIds: ['unit_1'],
    },
  ];
  const item = (over) => ({
    payableId: 'pay_' + Math.random().toString(36).slice(2, 8),
    installmentId: 'inst_' + Math.random().toString(36).slice(2, 8),
    companyId: 'comp_1',
    requisitionCode: 16916,
    requesterName: 'Nayara da Rocha Silveira',
    unitId: 'unit_1',
    unitName: 'Ulbra S.A',
    costCenterId: 'cc_1',
    natureName: 'ENSINO',
    approverName: 'Não identificado',
    approvedAt: '2026-07-27T00:00:00Z',
    counterpartyName: 'CIGAME COMERCIO DE MATERIAL ELETRICO LTDA',
    vendorDocument: '09721504000175',
    amount: 4732.82,
    dueDate: '2026-08-22',
    installmentSequence: 2,
    installmentCount: 2,
    payableStatus: 'Scheduled',
    installmentStatus: 'Open',
    statusLabel: 'Fila Pagamento',
    paymentTypeLabel: 'Pgto 2/2',
    description: null,
    observation: null,
    ...over,
  });
  const reportRows = [
    item({}),
    item({
      requisitionCode: 16926,
      requesterName: 'Silvana Soares',
      unitName: 'Canoas/RS',
      counterpartyName: 'BRS SUPRIMENTOS CORPORATIVOS S/A',
      amount: 5516.67,
      statusLabel: 'Fila Pagamento',
      installmentSequence: 1,
      installmentCount: 1,
      paymentTypeLabel: 'Pgto. Único',
    }),
    item({
      requisitionCode: 17082,
      requesterName: 'Beatriz Batistel',
      unitName: 'Canoas/RS',
      natureName: 'ENSINO',
      approverName: '-',
      approvedAt: null,
      counterpartyName: 'CIRURGICA SANTA CRUZ COM. DE PRODUTOS LTDA',
      amount: 471.59,
      payableStatus: 'PendingClassification',
      statusLabel: 'Em Triagem',
      installmentSequence: 1,
      installmentCount: 1,
      paymentTypeLabel: 'Pgto. Único',
    }),
  ];

  const withMocks = async (ctx) => {
    await ctx.route('**/api/companies*', (r) => json(r, companies));
    await ctx.route('**/api/units*', (r) => json(r, units));
    await ctx.route('**/api/cost-centers*', (r) => json(r, costCenters));
    await ctx.route('**/api/payables/report/summary*', (r) =>
      json(r, { count: 3, total: 10721.08, byUnit: [], byProvider: [] }),
    );
    await ctx.route('**/api/payables/report*', (r) => json(r, paginated(reportRows)));
  };

  {
    const ctx = await browser.newContext({ viewport: { width: 1360, height: 940 }, deviceScaleFactor: 2 });
    await blockExternal(ctx);
    await ctx.addInitScript(seedToken, FAKE_JWT);
    await mockApi(ctx, 'Finance');
    await withMocks(ctx);
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/payments-report?showImported=true`, { waitUntil: 'domcontentloaded' });
    await page
      .getByText('Relação de pagamentos cadastrados')
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(700);
    await shot(page, '50-relacao-pagamentos');
    await ctx.close();
  }
}

// 52) Livro Diário — cabeçalho sem o seletor de empresa (o escopo empresa/consolidado vive só no
// topbar) + aba "Planilha". Mesma prova vale para Balancete (05) e DRE (06), que perderam o campo
// "EMPRESA" da linha de filtros.
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const line = (ledgerAccountId, accountCode, accountName, side, amount) => ({
    ledgerAccountId,
    accountCode,
    accountName,
    side,
    amount,
    costCenterId: null,
  });
  const entry = (id, number, competenceDate, description, lines) => ({
    id,
    number,
    competenceDate,
    description,
    status: 'Posted',
    lines,
    totalDebit: lines.filter((l) => l.side === 'Debit').reduce((a, l) => a + l.amount, 0),
    totalCredit: lines.filter((l) => l.side === 'Credit').reduce((a, l) => a + l.amount, 0),
  });
  const day = (date, entries) => ({
    date,
    entries,
    totalDebit: entries.reduce((a, e) => a + e.totalDebit, 0),
    totalCredit: entries.reduce((a, e) => a + e.totalCredit, 0),
  });
  const days = [
    day('2026-08-05', [
      entry('journal_1', 4821, '2026-08-05', 'Provisão de despesa — Construtora Horizonte, NF 12345', [
        line('ledgacct_1', '4.2.05.05.020', 'Despesas com pessoal', 'Debit', 4500),
        line('ledgacct_2', '2.1.05.05.010', 'Fornecedores a pagar', 'Credit', 4500),
      ]),
    ]),
    day('2026-08-12', [
      entry('journal_2', 4822, '2026-08-12', 'Baixa de pagamento — Construtora Horizonte', [
        line('ledgacct_2', '2.1.05.05.010', 'Fornecedores a pagar', 'Debit', 4500),
        line('ledgacct_3', '1.2.05.15.015', 'Banco Conta Movimento', 'Credit', 4500),
      ]),
    ]),
  ];
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Accounting');
  // Rotas específicas escopadas a /api (precedem o catch-all) — ver memória pr-shots-mock-api-path-scope.
  await ctx.route('**/api/companies*', (r) =>
    json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
  );
  await ctx.route('**/api/units*', (r) =>
    json(r, [{ id: 'unit_1', companyId: 'comp_1', name: 'Campus Canoas', acronym: 'CAN', active: true }]),
  );
  await ctx.route('**/api/accounting/journal-book*', (r) =>
    json(r, {
      companyId: null,
      from: '2026-08-01',
      to: '2026-08-31',
      days,
      totalDebit: days.reduce((a, d) => a + d.totalDebit, 0),
      totalCredit: days.reduce((a, d) => a + d.totalCredit, 0),
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/accounting/journal-book`, { waitUntil: 'domcontentloaded' });
  await page
    .getByRole('heading', { name: 'Livro Diário' })
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(700);
  await shot(page, '52-livro-diario-sem-filtro-empresa');
  await ctx.close();
}

// 53) Pendências › Nota Fiscal — a badge da aba tem de contar o que a aba LISTA. O resumo devolve os
// dois buckets: o global (`fiscalDocumentPending`, que abrange também tributário/pago/conciliado) e o
// escopado à contabilização (`fiscalDocumentPendingAccounting`). O mock separa os dois de propósito —
// 3 no global, 1 no escopado — para o print provar qual deles a badge está lendo.
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const bucket = (count, totalAmount) => ({ count, totalAmount, netTotalAmount: null });
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 760 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  // Controllership: payables:approve é o que libera Pendências no menu. E a empresa precisa ser semeada —
  // sem isso o seletor cai em "Todas as empresas" (consolidado), e a tela renderiza o vazio "Nenhuma empresa".
  await ctx.addInitScript(() => localStorage.setItem('ulbra-one.empresaId', 'comp_1'));
  await mockApi(ctx, 'Controllership');
  await ctx.route('**/api/companies*', (r) =>
    json(r, [{ id: 'comp_1', legalName: 'ULBRA S.A.', cnpj: '92741990000199' }]),
  );
  await ctx.route('**/api/payables/stage-summary*', (r) =>
    json(r, {
      entry: bucket(0, 0),
      classification: bucket(0, 0),
      approval: bucket(0, 0),
      accounting: bucket(1, 4500),
      taxAnalysis: bucket(1, 9800),
      payment: bucket(1, 3200),
      reconciled: bucket(0, 0),
      fiscalDocumentPending: bucket(3, 17500),
      fiscalDocumentPendingAccounting: bucket(1, 4500),
    }),
  );
  await ctx.route('**/api/payables/stage-items*', (r) =>
    json(r, {
      items: [
        {
          payableId: 'payable_00000000000000000000000000000001',
          companyId: 'comp_1',
          counterpartyName: 'Construtora Horizonte Ltda',
          description: 'Manutenção preventiva do ar-condicionado — agosto',
          costCenterId: 'costcenter_00000000000000000000000000000001',
          categoryId: null,
          firstDueDate: '2026-09-10',
          installmentCount: 1,
          totalAmount: 4500,
          totalWithheld: 0,
          netTotal: 4500,
          status: 'PendingAccounting',
          origin: 'Manual',
          hasFiscalDocument: false,
          accountingAccountId: null,
          suggestedAccountingAccountId: null,
          classificationConfidence: null,
          classificationRationale: null,
          originRequisitionCode: null,
        },
      ],
      page: 1,
      pageSize: 20,
      total: 1,
      totalPages: 1,
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/pendencies?tab=fiscal`, { waitUntil: 'domcontentloaded' });
  await page
    .getByRole('heading', { name: 'Pendências' })
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(700);
  await shot(page, '53-pendencias-nota-fiscal');
  await ctx.close();
}

// ===== 54 — Caixa de entrada de notificações (claro, escuro e celular) =====
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const now = Date.now();
  const ago = (ms) => new Date(now - ms).toISOString();
  const MIN = 60_000;
  const H = 3_600_000;
  const D = 86_400_000;
  // Os agrupadores são por dia do calendário, então rodar o script de madrugada jogaria os itens
  // "de hoje" para dentro de ontem e o print sairia sem o grupo Hoje. Encurta o recuo para caber
  // no que já se passou desde a meia-noite — o print fica igual a qualquer hora do dia.
  const sinceMidnight = now - new Date().setHours(0, 0, 0, 0);
  const todayAgo = (ms) => new Date(now - Math.min(ms, Math.max(sinceMidnight - MIN, 0))).toISOString();
  // Um exemplar de cada tom do catálogo (aguardando / aprovado / exige ação / cancelado-SLA),
  // lidos e não lidos, cobrindo os quatro agrupadores de dia.
  const items = [
    {
      id: 'notif_1',
      type: 'RequisitionTriageSlaOverdue',
      title: 'SLA de triagem estourado — Solicitação #4',
      message: 'A solicitação está há mais de 8h úteis aguardando triagem.',
      referenceId: 'req_4',
      occurredAt: todayAgo(12 * MIN),
      readAt: null,
    },
    {
      id: 'notif_2',
      type: 'RequisitionAwaitingApproval',
      title: 'Solicitação #4 aguardando aprovação',
      message: 'Uma solicitação foi triada e aguarda aprovação.',
      referenceId: 'req_4',
      occurredAt: todayAgo(2 * H),
      readAt: null,
    },
    {
      id: 'notif_3',
      type: 'RequisitionAwaitingTriage',
      title: 'Solicitação #4 aguardando triagem',
      message: 'Uma solicitação foi enviada e aguarda triagem.',
      referenceId: 'req_4',
      occurredAt: todayAgo(2 * H + 4 * MIN),
      readAt: null,
    },
    {
      id: 'notif_4',
      type: 'RequisitionApproved',
      title: 'Solicitação #7 aprovada para pagamento',
      message: 'Sua solicitação foi aprovada e seguirá para pagamento.',
      referenceId: 'req_7',
      occurredAt: ago(D),
      readAt: null,
    },
    {
      id: 'notif_5',
      type: 'RequisitionFiscalDocumentRequested',
      title: 'Solicitação #7: envie a Nota Fiscal',
      message:
        'O Contábil solicitou a nota fiscal deste título. Anexe a NF na solicitação para dar seguimento ao pagamento.',
      referenceId: 'req_7',
      occurredAt: ago(D + 3 * H),
      readAt: null,
    },
    {
      id: 'notif_6',
      type: 'RequisitionReturnedForComplement',
      title: 'Solicitação #6 devolvida para complemento',
      message: 'Sua solicitação foi devolvida para complemento. Motivo: falta o centro de custo da unidade.',
      referenceId: 'req_6',
      occurredAt: ago(D + 6 * H),
      readAt: ago(D),
    },
    {
      id: 'notif_7',
      type: 'BudgetAdjustmentAwaitingTriage',
      title: 'Adequação #12 aguardando triagem',
      message: 'Uma adequação orçamentária foi criada e aguarda triagem.',
      referenceId: 'adeq_12',
      occurredAt: ago(2 * D),
      readAt: ago(D),
    },
    {
      id: 'notif_8',
      type: 'RequisitionCancelled',
      title: 'Solicitação #3 cancelada',
      message: 'Sua solicitação foi cancelada. Motivo: duplicidade com a solicitação #2.',
      referenceId: 'req_3',
      occurredAt: ago(3 * D),
      readAt: ago(2 * D),
    },
    {
      id: 'notif_9',
      type: 'BudgetAdjustmentReversed',
      title: 'Adequação #9 estornada',
      message: 'Sua adequação orçamentária aprovada foi estornada.',
      referenceId: 'adeq_9',
      occurredAt: ago(9 * D),
      readAt: ago(8 * D),
    },
  ];

  for (const [name, viewport, theme, mobile] of [
    ['54-notificacoes-claro', { width: 1360, height: 900 }, 'light', false],
    ['55-notificacoes-escuro', { width: 1360, height: 900 }, 'dark', false],
    ['56-notificacoes-celular', { width: 390, height: 900 }, 'light', true],
  ]) {
    const ctx = await browser.newContext({
      viewport,
      deviceScaleFactor: 2,
      ...(mobile ? { isMobile: true, hasTouch: true } : {}),
    });
    await blockExternal(ctx);
    // O tema é lido do localStorage no primeiro render; semeie junto com o token.
    await ctx.addInitScript(
      ([token, t]) => {
        localStorage.setItem('ulbra-one.token', token);
        localStorage.setItem('ulbra-one.theme', t);
      },
      [FAKE_JWT, theme],
    );
    await mockApi(ctx, 'Controllership');
    await ctx.route('**/api/notifications*', (r) =>
      json(r, { items, page: 1, pageSize: 20, totalCount: items.length, totalPages: 1 }),
    );
    await ctx.route('**/api/notifications/unread-count*', (r) => json(r, { count: 5 }));
    const page = await ctx.newPage();
    await page.goto(`${BASE}/app/notificacoes`, { waitUntil: 'domcontentloaded' });
    await page
      .getByRole('heading', { name: 'Notificações' })
      .first()
      .waitFor({ timeout: 8000 })
      .catch(() => {});
    await page.waitForTimeout(700);
    await shot(page, name);
    await ctx.close();
  }
}

// 57) Auditoria — a coluna "Autor" com a foto de quem fez a alteração (e as iniciais de quem não tem).
{
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  const ctx = await browser.newContext({ viewport: { width: 1360, height: 900 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  await mockApi(ctx, 'Admin');
  const actors = [
    { id: 'user_3fa85f6457174219973000000000000a', name: 'Ana Contábil', pictureUrl: AVATAR_1 },
    { id: 'user_00000000000000000000000000000001', name: 'Bruno Financeiro', pictureUrl: AVATAR_2 },
    { id: 'user_00000000000000000000000000000002', name: 'Carla Tesouraria', pictureUrl: null },
  ];
  const log = (i, actorUserId, entityType, action) => ({
    id: `auditlog_0000000000000000000000000000000${i}`,
    occurredAt: `2026-08-2${i}T13:0${i}:00Z`,
    actorUserId,
    action,
    entityType,
    schema: 'finance',
    tableName: 'payables',
    primaryKey: `payable_0000000000000000000000000000000${i}`,
    oldValues: null,
    newValues: null,
    affectedColumns: null,
  });
  await ctx.route('**/api/users', (r) => json(r, actors));
  await ctx.route('**/api/audit-logs*', (r) =>
    json(r, {
      items: [
        log(1, '3fa85f64-5717-4219-9730-00000000000a', 'Payable', 'Update'),
        log(2, '00000000-0000-0000-0000-000000000001', 'Payment', 'Create'),
        log(3, '00000000-0000-0000-0000-000000000002', 'Requisition', 'Update'),
        log(4, 'system', 'Payable', 'Create'),
      ],
      page: 1,
      pageSize: 20,
      totalCount: 4,
      totalPages: 1,
    }),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/settings/audit`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Ana Contábil')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(600);
  await shot(page, '57-auditoria-autor');
  await ctx.close();
}

// 58) Fila de triagem (Controllership) — o aside com os tiles de KPI e o orçamento por natureza.
// Cobre o range que o formatador precisa aguentar: milhares exatos, milhões e bilhões abreviados.
// Dois recortes de magnitude: a fila em milhares (o caso do bug — "R$ 42.500,00" quebrava em duas
// linhas) e a fila em bilhões (o caso que nenhum tamanho de fonte comporta exato).
for (const v of [
  { name: '58-triagem-kpis-milhares', factor: 1 },
  { name: '58b-triagem-kpis-bilhoes', factor: 100_000 },
]) {
  const ctx = await browser.newContext({ viewport: { width: 1440, height: 1000 }, deviceScaleFactor: 2 });
  await blockExternal(ctx);
  await ctx.addInitScript(seedToken, FAKE_JWT);
  // O aside só renderiza com UMA empresa escolhida — Controllership vê o consolidado por padrão, e aí
  // `empresa` é null. Semear a seleção no localStorage faz o store manter a escolha já válida.
  await ctx.addInitScript(() =>
    localStorage.setItem('ulbra-one.empresaId', 'company_00000000000000000000000000000001'),
  );
  await mockApi(ctx, 'Controllership');
  const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });
  await ctx.route('**/api/companies', (r) =>
    json(r, [
      {
        id: 'company_00000000000000000000000000000001',
        legalName: 'Universidade Luterana do Brasil',
        cnpj: '88332580000165',
        active: true,
      },
    ]),
  );
  const req = (i, value, description, type) => ({
    id: `requisition_0000000000000000000000000000000${i}`,
    code: 1000 + i,
    companyId: 'company_00000000000000000000000000000001',
    unitId: 'unit_00000000000000000000000000000001',
    costCenterId: `cost_center_0000000000000000000000000000000${i}`,
    projectId: null,
    vendorId: 'vendor_00000000000000000000000000000001',
    type,
    description,
    observation: null,
    estimateValue: value * v.factor,
    estimateType: 'Estimado',
    startCompetency: '2026-08',
    endCompetency: null,
    paymentFrequency: 'Unica',
    paymentInstallmentsCount: null,
    firstDueDate: '2026-09-10',
    issueDate: null,
    totalCost: value * v.factor,
    realizedValue: 0,
    status: 'Triage',
    attachments: [],
    installments: [],
    cycles: [],
    basePayableId: null,
    baseRealizedAmount: 0,
    transitions: [],
    createdAt: '2026-08-20T12:00:00Z',
    createdBy: '00000000-0000-0000-0000-000000000001',
    statusChangedAt: '2026-08-20T12:00:00Z',
    fiscalDocumentPending: false,
    vendorName: 'Construtora Meridiano Ltda',
  });
  const items = [
    req(1, 12_845, 'Reforma do bloco B — estrutura e fachada', 'Purchase'),
    req(2, 42_500, 'Campanha institucional de vestibular', 'ServiceProvision'),
    req(3, 84_500, 'Plano diretor do campus — obras plurianuais', 'Project'),
  ];
  await ctx.route('**/api/requisitions?*', (r) =>
    json(r, { items, page: 1, pageSize: 50, total: items.length, totalPages: 1 }),
  );
  // Cada linha da fila consulta o painel por requisição (`useBudgetBadge`); sem esta rota o catch-all
  // devolve `[]` e `budgetBadgeFor` quebra em `data.breaches.some`.
  await ctx.route('**/api/requisitions/*/budget-control*', (r) =>
    json(r, { months: [], breaches: [], unbudgeted: [], candidateExceeds: false }),
  );
  await ctx.route('**/api/requisitions/budget-controls*', (r) =>
    json(r, {
      controls: Object.fromEntries(
        items.map((i) => [i.id, { months: [], breaches: [], unbudgeted: [], candidateExceeds: false }]),
      ),
    }),
  );
  // Projeção da fila no topo do aside (`showQueueProjection`); o catch-all `[]` não tem `.months`.
  await ctx.route('**/api/requisitions/budget-projection*', (r) =>
    json(r, {
      months: [
        {
          competency: '2026-08',
          orcado: 94_250 * v.factor,
          consumed: 43_000 * v.factor,
          forecast: 12_845 * v.factor,
          budgeted: true,
        },
        {
          competency: '2026-09',
          orcado: 94_250 * v.factor,
          consumed: 51_000 * v.factor,
          forecast: 84_500 * v.factor,
          budgeted: true,
        },
        {
          competency: '2026-10',
          orcado: 94_250 * v.factor,
          consumed: 62_000 * v.factor,
          forecast: 9_000 * v.factor,
          budgeted: true,
        },
      ],
      breaches: ['2026-09'],
      queueTotal: 139_845 * v.factor,
    }),
  );
  await ctx.route('**/api/requisitions/budget-summary*', (r) =>
    json(r, [
      {
        costCenterId: 'cost_center_00000000000000000000000000000001',
        costCenterName: 'Comunicação',
        orcado: 25_000 * v.factor,
        realized: 0,
        committed: 0,
        forecast: 0,
        consumed: 0,
        available: 25_000 * v.factor,
        budgeted: true,
      },
      {
        costCenterId: 'cost_center_00000000000000000000000000000002',
        costCenterName: 'Obras e manutenção',
        orcado: 94_000 * v.factor,
        realized: 31_000 * v.factor,
        committed: 12_000 * v.factor,
        forecast: 8_000 * v.factor,
        consumed: 43_000 * v.factor,
        available: 51_000 * v.factor,
        budgeted: true,
      },
    ]),
  );
  const page = await ctx.newPage();
  await page.goto(`${BASE}/app/requisitions/triage`, { waitUntil: 'domcontentloaded' });
  await page
    .getByText('Valor na fila')
    .first()
    .waitFor({ timeout: 8000 })
    .catch(() => {});
  await page.waitForTimeout(800);
  await shot(page, v.name);
  await ctx.close();
}

await browser.close();
console.log('done →', OUT);
