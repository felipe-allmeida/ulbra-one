# Payables Pages (Stage-Based UX) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the new stage-based payables experience in `web/src/pages/payables/` — a triage dashboard + stage tabs (Entry · Classification · Approval · Payment · Reconciled), a per-title detail page with an audit timeline and balance forecast, and an inline classification drawer — consuming the plan-1 workflow components and plan-2 backend endpoints.

**Architecture:** New, self-contained page folder `web/src/pages/payables/`. A `layout.tsx` renders `PendencyDashboard` (fed by `GET /api/payables/stage-summary`) + `StageTabBar` (active stage in the `?stage=` query param) and mounts one of five tab components. Each tab is a focused list filtered by `usePayables({ status })`. The detail page reuses the existing presentational leaf components (copied verbatim from `contas-a-pagar/`, which sit at the same import depth so imports are unchanged) and adds `SlaChips`, `AuditTimeline` (via `GET /api/payables/{id}/audit-trail`), and `BalanceForecastPanel`. Pure logic (stage→status mapping, dashboard-card mapping, payment period grouping) lives in small tested helpers. The folder is self-contained so the later migration can delete `contas-a-pagar/` without breaking it.

**Tech Stack:** React 19, TypeScript, Vite, TanStack Query, react-hook-form/zod, react-i18next, react-router-dom v6 (`useSearchParams`), the workflow components from `web/src/components/workflow/`, vitest + @testing-library/react.

## Global Constraints

- All code (identifiers, files, comments) in English; only i18n string VALUES in pt-BR. No hardcoded pt-BR in JSX — every user-facing string via `t(...)`. (spec §Constraints; user feedback)
- Follow existing FE patterns: components from `web/src/ui.tsx`, query hooks in `web/src/queries.ts`, API methods in `web/src/api.ts`, types via `Schemas['...']` aliases, `brl()` from `lib`, `fmtDate`/`fmtDateTime` from `datetime`. (spec §Constraints)
- Reuse the workflow components from `web/src/components/workflow/` (`PendencyDashboard`, `StageTabBar`, `SlaChips`, `AuditTimeline`, `BalanceForecastBanner`, `BalanceForecastPanel`) — do NOT re-implement them. (plan 1)
- Generic/multitenant — no Ulbra-specific logic. (spec §Constraints)
- Do NOT modify `contas-a-pagar/` or any CNAB/remittance code — new files only (plus additive edits to `queries.ts`, `api.ts`, `App.tsx`, `i18n/locales/pt-BR.ts`). (spec §8; avoid conflict with the CNAB return slice)
- Tests use `renderWithProviders` from `web/src/test/renderWithProviders.tsx`, import real i18n via `import '../../i18n'`, and mock `../../queries` / `../../store` / `react-router-dom` with `vi.mock` (the established pattern in `contas-a-pagar/badges-classify.test.tsx`). Tests for everything. (user feedback)
- Verification commands run from `web/`: `pnpm test`, `pnpm typecheck`, `pnpm build`.

**Verified facts (use verbatim):**
- The 4 new schema types already exist in `web/src/api/schema.d.ts`: `PayableStageSummaryResponse { entry, classification, approval, payment, reconciled: StageBucket }`, `ReceivableStageSummaryResponse { entry, receipt, reconciled }`, `StageBucket { count: number; totalAmount: number }`, `AuditTrailEventResponse { type: string; actorId: string|null; actorName: string; timestamp: string; metadata: { [k]: string } }`.
- `PayableStatus = Received | PendingClassification | PendingApproval | Scheduled | Paid | Rejected | Cancelled`. `InstallmentStatus = Open | PartiallyPaid | Paid | InRemittance`.
- `PayableListItem` fields: `payableId, installmentId, sequence, installmentCount, counterpartyName, description, costCenterId, categoryId|null, dueDate, amount, installmentStatus, payableStatus`. `usePayables(params)` accepts `{ companyId, status, page, dueFrom, dueTo, paid, classified, ... }` and returns `Paginated<PayableListItem>` (`.items`, `.total`, `.totalPages`).
- `Payable` (detail) exposes `accountingAccountId, financialAccountId, suggestedAccountingAccountId, suggestedFinancialAccountId, classificationConfidence, classificationRationale, isClassified, status, totalAmount, vendorId, counterpartyName, installments[], attachments[], approvedBy/At, rejectedBy/At, rejectionReason, cancelReason, documentNumber, issueDate, costCenterId, categoryId, companyId`.
- These leaf components in `contas-a-pagar/` are generic (only depend on `../../*` and Payable/Installment types) and copy verbatim with NO edits because the destination is the same depth (`web/src/pages/<folder>/`): `badges.tsx`, `download.ts`, `use-names.ts`, `installments-table.tsx`, `payment-history.tsx`, `payment-form.tsx`, `attachments-panel.tsx`, `vendor-picker.tsx`, `fiscal-xml-reader.tsx`. Their exact prop signatures: `InstallmentsTable({ payable, onRegister })`, `PaymentHistory({ payable, installment })`, `PaymentForm({ payable, installment, onDone, onCancel })`, `AttachmentsPanel({ payable, onDelete })`, `PayableStatusBadge({ s })`, `InstallmentStatusBadge({ s })`, `VendorPicker({ companyId, value, onChange, invalid?, prefill?, notice? })`, `useNames(companyId) → { centros, centroNome, categoriaNome }`, `downloadAttachment(payableId, att)`, `downloadPaymentProof(payableId, payment)`.
- `useApprovePayable()`, `useRejectPayable()`, `useCancelPayable()`, `useClassifyPayable()`, `useRegisterPayment()`, `useCostCenters(companyId)`, `useExpenseCategories(true)`, `useChartAccounts({ chartType })` already exist in `queries.ts`. `useApprovePayable().mutate({ id })`; `useRejectPayable().mutate({ id, reason })`; `useClassifyPayable().mutate({ id, body: { accountingAccountId, financialAccountId } })`.
- `useApp()` from `web/src/store` returns `{ empresa, empresaId, empresas, setEmpresaId }`. `EmpresaPicker` is exported from `web/src/pages/shared`.

---

### Task 1: API methods, type aliases, query hooks, and i18n keys

Wire the data layer for the two new endpoints, plus the new i18n keys the pages need.

**Files:**
- Modify: `web/src/api.ts` (add 4 type aliases + 2 methods)
- Modify: `web/src/queries.ts` (add 2 query keys + 2 hooks + type imports)
- Modify: `web/src/i18n/locales/pt-BR.ts` (add `payables.stages.*` and a few page keys under the existing `payables` object)

**Interfaces:**
- Produces:
  - `api.ts`: `export type PayableStageSummaryResponse = Schemas['PayableStageSummaryResponse'];`, `export type ReceivableStageSummaryResponse = Schemas['ReceivableStageSummaryResponse'];`, `export type StageBucket = Schemas['StageBucket'];`, `export type AuditTrailEvent = Schemas['AuditTrailEventResponse'];`; methods `api.payableStageSummary(companyId): Promise<PayableStageSummaryResponse>` and `api.payableAuditTrail(id): Promise<AuditTrailEvent[]>`.
  - `queries.ts`: `usePayableStageSummary(companyId?: string)` → query of `PayableStageSummaryResponse`; `usePayableAuditTrail(id?: string)` → query of `AuditTrailEvent[]`.
  - i18n: keys under `payables.stages.{entry,classification,approval,payment,reconciled}`, `payables.dashboard.title`, `payables.approval.myPending`, `payables.approval.all`, `payables.classify.{title,accounting,financial,suggestion,confirm,rationale}`, `payables.payment.generateRemittance`, `payables.reconciled.{paidAmount,lastPayment}`.

- [ ] **Step 1: Add the type aliases in `api.ts`**

In `web/src/api.ts`, near the other Payable type aliases (search for `export type Payable = Schemas['PayableResponse'];`), add:

```ts
export type PayableStageSummaryResponse = Schemas['PayableStageSummaryResponse'];
export type ReceivableStageSummaryResponse = Schemas['ReceivableStageSummaryResponse'];
export type StageBucket = Schemas['StageBucket'];
export type AuditTrailEvent = Schemas['AuditTrailEventResponse'];
```

- [ ] **Step 2: Add the API methods in `api.ts`**

In the `api` object (search for `getPayable: (id: string) => request<Payable>(\`/payables/${id}\`),`), add after it:

