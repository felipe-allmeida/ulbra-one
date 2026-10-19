#!/usr/bin/env node
// Task 18 — wide sweep across every route in `App.tsx` at 390px and 768px, reporting layout
// containers that scroll sideways. `e2e/mobile-layout.spec.ts` is the CI-blocking guard and only
// checks 8 hand-picked routes; this script walks the other ~70 as a reporting tool (no assertions,
// exits 0 regardless) so a human can look at what the guard's narrow route list misses.
//
// Usage: start the dev server (`pnpm --dir web dev`, port 5199) then `pnpm --dir web mobile:audit`.
//
// Known scope limits (read before trusting a clean result):
//  1. Routes with a required `:id`/`:sectionId` get a plausible fixture id (see ID_FIXTURES below).
//     A handful of detail endpoints have a matching object-shaped mock (payables/vendors/requisitions);
//     the rest fall through to the blanket `**/api/**` → `[]` mock. Since this codebase consistently
//     guards query data with `?? null` + an early "not found" return, an array-shaped response for an
//     endpoint that expects an object usually renders a benign "not found" empty state rather than
//     crashing — but it is NOT the same as auditing the page with real data, so a "clean" result on
//     one of those routes only means "the chrome around an empty/not-found state doesn't scroll
//     sideways", not "this screen never overflows with real content".
//  2. A render crash (caught by the root ErrorBoundary, `.error-fallback`) replaces the ENTIRE app,
//     including `.topbar`/`.panel` — so a crashed route trivially has zero LAYOUT_CONTAINERS and would
//     silently "pass" if we only checked for offenders. This script checks for `.error-fallback` (and
//     listens for uncaught page errors) FIRST and reports CRASHED instead of a false clean result.
//  3. `/` and `/login` render outside the authenticated app chrome (`.lp-*` / their own markup, not
//     `.content`/`.panel`/etc.) — they're visited for completeness but have no monitored containers.
//  4. This script does not replace looking at the app with your own eyes (per the task brief: overlap,
//     clipping, and loading/error states need a human, not `scrollWidth`).
import { chromium } from '@playwright/test';

const BASE = process.env.SHOT_BASE ?? 'http://localhost:5199';
const VIEWPORTS = [
  { width: 390, height: 844, label: '390px' },
  { width: 768, height: 1024, label: '768px' },
];

const json = (r, body) => r.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(body) });

/* ============ Fixtures ============
 * Duplicated (not imported) from `e2e/helpers/mobile-fixtures.ts` on purpose: that file is
 * TypeScript and this script has no TS pipeline (see file header). Keep the two in sync by hand —
 * this comment is the pointer for whoever edits one and forgets the other. */
const COMPANIES = [
  {
    id: 'company_00000000000000000000000000000001',
    legalName: 'UNIVERSIDADE LUTERANA DO BRASIL — CAMPUS CANOAS',
    cnpj: '88332580000123',
    active: true,
  },
];

const PAYABLES = {
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
      installments: [],
      withholdings: [],
      attachments: [],
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
      installments: [],
      withholdings: [],
      attachments: [],
    },
  ],
  page: 1,
  pageSize: 20,
  total: 2,
  totalPages: 1,
};

// `PayableListItemResponse` shape (GET /api/payables — the raw installment queue), distinct from
// `PayableStageItemResponse` (GET /api/payables/stage-items, the esteira/by-stage view) above. Adding
// this was needed to reach /app/treasury: its queue grouping (queue-groups.ts, payment-periods.ts)
// does date math straight on `dueDate`/`netAmount` with no optional chaining — PAYABLES above (the
// stage-item shape) has neither field, so those crashed with a bare TypeError until this existed.
const QUEUE_ITEMS = {
  items: [
    {
      payableId: 'payable_00000000000000000000000000000001',
      installmentId: 'installment_00000000000000000000000000000001',
      sequence: 1,
      installmentCount: 1,
      counterpartyName: 'CONSTRUTORA HORIZONTE EMPREENDIMENTOS LTDA ME',
      description: 'Manutenção predial preventiva e corretiva — bloco C, 3º pavimento',
      costCenterId: 'costcenter_00000000000000000000000000000001',
      categoryId: null,
      dueDate: '2026-09-15',
      amount: 187402.31,
      netAmount: 178031.19,
      installmentStatus: 'Scheduled',
      payableStatus: 'Scheduled',
      scheduledPaymentDate: '2026-09-15',
      isHeld: false,
      holdReason: null,
      remittanceId: null,
      remittanceSequence: null,
      rail: 'Manual',
      manualReason: 'BankNotSupported',
      vendorDocument: '12345678000199',
      vendorId: 'vendor_00000000000000000000000000000001',
      effectiveMethod: 'Boleto',
      boletoDigitableLine: null,
      documentNumber: null,
      issueDate: '2026-08-15',
      originUnitId: null,
      hasFiscalDocument: false,
    },
  ],
  page: 1,
  pageSize: 20,
  total: 1,
  totalPages: 1,
};

