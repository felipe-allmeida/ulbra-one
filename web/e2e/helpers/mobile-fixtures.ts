import { test as base, type BrowserContext, type Page, type Route } from '@playwright/test';

/** Viewport de referência: iPhone 14/15 em retrato. */
export const PHONE = { width: 390, height: 844 } as const;
/** Task 18 fix-round 1: cobre a faixa 641-1023px, cega até aqui — a trava só olhava 390px e o
 *  card do ResponsiveTable só entra em cena até 640px, então de 641 a 1023px a tabela desktop
 *  inteira nunca era vista por nada neste projeto. iPad em retrato é a largura mais comum
 *  nessa faixa. */
export const TABLET = { width: 768, height: 1024 } as const;

const json = (r: Route, body: unknown) =>
  r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

// O brief da Task 4 dá estes valores verbatim, mas alguns nomes de campo e formatos de envelope
// no brief não batem com o contrato real (`web/src/api.ts` + `web/src/api/schema.d.ts`) — usá-los
// como estão faria a tela cair num crash (ErrorBoundary) ou num empty state, o falso-negativo que
// esta task existe para eliminar. Os valores (nomes, descrições, montantes) são os do brief; os
// nomes de campo e envelopes de paginação foram corrigidos contra o schema gerado.
export const COMPANIES = [
  {
    id: 'company_00000000000000000000000000000001',
    legalName: 'UNIVERSIDADE LUTERANA DO BRASIL — CAMPUS CANOAS',
    cnpj: '88332580000123',
    // CompanyResponse exige `active`; sem ele, o filtro `.filter(c => c.active)` da tela de
    // Usuários (src/pages/users.tsx:68) zera a lista e cai no empty state "Nenhuma empresa".
    active: true,
  },
];

/** Nomes e valores longos de propósito: a largura é o que está sob teste. */
export const PAYABLES = {
  items: [
    {
      payableId: 'payable_00000000000000000000000000000001',
      companyId: COMPANIES[0].id,
      description: 'Manutenção predial preventiva e corretiva — bloco C, 3º pavimento',
      counterpartyName: 'CONSTRUTORA HORIZONTE EMPREENDIMENTOS LTDA ME',
      costCenterId: 'costcenter_00000000000000000000000000000001',
      categoryId: null,
      firstDueDate: '2026-09-15',
      installmentCount: 12,
      totalAmount: 187402.31,
      totalWithheld: 9371.12,
      netTotal: 178031.19,
      status: 'PendingAccounting',
      origin: 'Invoice',
      hasFiscalDocument: false,
      accountingAccountId: null,
      suggestedAccountingAccountId: null,
      classificationConfidence: null,
      classificationRationale: null,
      originRequisitionCode: null,
    },
    {
      payableId: 'payable_00000000000000000000000000000002',
      companyId: COMPANIES[0].id,
      description: 'Licenciamento de software corporativo — renovação anual',
      counterpartyName: 'MICROSOFT INFORMÁTICA DO BRASIL LTDA',
      costCenterId: 'costcenter_00000000000000000000000000000002',
      categoryId: null,
      firstDueDate: '2026-09-18',
      installmentCount: 1,
      totalAmount: 1248900.0,
      totalWithheld: 0,
      netTotal: 1248900.0,
      status: 'Scheduled',
      origin: 'Invoice',
      hasFiscalDocument: true,
      accountingAccountId: null,
      suggestedAccountingAccountId: null,
      classificationConfidence: null,
      classificationRationale: null,
      originRequisitionCode: null,
    },
  ],
  page: 1,
  pageSize: 20,
  total: 2,
  totalPages: 1,
};

/** `PayableStageSummaryResponse`: um bucket por estágio — sem ele, o dashboard de cards de
 * `PayablesLayout` (src/pages/payables/stages.tsx:61) crasha lendo `.count` de undefined. */