```ts
  payableStageSummary: (companyId: string): Promise<PayableStageSummaryResponse> => {
    const q = new URLSearchParams({ companyId });
    return request<PayableStageSummaryResponse>(`/payables/stage-summary?${q}`);
  },
  payableAuditTrail: (id: string): Promise<AuditTrailEvent[]> =>
    request<AuditTrailEvent[]>(`/payables/${id}/audit-trail`),
```

- [ ] **Step 3: Add query keys + hooks in `queries.ts`**

In `web/src/queries.ts`, add to the `queryKeys` object:

```ts
  payableStageSummary: (companyId: string) => ['payableStageSummary', companyId] as const,
  payableAuditTrail: (id: string) => ['payableAuditTrail', id] as const,
```

Add `PayableStageSummaryResponse` and `AuditTrailEvent` to the type import from `./api` at the top of the file. Then add the hooks (near `usePayable`):

```ts
export function usePayableStageSummary(companyId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.payableStageSummary(companyId ?? ''),
    queryFn: () => api.payableStageSummary(companyId!),
    enabled: !!companyId,
  });
}

export function usePayableAuditTrail(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.payableAuditTrail(id ?? ''),
    queryFn: () => api.payableAuditTrail(id!),
    enabled: !!id,
  });
}
```

- [ ] **Step 4: Add i18n keys**

In `web/src/i18n/locales/pt-BR.ts`, inside the existing top-level `payables: { ... }` object, add these keys (place them alongside the existing `payables` sub-objects; do not duplicate existing keys):

```ts
    stages: {
      entry: 'Entrada',
      classification: 'Classificação',
      approval: 'Aprovação',
      payment: 'Pagamento',
      reconciled: 'Conciliado',
    },
    dashboard: { title: 'Triagem' },
    approval: { myPending: 'Meus pendentes', all: 'Todos' },
    classify: {
      title: 'Classificar título',
      accounting: 'Conta contábil',
      financial: 'Conta financeira',
      suggestion: 'Sugestão da IA',
      confirm: 'Confirmar classificação',
      rationale: 'Justificativa',
    },
    payment: { generateRemittance: 'Gerar remessa' },
    reconciled: { paidAmount: 'Valor pago', lastPayment: 'Último pagamento' },
```

- [ ] **Step 5: Verify it type-checks**

Run (from `web/`): `pnpm typecheck`
Expected: PASS (no errors; the new aliases resolve against the regenerated `schema.d.ts`, the i18n object stays valid).

- [ ] **Step 6: Commit**

```bash
git add web/src/api.ts web/src/queries.ts web/src/i18n/locales/pt-BR.ts
git commit -m "feat(AEL-626): payable stage-summary + audit-trail api/query hooks + i18n keys"
```

---

### Task 2: `stages.ts` — stage definitions, status mapping, dashboard cards

Pure module: the five stages, their backend status, their icon, and a mapper from the stage-summary response to `PendencyCard[]`.

**Files:**
- Create: `web/src/pages/payables/stages.ts`
- Test: `web/src/pages/payables/stages.test.ts`

**Interfaces:**
- Consumes: `PayableStatus`, `PayableStageSummaryResponse`, `StageBucket` from `../../api`; `PendencyCard` from `../../components/workflow`.
- Produces:
  - `export type StageKey = 'entry' | 'classification' | 'approval' | 'payment' | 'reconciled';`
  - `export const STAGE_ORDER: StageKey[]`
  - `export const STAGE_STATUS: Record<StageKey, PayableStatus>` (entry→Received, classification→PendingClassification, approval→PendingApproval, payment→Scheduled, reconciled→Paid)
  - `export function stageIcon(key: StageKey): ReactNode`
  - `export function dashboardCards(summary: PayableStageSummaryResponse, t: (k: string) => string): PendencyCard[]` — one card per stage in STAGE_ORDER, `label: t('payables.stages.'+key)`, `count`/`totalAmount` from the matching bucket, `stageKey: key`, `icon: stageIcon(key)`.
  - `export function isStageKey(v: string | null): v is StageKey`

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/payables/stages.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { STAGE_ORDER, STAGE_STATUS, dashboardCards, isStageKey } from './stages';

const summary = {
  entry: { count: 5, totalAmount: 12400 },
  classification: { count: 3, totalAmount: 8750 },
  approval: { count: 2, totalAmount: 45000 },
  payment: { count: 4, totalAmount: 22100 },
  reconciled: { count: 18, totalAmount: 134500 },
};