const PAYABLE_STAGE_SUMMARY = {
  entry: { count: 2, totalAmount: 187402.31, netTotalAmount: null },
  classification: { count: 1, totalAmount: 42500.0, netTotalAmount: null },
  approval: { count: 1, totalAmount: 96300.5, netTotalAmount: null },
  accounting: { count: 1, totalAmount: 187402.31, netTotalAmount: null },
  taxAnalysis: { count: 1, totalAmount: 187402.31, netTotalAmount: null },
  payment: { count: 1, totalAmount: 1248900.0, netTotalAmount: 1248900.0 },
  reconciled: { count: 1, totalAmount: 1248900.0, netTotalAmount: 1248900.0 },
  fiscalDocumentPending: { count: 1, totalAmount: 187402.31, netTotalAmount: null },
};

const PAYMENT_QUEUE_SUMMARY = {
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

const PERIOD_SUMMARY = {
  from: '2026-08-01',
  to: '2026-08-31',
  reconciled: { count: 148, total: 987654.32 },
  suggested: { count: 12, total: 45890.5 },
  unmatched: { count: 4, total: 8320.15 },
  ignored: { count: 2, total: 1200.0 },
  debitTotal: 248390.75,
  creditTotal: 312400.0,
};

const VENDORS = {
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

const TRIAL_BALANCE = {
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
      openingBalance: 12000,
      totalDebit: 5000,
      totalCredit: 2000,
      closingBalance: 15000,
    },
  ],
  totalDebit: 5000,
  totalCredit: 2000,
  isBalanced: true,
};

// AdminUser needs isAdmin/canApproveBudgetAdjustment/pendingFirstLogin/companyIds/units/costCenters —
// users.tsx does a `.map()` over `units`/`costCenters` in an effect that runs as soon as the list
// arrives, and `undefined` there crashes the whole screen. Kept identical to mobile-fixtures.ts.
const USERS = [
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

const REQUISITIONS = {
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
      transitions: [],
    },
  ],
  page: 1,
  pageSize: 20,
  total: 1,
  totalPages: 1,
};

const WITHHOLDING_RULES = [
  {
    id: 'whrule_00000000000000000000000000000001',
    companyId: null,
    taxType: 'Irrf',
    lc116Subitem: '7.02',
    vendorRegime: 'LucroReal',
    municipalityIbge: '4304606',
    retains: true,
    reason: null,
    rateSource: 'Fixed',
    rate: 4.65,
    baseRule: 'GrossAmount',
    minimumAmount: 10,
    cprb: false,
    simplesAnexo: null,
    active: true,
  },
];

const JOURNAL_ENTRIES = [
  {
    id: 'journal_00000000000000000000000000000001',
    companyId: COMPANIES[0].id,
    number: 4821,
    competenceDate: '2026-07-31',
    description: 'Provisão de despesa — Construtora Horizonte Empreendimentos Ltda ME, NF 12345',
    origin: 'Manual',
    sourceReference: null,
    status: 'Draft',
    postedAt: null,
    reversalOfEntryId: null,
    reversedByEntryId: null,
    totalDebit: 4500,
    totalCredit: 4500,
    lines: [],
  },
];

// Task 18 fix-round 1: fixtures for routes that crashed in the first sweep pass purely from
// fixture-shape gaps in THIS script (not product bugs) — each one traced to its real schema type
// via `src/api/schema.d.ts` so the mock actually matches what the page reads.