export const PAYABLE_STAGE_SUMMARY = {
  entry: { count: 2, totalAmount: 187402.31, netTotalAmount: null },
  classification: { count: 1, totalAmount: 42500.0, netTotalAmount: null },
  approval: { count: 1, totalAmount: 96300.5, netTotalAmount: null },
  accounting: { count: 1, totalAmount: 187402.31, netTotalAmount: null },
  taxAnalysis: { count: 1, totalAmount: 187402.31, netTotalAmount: null },
  payment: { count: 1, totalAmount: 1248900.0, netTotalAmount: 1248900.0 },
  reconciled: { count: 1, totalAmount: 1248900.0, netTotalAmount: 1248900.0 },
  fiscalDocumentPending: { count: 1, totalAmount: 187402.31, netTotalAmount: null },
};

/** `PaymentQueueSummary` — sem isto, o aside do Financeiro (TreasuryAside) crasha em
 * `summary.groups.find(...)` (`.groups` vem undefined de um `[]` do catch-all). */
export const PAYMENT_QUEUE_SUMMARY = {
  asOf: '2026-08-27',
  overdueCount: 3,
  overdueNetTotal: 245890.44,
  groups: [
    { dueDate: '2026-08-27', count: 2, netTotal: 178031.19 },
    { dueDate: '2026-09-03', count: 1, netTotal: 1248900.0 },
  ],
  totalCount: 6,
  totalNetTotal: 1672821.63,
};

/** `PeriodSummaryResponse` — a mesma tela lê `period.suggested.count`/`.unmatched.count`. */
export const PERIOD_SUMMARY = {
  from: '2026-08-01',
  to: '2026-08-31',
  reconciled: { count: 148, total: 987654.32 },
  suggested: { count: 12, total: 45890.5 },
  unmatched: { count: 4, total: 8320.15 },
  ignored: { count: 2, total: 1200.0 },
  debitTotal: 248390.75,
  creditTotal: 312400.0,
};

// VendorResponse usa `name` (não `legalName`) e a lista vem paginada (`Paginated<Vendor>`), não um
// array solto — src/pages/fornecedores.tsx:41 lê `list.data?.items`.
export const VENDORS = {
  items: [
    {
      id: 'vendor_00000000000000000000000000000001',
      personType: 'Company',
      name: 'CONSTRUTORA HORIZONTE EMPREENDIMENTOS LTDA ME',
      tradeName: 'Horizonte Construções',
      document: '12345678000199',
      email: 'financeiro@construtorahorizonte.com.br',
      phone: '5133451234',
      active: true,
    },
  ],
  page: 1,
  pageSize: 20,
  total: 1,
  totalPages: 1,
};

// TrialBalanceResponse usa `rows` (não `lines`) e cada linha usa `totalDebit`/`totalCredit`/
// `balance` (não `debit`/`credit`/`closingBalance`) — src/pages/balancete/table.tsx:63-83.
export const TRIAL_BALANCE = {
  companyId: COMPANIES[0].id,
  from: '2026-08-01',
  to: '2026-08-31',
  costCenterId: null,
  rows: [
    {
      ledgerAccountId: 'ledgacct_00000000000000000000000000000001',
      accountCode: '1.1.01.001.0001',
      reducedCode: '11001',
      accountName: 'Caixa Geral — Tesouraria Campus Canoas',
      accountGroup: 'Asset',
      normalBalance: 'Debit',
      openingBalance: 1284900.55,
      totalDebit: 987654.32,
      totalCredit: 456789.01,
      balance: 1815765.86,
    },
  ],
  totalDebit: 987654.32,
  totalCredit: 456789.01,
  isBalanced: true,
};

// AdminUser exige isAdmin/canApproveBudgetAdjustment/pendingFirstLogin/companyIds/units/costCenters
// — src/pages/users.tsx:90-92 faz `.map()` sobre `units`/`costCenters` num efeito que roda assim
// que a lista chega, e undefined ali quebra a tela inteira.
export const USERS = [
  {
    id: 'user_00000000000000000000000000000002',
    name: 'Maria Aparecida do Nascimento Silva',
    email: 'redacted-12@example.invalid',
    role: 'Controllership',
    isAdmin: false,
    active: true,
    canApproveBudgetAdjustment: true,
    pendingFirstLogin: false,
    companyIds: [COMPANIES[0].id],
    units: [],
    costCenters: [],
  },
];