describe('stages', () => {
  it('orders the five stages and maps each to its backend status', () => {
    expect(STAGE_ORDER).toEqual(['entry', 'classification', 'approval', 'payment', 'reconciled']);
    expect(STAGE_STATUS.entry).toBe('Received');
    expect(STAGE_STATUS.classification).toBe('PendingClassification');
    expect(STAGE_STATUS.approval).toBe('PendingApproval');
    expect(STAGE_STATUS.payment).toBe('Scheduled');
    expect(STAGE_STATUS.reconciled).toBe('Paid');
  });
  it('builds one dashboard card per stage with translated label, count and amount', () => {
    const cards = dashboardCards(summary, (k) => k);
    expect(cards).toHaveLength(5);
    expect(cards[0]).toMatchObject({ stageKey: 'entry', label: 'payables.stages.entry', count: 5, totalAmount: 12400 });
    expect(cards[2]).toMatchObject({ stageKey: 'approval', count: 2, totalAmount: 45000 });
  });
  it('isStageKey guards valid keys', () => {
    expect(isStageKey('approval')).toBe(true);
    expect(isStageKey('bogus')).toBe(false);
    expect(isStageKey(null)).toBe(false);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- stages`
Expected: FAIL — cannot resolve `./stages`.

- [ ] **Step 3: Implement `stages.ts`**

Create `web/src/pages/payables/stages.ts`:

```ts
import { type ReactNode } from 'react';
import { Inbox, Tags, CheckCircle2, Wallet, Archive } from 'lucide-react';
import { type PayableStatus, type PayableStageSummaryResponse, type StageBucket } from '../../api';
import { type PendencyCard } from '../../components/workflow';

export type StageKey = 'entry' | 'classification' | 'approval' | 'payment' | 'reconciled';

export const STAGE_ORDER: StageKey[] = ['entry', 'classification', 'approval', 'payment', 'reconciled'];

export const STAGE_STATUS: Record<StageKey, PayableStatus> = {
  entry: 'Received',
  classification: 'PendingClassification',
  approval: 'PendingApproval',
  payment: 'Scheduled',
  reconciled: 'Paid',
};

export function stageIcon(key: StageKey): ReactNode {
  switch (key) {
    case 'entry':
      return <Inbox size={18} />;
    case 'classification':
      return <Tags size={18} />;
    case 'approval':
      return <CheckCircle2 size={18} />;
    case 'payment':
      return <Wallet size={18} />;
    case 'reconciled':
      return <Archive size={18} />;
  }
}

export function dashboardCards(summary: PayableStageSummaryResponse, t: (k: string) => string): PendencyCard[] {
  return STAGE_ORDER.map((key) => {
    const bucket = summary[key] as StageBucket;
    return {
      stageKey: key,
      label: t(`payables.stages.${key}`),
      icon: stageIcon(key),
      count: bucket.count,
      totalAmount: bucket.totalAmount,
    };
  });
}

export function isStageKey(v: string | null): v is StageKey {
  return v != null && (STAGE_ORDER as string[]).includes(v);
}
```

(The file is `.ts` but uses JSX in `stageIcon`. Rename it to `stages.tsx` if the project's TS config rejects JSX in `.ts` — check by running the test; if JSX fails to parse, rename the file to `stages.tsx` and update the test import to `./stages` which resolves either extension.)

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- stages`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/stages.tsx web/src/pages/payables/stages.test.ts
git commit -m "feat(AEL-626): payable stage definitions + dashboard-card mapping"
```

---

### Task 3: `payment-periods.ts` — group scheduled installments by period

Pure helper used by the payment tab: bucket installment list-items into Today / This week / Next week / Future, each with a subtotal.

**Files:**
- Create: `web/src/pages/payables/payment-periods.ts`
- Test: `web/src/pages/payables/payment-periods.test.ts`

**Interfaces:**
- Consumes: `PayableListItem` from `../../api`.
- Produces:
  - `export type PeriodKey = 'today' | 'thisWeek' | 'nextWeek' | 'future';`
  - `export interface PeriodGroup { key: PeriodKey; items: PayableListItem[]; subtotal: number }`
  - `export function groupByPeriod(items: PayableListItem[], now?: Date): PeriodGroup[]` — groups by `dueDate` (YYYY-MM-DD). today = due ≤ end of today; thisWeek = after today through the coming Sunday; nextWeek = the following Mon–Sun; future = later. Overdue (due before today) goes in `today`. Returns groups in fixed order, omitting empty ones. `subtotal` = sum of `amount`.

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/payables/payment-periods.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { groupByPeriod } from './payment-periods';

const mk = (id: string, dueDate: string, amount: number) =>
  ({ payableId: id, installmentId: id, dueDate, amount } as any);

describe('groupByPeriod', () => {
  // Thursday 2026-06-25
  const now = new Date(2026, 5, 25);
  it('buckets overdue and due-today into today, with subtotal', () => {
    const groups = groupByPeriod([mk('a', '2026-06-20', 100), mk('b', '2026-06-25', 50)], now);
    const today = groups.find((g) => g.key === 'today')!;
    expect(today.items).toHaveLength(2);
    expect(today.subtotal).toBe(150);
  });
  it('separates this week, next week and future', () => {
    const groups = groupByPeriod(
      [mk('c', '2026-06-27', 10), mk('d', '2026-06-30', 20), mk('e', '2026-07-20', 30)],
      now,
    );
    expect(groups.find((g) => g.key === 'thisWeek')?.items.map((i) => i.payableId)).toEqual(['c']);
    expect(groups.find((g) => g.key === 'nextWeek')?.items.map((i) => i.payableId)).toEqual(['d']);
    expect(groups.find((g) => g.key === 'future')?.items.map((i) => i.payableId)).toEqual(['e']);
  });
  it('omits empty groups and returns them in fixed order', () => {
    const groups = groupByPeriod([mk('f', '2026-07-20', 5)], now);
    expect(groups.map((g) => g.key)).toEqual(['future']);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- payment-periods`
Expected: FAIL — cannot resolve `./payment-periods`.

- [ ] **Step 3: Implement `payment-periods.ts`**

Create `web/src/pages/payables/payment-periods.ts`:

```ts
import { type PayableListItem } from '../../api';

export type PeriodKey = 'today' | 'thisWeek' | 'nextWeek' | 'future';

export interface PeriodGroup {
  key: PeriodKey;
  items: PayableListItem[];
  subtotal: number;
}

const ORDER: PeriodKey[] = ['today', 'thisWeek', 'nextWeek', 'future'];

/** Days from `from` (date-only) to the coming Sunday (0 if already Sunday). */
function daysToSunday(from: Date): number {
  return (7 - from.getDay()) % 7;
}

function bucketOf(dueDate: string, now: Date): PeriodKey {
  const [y, m, d] = dueDate.split('-').map(Number);
  const due = Date.UTC(y, m - 1, d);
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  if (due <= today) return 'today';
  const endOfThisWeek = today + (daysToSunday(now) + 0) * 86_400_000;
  if (due <= endOfThisWeek) return 'thisWeek';
  const endOfNextWeek = endOfThisWeek + 7 * 86_400_000;
  if (due <= endOfNextWeek) return 'nextWeek';
  return 'future';
}

export function groupByPeriod(items: PayableListItem[], now: Date = new Date()): PeriodGroup[] {
  const map = new Map<PeriodKey, PayableListItem[]>();
  for (const item of items) {
    const key = bucketOf(item.dueDate, now);
    (map.get(key) ?? map.set(key, []).get(key)!).push(item);
  }
  return ORDER.filter((k) => map.has(k)).map((key) => {
    const groupItems = map.get(key)!;
    return { key, items: groupItems, subtotal: groupItems.reduce((s, i) => s + i.amount, 0) };
  });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- payment-periods`
Expected: PASS (3 tests). (Note: `now = new Date(2026,5,25)` is Thursday; `daysToSunday` = 3 → end of this week = Sun 2026-06-28; `2026-06-27` ≤ that → thisWeek; `2026-06-30` ≤ +7 → nextWeek; `2026-07-20` → future.)

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/payment-periods.ts web/src/pages/payables/payment-periods.test.ts
git commit -m "feat(AEL-626): payment period grouping helper"
```

---

### Task 4: Copy the generic leaf components into `payables/`

The presentational/leaf components are generic and sit at the same import depth, so they copy verbatim with NO edits. This keeps `payables/` self-contained for the later migration.

**Files (each Create = exact byte copy of the same-named file in `contas-a-pagar/`):**
- Create: `web/src/pages/payables/badges.tsx`
- Create: `web/src/pages/payables/download.ts`
- Create: `web/src/pages/payables/use-names.ts`
- Create: `web/src/pages/payables/installments-table.tsx`
- Create: `web/src/pages/payables/payment-history.tsx`
- Create: `web/src/pages/payables/payment-form.tsx`
- Create: `web/src/pages/payables/attachments-panel.tsx`
- Create: `web/src/pages/payables/vendor-picker.tsx`
- Create: `web/src/pages/payables/fiscal-xml-reader.tsx`

**Interfaces:**
- Produces (re-exported, identical signatures): `PayableStatusBadge`, `InstallmentStatusBadge`, `downloadAttachment`, `downloadPaymentProof`, `useNames`, `InstallmentsTable`, `PaymentHistory`, `PaymentForm`, `AttachmentsPanel`, `VendorPicker`, `FiscalXmlReader`.

- [ ] **Step 1: Copy each file verbatim**

Run (from repo root):

```bash
cd web/src/pages
for f in badges.tsx download.ts use-names.ts installments-table.tsx payment-history.tsx payment-form.tsx attachments-panel.tsx vendor-picker.tsx fiscal-xml-reader.tsx; do
  cp "contas-a-pagar/$f" "payables/$f"
done
```

Do NOT edit the copies — they import from `../../*` (ui, api, queries, lib, datetime, schemas, registry-forms) which resolve identically from `payables/`, and from `./badges` / `./download` (sibling files now present in `payables/`).

- [ ] **Step 2: Verify the copies type-check**

Run (from `web/`): `pnpm typecheck`
Expected: PASS — every relative import in the copied files resolves from the new location (same depth; siblings copied alongside).

- [ ] **Step 3: Commit**

```bash
git add web/src/pages/payables/badges.tsx web/src/pages/payables/download.ts web/src/pages/payables/use-names.ts web/src/pages/payables/installments-table.tsx web/src/pages/payables/payment-history.tsx web/src/pages/payables/payment-form.tsx web/src/pages/payables/attachments-panel.tsx web/src/pages/payables/vendor-picker.tsx web/src/pages/payables/fiscal-xml-reader.tsx
git commit -m "chore(AEL-626): copy generic payable leaf components into payables/"
```

---

### Task 5: `PayableTable` — shared stage list table

A small shared table the simple stage tabs render, so Entry/Approval/Reconciled don't duplicate markup. Renders `PayableListItem` rows with `SlaChips` and row→detail navigation.

**Files:**
- Create: `web/src/pages/payables/payable-table.tsx`
- Test: `web/src/pages/payables/payable-table.test.tsx`

**Interfaces:**
- Consumes: `PayableListItem` from `../../api`; `SlaChips` from `../../components/workflow`; `useNames` from `./use-names`; `Panel`, `Pager`, `EmptyState` from `../../ui`.
- Produces: `export function PayableTable({ companyId, stageLabel, items, total, page, totalPages, onPage, extraColumn? }: { companyId: string; stageLabel: string; items: PayableListItem[]; total: number; page: number; totalPages: number; onPage: (p: number) => void; extraColumn?: { header: string; cell: (item: PayableListItem) => ReactNode } })`. Renders a `Panel` titled with the count, a table (dueDate, description, vendor, costCenter, amount, SlaChips, + optional extra column), each row clickable → `nav('/app/payables/' + item.payableId)`. Empty list → `EmptyState`.

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/payables/payable-table.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const navigate = vi.fn();
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => navigate };
});
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: { cc_1: 'Marketing' }, categoriaNome: {} }) }));

import { PayableTable } from './payable-table';

const items = [
  { payableId: 'payable_1', installmentId: 'i1', sequence: 1, installmentCount: 1, counterpartyName: 'ACME', description: 'NF 100', costCenterId: 'cc_1', categoryId: null, dueDate: '2026-06-30', amount: 1234, installmentStatus: 'Open', payableStatus: 'Received' },
] as any;

describe('PayableTable', () => {
  it('renders rows and navigates to detail on row click', () => {
    render(
      <MemoryRouter>
        <PayableTable companyId="company_1" stageLabel="Entrada" items={items} total={1} page={1} totalPages={1} onPage={() => {}} />
      </MemoryRouter>,
    );
    expect(screen.getByText('NF 100')).toBeInTheDocument();
    expect(screen.getByText('ACME')).toBeInTheDocument();
    fireEvent.click(screen.getByText('NF 100'));
    expect(navigate).toHaveBeenCalledWith('/app/payables/payable_1');
  });
  it('shows an empty state when there are no items', () => {
    render(
      <MemoryRouter>
        <PayableTable companyId="company_1" stageLabel="Entrada" items={[]} total={0} page={1} totalPages={1} onPage={() => {}} />
      </MemoryRouter>,
    );
    expect(screen.getByText(/Entrada/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- payable-table`
Expected: FAIL — cannot resolve `./payable-table`.

- [ ] **Step 3: Implement `payable-table.tsx`**

Create `web/src/pages/payables/payable-table.tsx`:

```tsx
import { type ReactNode } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Wallet } from 'lucide-react';
import { type PayableListItem } from '../../api';
import { brl } from '../../lib';
import { Panel, Pager, EmptyState } from '../../ui';
import { fmtDate } from '../../datetime';
import { SlaChips } from '../../components/workflow';
import { useNames } from './use-names';

export function PayableTable({
  companyId,
  stageLabel,
  items,
  total,
  page,
  totalPages,
  onPage,
  extraColumn,
}: {
  companyId: string;
  stageLabel: string;
  items: PayableListItem[];
  total: number;
  page: number;
  totalPages: number;
  onPage: (p: number) => void;
  extraColumn?: { header: string; cell: (item: PayableListItem) => ReactNode };
}) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { centroNome } = useNames(companyId);

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<Wallet />}
        title={`${stageLabel} — ${t('payables.empty.noItemsTitle')}`}
        sub={t('payables.empty.noItemsSub')}
      />
    );
  }

  return (
    <Panel title={`${stageLabel} · ${t('payables.count', { count: total })}`}>
      <table>
        <thead>
          <tr>
            <th>{t('payables.columns.dueDate')}</th>
            <th>{t('payables.columns.description')}</th>
            <th>{t('payables.columns.vendor')}</th>
            <th>{t('payables.columns.costCenter')}</th>
            <th style={{ textAlign: 'right' }}>{t('payables.columns.amount')}</th>
            <th>{t('payables.columns.status')}</th>
            {extraColumn && <th>{extraColumn.header}</th>}
          </tr>
        </thead>
        <tbody>
          {items.map((l) => (
            <tr key={l.installmentId} className='row-click' onClick={() => nav(`/app/payables/${l.payableId}`)}>
              <td className='t-mono'>{fmtDate(l.dueDate)}</td>
              <td>{l.description}</td>
              <td>{l.counterpartyName || '—'}</td>
              <td>{centroNome[l.costCenterId] ?? '—'}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>{brl(l.amount)}</td>
              <td onClick={(e) => e.stopPropagation()}>
                <SlaChips stageLabel={stageLabel} dueDate={l.dueDate} />
              </td>
              {extraColumn && <td onClick={(e) => e.stopPropagation()}>{extraColumn.cell(l)}</td>}
            </tr>
          ))}
        </tbody>
      </table>
      <Pager page={page} totalPages={totalPages} onPage={onPage} />
    </Panel>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- payable-table`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/payable-table.tsx web/src/pages/payables/payable-table.test.tsx
git commit -m "feat(AEL-626): shared PayableTable stage list"
```

---

### Task 6: Entry, Reconciled, and Approval tabs

Three tab components. Entry + Reconciled are thin lists over `PayableTable`. Approval adds the `BalanceForecastBanner` and inline Approve (with `BalanceForecastPanel`) / Reject.

**Files:**
- Create: `web/src/pages/payables/entry-tab.tsx`
- Create: `web/src/pages/payables/reconciled-tab.tsx`
- Create: `web/src/pages/payables/approval-tab.tsx`
- Test: `web/src/pages/payables/approval-tab.test.tsx`

**Interfaces:**
- Consumes: `usePayables`, `useApprovePayable`, `useRejectPayable` from `../../queries`; `STAGE_STATUS` from `./stages`; `PayableTable` from `./payable-table`; `BalanceForecastBanner`, `BalanceForecastPanel` from `../../components/workflow`; `Button`, `ConfirmDialog`, `Input` from `../../ui`.
- Produces: `export function EntryTab({ companyId }: { companyId: string })`, `export function ReconciledTab({ companyId }: { companyId: string })`, `export function ApprovalTab({ companyId }: { companyId: string })`.

- [ ] **Step 1: Write the failing test (approval tab)**

Create `web/src/pages/payables/approval-tab.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUsePayables, mockApprove, mockReject, mockUseCashFlow } = vi.hoisted(() => ({
  mockUsePayables: vi.fn(),
  mockApprove: vi.fn(),
  mockReject: vi.fn(),
  mockUseCashFlow: vi.fn(),
}));
vi.mock('../../queries', () => ({
  usePayables: mockUsePayables,
  useApprovePayable: () => ({ mutate: mockApprove, isPending: false }),
  useRejectPayable: () => ({ mutate: mockReject, isPending: false }),
  useCashFlow: mockUseCashFlow,
}));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

import { ApprovalTab } from './approval-tab';

const item = { payableId: 'payable_1', installmentId: 'i1', sequence: 1, installmentCount: 1, counterpartyName: 'ACME', description: 'NF 1', costCenterId: 'cc_1', categoryId: null, dueDate: '2026-06-30', amount: 1000, installmentStatus: 'Open', payableStatus: 'PendingApproval' };

beforeEach(() => {
  mockUsePayables.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } });
  mockUseCashFlow.mockReturnValue({ data: { startingBalance: 5000, kpis: { lowestProjectedBalance: 4000 }, periods: [{ closingBalance: 4000, belowReserve: false }] }, isLoading: false });
});

describe('ApprovalTab', () => {
  it('lists pending-approval payables with an approve action', () => {
    render(<MemoryRouter><ApprovalTab companyId="company_1" /></MemoryRouter>);
    expect(screen.getByText('NF 1')).toBeInTheDocument();
    expect(mockUsePayables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'PendingApproval' }),
      expect.anything(),
    );
  });
  it('rejects with a reason', () => {
    render(<MemoryRouter><ApprovalTab companyId="company_1" /></MemoryRouter>);
    fireEvent.click(screen.getByText('payables.actions.reject'));
    const input = screen.getByRole('textbox');
    fireEvent.change(input, { target: { value: 'duplicado' } });
    fireEvent.click(screen.getByText('forms.actions.confirm', { exact: false }));
    expect(mockReject).toHaveBeenCalledWith({ id: 'payable_1', reason: 'duplicado' });
  });
});
```

(If the reject confirm button's label key differs, adjust the click target to the actual translated text rendered by `ConfirmDialog`'s `confirmLabel` — read `ui.tsx` `ConfirmDialog` to confirm; it defaults to `'Confirmar'`. Use `screen.getByText('Confirmar')` if no explicit label is passed.)

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- approval-tab`
Expected: FAIL — cannot resolve `./approval-tab`.

- [ ] **Step 3: Implement `entry-tab.tsx`**

Create `web/src/pages/payables/entry-tab.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayables } from '../../queries';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';

export function EntryTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: STAGE_STATUS.entry, page }, { enabled: !!companyId });
  const data = list.data;
  return (
    <PayableTable
      companyId={companyId}
      stageLabel={t('payables.stages.entry')}
      items={data?.items ?? []}
      total={data?.total ?? 0}
      page={page}
      totalPages={data?.totalPages ?? 1}
      onPage={setPage}
    />
  );
}
```

- [ ] **Step 4: Implement `reconciled-tab.tsx`**

Create `web/src/pages/payables/reconciled-tab.tsx` (identical shape to EntryTab but `STAGE_STATUS.reconciled` and the reconciled label):

```tsx
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayables } from '../../queries';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';

export function ReconciledTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: STAGE_STATUS.reconciled, page }, { enabled: !!companyId });
  const data = list.data;
  return (
    <PayableTable
      companyId={companyId}
      stageLabel={t('payables.stages.reconciled')}
      items={data?.items ?? []}
      total={data?.total ?? 0}
      page={page}
      totalPages={data?.totalPages ?? 1}
      onPage={setPage}
    />
  );
}
```

- [ ] **Step 5: Implement `approval-tab.tsx`**

Create `web/src/pages/payables/approval-tab.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Check, X } from 'lucide-react';
import { type PayableListItem } from '../../api';
import { usePayables, useApprovePayable, useRejectPayable } from '../../queries';
import { Button, ConfirmDialog, Input } from '../../ui';
import { BalanceForecastBanner, BalanceForecastPanel } from '../../components/workflow';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';

export function ApprovalTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: STAGE_STATUS.approval, page }, { enabled: !!companyId });
  const data = list.data;

  const approve = useApprovePayable();
  const reject = useRejectPayable();
  const [toApprove, setToApprove] = useState<PayableListItem | null>(null);
  const [toReject, setToReject] = useState<PayableListItem | null>(null);
  const [reason, setReason] = useState('');

  return (
    <>
      <div style={{ marginBottom: 12 }}>
        <BalanceForecastBanner companyId={companyId} />
      </div>
      <PayableTable
        companyId={companyId}
        stageLabel={t('payables.stages.approval')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        extraColumn={{
          header: t('payables.columns.status'),
          cell: (item) => (
            <div style={{ display: 'flex', gap: 6 }}>
              <Button variant='brand' icon={<Check />} onClick={() => setToApprove(item)}>
                {t('payables.actions.approve')}
              </Button>
              <Button icon={<X />} onClick={() => { setToReject(item); setReason(''); }}>
                {t('payables.actions.reject')}
              </Button>
            </div>
          ),
        }}
      />

      {toApprove && (
        <BalanceForecastPanel
          companyId={companyId}
          impactAmount={toApprove.amount}
          open
          busy={approve.isPending}
          onClose={() => setToApprove(null)}
          onConfirm={() =>
            approve.mutate({ id: toApprove.payableId }, { onSuccess: () => setToApprove(null) })
          }
        />
      )}

      {toReject && (
        <ConfirmDialog
          title={t('payables.actions.reject')}
          danger
          busy={reject.isPending}
          message={<Input value={reason} onChange={(e) => setReason(e.target.value)} placeholder={t('payables.reject.reason')} />}
          onCancel={() => setToReject(null)}
          onConfirm={() => reject.mutate({ id: toReject.payableId, reason }, { onSuccess: () => setToReject(null) })}
        />
      )}
    </>
  );
}
```

(If `payables.reject.reason` doesn't exist as an i18n key, use the existing reject-dialog key from the current `contas-a-pagar/detail.tsx` — read it and reuse the same key; do not invent a new one if one exists.)

- [ ] **Step 6: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- approval-tab`
Expected: PASS (2 tests).

- [ ] **Step 7: Commit**

```bash
git add web/src/pages/payables/entry-tab.tsx web/src/pages/payables/reconciled-tab.tsx web/src/pages/payables/approval-tab.tsx web/src/pages/payables/approval-tab.test.tsx
git commit -m "feat(AEL-626): entry, reconciled, approval tabs"
```

---

### Task 7: Classification tab + drawer

List `PendingClassification` payables; each row shows an AI-suggestion chip when available; a drawer classifies (accounting + financial chart account selects, pre-filled from the payable's `suggested*` ids), confirm → `useClassifyPayable`.

**Files:**
- Create: `web/src/pages/payables/classification-drawer.tsx`
- Create: `web/src/pages/payables/classification-tab.tsx`
- Test: `web/src/pages/payables/classification-drawer.test.tsx`

**Interfaces:**
- Consumes: `usePayable`, `useChartAccounts`, `useClassifyPayable` from `../../queries`; `Drawer`, `Button`, `Field`, `Combobox`, `Chip` from `../../ui`; `usePayables` + `STAGE_STATUS` + `PayableTable` for the list.
- Produces:
  - `export function ClassificationDrawer({ payableId, onClose }: { payableId: string; onClose: () => void })` — loads the payable via `usePayable`, pre-selects `suggestedAccountingAccountId`/`suggestedFinancialAccountId` as the initial select values, shows `classificationRationale` read-only when present, confirm calls `useClassifyPayable().mutate({ id, body: { accountingAccountId, financialAccountId } })` then `onClose()`.
  - `export function ClassificationTab({ companyId }: { companyId: string })`.

- [ ] **Step 1: Write the failing test (drawer pre-fill)**

Create `web/src/pages/payables/classification-drawer.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUsePayable, mockClassify } = vi.hoisted(() => ({ mockUsePayable: vi.fn(), mockClassify: vi.fn() }));
vi.mock('../../queries', () => ({
  usePayable: mockUsePayable,
  useClassifyPayable: () => ({ mutate: mockClassify, isPending: false }),
  useChartAccounts: ({ chartType }: { chartType: string }) => ({
    data:
      chartType === 'Accounting'
        ? [{ id: 'acc_1', code: '3.1', name: 'Despesas' }]
        : [{ id: 'fin_1', code: 'F1', name: 'Caixa' }],
  }),
}));

import { ClassificationDrawer } from './classification-drawer';

beforeEach(() => {
  mockUsePayable.mockReturnValue({
    data: {
      id: 'payable_1', companyId: 'company_1', counterpartyName: 'ACME',
      suggestedAccountingAccountId: 'acc_1', suggestedFinancialAccountId: 'fin_1',
      classificationRationale: 'Fornecedor recorrente de TI', accountingAccountId: null, financialAccountId: null,
    },
  });
});

describe('ClassificationDrawer', () => {
  it('pre-selects the AI-suggested accounts and shows the rationale', () => {
    renderWithProviders(<ClassificationDrawer payableId="payable_1" onClose={() => {}} />);
    // Suggested account labels are visible in the trigger (Combobox renders the selected item's label).
    expect(screen.getByText('Despesas')).toBeInTheDocument();
    expect(screen.getByText('Caixa')).toBeInTheDocument();
    expect(screen.getByText(/Fornecedor recorrente/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- classification-drawer`
Expected: FAIL — cannot resolve `./classification-drawer`.

- [ ] **Step 3: Implement `classification-drawer.tsx`**

Create `web/src/pages/payables/classification-drawer.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayable, useChartAccounts, useClassifyPayable } from '../../queries';
import { Drawer, Button, Field, Combobox, type ComboboxItem } from '../../ui';

export function ClassificationDrawer({ payableId, onClose }: { payableId: string; onClose: () => void }) {
  const { t } = useTranslation();
  const { data: p } = usePayable(payableId);
  const accounting = useChartAccounts({ chartType: 'Accounting' }).data ?? [];
  const financial = useChartAccounts({ chartType: 'Financial' }).data ?? [];
  const classify = useClassifyPayable();

  const [accountingId, setAccountingId] = useState<string | null>(null);
  const [financialId, setFinancialId] = useState<string | null>(null);

  // Pre-fill from the AI suggestion once the payable loads.
  useEffect(() => {
    if (p) {
      setAccountingId(p.accountingAccountId ?? p.suggestedAccountingAccountId ?? null);
      setFinancialId(p.financialAccountId ?? p.suggestedFinancialAccountId ?? null);
    }
  }, [p]);

  const toItems = (xs: { id: string; code: string; name: string }[]): ComboboxItem[] =>
    xs.map((a) => ({ id: a.id, label: a.name, sub: a.code }));

  return (
    <Drawer
      open
      onClose={onClose}
      title={t('payables.classify.title')}
      sub={p?.counterpartyName}
      footer={
        <Button
          variant='brand'
          disabled={classify.isPending || !accountingId || !financialId}
          onClick={() =>
            classify.mutate(
              { id: payableId, body: { accountingAccountId: accountingId, financialAccountId: financialId } },
              { onSuccess: onClose },
            )
          }
        >
          {t('payables.classify.confirm')}
        </Button>
      }
    >
      <Field label={t('payables.classify.accounting')}>
        <Combobox value={accountingId} onChange={setAccountingId} items={toItems(accounting)} />
      </Field>
      <Field label={t('payables.classify.financial')}>
        <Combobox value={financialId} onChange={setFinancialId} items={toItems(financial)} />
      </Field>
      {p?.classificationRationale && (
        <Field label={t('payables.classify.rationale')}>
          <div className='help'>{p.classificationRationale}</div>
        </Field>
      )}
    </Drawer>
  );
}
```

- [ ] **Step 4: Implement `classification-tab.tsx`**

Create `web/src/pages/payables/classification-tab.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Tags } from 'lucide-react';
import { usePayables } from '../../queries';
import { Button } from '../../ui';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';
import { ClassificationDrawer } from './classification-drawer';

export function ClassificationTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: STAGE_STATUS.classification, page }, { enabled: !!companyId });
  const data = list.data;
  const [classifyId, setClassifyId] = useState<string | null>(null);

  return (
    <>
      <PayableTable
        companyId={companyId}
        stageLabel={t('payables.stages.classification')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        extraColumn={{
          header: t('payables.classify.title'),
          cell: (item) => (
            <Button icon={<Tags />} onClick={() => setClassifyId(item.payableId)}>
              {t('payables.classify.title')}
            </Button>
          ),
        }}
      />
      {classifyId && <ClassificationDrawer payableId={classifyId} onClose={() => setClassifyId(null)} />}
    </>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- classification-drawer`
Expected: PASS (1 test).

- [ ] **Step 6: Commit**

```bash
git add web/src/pages/payables/classification-drawer.tsx web/src/pages/payables/classification-tab.tsx web/src/pages/payables/classification-drawer.test.tsx
git commit -m "feat(AEL-626): classification tab + AI-prefilled classification drawer"
```

---

### Task 8: Payment tab (period-grouped scheduling)

List `Scheduled` installments grouped by period with a summary header; register a baixa via the copied `PaymentForm` drawer; show the `InRemittance` badge; link to the existing Remessas page for batch generation (no CNAB code changes).

**Files:**
- Create: `web/src/pages/payables/payment-tab.tsx`
- Test: `web/src/pages/payables/payment-tab.test.tsx`

**Interfaces:**
- Consumes: `usePayables`, `usePayable` from `../../queries`; `groupByPeriod` from `./payment-periods`; `InstallmentStatusBadge` from `./badges`; `PaymentForm` from `./payment-form`; `Panel`, `Button`, `Drawer`, `EmptyState` from `../../ui`; `brl` from `../../lib`.
- Produces: `export function PaymentTab({ companyId }: { companyId: string })`. Lists `usePayables({ status: 'Scheduled' })` items, groups with `groupByPeriod`, renders a per-period `Panel` with subtotal, each row has vendor/sequence/amount + a "Register" button that opens the `PaymentForm` for that payable+installment, a header total, and a "Generate Remittance" button linking to `/app/remessas`.

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/payables/payment-tab.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUsePayables } = vi.hoisted(() => ({ mockUsePayables: vi.fn() }));
vi.mock('../../queries', () => ({ usePayables: mockUsePayables, usePayable: vi.fn() }));

import { PaymentTab } from './payment-tab';

const item = { payableId: 'payable_1', installmentId: 'i1', sequence: 1, installmentCount: 1, counterpartyName: 'ACME', description: 'NF 1', costCenterId: 'cc_1', categoryId: null, dueDate: '2026-06-20', amount: 800, installmentStatus: 'Open', payableStatus: 'Scheduled' };

beforeEach(() => mockUsePayables.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } }));

describe('PaymentTab', () => {
  it('queries Scheduled payables and renders a period group with the vendor + amount', () => {
    render(<MemoryRouter><PaymentTab companyId="company_1" /></MemoryRouter>);
    expect(mockUsePayables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'Scheduled' }),
      expect.anything(),
    );
    expect(screen.getByText('ACME')).toBeInTheDocument();
    expect(screen.getByText(/R\$/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- payment-tab`
Expected: FAIL — cannot resolve `./payment-tab`.

- [ ] **Step 3: Implement `payment-tab.tsx`**

Create `web/src/pages/payables/payment-tab.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Receipt, FileOutput } from 'lucide-react';
import { type PayableListItem } from '../../api';
import { usePayables, usePayable } from '../../queries';
import { brl } from '../../lib';
import { Panel, Button, Drawer, EmptyState } from '../../ui';
import { fmtDate } from '../../datetime';
import { groupByPeriod } from './payment-periods';
import { InstallmentStatusBadge } from './badges';
import { PaymentForm } from './payment-form';

export function PaymentTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: 'Scheduled', page }, { enabled: !!companyId });
  const items = list.data?.items ?? [];
  const groups = groupByPeriod(items);
  const total = items.reduce((s, i) => s + i.amount, 0);

  const [baixa, setBaixa] = useState<PayableListItem | null>(null);
  const detail = usePayable(baixa?.payableId);
  const installment = detail.data?.installments.find((i) => i.id === baixa?.installmentId) ?? null;

  if (items.length === 0) {
    return <EmptyState icon={<Receipt />} title={t('payables.empty.noItemsTitle')} sub={t('payables.empty.noItemsSub')} />;
  }

  return (
    <>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <strong>{t('workflow.payment.weekSummary', { amount: brl(total), count: items.length })}</strong>
        <Button icon={<FileOutput />} onClick={() => nav('/app/remessas')}>{t('payables.payment.generateRemittance')}</Button>
      </div>

      {groups.map((g) => (
        <Panel key={g.key} title={`${t('workflow.payment.group' + g.key[0].toUpperCase() + g.key.slice(1))} · ${brl(g.subtotal)}`}>
          <table>
            <thead>
              <tr>
                <th>{t('payables.columns.dueDate')}</th>
                <th>{t('payables.columns.vendor')}</th>
                <th>{t('payables.columns.installment')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.amount')}</th>
                <th>{t('payables.columns.status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {g.items.map((l) => (
                <tr key={l.installmentId}>
                  <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                  <td>{l.counterpartyName || '—'}</td>
                  <td className='t-mono'>{l.sequence}/{l.installmentCount}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>{brl(l.amount)}</td>
                  <td><InstallmentStatusBadge s={l.installmentStatus} /></td>
                  <td>
                    {l.installmentStatus !== 'InRemittance' && l.installmentStatus !== 'Paid' && (
                      <Button icon={<Receipt />} onClick={() => setBaixa(l)}>{t('payments.register')}</Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      ))}

      <Drawer open={!!baixa} onClose={() => setBaixa(null)} title={t('payments.register')} sub={baixa?.counterpartyName}>
        {detail.data && installment && (
          <PaymentForm payable={detail.data} installment={installment} onDone={() => setBaixa(null)} onCancel={() => setBaixa(null)} />
        )}
      </Drawer>
    </>
  );
}
```

(The `workflow.payment.group{Today,ThisWeek,NextWeek,Future}` and `workflow.payment.weekSummary` keys were added in plan 1's `workflow` namespace — verify they exist in `pt-BR.ts`; if a key is missing, add it under `workflow.payment`. `payments.register` is an existing key used by the current installments table.)

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- payment-tab`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/payment-tab.tsx web/src/pages/payables/payment-tab.test.tsx
git commit -m "feat(AEL-626): payment tab with period grouping + baixa drawer"
```

---

### Task 9: `layout.tsx` — dashboard + stage tabs shell

The section shell: header + `PendencyDashboard` + `StageTabBar`, active stage driven by `?stage=`, mounting the active tab.

**Files:**
- Create: `web/src/pages/payables/layout.tsx`
- Test: `web/src/pages/payables/layout.test.tsx`

**Interfaces:**
- Consumes: `useApp` from `../../store`; `usePayableStageSummary` from `../../queries`; `EmpresaPicker` from `../shared`; `PageHeader`, `Button`, `EmptyState` from `../../ui`; `PendencyDashboard`, `StageTabBar`, `type StageTab` from `../../components/workflow`; `STAGE_ORDER`, `STAGE_STATUS`, `dashboardCards`, `isStageKey`, `type StageKey` from `./stages`; the five tab components; `useSearchParams`/`useNavigate` from `react-router-dom`.
- Produces: `export function PayablesLayout()`. Reads `?stage=` (default `entry`), renders dashboard cards (clicking a card sets `?stage=`), the tab bar (counts from the summary buckets), and the active tab.

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/payables/layout.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageSummary, mockUsePayables } = vi.hoisted(() => ({ mockUseStageSummary: vi.fn(), mockUsePayables: vi.fn() }));
vi.mock('../../store', () => ({ useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }) }));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({ usePayableStageSummary: mockUseStageSummary, usePayables: mockUsePayables }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {}, categoriaNome: {} }) }));

import { PayablesLayout } from './layout';

beforeEach(() => {
  mockUseStageSummary.mockReturnValue({
    data: {
      entry: { count: 2, totalAmount: 100 }, classification: { count: 0, totalAmount: 0 },
      approval: { count: 1, totalAmount: 50 }, payment: { count: 0, totalAmount: 0 }, reconciled: { count: 9, totalAmount: 900 },
    },
  });
  mockUsePayables.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

describe('PayablesLayout', () => {
  it('renders the dashboard cards and stage tabs, defaulting to the entry tab', () => {
    render(<MemoryRouter initialEntries={['/app/payables']}><PayablesLayout /></MemoryRouter>);
    expect(screen.getByTestId('empresa-picker')).toBeInTheDocument();
    // Stage labels appear in both the dashboard and the tab bar.
    expect(screen.getAllByText('Entrada').length).toBeGreaterThan(0);
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
  });
  it('switches tab when a dashboard card is clicked', () => {
    render(<MemoryRouter initialEntries={['/app/payables']}><PayablesLayout /></MemoryRouter>);
    fireEvent.click(screen.getByText('Ver pendências', { selector: '*' }));
    // entry tab queried with the Received status (default active tab still renders a list)
    expect(mockUsePayables).toHaveBeenCalled();
  });
});
```

(If `getByText('Ver pendências', ...)` is ambiguous because every card has the link, use `screen.getAllByText('Ver pendências')[1]` to click the approval card. Adjust to the rendered DOM.)

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- layout`
Expected: FAIL — cannot resolve `./layout`.

- [ ] **Step 3: Implement `layout.tsx`**

Create `web/src/pages/payables/layout.tsx`:

```tsx
import { useTranslation } from 'react-i18next';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { Plus, Wallet } from 'lucide-react';
import { useApp } from '../../store';
import { usePayableStageSummary } from '../../queries';
import { PageHeader, Button, EmptyState } from '../../ui';
import { EmpresaPicker } from '../shared';
import { PendencyDashboard, StageTabBar, type StageTab } from '../../components/workflow';
import { STAGE_ORDER, dashboardCards, isStageKey, type StageKey } from './stages';
import { EntryTab } from './entry-tab';
import { ClassificationTab } from './classification-tab';
import { ApprovalTab } from './approval-tab';
import { PaymentTab } from './payment-tab';
import { ReconciledTab } from './reconciled-tab';

export function PayablesLayout() {
  const { t } = useTranslation();
  const nav = useNavigate();
  const { empresa, empresaId } = useApp();
  const [params, setParams] = useSearchParams();

  const stageParam = params.get('stage');
  const active: StageKey = isStageKey(stageParam) ? stageParam : 'entry';
  const setStage = (key: string) => setParams((prev) => { prev.set('stage', key); return prev; });

  const summaryQ = usePayableStageSummary(empresaId);
  const summary = summaryQ.data;

  const cards = summary ? dashboardCards(summary, t) : [];
  const tabs: StageTab[] = STAGE_ORDER.map((key) => ({
    key,
    label: t(`payables.stages.${key}`),
    count: summary ? summary[key].count : undefined,
    badge: key === 'approval' && summary && summary.approval.count > 0 ? 'warning' : undefined,
  }));

  return (
    <>
      <PageHeader title={t('payables.title')} sub={t('payables.subtitle')}>
        <EmpresaPicker />
        <Button variant='brand' icon={<Plus />} disabled={!empresa} style={{ alignSelf: 'flex-end' }} onClick={() => nav('/app/payables/novo')}>
          {t('payables.new')}
        </Button>
      </PageHeader>

      {!empresa ? (
        <EmptyState icon={<Wallet />} title={t('payables.empty.noCompanyTitle')} sub={t('payables.empty.noCompanySub')} />
      ) : (
        <>
          {cards.length > 0 && <PendencyDashboard cards={cards} onCardClick={setStage} />}
          <StageTabBar tabs={tabs} active={active} onChange={setStage} />
          {active === 'entry' && <EntryTab companyId={empresaId} />}
          {active === 'classification' && <ClassificationTab companyId={empresaId} />}
          {active === 'approval' && <ApprovalTab companyId={empresaId} />}
          {active === 'payment' && <PaymentTab companyId={empresaId} />}
          {active === 'reconciled' && <ReconciledTab companyId={empresaId} />}
        </>
      )}
    </>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- layout`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/layout.tsx web/src/pages/payables/layout.test.tsx
git commit -m "feat(AEL-626): payables layout — dashboard + stage tabs shell"
```

---

### Task 10: Detail page (audit timeline + SLA chips + forecast)

The per-payable detail page: adapted from the existing detail but in the new self-contained folder, with `SlaChips` in the header, an `AuditTimeline` fed by `usePayableAuditTrail`, and a `BalanceForecastPanel` shown for `PendingApproval`. Reuses the copied leaf components.

**Files:**
- Create: `web/src/pages/payables/detail.tsx`
- Test: `web/src/pages/payables/detail.test.tsx`

**Interfaces:**
- Consumes: `usePayable`, `usePayableAuditTrail`, `useApprovePayable`, `useRejectPayable`, `useCancelPayable`, `useDeletePayableAttachment`, `useCostCenters`, `useExpenseCategories`, `useChartAccounts` from `../../queries`; `AuditTimeline`, `SlaChips` from `../../components/workflow`; the copied `PayableStatusBadge`, `InstallmentsTable`, `PaymentHistory`, `PaymentForm`, `AttachmentsPanel`; `Button`, `Panel`, `Drawer`, `ConfirmDialog`, `Input`, `EmptyState` from `../../ui`.
- Produces: `export function PayableDetailPage()` (reads `:id` from the route).

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/payables/detail.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUsePayable, mockUseAudit } = vi.hoisted(() => ({ mockUsePayable: vi.fn(), mockUseAudit: vi.fn() }));
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({ id: 'payable_1' }) };
});
vi.mock('../../queries', () => ({
  usePayable: mockUsePayable,
  usePayableAuditTrail: mockUseAudit,
  useApprovePayable: () => ({ mutate: vi.fn() }),
  useRejectPayable: () => ({ mutate: vi.fn() }),
  useCancelPayable: () => ({ mutate: vi.fn() }),
  useDeletePayableAttachment: () => ({ mutate: vi.fn() }),
  useCostCenters: () => ({ data: [] }),
  useExpenseCategories: () => ({ data: [] }),
  useChartAccounts: () => ({ data: [] }),
}));

import { PayableDetailPage } from './detail';

beforeEach(() => {
  mockUsePayable.mockReturnValue({
    data: {
      id: 'payable_1', companyId: 'company_1', counterpartyName: 'ACME', description: 'NF 1', status: 'Scheduled',
      totalAmount: 1000, costCenterId: 'cc_1', categoryId: null, accountingAccountId: null, financialAccountId: null,
      documentNumber: 'NF-1', issueDate: '2026-06-01', installments: [{ id: 'i1', sequence: 1, dueDate: '2026-06-30', amount: 1000, status: 'Open', paidAmount: 0, payments: [] }], attachments: [],
    },
  });
  mockUseAudit.mockReturnValue({
    data: [
      { type: 'Created', actorId: 'u1', actorName: 'João', timestamp: '2026-06-01T10:00:00Z', metadata: {} },
      { type: 'Approved', actorId: 'u2', actorName: 'Maria', timestamp: '2026-06-02T10:00:00Z', metadata: {} },
    ],
    isLoading: false,
  });
});

describe('PayableDetailPage', () => {
  it('renders the payable header and the audit timeline events', () => {
    renderWithProviders(<PayableDetailPage />);
    expect(screen.getByText('ACME')).toBeInTheDocument();
    expect(screen.getByText('Título criado')).toBeInTheDocument();
    expect(screen.getByText('Aprovado')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- detail`
Expected: FAIL — cannot resolve `./detail`.

- [ ] **Step 3: Implement `detail.tsx`**

Read the current `web/src/pages/contas-a-pagar/detail.tsx` in full and recreate it as `web/src/pages/payables/detail.tsx` with these specific changes (everything else — the info grid, reject/cancel dialogs, payment drawer, attachment delete — stays structurally the same, importing the copied siblings):
1. Rename the component `export function PayableDetailPage()`.
2. All `nav('/app/contas-a-pagar/...')` targets become `'/app/payables/...'` (back link → `/app/payables`, edit → `/app/payables/${p.id}/editar`).
3. Add `SlaChips` next to the `PayableStatusBadge` in the header: `{p.installments[0] && <SlaChips stageLabel={t('payables.status.' + p.status)} dueDate={p.installments[0].dueDate} />}`.
4. Add an audit timeline panel after the attachments panel:
```tsx
<Panel title={t('payables.cards.timeline')}>
  <AuditTimeline events={audit.data ?? []} loading={audit.isLoading} />
</Panel>
```
   where `const audit = usePayableAuditTrail(id);`.
5. When `p.status === 'PendingApproval'`, render a `BalanceForecastPanel`-backed approve flow (or, to keep this page read-mostly, keep the existing approve/reject buttons and rely on the approval tab's forecast panel — the spec says the panel is "visible when status is PendingApproval"; render `BalanceForecastBanner` inline in the header area for that status: `{p.status === 'PendingApproval' && <BalanceForecastBanner companyId={p.companyId} additionalOutflow={p.totalAmount} />}`).
6. Import `AuditTimeline`, `SlaChips`, `BalanceForecastBanner` from `../../components/workflow`; import the leaf components from `./` (the copied siblings) instead of the old folder.
7. Add the i18n key `payables.cards.timeline: 'Linha do tempo'` to `pt-BR.ts` under `payables.cards` if it doesn't already exist.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- detail`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/detail.tsx web/src/pages/payables/detail.test.tsx web/src/i18n/locales/pt-BR.ts
git commit -m "feat(AEL-626): payable detail with audit timeline + SLA chips + forecast"
```

---

### Task 11: Create/edit form pages

Copy the existing payable create/edit form into the self-contained folder, changing only the post-save navigation targets.

**Files:**
- Create: `web/src/pages/payables/payable-form.tsx` (copy of `contas-a-pagar/payable-form.tsx` with renamed exports + redirected navigation)

**Interfaces:**
- Produces: `export function NewPayablePage()` and `export function EditPayablePage()` (the new names for the copied `NovaContaPagarPage` / `EditarContaPagarPage`).

- [ ] **Step 1: Copy and adapt the form**

Copy `web/src/pages/contas-a-pagar/payable-form.tsx` to `web/src/pages/payables/payable-form.tsx`. Then make exactly these edits in the copy:
1. Rename the exported components: `NovaContaPagarPage` → `NewPayablePage`, `EditarContaPagarPage` → `EditPayablePage`.
2. Change every navigation target from `/app/contas-a-pagar/...` to `/app/payables/...` (post-create redirect to the created payable's detail `/app/payables/${created.id}`, post-edit redirect to `/app/payables/${id}`, back/cancel links to `/app/payables`).
3. Leave all imports as `../../*` and `./vendor-picker` / `./fiscal-xml-reader` (the copied siblings) — they resolve from `payables/`.

- [ ] **Step 2: Verify it type-checks**

Run (from `web/`): `pnpm typecheck`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add web/src/pages/payables/payable-form.tsx
git commit -m "feat(AEL-626): payable create/edit form pages (self-contained)"
```

---

### Task 12: Routes, sidebar entry, barrel, and full verification

Wire the new pages into the router and sidebar, export them, and verify the whole web app.

**Files:**
- Create: `web/src/pages/payables/index.tsx` (barrel)
- Modify: `web/src/App.tsx` (routes + sidebar nav item + breadcrumb)

**Interfaces:**
- Produces: barrel exporting `PayablesLayout`, `PayableDetailPage`, `NewPayablePage`, `EditPayablePage`. Routes `/app/payables`, `/app/payables/novo`, `/app/payables/:id`, `/app/payables/:id/editar`. A sidebar item under Finance pointing to `/app/payables`.

- [ ] **Step 1: Create the barrel**

Create `web/src/pages/payables/index.tsx`:

```tsx
export { PayablesLayout } from './layout';
export { PayableDetailPage } from './detail';
export { NewPayablePage, EditPayablePage } from './payable-form';
```

- [ ] **Step 2: Register the routes in `App.tsx`**

In `web/src/App.tsx`, add an import alongside the other page imports:

```tsx
import { PayablesLayout, PayableDetailPage, NewPayablePage, EditPayablePage } from './pages/payables';
```

Add these routes next to the existing `contas-a-pagar` routes (keep the old ones — migration removes them later):

```tsx
        <Route path='payables' element={<PayablesLayout />} />
        <Route path='payables/novo' element={<NewPayablePage />} />
        <Route path='payables/:id' element={<PayableDetailPage />} />
        <Route path='payables/:id/editar' element={<EditPayablePage />} />
```

- [ ] **Step 3: Add the sidebar entry + breadcrumb**

In `web/src/App.tsx`, in the Finance section of the sidebar (next to the existing `nav.accountsPayable` item), add a new `NavItem`:

```tsx
          <NavItem
            icon={<Layers />}
            label={t('nav.payablesWorkflow')}
            active={at('payables')}
            onClick={() => go('/app/payables')}
          />
```

Add `Layers` to the `lucide-react` import in `App.tsx` if not already imported. Add a breadcrumb branch mirroring the existing `contas-a-pagar` one: `at('payables') ? (<>{t('nav.sections.finance')} <b>/ {t('nav.payablesWorkflow')}</b></>) : ...`. Add the i18n key `nav.payablesWorkflow: 'Contas a pagar (esteira)'` to `pt-BR.ts` under the existing `nav` object.

- [ ] **Step 4: Full verification**

Run (from `web/`):
- `pnpm test` — Expected: all tests pass (the new payables tests + the full existing suite).
- `pnpm typecheck` — Expected: PASS.
- `pnpm build` — Expected: build succeeds.

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/payables/index.tsx web/src/App.tsx web/src/i18n/locales/pt-BR.ts
git commit -m "feat(AEL-626): wire payables routes + sidebar entry"
```

---

## Self-Review

**Spec coverage (this plan = spec §2 Payables Pages):**
- §2.1 Routing (`/app/payables`, `?stage=`, `/novo`, `/:id`, `/:id/editar`) → Tasks 9, 11, 12 ✓
- §2.2 Layout (PageHeader + EmpresaPicker + PendencyDashboard + StageTabBar, stage→status map) → Tasks 2, 9 ✓
- §2.3 Entry tab → Task 6 ✓
- §2.4 Classification tab (AI suggestion + drawer pre-filled from `suggested*`) → Task 7 ✓
- §2.5 Approval tab (BalanceForecastBanner + inline approve/reject + BalanceForecastPanel) → Task 6 ✓
- §2.6 Payment tab (today/week summary + period grouping + baixa + remittance link) → Tasks 3, 8 ✓ (batch "Generate Remittance" links to the existing Remessas page rather than re-implementing CNAB — consistent with the no-CNAB-changes constraint; noted as a deliberate scope choice)
- §2.7 Reconciled tab → Task 6 ✓
- §2.8 Detail (SlaChips + AuditTimeline + BalanceForecastPanel/Banner + reused installments/payments/attachments) → Task 10 ✓
- API/query hooks + i18n → Task 1 ✓
- Leaf component reuse (migration-safe copy) → Task 4 ✓

**Placeholder scan:** Tasks 10 and 11 instruct "read the current file and recreate with these exact changes" — this is a precise transform of a verbatim source (the existing file), with each change enumerated, not an open-ended placeholder. Every new component has full code. The few "verify key exists, else add it" notes name the exact key and fallback.

**Type consistency:** `StageKey`/`STAGE_STATUS`/`dashboardCards` (Task 2) are consumed by Tasks 6–9 with matching names. `PeriodGroup`/`groupByPeriod` (Task 3) consumed by Task 8. `usePayableStageSummary`/`usePayableAuditTrail` (Task 1) consumed by Tasks 9/10. `PayableTable` props (Task 5) consumed by Tasks 6/7. Copied leaf component signatures (Task 4) match their usage in Tasks 8/10. The workflow components' props (`PendencyDashboard cards/onCardClick`, `StageTabBar tabs/active/onChange`, `SlaChips stageLabel/dueDate`, `AuditTimeline events/loading`, `BalanceForecastBanner companyId/additionalOutflow`, `BalanceForecastPanel companyId/impactAmount/open/onClose/onConfirm/busy`) match plan-1 definitions.

**Cross-task file note:** Tasks 1, 10, 12 each append to `web/src/i18n/locales/pt-BR.ts` (distinct keys) and Tasks 12 edits `App.tsx`; sequential execution avoids conflicts.

---

## Decomposition Note

This is plan **3 of 5** (AEL-626 under epic AEL-620), stacked on the plan-2 branch (AEL-621) for the regenerated schema types. It consumes plan-1 workflow components and plan-2 endpoints. Plan 4 (receivables pages) mirrors this structure for the 3-stage receivable flow; plan 5 (approvals inbox) reuses the forecast + SLA components. The old `contas-a-pagar/` routes remain during the transition; a later migration step (spec §8) redirects and removes them once the new pages are validated.