/** ReceivableStageSummaryResponse (GET /receivables/stage-summary) — receivables/layout.tsx reads
 *  `summary[key].count` per bucket with no guard, same shape as PAYABLE_STAGE_SUMMARY above. */
const RECEIVABLE_STAGE_SUMMARY = {
  entry: { count: 0, totalAmount: 0 },
  receipt: { count: 0, totalAmount: 0 },
  reconciled: { count: 0, totalAmount: 0 },
};

/** ReceivableResponse (GET /receivables/{id}) — detail.tsx does `r.installments.some(...)` and the
 *  edit form spreads `initial.installments`, both unguarded. */
const RECEIVABLE_DETAIL = {
  id: 'receivable_00000000000000000000000000000001',
  companyId: COMPANIES[0].id,
  origin: 'Manual',
  payerId: 'customer_00000000000000000000000000000001',
  payerName: 'PREFEITURA MUNICIPAL DE CANOAS',
  documentNumber: null,
  issueDate: null,
  description: 'Convênio de estágio — agosto',
  costCenterId: 'costcenter_00000000000000000000000000000001',
  revenueCategoryId: null,
  ledgerAccountId: null,
  totalAmount: 18000,
  status: 'AwaitingReceipt',
  cancelReason: null,
  writeOffReason: null,
  createdAt: '2026-08-01T00:00:00Z',
  installments: [],
};

/** BudgetControlResponse — shared shape for the three requisition budget-control endpoints below
 *  (batch, dimension, per-id); all three crashed on `.months`/`.controls`/`[id]` being read off an
 *  array (the blanket `[]` catch-all) instead of an object. */
const BUDGET_CONTROL = { months: [], breaches: [], unbudgeted: [], candidateExceeds: false };

/** IncomeStatement (GET /accounting/income-statement) — contabil-dre.tsx does
 *  `dre.sections.length` unguarded. */
const INCOME_STATEMENT = {
  from: '2026-08-01',
  to: '2026-08-31',
  sections: [],
  grossRevenue: 0,
  revenueDeductions: 0,
  netRevenue: 0,
  costOfSales: 0,
  grossProfit: 0,
  otherOperatingIncome: 0,
  operatingExpenses: 0,
  operatingResult: 0,
  financialRevenue: 0,
  financialExpense: 0,
  financialResult: 0,
  otherResult: 0,
  preTaxResult: 0,
  incomeTax: 0,
  socialContribution: 0,
  netIncome: 0,
};

/** BalanceSheetResponse (GET /accounting/balance-sheet). */
const BALANCE_SHEET = {
  companyId: null,
  asOf: '2026-08-31',
  assets: [],
  liabilities: [],
  equity: [],
  periodResult: 0,
  totalAssets: 0,
  totalLiabilities: 0,
  totalEquity: 0,
  isBalanced: true,
};

/** JournalBookResponse (GET /accounting/journal-book). */
const JOURNAL_BOOK = { companyId: null, from: null, to: null, days: [], totalDebit: 0, totalCredit: 0 };

/** WithholdingsReportResponse (GET /payables/withholdings). */
const WITHHOLDINGS_REPORT = {
  totals: { grossTotal: 0, withheldTotal: 0, netTotal: 0, payableCount: 0, realizedTotal: 0, foreseenTotal: 0 },
  byTax: [],
  byPayable: [],
};

/** WithholdingAssessmentResponse (GET /fiscal/assessment) — feeds /app/accounting/obrigacoes. */
const FISCAL_ASSESSMENT = {
  perApur: '2026-08',
  companyId: null,
  totals: { baseTotal: 0, withheldTotal: 0, payableCount: 0 },
  byCompany: [],
  byRetentionGroup: [],
  byTaxType: [],
  byCostCenter: [],
  facts: [],
};

/** AgingReportResponse (GET /payables/aging) — aging.tsx does `report.byVendor.map(...)`. */
const AGING_REPORT = {
  totals: { notYetDue: 0, overdue1To30: 0, overdue31To60: 0, overdue61To90: 0, overdue90Plus: 0 },
  byVendor: [],
  byCostCenter: [],
};

/** CashFlowResponse (GET /cash-flow) — cash-flow.tsx reads `report?.kpis.lowestProjectedPeriodLabel`;
 *  BalanceForecastBanner.tsx (shown on /app/pendencies and /app/approvals too) reads
 *  `cf.data.periods[0]`. Both need `kpis`/`periods` to be real fields, not array indices. */