// RequisitionResponse (Paginated) — não listada no Step 1 do brief, mas /app/requisitions cai no
// catch-all `[]` sem ela e passa trivialmente por estado vazio (o mesmo defeito que esta task
// existe para eliminar). Nomes longos + tipo recorrente (FixedExpense) para estourar as 8 colunas.
export const REQUISITIONS = {
  items: [
    {
      id: 'req_00000000000000000000000000000001',
      code: 4821,
      companyId: COMPANIES[0].id,
      unitId: 'unit_00000000000000000000000000000001',
      costCenterId: 'costcenter_00000000000000000000000000000001',
      projectId: null,
      vendorId: 'vendor_00000000000000000000000000000001',
      vendorName: 'CONSTRUTORA HORIZONTE EMPREENDIMENTOS LTDA ME',
      type: 'FixedExpense',
      description: 'Locação de impressoras multifuncionais — contrato anual todos os campi',
      observation: null,
      estimateValue: 284750.9,
      estimateType: 'Monthly',
      startCompetency: '2026-08',
      endCompetency: null,
      paymentFrequency: 'Monthly',
      paymentInstallmentsCount: 12,
      firstDueDate: '2026-09-10',
      issueDate: null,
      totalCost: 0,
      realizedValue: 0,
      status: 'Draft',
      fiscalDocumentPending: false,
      attachments: [],
      installments: [],
      cycles: [],
      basePayableId: null,
    },
  ],
  page: 1,
  pageSize: 20,
  total: 1,
  totalPages: 1,
};

const PERMS = [
  'payables:read',
  'payables:write',
  'payables:pay',
  'payables:approve',
  'receivables:read',
  'receivables:write',
  'treasury:read',
  'treasury:write',
  'registry:read',
  'registry:write',
  'core:read',
  'core:write',
  'reports:read',
  'accounting:read',
  'fiscal:read',
  'audit:read',
  'projects:read',
  'requisitions:read',
  'requisitions:write',
  'requisitions:approve',
  'requisitions:triage',
];

const ME = {
  userId: 'user_00000000000000000000000000000001',
  name: 'Admin Dev',
  email: 'admin@ulbra.local',
  role: 'Admin',
  isAdmin: true,
  permissions: PERMS,
};

/**
 * `main.tsx` limpa o token guardado quando `auth.hasValidToken()` é falso (decodifica o JWT e
 * confere `exp`), então um placeholder seria descartado antes do app renderizar e o teste cairia
 * em /login. Semeia um JWT sem assinatura com exp distante — a API está mockada, a assinatura
 * nunca é verificada.
 */
function fakeJwt() {
  const b64 = (o: unknown) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const exp = Math.floor(Date.now() / 1000) + 60 * 60 * 24 * 365;
  return `${b64({ alg: 'none', typ: 'JWT' })}.${b64({ sub: 'mobile-guard', exp })}.sig`;
}

/** Same mocking (routes + auth seed) either fixture needs — extracted so `mobilePage` (390px) and
 *  `tabletPage` (768px, Task 18 fix-round 1) can't drift apart on what they mock. */
/** Exportada para que uma verificação de desktop possa montar seu próprio contexto na largura que
 *  quiser — as fixtures abaixo fixam 390/768px. */