const CASH_FLOW = {
  asOf: '2026-08-27',
  granularity: 'Month',
  currency: 'BRL',
  startingBalance: 0,
  accounts: [],
  kpis: {
    startingBalance: 0,
    lowestProjectedBalance: 0,
    lowestProjectedPeriodLabel: '—',
    runwayPeriods: null,
    netResult: 0,
    totalInflowForecast: 0,
    totalOutflowForecast: 0,
  },
  overdueReceivable: 0,
  periods: [],
};

/** RequisitionSlaReportResponse (GET /requisitions/sla-report) — sla-report.tsx does
 *  `data?.stages.find(...)`; `data` being `[]` (truthy) skips the `?.` short-circuit. */
const SLA_REPORT = {
  companyId: null,
  unitId: null,
  from: null,
  to: null,
  generatedAt: '2026-08-27T00:00:00Z',
  triageThresholdHours: 24,
  approvalThresholdHours: 48,
  live: { triageOverdue: 0, approvalOverdue: 0, totalOverdue: 0, oldestOverdueBusinessHours: 0, rows: [] },
  stages: [],
  cycle: { count: 0, averageBusinessHours: 0 },
  byUnit: [],
};

/** TransactionResponse (GET /transactions/{id}) — lancamentos.tsx's `StatusBadge` does
 *  `STATUS[s].cls` with no fallback; `s` must be one of the enum's real keys. */
const TRANSACTION_DETAIL = {
  id: 'transaction_00000000000000000000000000000001',
  companyId: COMPANIES[0].id,
  unitId: 'unit_00000000000000000000000000000001',
  costCenterId: 'costcenter_00000000000000000000000000000001',
  type: 'Payable',
  description: 'Reembolso de despesa de viagem',
  vendorId: 'vendor_00000000000000000000000000000001',
  customerId: null,
  counterpartyName: 'CONSTRUTORA HORIZONTE EMPREENDIMENTOS LTDA ME',
  amount: 1250,
  dueDate: '2026-09-10',
  status: 'Pending',
  rejectionReason: null,
  createdAt: '2026-08-01T00:00:00Z',
  approvedAt: null,
};

/** ProjectResponse (GET /projects/{id}) — the edit form does
 *  `moneyInput(initial.budgetLimit)` → `undefined.toLocaleString(...)` with no guard. */
const PROJECT_DETAIL = {
  id: 'project_00000000000000000000000000000001',
  companyId: COMPANIES[0].id,
  unitId: 'unit_00000000000000000000000000000001',
  name: 'Reforma do bloco B',
  description: null,
  budgetLimit: 800000,
  initialConsumedValue: 120000,
  status: 'Active',
};

/** BudgetAdjustmentResponse (GET /budget-adjustments/{id}) — `latestReason` (timeline.ts) does
 *  `a.transitions.length` with no guard. */
const BUDGET_ADJUSTMENT_DETAIL = {
  id: 'budgetadj_00000000000000000000000000000001',
  code: 57,
  companyId: COMPANIES[0].id,
  unitId: 'unit_00000000000000000000000000000001',
  costCenterId: 'costcenter_00000000000000000000000000000001',
  type: 'Supplementation',
  competency: '2026-08',
  sourceCompetency: null,
  value: 25000,
  justification: 'Remanejamento do saldo de treinamento para cobrir manutenção de equipamentos.',
  counterparts: null,
  status: 'Approved',
  transitions: [],
  createdAt: '2026-08-01T00:00:00Z',
  createdBy: 'admin@ulbra.local',
  statusChangedAt: '2026-08-01T00:00:00Z',
};

/** QueueProjection (GET /requisitions/budget-projection) — QueueProjectionPanel.tsx has a real bug
 *  (`proj?.breaches.length ?? 0` only guards `proj`, not `.breaches`), but the endpoint should still
 *  be mocked like every other one here rather than leaning on that gap. */
const QUEUE_PROJECTION = { months: [], breaches: [], queueTotal: 0 };

const PERMS = [
  'payables:read',
  'payables:write',
  'payables:pay',
  'payables:approve',
  'payables:tax',
  'receivables:read',
  'receivables:write',
  'receivables:receive',
  'treasury:read',
  'treasury:write',
  'treasury:approve',
  'registry:read',
  'registry:write',
  'core:read',
  'core:write',
  'reports:read',
  'accounting:read',
  'accounting:write',
  'accounting:post',
  'fiscal:read',
  'fiscal:write',
  'audit:read',
  'projects:read',
  'projects:write',
  'requisitions:read',
  'requisitions:write',
  'requisitions:approve',
  'requisitions:triage',
  'requisitions:sla-report',
  'budget-adjustments:read',
  'budget-adjustments:write',
  'admin:users',
];

// Admin bypasses every `RequirePermission`/`FinalApproverRedirect` guard (see auth-guards.tsx) — the
// broadest role for reaching every route in one pass.
const ME = {
  userId: 'user_00000000000000000000000000000001',
  name: 'Admin Dev',
  email: 'admin@ulbra.local',
  role: 'Admin',
  isAdmin: true,
  permissions: PERMS,
};

function fakeJwt() {
  const b64 = (o) => Buffer.from(JSON.stringify(o)).toString('base64url');
  const exp = Math.floor(Date.now() / 1000) + 60 * 60 * 24 * 365;
  return `${b64({ alg: 'none', typ: 'JWT' })}.${b64({ sub: 'mobile-audit', exp })}.sig`;
}

/* ============ Routes ============
 * Mirrors `web/src/App.tsx` (all children of the `/app` route) plus `/` and `/login`. Kept as a
 * hand-written list, same call as the guard's `AGUARDANDO_MIGRACAO`: App.tsx isn't parsed at
 * runtime (JSX + TS, no pipeline here), so this needs a human to keep it in sync when a route is
 * added/removed/renamed.
 *
 * `id` fields substitute a route's `:param` with a fixture id shaped like the real typed-id prefix
 * (`payable_`, `vendor_`, `req_`, …). Where a matching object-shaped detail mock exists (see
 * `DETAIL_MOCKS` below) the route gets real-ish data; otherwise it falls through to the blanket
 * `[]` mock — see scope-limit #1 in the file header.
 */
const ROUTES = [
  { path: '/' },
  { path: '/login' },
  { path: '/app' },
  { path: '/app/home' },
  { path: '/app/payables' },
  { path: '/app/payables/new' },
  { path: '/app/payables/:id', id: 'payable_00000000000000000000000000000001' },
  { path: '/app/payables/:id/edit', id: 'payable_00000000000000000000000000000001' },
  { path: '/app/receivables' },
  { path: '/app/receivables/new' },
  { path: '/app/receivables/:id', id: 'receivable_00000000000000000000000000000001' },
  { path: '/app/receivables/:id/edit', id: 'receivable_00000000000000000000000000000001' },
  { path: '/app/expense-categories' },
  { path: '/app/retention-rules' },
  { path: '/app/bank-accounts' },
  { path: '/app/treasury' },
  { path: '/app/remittances' },
  { path: '/app/accounting/chart-of-accounts' },
  { path: '/app/accounting/journal-entries' },
  { path: '/app/accounting/journal-book' },
  { path: '/app/accounting/ledger' },
  { path: '/app/accounting/trial-balance' },
  { path: '/app/accounting/income-statement' },
  { path: '/app/accounting/balance-sheet' },
  { path: '/app/accounting/withholdings' },
  { path: '/app/accounting/obrigacoes' },
  { path: '/app/fiscal/obrigacoes' },
  { path: '/app/accounting/review' },
  { path: '/app/tax/review' },
  { path: '/app/accounting/pending' },
  { path: '/app/accounting/expense-recording' },
  { path: '/app/accounting/classification' },
  { path: '/app/reconciliation' },
  { path: '/app/aging' },
  { path: '/app/cash-flow' },
  { path: '/app/payments-report' },
  { path: '/app/accounts-payable' },
  { path: '/app/accounts-receivable' },
  { path: '/app/pendencies' },
  { path: '/app/approvals' },
  { path: '/app/entries/new' },
  { path: '/app/entries/:id', id: 'transaction_00000000000000000000000000000001' },
  { path: '/app/cost-centers' },
  { path: '/app/cost-centers/new' },
  { path: '/app/cost-centers/:id', id: 'cost_center_00000000000000000000000000000001' },
  { path: '/app/projects' },
  { path: '/app/projects/new' },
  { path: '/app/projects/:id', id: 'project_00000000000000000000000000000001' },
  { path: '/app/projects/:id/edit', id: 'project_00000000000000000000000000000001' },
  { path: '/app/requisitions' },
  { path: '/app/requisitions/mine' },
  { path: '/app/requisitions/triage' },
  { path: '/app/requisitions/triage/:id', id: 'req_00000000000000000000000000000001' },
  { path: '/app/requisitions/overview' },
  { path: '/app/requisitions/budgets' },
  { path: '/app/requisitions/sla-report' },
  { path: '/app/requisitions/approval' },
  { path: '/app/requisitions/approval/:id', id: 'req_00000000000000000000000000000001' },
  { path: '/app/requisitions/new' },
  { path: '/app/requisitions/:id', id: 'req_00000000000000000000000000000001' },
  { path: '/app/requisitions/:id/edit', id: 'req_00000000000000000000000000000001' },
  { path: '/app/budget-adjustments' },
  { path: '/app/budget-adjustments/new' },
  { path: '/app/budget-adjustments/:id', id: 'budgetadj_00000000000000000000000000000001' },
  { path: '/app/budget-adjustments/:id/edit', id: 'budgetadj_00000000000000000000000000000001' },
  { path: '/app/notificacoes' },
  { path: '/app/structure' },
  { path: '/app/vendors' },
  { path: '/app/vendors/new' },
  { path: '/app/vendors/:id', id: 'vendor_00000000000000000000000000000001' },
  { path: '/app/customers' },
  { path: '/app/employees' },
  { path: '/app/documentation' },
  { path: '/app/documentation/:sectionId', id: 'placeholder' },
  { path: '/app/settings/roles' },
  { path: '/app/settings/users' },
  { path: '/app/settings/audit' },
];