export async function seedContext(ctx: BrowserContext) {
  // Hosts externos (fontes/Clarity) pendurariam o networkidle.
  await ctx.route(/googleapis|gstatic|accounts\.google|clarity\.ms/, (r) => r.abort());
  // Catch-all primeiro: o Playwright testa rotas na ordem inversa ao registro, então as rotas
  // específicas abaixo (e /auth/me, por último) precisam ser registradas DEPOIS para vencer
  // (mesma ordem de scripts/pr-shots.mjs:94-95).
  await ctx.route('**/api/**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await ctx.route('**/api/companies*', (r) => json(r, COMPANIES));
  // `?` no glob do Playwright casa UM caractere qualquer, não um "?" literal — então
  // `**/api/payables?*` também bate com `/payables/stage-items?...` etc (o "?" absorve a barra).
  // Registrar o padrão mais largo PRIMEIRO garante que os específicos abaixo (registrados depois)
  // vençam para as próprias URLs, na mesma regra de "o último que casar vence".
  await ctx.route('**/api/payables?*', (r) => json(r, PAYABLES));
  // PaymentQueueTab (Financeiro → Fila de Pagamentos) reads the SAME `/payables` endpoint through
  // `usePayables`, not `usePayableStageItems` — a different response DTO (`PayableListItemResponse`:
  // `dueDate`/`installmentId` per installment) than `PAYABLES` above (`PayableStageItemResponse`:
  // `firstDueDate`, no `installmentId`). Serving `PAYABLES` here throws inside `daysUntil` reading
  // `l.dueDate` off a shape that doesn't carry it. An empty queue is a real, valid state and avoids
  // inventing a second parallel fixture for a screen this suite otherwise never renders.
  await ctx.route('**/api/payables?*paid=false*', (r) =>
    json(r, { items: [], total: 0, totalPages: 0, page: 1, pageSize: 50 }),
  );
  await ctx.route('**/api/payables/stage-items*', (r) => json(r, PAYABLES));
  await ctx.route('**/api/payables/stage-summary*', (r) => json(r, PAYABLE_STAGE_SUMMARY));
  await ctx.route('**/api/payables/payment-queue-summary*', (r) => json(r, PAYMENT_QUEUE_SUMMARY));
  await ctx.route('**/api/bank-transactions/period-summary*', (r) => json(r, PERIOD_SUMMARY));
  await ctx.route('**/api/vendors*', (r) => json(r, VENDORS));
  await ctx.route('**/api/accounting/trial-balance*', (r) => json(r, TRIAL_BALANCE));
  await ctx.route('**/api/users*', (r) => json(r, USERS));
  await ctx.route('**/api/requisitions?*', (r) => json(r, REQUISITIONS));
  // /auth/me depois de todas, senão o catch-all de /api/** o engole.
  await ctx.route('**/auth/me', (r) =>
    r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(ME) }),
  );
  await ctx.addInitScript(
    ([t, c]: [string, string]) => {
      localStorage.setItem('ulbra-one.token', t);
      // Chave real é `.empresaId` (ver src/store.tsx) — o brief da Task 4 dizia `.empresa`,
      // que não bate com nenhuma leitura do app e deixaria a tela em "Nenhuma empresa".
      localStorage.setItem('ulbra-one.empresaId', c);
    },
    [fakeJwt(), COMPANIES[0].id],
  );
}

export const mobileTest = base.extend<{ mobilePage: Page; tabletPage: Page }>({
  mobilePage: async ({ browser }, use) => {
    const ctx = await browser.newContext({ viewport: PHONE, isMobile: true, hasTouch: true });
    await seedContext(ctx);
    const page = await ctx.newPage();
    // `use` is Playwright's fixture-setup callback, not a React Hook — the eslint-plugin-react-hooks
    // rule pattern-matches on the name and flags every Playwright fixture in the project this way.
    // eslint-disable-next-line react-hooks/rules-of-hooks
    await use(page);
    await ctx.close();
  },
  // 768px, no touch emulation — a tablet/narrow-laptop window, not a phone. This is the width band
  // (641-1023px) where ResponsiveTable renders its full desktop `<table>` (card mode ends at 640px)
  // and nothing else in this project ever looked at it before Task 18 fix-round 1.
  tabletPage: async ({ browser }, use) => {
    const ctx = await browser.newContext({ viewport: TABLET });
    await seedContext(ctx);
    const page = await ctx.newPage();
    // eslint-disable-next-line react-hooks/rules-of-hooks
    await use(page);
    await ctx.close();
  },
});

export { expect } from '@playwright/test';