function resolvePath(route) {
  if (!route.id) return route.path;
  return route.path.replace(/:[a-zA-Z]+/, route.id);
}

/* ============ sidewaysScroll ============
 * Duplicated (not imported) from `e2e/mobile-layout.spec.ts` — see that file's matching comment.
 * Keep both in sync by hand; this is the same ~20-line function, same selector list.
 * `.rt-scroll` (Task 18 fix-round 1) is ResponsiveTable's desktop-table local-scroll wrapper —
 * opt-in reachability, same status as `.table-scroll`, not a defect. */
const LAYOUT_CONTAINERS = ['.content', '.panel', '.topbar', '.drawer-body', '.modal-body'];
const INTENTIONAL_SCROLL_SELECTOR = '.table-scroll, .rt-scroll';

async function sidewaysScroll(page) {
  return page.evaluate(
    ([selectors, intentional]) => {
      const out = [];
      for (const sel of selectors) {
        for (const box of document.querySelectorAll(sel)) {
          if (box.scrollWidth <= box.clientWidth) continue;
          const limit = box.getBoundingClientRect().right;
          const blame = [
            ...new Set(
              [...box.querySelectorAll('*')]
                .filter((el) => !el.closest(intentional))
                .filter((el) => el.getBoundingClientRect().right > limit + 1)
                .map((el) => {
                  const cls = typeof el.className === 'string' ? el.className : '';
                  return `<${el.tagName.toLowerCase()} class="${cls}">`;
                }),
            ),
          ].slice(0, 4);
          out.push(
            `${sel} rola de lado (${box.scrollWidth}px em ${box.clientWidth}px) — culpados: ${blame.join(', ') || '?'}`,
          );
        }
      }
      return out;
    },
    [LAYOUT_CONTAINERS, INTENTIONAL_SCROLL_SELECTOR],
  );
}

async function makeContext(browser, viewport) {
  const ctx = await browser.newContext({ viewport });
  await ctx.route(/googleapis|gstatic|accounts\.google|clarity\.ms/, (r) => r.abort());
  // Broad catch-all FIRST, specific mocks AFTER — Playwright resolves overlapping routes in reverse
  // registration order, so the later (specific) ones win. Same rule as mobile-fixtures.ts.
  await ctx.route('**/api/**', (r) => r.fulfill({ status: 200, contentType: 'application/json', body: '[]' }));
  await ctx.route('**/api/companies*', (r) => json(r, COMPANIES));
  await ctx.route('**/api/payables?*', (r) => json(r, QUEUE_ITEMS));
  await ctx.route('**/api/payables/stage-items*', (r) => json(r, PAYABLES));
  await ctx.route('**/api/payables/stage-summary*', (r) => json(r, PAYABLE_STAGE_SUMMARY));
  await ctx.route('**/api/payables/payment-queue-summary*', (r) => json(r, PAYMENT_QUEUE_SUMMARY));
  await ctx.route('**/api/bank-transactions/period-summary*', (r) => json(r, PERIOD_SUMMARY));
  await ctx.route('**/api/vendors*', (r) => json(r, VENDORS));
  await ctx.route('**/api/accounting/trial-balance*', (r) => json(r, TRIAL_BALANCE));
  await ctx.route('**/api/users*', (r) => json(r, USERS));
  await ctx.route('**/api/requisitions?*', (r) => json(r, REQUISITIONS));
  await ctx.route('**/api/withholding-rules*', (r) => json(r, WITHHOLDING_RULES));
  await ctx.route('**/api/journal-entries?*', (r) => json(r, JOURNAL_ENTRIES));
  // Task 18 fix-round 1: report/summary endpoints that were missing entirely (fell through to the
  // blanket `[]`, which several pages read as an object with no guard). Registered after the broad
  // `**/api/requisitions?*`/`**/api/payables?*` etc. above so the more specific path wins — same
  // `?` -matches-one-char glob quirk noted for stage-items.
  await ctx.route('**/api/receivables/stage-summary*', (r) => json(r, RECEIVABLE_STAGE_SUMMARY));
  await ctx.route(/\/api\/receivables\/receivable_[^/?]+$/, (r) => json(r, RECEIVABLE_DETAIL));
  await ctx.route('**/api/requisitions/budget-control*', (r) => json(r, BUDGET_CONTROL));
  await ctx.route('**/api/requisitions/budget-controls*', (r) => json(r, { controls: {} }));
  await ctx.route(/\/api\/requisitions\/[^/?]+\/budget-control(\?.*)?$/, (r) => json(r, BUDGET_CONTROL));
  await ctx.route('**/api/requisitions/sla-report*', (r) => json(r, SLA_REPORT));
  await ctx.route('**/api/requisitions/budget-projection*', (r) => json(r, QUEUE_PROJECTION));
  await ctx.route('**/api/accounting/income-statement*', (r) => json(r, INCOME_STATEMENT));
  await ctx.route('**/api/accounting/balance-sheet*', (r) => json(r, BALANCE_SHEET));
  await ctx.route('**/api/accounting/journal-book*', (r) => json(r, JOURNAL_BOOK));
  await ctx.route('**/api/payables/withholdings*', (r) => json(r, WITHHOLDINGS_REPORT));
  await ctx.route('**/api/fiscal/assessment*', (r) => json(r, FISCAL_ASSESSMENT));
  await ctx.route('**/api/payables/aging*', (r) => json(r, AGING_REPORT));
  await ctx.route('**/api/cash-flow*', (r) => json(r, CASH_FLOW));
  // Detail-endpoint mocks for the few `:id` routes with a matching object shape (scope-limit #1).
  await ctx.route(/\/api\/payables\/payable_[^/?]+$/, (r) => json(r, PAYABLES.items[0]));
  await ctx.route(/\/api\/vendors\/vendor_[^/?]+$/, (r) => json(r, VENDORS.items[0]));
  await ctx.route(/\/api\/requisitions\/req_[^/?]+$/, (r) => json(r, REQUISITIONS.items[0]));
  await ctx.route(/\/api\/transactions\/transaction_[^/?]+$/, (r) => json(r, TRANSACTION_DETAIL));
  await ctx.route(/\/api\/projects\/project_[^/?]+$/, (r) => json(r, PROJECT_DETAIL));
  await ctx.route(/\/api\/budget-adjustments\/budgetadj_[^/?]+$/, (r) => json(r, BUDGET_ADJUSTMENT_DETAIL));
  await ctx.route('**/auth/me', (r) => json(r, ME));
  await ctx.addInitScript(
    ([t, c]) => {
      localStorage.setItem('ulbra-one.token', t);
      localStorage.setItem('ulbra-one.empresaId', c);
    },
    [fakeJwt(), COMPANIES[0].id],
  );
  return ctx;
}

async function auditRoute(page, route) {
  const target = resolvePath(route);
  // Real uncaught exceptions only — this is what actually decides CRASHED.
  const pageErrors = [];
  const onPageError = (err) => pageErrors.push(err.message);
  // React's error boundary swallows the exception in componentDidCatch before it can reach
  // `pageerror` — the message only ever shows up via `console.error`. This is diagnostic-only:
  // console.error also fires for harmless noise (blocked-resource logs, React warnings), so it
  // must NEVER by itself flip a route to CRASHED — only `.error-fallback` or a real `pageerror`
  // does that; this just gives that verdict a better message than "error-fallback visível".
  const consoleErrors = [];
  const onConsole = (msg) => {
    if (msg.type() === 'error') consoleErrors.push(msg.text());
  };
  page.on('pageerror', onPageError);
  page.on('console', onConsole);
  try {
    await page.goto(`${BASE}${target}`, { waitUntil: 'domcontentloaded', timeout: 15000 });
    try {
      await page.waitForLoadState('networkidle', { timeout: 6000 });
    } catch {
      // Some route keeps a connection open (rare) — proceed with whatever rendered so far rather
      // than silently skipping the route.
    }
    const crashed = (await page.locator('.error-fallback').count()) > 0;
    if (crashed || pageErrors.length > 0) {
      const boundaryMsg = consoleErrors.find((e) => e.includes('Render error caught by ErrorBoundary'));
      return { target, status: 'CRASHED', detail: boundaryMsg ?? pageErrors[0] ?? 'error-fallback visível' };
    }
    const containerCount = await page.evaluate(
      (selectors) => selectors.reduce((n, sel) => n + document.querySelectorAll(sel).length, 0),
      LAYOUT_CONTAINERS,
    );
    const offenders = await sidewaysScroll(page);
    if (offenders.length > 0) return { target, status: 'OFFENDERS', detail: offenders };
    if (containerCount === 0) return { target, status: 'NO_CONTAINERS', detail: null };
    return { target, status: 'OK', detail: null };
  } catch (e) {
    return { target, status: 'ERROR', detail: e instanceof Error ? e.message : String(e) };
  } finally {
    page.off('pageerror', onPageError);
    page.off('console', onConsole);
  }
}

async function main() {
  const browser = await chromium.launch();
  const summary = { OK: 0, OFFENDERS: 0, CRASHED: 0, NO_CONTAINERS: 0, ERROR: 0 };
  const findings = [];

  for (const viewport of VIEWPORTS) {
    console.log(`\n=== ${viewport.label} ===`);
    const ctx = await makeContext(browser, viewport);
    const page = await ctx.newPage();
    for (const route of ROUTES) {
      const result = await auditRoute(page, route);
      summary[result.status]++;
      const line = `[${viewport.label}] ${result.status.padEnd(13)} ${result.target}`;
      console.log(line);
      if (result.status === 'OFFENDERS') {
        for (const o of result.detail) console.log(`    - ${o}`);
        findings.push({ viewport: viewport.label, ...result });
      } else if (result.status === 'CRASHED' || result.status === 'ERROR') {
        console.log(`    - ${result.detail}`);
        findings.push({ viewport: viewport.label, ...result });
      }
    }
    await ctx.close();
  }

  console.log('\n=== Resumo ===');
  console.log(
    `OK: ${summary.OK}  OFFENDERS: ${summary.OFFENDERS}  CRASHED: ${summary.CRASHED}  ` +
      `NO_CONTAINERS: ${summary.NO_CONTAINERS}  ERROR: ${summary.ERROR}`,
  );
  if (findings.length > 0) {
    console.log('\nRotas que precisam de atenção:');
    for (const f of findings) {
      console.log(`  [${f.viewport}] ${f.status} ${f.target}`);
    }
  }

  await browser.close();
  // Reporting tool, not a CI gate (see file header) — the guard spec is what fails the build.
  process.exit(0);
}

main().catch((e) => {
  console.error(e);
  process.exit(1);
});
