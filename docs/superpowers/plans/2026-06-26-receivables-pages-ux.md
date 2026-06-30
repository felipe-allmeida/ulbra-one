# Receivables Pages (Stage-Based UX) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the new stage-based receivables experience in `web/src/pages/receivables/` — a triage dashboard (3 informational buckets) + 2 stage tabs (A Receber · Conciliado), a per-title detail page with an audit timeline + SLA chips, mirroring the payables pages but for the simpler receivable flow (no classification/approval).

**Architecture:** New self-contained folder `web/src/pages/receivables/`, structurally mirroring `web/src/pages/payables/`. The dashboard shows 3 cards (Entrada/Recebimento/Conciliado) from `GET /api/receivables/stage-summary`; navigation uses **2 tabs**: **A Receber** (all `AwaitingReceipt` — a period-grouped baixa queue with a value summary + Register Receipt) and **Conciliado** (`Received`, read-only). The Entrada and Recebimento dashboard cards both navigate to the A-Receber tab; the Conciliado card to the Conciliado tab. The detail page reuses leaf components copied verbatim from `contas-a-receber/` and adds `SlaChips` + an `AuditTimeline` fed by `GET /api/receivables/{id}/audit-trail`. The folder is self-contained so the later migration can delete `contas-a-receber/`.

**Tech Stack:** React 19, TypeScript, Vite, TanStack Query, react-hook-form/zod, react-i18next, react-router-dom v6, the workflow components from `web/src/components/workflow/`, vitest + @testing-library/react.

## Global Constraints

- All code in English; only i18n VALUES in pt-BR; no hardcoded pt-BR in JSX (every user-facing string via `t(...)`).
- Follow existing FE patterns: `web/src/ui.tsx`, `web/src/queries.ts`, `web/src/api.ts`, types via `Schemas['...']`, `brl()`/`fmtDate`/`fmtDateTime`.
- Reuse the workflow components from `web/src/components/workflow/` (`PendencyDashboard`, `StageTabBar`, `SlaChips`, `AuditTimeline`) — do NOT re-implement.
- Generic/multitenant; no Ulbra-specific logic.
- Do NOT modify `contas-a-receber/` or any CNAB/remittance code — new files only (plus additive edits to `queries.ts`, `api.ts`, `App.tsx`, `i18n/locales/pt-BR.ts`).
- The `receivables/` folder must be SELF-CONTAINED — NO imports from `../contas-a-receber/`.
- Tests use `renderWithProviders` / `import '../../i18n'` / `vi.mock` of `../../queries`, `../../store`, `react-router-dom` (the established pattern). Tests for everything.
- Verification from `web/`: `pnpm test`, `pnpm typecheck`, `pnpm exec eslint <files>`, `pnpm build`. NOTE: pre-commit `lint-staged` runs eslint/prettier but NOT typecheck — always run `pnpm typecheck` explicitly before committing.

**Verified facts (use verbatim):**
- `ReceivableStageSummaryResponse` type alias ALREADY EXISTS in `api.ts` (`Schemas['ReceivableStageSummaryResponse']`, imported in queries.ts but unused). `AuditTrailEvent` alias ALREADY EXISTS (added in plan 3). Both `StageBucket` and `AuditTrailEventResponse` schema types are present.
- `ReceivableStatus = 'AwaitingReceipt' | 'Received' | 'Cancelled' | 'WrittenOff'`. `ReceivableInstallmentStatus = 'Open' | 'PartiallyReceived' | 'Received'`.
- `ReceivableStageSummaryResponse = { entry: StageBucket; receipt: StageBucket; reconciled: StageBucket }` (matches `GET /api/receivables/stage-summary`).
- `ReceivableListItem` fields: `receivableId, installmentId, description, payerName, costCenterId, amount, dueDate, sequence, installmentCount, receivableStatus, installmentStatus, isOverdue`.
- `Receivable` (detail) fields: `id, companyId, payerId, payerName, costCenterId, description, documentNumber, issueDate, totalAmount, status, installments[] (each: id, sequence, dueDate, amount, status, receivedAmount, receipts[], isOverdue), cancelReason, cancelledBy, cancelledAt, origin`.
- `useReceivables(params, options?)`, `useReceivable(id?)`, `useCancelReceivable()` (`.mutate({ id, reason })`), `useRegisterReceipt()` (`.mutate({ receivableId, installmentId, body })`), `useReverseReceipt()`, `useUploadReceiptProof()` already exist. `ReceivablesParams = { companyId?, status?, payerId?, costCenterId?, dueFrom?, dueTo?, received?, page?, pageSize? }`.
- `api.listReceivables`, `api.getReceivable`, `api.cancelReceivable`, `api.registerReceipt` already exist. There is NO `api.receivableStageSummary` / `api.receivableAuditTrail` and NO `useReceivableStageSummary` / `useReceivableAuditTrail` yet — Task 1 adds them.
- These `contas-a-receber/` leaf components copy verbatim (same import depth, generic): `receipt-form.tsx` (`ReceiptForm({ receivable, installment, onDone, onCancel })`), `receipt-history.tsx` (`ReceiptHistory({ receivable, installment })`), `installments-table.tsx` (`InstallmentsTable({ receivable, onRegister })`), `badges.tsx` (`ReceivableStatusBadge({ s })`, `ReceivableInstallmentStatusBadge({ s })`, `OverdueBadge()`), `customer-picker.tsx` (`CustomerPicker({ companyId, value, onChange, invalid? })`), `use-names.ts` (`useNames(companyId) → { centros, centroNome }`), `download.ts` (`downloadReceiptProof(receivableId, receipt)`).
- The reference implementation to mirror is `web/src/pages/payables/` (just built): `stages.tsx`, `payment-periods.ts`, `payable-table.tsx`, `layout.tsx`, `detail.tsx`, the tab files. Receivables files mirror these with substitutions Payable→Receivable, payment→receipt, `/app/payables`→`/app/receivables`, `usePayables`→`useReceivables`.

---

### Task 1: API methods, query hooks, and i18n keys

**Files:**
- Modify: `web/src/api.ts` (2 methods)
- Modify: `web/src/queries.ts` (2 query keys + 2 hooks)
- Modify: `web/src/i18n/locales/pt-BR.ts` (`receivables.stages.*` + a few page keys under the existing `receivables` object)

**Interfaces:**
- Produces:
  - `api.receivableStageSummary(companyId): Promise<ReceivableStageSummaryResponse>` (GET `/receivables/stage-summary?companyId=`), `api.receivableAuditTrail(id): Promise<AuditTrailEvent[]>` (GET `/receivables/{id}/audit-trail`).
  - `useReceivableStageSummary(companyId?)`, `useReceivableAuditTrail(id?)`.
  - i18n keys under `receivables.stages.{entry,receipt,reconciled}`, `receivables.tabs.{areceber,reconciled}`, `receivables.dashboard.title`, `receivables.reconciled.{receivedAmount,lastReceipt}`.

- [ ] **Step 1: Add the API methods in `api.ts`**

In `web/src/api.ts`, after `getReceivable`, add:

```ts
  receivableStageSummary: (companyId: string): Promise<ReceivableStageSummaryResponse> => {
    const q = new URLSearchParams({ companyId });
    return request<ReceivableStageSummaryResponse>(`/receivables/stage-summary?${q}`);
  },
  receivableAuditTrail: (id: string): Promise<AuditTrailEvent[]> =>
    request<AuditTrailEvent[]>(`/receivables/${id}/audit-trail`),
```

(`ReceivableStageSummaryResponse` and `AuditTrailEvent` aliases already exist in this file.)

- [ ] **Step 2: Add query keys + hooks in `queries.ts`**

Add to `queryKeys`:

```ts
  receivableStageSummary: (companyId: string) => ['receivableStageSummary', companyId] as const,
  receivableAuditTrail: (id: string) => ['receivableAuditTrail', id] as const,
```

Ensure `ReceivableStageSummaryResponse` and `AuditTrailEvent` are in the `import type ... from './api'` block (the former is already imported; add `AuditTrailEvent` if not present in this file's import). Add the hooks near `useReceivable`:

```ts
export function useReceivableStageSummary(companyId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.receivableStageSummary(companyId ?? ''),
    queryFn: () => api.receivableStageSummary(companyId!),
    enabled: !!companyId,
  });
}

export function useReceivableAuditTrail(id: string | undefined) {
  return useQuery({
    queryKey: queryKeys.receivableAuditTrail(id ?? ''),
    queryFn: () => api.receivableAuditTrail(id!),
    enabled: !!id,
  });
}
```

- [ ] **Step 3: Add i18n keys**

In `web/src/i18n/locales/pt-BR.ts`, inside the existing top-level `receivables: { ... }` object, add (do not duplicate existing keys):

```ts
    stages: { entry: 'Entrada', receipt: 'Recebimento', reconciled: 'Conciliado' },
    tabs: { areceber: 'A receber', reconciled: 'Conciliado' },
    dashboard: { title: 'Triagem' },
    reconciled: { receivedAmount: 'Valor recebido', lastReceipt: 'Último recebimento' },
```

(If a `receivables.tabs` object already exists from the legacy page, MERGE these keys into it rather than redeclaring — read the file first. The legacy `tabs` keys were `all/overdue/thisMonth/future/received`; add `areceber`/`reconciled` alongside.)

- [ ] **Step 4: Verify**

Run (from `web/`): `pnpm typecheck` → PASS. `pnpm exec eslint src/api.ts src/queries.ts` → 0 errors.

- [ ] **Step 5: Commit**

```bash
git add web/src/api.ts web/src/queries.ts web/src/i18n/locales/pt-BR.ts
git commit -m "feat(AEL-633): receivable stage-summary + audit-trail api/query hooks + i18n"
```

---

### Task 2: `stages.ts` — receivable stage definitions + dashboard cards

3 dashboard buckets (entry/receipt/reconciled) but 2 navigation tabs (areceber/reconciled). The dashboard cards map to tabs in the layout (Task 7).

**Files:**
- Create: `web/src/pages/receivables/stages.tsx`
- Test: `web/src/pages/receivables/stages.test.ts`

**Interfaces:**
- Consumes: `ReceivableStatus`, `ReceivableStageSummaryResponse`, `StageBucket` from `../../api`; `PendencyCard` from `../../components/workflow`.
- Produces:
  - `export type TabKey = 'areceber' | 'reconciled';`
  - `export const TAB_ORDER: TabKey[] = ['areceber', 'reconciled'];`
  - `export const TAB_STATUS: Record<TabKey, ReceivableStatus> = { areceber: 'AwaitingReceipt', reconciled: 'Received' };`
  - `export type DashboardKey = 'entry' | 'receipt' | 'reconciled';`
  - `export function dashboardCards(summary: ReceivableStageSummaryResponse, t: (k: string) => string): PendencyCard[]` — 3 cards keyed by DashboardKey (`stageKey: 'entry'|'receipt'|'reconciled'`), label `t('receivables.stages.'+key)`, count/totalAmount from `summary[key]`, icon per key.
  - `export function cardTargetTab(cardKey: string): TabKey` — `'reconciled' → 'reconciled'`, else `'areceber'`.
  - `export function isTabKey(v: string | null): v is TabKey`

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/receivables/stages.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { TAB_ORDER, TAB_STATUS, dashboardCards, cardTargetTab, isTabKey } from './stages';

const summary = {
  entry: { count: 4, totalAmount: 1200 },
  receipt: { count: 2, totalAmount: 800 },
  reconciled: { count: 9, totalAmount: 5000 },
};

describe('receivable stages', () => {
  it('has two tabs mapped to receivable statuses', () => {
    expect(TAB_ORDER).toEqual(['areceber', 'reconciled']);
    expect(TAB_STATUS.areceber).toBe('AwaitingReceipt');
    expect(TAB_STATUS.reconciled).toBe('Received');
  });
  it('builds three dashboard cards from the summary buckets', () => {
    const cards = dashboardCards(summary, (k) => k);
    expect(cards.map((c) => c.stageKey)).toEqual(['entry', 'receipt', 'reconciled']);
    expect(cards[0]).toMatchObject({ count: 4, totalAmount: 1200, label: 'receivables.stages.entry' });
  });
  it('maps entry/receipt cards to the areceber tab and reconciled to reconciled', () => {
    expect(cardTargetTab('entry')).toBe('areceber');
    expect(cardTargetTab('receipt')).toBe('areceber');
    expect(cardTargetTab('reconciled')).toBe('reconciled');
  });
  it('isTabKey guards valid tab keys', () => {
    expect(isTabKey('areceber')).toBe(true);
    expect(isTabKey('entry')).toBe(false);
    expect(isTabKey(null)).toBe(false);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- receivables/stages`
Expected: FAIL — cannot resolve `./stages`.

- [ ] **Step 3: Implement `stages.tsx`**

Create `web/src/pages/receivables/stages.tsx`:

```tsx
import { type ReactNode } from 'react';
import { Inbox, HandCoins, Archive } from 'lucide-react';
import { type ReceivableStatus, type ReceivableStageSummaryResponse } from '../../api';
import { type PendencyCard } from '../../components/workflow';

export type TabKey = 'areceber' | 'reconciled';
export const TAB_ORDER: TabKey[] = ['areceber', 'reconciled'];
export const TAB_STATUS: Record<TabKey, ReceivableStatus> = {
  areceber: 'AwaitingReceipt',
  reconciled: 'Received',
};

export type DashboardKey = 'entry' | 'receipt' | 'reconciled';
const DASHBOARD_ORDER: DashboardKey[] = ['entry', 'receipt', 'reconciled'];

function cardIcon(key: DashboardKey): ReactNode {
  switch (key) {
    case 'entry':
      return <Inbox size={18} />;
    case 'receipt':
      return <HandCoins size={18} />;
    case 'reconciled':
      return <Archive size={18} />;
  }
}

export function dashboardCards(summary: ReceivableStageSummaryResponse, t: (k: string) => string): PendencyCard[] {
  return DASHBOARD_ORDER.map((key) => {
    const bucket = summary[key];
    return { stageKey: key, label: t(`receivables.stages.${key}`), icon: cardIcon(key), count: bucket.count, totalAmount: bucket.totalAmount };
  });
}

export function cardTargetTab(cardKey: string): TabKey {
  return cardKey === 'reconciled' ? 'reconciled' : 'areceber';
}

export function isTabKey(v: string | null): v is TabKey {
  return v != null && (TAB_ORDER as string[]).includes(v);
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- receivables/stages`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/receivables/stages.tsx web/src/pages/receivables/stages.test.ts
git commit -m "feat(AEL-633): receivable stage/tab definitions + dashboard-card mapping"
```

---

### Task 3: `receipt-periods.ts` — group A-Receber installments by period

Mirror `payables/payment-periods.ts`, retyped for `ReceivableListItem`.

**Files:**
- Create: `web/src/pages/receivables/receipt-periods.ts`
- Test: `web/src/pages/receivables/receipt-periods.test.ts`

**Interfaces:**
- Consumes: `ReceivableListItem` from `../../api`.
- Produces: `export type PeriodKey = 'today' | 'thisWeek' | 'nextWeek' | 'future';`, `export interface PeriodGroup { key: PeriodKey; items: ReceivableListItem[]; subtotal: number }`, `export function groupByPeriod(items: ReceivableListItem[], now?: Date): PeriodGroup[]` — same bucketing rules as `payables/payment-periods.ts` (overdue+today → today; through coming Sunday → thisWeek; following Mon–Sun → nextWeek; later → future; fixed order, omit empty, subtotal = sum of amount).

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/receivables/receipt-periods.test.ts`:

```ts
import { describe, it, expect } from 'vitest';
import { groupByPeriod } from './receipt-periods';

const mk = (id: string, dueDate: string, amount: number) =>
  ({ receivableId: id, installmentId: id, dueDate, amount }) as any;

describe('groupByPeriod (receivables)', () => {
  const now = new Date(2026, 5, 25); // Thursday
  it('buckets overdue and due-today into today with subtotal', () => {
    const groups = groupByPeriod([mk('a', '2026-06-20', 100), mk('b', '2026-06-25', 50)], now);
    const today = groups.find((g) => g.key === 'today')!;
    expect(today.items).toHaveLength(2);
    expect(today.subtotal).toBe(150);
  });
  it('separates this week, next week and future', () => {
    const groups = groupByPeriod([mk('c', '2026-06-27', 10), mk('d', '2026-06-30', 20), mk('e', '2026-07-20', 30)], now);
    expect(groups.find((g) => g.key === 'thisWeek')?.items.map((i) => i.receivableId)).toEqual(['c']);
    expect(groups.find((g) => g.key === 'nextWeek')?.items.map((i) => i.receivableId)).toEqual(['d']);
    expect(groups.find((g) => g.key === 'future')?.items.map((i) => i.receivableId)).toEqual(['e']);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- receipt-periods`
Expected: FAIL — cannot resolve `./receipt-periods`.

- [ ] **Step 3: Implement `receipt-periods.ts`**

Create `web/src/pages/receivables/receipt-periods.ts` by copying `web/src/pages/payables/payment-periods.ts` verbatim and changing only the imported type `PayableListItem` → `ReceivableListItem` (in the import and the two `PayableListItem` references in the signatures). The date-bucketing logic is unchanged.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- receipt-periods`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/receivables/receipt-periods.ts web/src/pages/receivables/receipt-periods.test.ts
git commit -m "feat(AEL-633): receivable period grouping helper"
```

---

### Task 4: Copy the generic leaf components into `receivables/`

**Files (each Create = exact byte copy of the same-named file in `contas-a-receber/`):**
- Create: `web/src/pages/receivables/badges.tsx`
- Create: `web/src/pages/receivables/download.ts`
- Create: `web/src/pages/receivables/use-names.ts`
- Create: `web/src/pages/receivables/installments-table.tsx`
- Create: `web/src/pages/receivables/receipt-history.tsx`
- Create: `web/src/pages/receivables/receipt-form.tsx`
- Create: `web/src/pages/receivables/customer-picker.tsx`

- [ ] **Step 1: Copy each file verbatim**

Run (from repo root):

```bash
cd web/src/pages
for f in badges.tsx download.ts use-names.ts installments-table.tsx receipt-history.tsx receipt-form.tsx customer-picker.tsx; do
  cp "contas-a-receber/$f" "receivables/$f"
done
```

Do NOT edit the copies — they import from `../../*` and sibling `./badges` / `./download` (now present in `receivables/`).

- [ ] **Step 2: Verify**

Run (from `web/`): `pnpm typecheck` → PASS. `pnpm exec eslint src/pages/receivables/*.tsx src/pages/receivables/*.ts` → 0 errors (pre-existing warnings carried from the source are acceptable).

- [ ] **Step 3: Commit**

```bash
git add web/src/pages/receivables/badges.tsx web/src/pages/receivables/download.ts web/src/pages/receivables/use-names.ts web/src/pages/receivables/installments-table.tsx web/src/pages/receivables/receipt-history.tsx web/src/pages/receivables/receipt-form.tsx web/src/pages/receivables/customer-picker.tsx
git commit -m "chore(AEL-633): copy generic receivable leaf components into receivables/"
```

---

### Task 5: `ReceivableTable` — shared list table

Mirror `payables/payable-table.tsx` for receivables (payer column, row → `/app/receivables/:id`).

**Files:**
- Create: `web/src/pages/receivables/receivable-table.tsx`
- Test: `web/src/pages/receivables/receivable-table.test.tsx`

**Interfaces:**
- Consumes: `ReceivableListItem` from `../../api`; `SlaChips` from `../../components/workflow`; `useNames` from `./use-names`; `Panel`/`Pager`/`EmptyState` from `../../ui`; `brl`, `fmtDate`.
- Produces: `export function ReceivableTable({ companyId, stageLabel, items, total, page, totalPages, onPage, extraColumn? }: { companyId: string; stageLabel: string; items: ReceivableListItem[]; total: number; page: number; totalPages: number; onPage: (p: number) => void; extraColumn?: { header: string; cell: (item: ReceivableListItem) => ReactNode } })`. Columns: dueDate, description, payer (`payerName`), costCenter, amount, SlaChips, + optional extra. Row click → `nav('/app/receivables/' + item.receivableId)`. Empty → `EmptyState`. Interactive cells stop propagation.

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/receivables/receivable-table.test.tsx`:

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
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: { cc_1: 'Vendas' } }) }));

import { ReceivableTable } from './receivable-table';

const items = [
  { receivableId: 'rcv_1', installmentId: 'i1', sequence: 1, installmentCount: 1, payerName: 'Cliente A', description: 'Mensalidade', costCenterId: 'cc_1', amount: 500, dueDate: '2026-06-30', receivableStatus: 'AwaitingReceipt', installmentStatus: 'Open', isOverdue: false },
] as any;

describe('ReceivableTable', () => {
  it('renders rows and navigates to detail on row click', () => {
    render(<MemoryRouter><ReceivableTable companyId="company_1" stageLabel="A receber" items={items} total={1} page={1} totalPages={1} onPage={() => {}} /></MemoryRouter>);
    expect(screen.getByText('Mensalidade')).toBeInTheDocument();
    expect(screen.getByText('Cliente A')).toBeInTheDocument();
    fireEvent.click(screen.getByText('Mensalidade'));
    expect(navigate).toHaveBeenCalledWith('/app/receivables/rcv_1');
  });
  it('shows an empty state when there are no items', () => {
    render(<MemoryRouter><ReceivableTable companyId="company_1" stageLabel="Conciliado" items={[]} total={0} page={1} totalPages={1} onPage={() => {}} /></MemoryRouter>);
    expect(screen.getByText(/Conciliado/)).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- receivable-table`
Expected: FAIL — cannot resolve `./receivable-table`.

- [ ] **Step 3: Implement `receivable-table.tsx`**

Create `web/src/pages/receivables/receivable-table.tsx` by mirroring `web/src/pages/payables/payable-table.tsx` with these substitutions: type `PayableListItem` → `ReceivableListItem`; the vendor column uses `l.payerName` and the header key `receivables.columns.payerName` (verify the exact existing key in pt-BR.ts — the legacy receivables list uses a payer column header; reuse that key); row nav target `/app/payables/` → `/app/receivables/` and `l.payableId` → `l.receivableId`; the Panel count title uses `receivables.count` (verify it exists; else use `payables.count` pattern key that exists for receivables). Keep `SlaChips`, `Pager`, `EmptyState`, `useNames` exactly as in the payables version. Read both `payables/payable-table.tsx` and the legacy `contas-a-receber/list.tsx` to copy the exact column i18n keys the receivables list already uses (dueDate, description, payer, costCenter, amount, status).

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- receivable-table`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/receivables/receivable-table.tsx web/src/pages/receivables/receivable-table.test.tsx
git commit -m "feat(AEL-633): shared ReceivableTable list"
```

---

### Task 6: A-Receber tab + Reconciled tab

**Files:**
- Create: `web/src/pages/receivables/areceber-tab.tsx`
- Create: `web/src/pages/receivables/reconciled-tab.tsx`
- Test: `web/src/pages/receivables/areceber-tab.test.tsx`

**Interfaces:**
- Consumes: `useReceivables`, `useReceivable` from `../../queries`; `groupByPeriod` from `./receipt-periods`; `ReceivableInstallmentStatusBadge` from `./badges`; `ReceiptForm` from `./receipt-form`; `ReceivableTable` from `./receivable-table`; `TAB_STATUS` from `./stages`; `Panel`/`Button`/`Drawer`/`EmptyState`/`brl`/`fmtDate`.
- Produces: `export function ArecederTab({ companyId })` — `useReceivables({ status: 'AwaitingReceipt', page })`, period-grouped via `groupByPeriod`, header value summary `t('workflow.payment.weekSummary', { amount: brl(total), count })` (reuse the existing workflow key), each row: dueDate/payer/sequence/amount + `ReceivableInstallmentStatusBadge` + a "Register Receipt" button (opens `ReceiptForm` drawer for the row's receivable+installment), shown when `installmentStatus !== 'Received'`. `export function ReconciledTab({ companyId })` — `useReceivables({ status: 'Received', page })` rendered via `ReceivableTable`.

(Note the export name `ArecederTab` — keep this exact spelling consistent across files, or rename to `AReceberTab`; pick one and use it in the layout import. This plan uses `AReceberTab`.)

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/receivables/areceber-tab.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseReceivables } = vi.hoisted(() => ({ mockUseReceivables: vi.fn() }));
vi.mock('../../queries', () => ({ useReceivables: mockUseReceivables, useReceivable: vi.fn() }));

import { AReceberTab } from './areceber-tab';

const item = { receivableId: 'rcv_1', installmentId: 'i1', sequence: 1, installmentCount: 1, payerName: 'Cliente A', description: 'Mensalidade', costCenterId: 'cc_1', amount: 500, dueDate: '2026-06-20', receivableStatus: 'AwaitingReceipt', installmentStatus: 'Open', isOverdue: true };

beforeEach(() => mockUseReceivables.mockReturnValue({ data: { items: [item], total: 1, totalPages: 1 } }));

describe('AReceberTab', () => {
  it('queries AwaitingReceipt and renders the payer + a value summary', () => {
    render(<MemoryRouter><AReceberTab companyId="company_1" /></MemoryRouter>);
    expect(mockUseReceivables).toHaveBeenCalledWith(
      expect.objectContaining({ companyId: 'company_1', status: 'AwaitingReceipt' }),
      expect.anything(),
    );
    expect(screen.getByText('Cliente A')).toBeInTheDocument();
    expect(screen.getByText(/Total:/)).toBeInTheDocument();
    expect(screen.getAllByText(/R\$/).length).toBeGreaterThan(0);
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- areceber-tab`
Expected: FAIL — cannot resolve `./areceber-tab`.

- [ ] **Step 3: Implement the tabs**

Create `web/src/pages/receivables/areceber-tab.tsx` by mirroring `web/src/pages/payables/payment-tab.tsx` with substitutions: `usePayables`→`useReceivables` with `status: 'AwaitingReceipt'`; `usePayable`→`useReceivable`; `groupByPeriod` from `./receipt-periods`; `InstallmentStatusBadge`→`ReceivableInstallmentStatusBadge`; `PaymentForm`→`ReceiptForm`; the drawer wires `ReceiptForm` with `{ receivable: detail.data, installment, onDone, onCancel }`; the Register button shows when `l.installmentStatus !== 'Received'`; vendor → `l.payerName`; remove the "Generate Remittance" button (receivables have no remittance) — instead the header is just the value summary `<strong>{t('workflow.payment.weekSummary', { amount: brl(total), count: items.length })}</strong>`. Keep the `GROUP_LABEL` map + visible per-period subtotals exactly as in the payables payment-tab (visible R$ amounts — do NOT hide them). The component export is `AReceberTab`.

Create `web/src/pages/receivables/reconciled-tab.tsx` mirroring `web/src/pages/payables/reconciled-tab.tsx`: `useReceivables({ companyId, status: TAB_STATUS.reconciled, page })` rendered via `ReceivableTable` with `stageLabel={t('receivables.tabs.reconciled')}`.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- areceber-tab`
Expected: PASS. Do NOT degrade the value summary to pass a test — if an assertion collides on multiple `R$` nodes, use `getAllByText`.

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/receivables/areceber-tab.tsx web/src/pages/receivables/reconciled-tab.tsx web/src/pages/receivables/areceber-tab.test.tsx
git commit -m "feat(AEL-633): a-receber (period-grouped baixa) + reconciled tabs"
```

---

### Task 7: `layout.tsx` — dashboard + 2 tabs shell

**Files:**
- Create: `web/src/pages/receivables/layout.tsx`
- Test: `web/src/pages/receivables/layout.test.tsx`

**Interfaces:**
- Consumes: `useApp`; `useReceivableStageSummary`; `EmpresaPicker`; `PageHeader`/`Button`/`EmptyState`; `PendencyDashboard`/`StageTabBar`/`type StageTab`; `TAB_ORDER`/`TAB_STATUS`/`dashboardCards`/`cardTargetTab`/`isTabKey`/`type TabKey` from `./stages`; the two tabs; `useNavigate`/`useSearchParams`.
- Produces: `export function ReceivablesLayout()`. Reads `?stage=` (default `'areceber'` via `isTabKey`). Renders `PendencyDashboard` (3 cards; `onCardClick={(cardKey) => setStage(cardTargetTab(cardKey))}`), `StageTabBar` (2 tabs from `TAB_ORDER`, labels `t('receivables.tabs.'+key)`, counts derived: areceber = `summary.entry.count + summary.receipt.count`, reconciled = `summary.reconciled.count`), and the active tab. No-company `EmptyState`. "New" button → `/app/receivables/novo`.

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/receivables/layout.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import '../../i18n';

const { mockUseStageSummary, mockUseReceivables } = vi.hoisted(() => ({ mockUseStageSummary: vi.fn(), mockUseReceivables: vi.fn() }));
vi.mock('../../store', () => ({ useApp: () => ({ empresa: { id: 'company_1', legalName: 'ACME' }, empresaId: 'company_1' }) }));
vi.mock('../shared', () => ({ EmpresaPicker: () => <div data-testid='empresa-picker' /> }));
vi.mock('../../queries', () => ({ useReceivableStageSummary: mockUseStageSummary, useReceivables: mockUseReceivables, useReceivable: vi.fn() }));
vi.mock('./use-names', () => ({ useNames: () => ({ centros: [], centroNome: {} }) }));

import { ReceivablesLayout } from './layout';

beforeEach(() => {
  mockUseStageSummary.mockReturnValue({ data: { entry: { count: 2, totalAmount: 100 }, receipt: { count: 1, totalAmount: 50 }, reconciled: { count: 9, totalAmount: 900 } } });
  mockUseReceivables.mockReturnValue({ data: { items: [], total: 0, totalPages: 1 } });
});

describe('ReceivablesLayout', () => {
  it('renders dashboard cards + two tabs, defaulting to A receber', () => {
    render(<MemoryRouter initialEntries={['/app/receivables']}><ReceivablesLayout /></MemoryRouter>);
    expect(screen.getByTestId('empresa-picker')).toBeInTheDocument();
    expect(screen.getAllByText('A receber').length).toBeGreaterThan(0);
    expect(screen.getByText('Recebimento')).toBeInTheDocument(); // dashboard card
  });
  it('clicking the Conciliado card switches to the reconciled tab', () => {
    render(<MemoryRouter initialEntries={['/app/receivables']}><ReceivablesLayout /></MemoryRouter>);
    const links = screen.getAllByText('Ver pendências');
    fireEvent.click(links[links.length - 1]); // reconciled card
    expect(mockUseReceivables).toHaveBeenCalled();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- receivables/layout`
Expected: FAIL — cannot resolve `./layout`.

- [ ] **Step 3: Implement `layout.tsx`**

Create `web/src/pages/receivables/layout.tsx` mirroring `web/src/pages/payables/layout.tsx` with these differences: use `useReceivableStageSummary`; dashboard `onCardClick={(cardKey) => setStage(cardTargetTab(cardKey))}`; the tab bar built from `TAB_ORDER` (2 tabs) with derived counts (`areceber: summary.entry.count + summary.receipt.count`, `reconciled: summary.reconciled.count`); active tab default `'areceber'`; render `<AReceberTab>` / `<ReconciledTab>`; title/subtitle keys `receivables.title`/`receivables.subtitle` (verify they exist; the legacy page uses `receivables.title`); New button → `/app/receivables/novo`.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- receivables/layout`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/receivables/layout.tsx web/src/pages/receivables/layout.test.tsx
git commit -m "feat(AEL-633): receivables layout — dashboard + 2 tabs shell"
```

---

### Task 8: Detail page (audit timeline + SLA chips)

**Files:**
- Create: `web/src/pages/receivables/detail.tsx`
- Test: `web/src/pages/receivables/detail.test.tsx`

**Interfaces:**
- Consumes: `useReceivable`, `useReceivableAuditTrail`, `useCancelReceivable`, `useCostCenters` from `../../queries`; `AuditTimeline`, `SlaChips` from `../../components/workflow`; the copied `ReceivableStatusBadge`, `InstallmentsTable`, `ReceiptHistory`, `ReceiptForm`; `Button`/`Panel`/`Drawer`/`ConfirmDialog`/`Input`/`EmptyState`.
- Produces: `export function ReceivableDetailPage()` (reads `:id`).

- [ ] **Step 1: Write the failing test**

Create `web/src/pages/receivables/detail.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { screen } from '@testing-library/react';
import { renderWithProviders } from '../../test/renderWithProviders';
import '../../i18n';

const { mockUseReceivable, mockUseAudit } = vi.hoisted(() => ({ mockUseReceivable: vi.fn(), mockUseAudit: vi.fn() }));
vi.mock('react-router-dom', async (orig) => {
  const actual = await orig<typeof import('react-router-dom')>();
  return { ...actual, useNavigate: () => vi.fn(), useParams: () => ({ id: 'rcv_1' }) };
});
vi.mock('../../queries', () => ({
  useReceivable: mockUseReceivable,
  useReceivableAuditTrail: mockUseAudit,
  useCancelReceivable: () => ({ mutate: vi.fn() }),
  useCostCenters: () => ({ data: [] }),
}));

import { ReceivableDetailPage } from './detail';

beforeEach(() => {
  mockUseReceivable.mockReturnValue({
    data: { id: 'rcv_1', companyId: 'company_1', payerName: 'Cliente A', description: 'Mensalidade', status: 'AwaitingReceipt', totalAmount: 500, costCenterId: 'cc_1', documentNumber: 'NF-1', issueDate: '2026-06-01', installments: [{ id: 'i1', sequence: 1, dueDate: '2026-06-30', amount: 500, status: 'Open', receivedAmount: 0, receipts: [], isOverdue: false }] },
  });
  mockUseAudit.mockReturnValue({ data: [{ type: 'Created', actorId: 'u1', actorName: 'Ana', timestamp: '2026-06-01T10:00:00Z', metadata: {} }], isLoading: false });
});

describe('ReceivableDetailPage', () => {
  it('renders the receivable header and the audit timeline', () => {
    renderWithProviders(<ReceivableDetailPage />);
    expect(screen.getByText(/Cliente A/)).toBeInTheDocument();
    expect(screen.getByText('Título criado')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- receivables/detail`
Expected: FAIL — cannot resolve `./detail`.

- [ ] **Step 3: Implement `detail.tsx`**

Read `web/src/pages/contas-a-receber/detail.tsx` in full and recreate it as `web/src/pages/receivables/detail.tsx` with these changes: rename to `ReceivableDetailPage`; all `nav('/app/contas-a-receber/...')` → `'/app/receivables/...'`; import the copied leaf siblings (`ReceivableStatusBadge`, `InstallmentsTable`, `ReceiptHistory`, `ReceiptForm`) from `./`; add `SlaChips` in the header next to the status badge (`{r.installments[0] && <SlaChips stageLabel={t('receivables.status.' + r.status)} dueDate={r.installments[0].dueDate} />}` — verify `receivables.status.*` keys exist; the legacy badges component uses them); add an audit timeline panel after the receipt history: `const audit = useReceivableAuditTrail(id);` then `<Panel title={t('receivables.cards.timeline')}><AuditTimeline events={audit.data ?? []} loading={audit.isLoading} /></Panel>`; import `AuditTimeline`/`SlaChips` from `../../components/workflow`. Keep the existing edit + cancel actions and the receipt drawer unchanged. Add the i18n key `receivables.cards.timeline: 'Linha do tempo'` under `receivables.cards` (or alongside other receivables keys) only if missing. Ensure NO `contas-a-receber` substring remains in the file.

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- receivables/detail`
Expected: PASS. Do NOT degrade the product to satisfy a test; disambiguate the TEST if an assertion collides.

- [ ] **Step 5: Commit**

```bash
git add web/src/pages/receivables/detail.tsx web/src/pages/receivables/detail.test.tsx web/src/i18n/locales/pt-BR.ts
git commit -m "feat(AEL-633): receivable detail with audit timeline + SLA chips"
```

---

### Task 9: Create/edit form pages

**Files:**
- Create: `web/src/pages/receivables/receivable-form.tsx` (copy of `contas-a-receber/receivable-form.tsx` with renamed exports + redirected nav)

**Interfaces:**
- Produces: `export function NewReceivablePage()` and `export function EditReceivablePage()`.

- [ ] **Step 1: Copy and adapt**

Copy `web/src/pages/contas-a-receber/receivable-form.tsx` to `web/src/pages/receivables/receivable-form.tsx`. Then: rename `NovaContaReceberPage` → `NewReceivablePage`, `EditarContaReceberPage` → `EditReceivablePage`; change every `/app/contas-a-receber/...` nav target to `/app/receivables/...`; if it imports `./customer-picker`, that sibling is present in `receivables/`. Confirm zero `contas-a-receber` substring remains.

- [ ] **Step 2: Verify**

Run (from `web/`): `pnpm typecheck` → PASS. `pnpm exec eslint src/pages/receivables/receivable-form.tsx` → 0 errors (pre-existing warnings acceptable).

- [ ] **Step 3: Commit**

```bash
git add web/src/pages/receivables/receivable-form.tsx
git commit -m "feat(AEL-633): receivable create/edit form pages (self-contained)"
```

---

### Task 10: Routes, sidebar, barrel, full verification

**Files:**
- Create: `web/src/pages/receivables/index.tsx`
- Modify: `web/src/App.tsx`
- Modify: `web/src/i18n/locales/pt-BR.ts` (`nav.receivablesWorkflow`)

**Interfaces:**
- Produces: barrel exporting `ReceivablesLayout`, `ReceivableDetailPage`, `NewReceivablePage`, `EditReceivablePage`; routes `/app/receivables{,/novo,/:id,/:id/editar}`; sidebar item under Finance.

- [ ] **Step 1: Create the barrel**

Create `web/src/pages/receivables/index.tsx`:

```tsx
export { ReceivablesLayout } from './layout';
export { ReceivableDetailPage } from './detail';
export { NewReceivablePage, EditReceivablePage } from './receivable-form';
```

- [ ] **Step 2: Register routes + sidebar in `App.tsx`**

Read `web/src/App.tsx` to match exact patterns. Add the import:

```tsx
import { ReceivablesLayout, ReceivableDetailPage, NewReceivablePage, EditReceivablePage } from './pages/receivables';
```

Add routes next to the existing `contas-a-receber` routes (keep the old ones):

```tsx
        <Route path='receivables' element={<ReceivablesLayout />} />
        <Route path='receivables/novo' element={<NewReceivablePage />} />
        <Route path='receivables/:id' element={<ReceivableDetailPage />} />
        <Route path='receivables/:id/editar' element={<EditReceivablePage />} />
```

Add a sidebar `NavItem` under Finance (next to the existing `nav.payablesWorkflow` item added in plan 3), using the `HandCoins` lucide icon (add to the import if missing): label `t('nav.receivablesWorkflow')`, `active={at('receivables')}`, `onClick={() => go('/app/receivables')}`. Add a breadcrumb branch for `at('receivables')` mirroring the `payables` one. Add `nav.receivablesWorkflow: 'Contas a receber (esteira)'` under the existing `nav` object in `pt-BR.ts`.

- [ ] **Step 3: Full verification**

Run (from `web/`): `pnpm test` (all pass) · `pnpm typecheck` (0 errors) · `pnpm exec eslint src/App.tsx src/pages/receivables/index.tsx` (0 errors) · `pnpm build` (succeeds).

- [ ] **Step 4: Commit**

```bash
git add web/src/pages/receivables/index.tsx web/src/App.tsx web/src/i18n/locales/pt-BR.ts
git commit -m "feat(AEL-633): wire receivables routes + sidebar entry"
```

---

## Self-Review

**Spec coverage (this plan = spec §3 Receivables Pages, with the approved 2-tab simplification):**
- §3.1 Routing → Tasks 7, 9, 10 ✓
- §3.2 Layout (dashboard 3 buckets + tabs) → Tasks 2, 7 ✓ (navigation collapsed to 2 tabs per approved decision; dashboard still shows the 3 buckets)
- §3.3/§3.4 Entry/Receipt → merged into the **A Receber** tab (period-grouped baixa queue + value summary + Register Receipt) → Tasks 3, 6 ✓
- §3.5 Reconciled tab → Task 6 ✓
- §3.6 Detail (SlaChips + AuditTimeline + reused installments/receipts) → Task 8 ✓ (write-off action: the legacy detail only exposes edit + cancel; write-off UI is out of scope — kept consistent with the existing page, noted as a follow-up)
- API/query hooks + i18n → Task 1 ✓
- Leaf component reuse (self-contained copy) → Task 4 ✓

**Placeholder scan:** Tasks 5/6/7/8/9 say "mirror payables/X with these substitutions" / "read the source and recreate with these changes" — precise transforms of verbatim-available sources with enumerated substitutions, not open-ended placeholders. Pure-logic files (stages, receipt-periods) have full code.

**Type consistency:** `TabKey`/`TAB_STATUS`/`dashboardCards`/`cardTargetTab` (Task 2) consumed by Tasks 6/7. `groupByPeriod`/`PeriodGroup` (Task 3) consumed by Task 6. `useReceivableStageSummary`/`useReceivableAuditTrail` (Task 1) consumed by Tasks 7/8. `ReceivableTable` props (Task 5) consumed by Task 6. Copied leaf signatures (Task 4) used in Tasks 6/8. Component export name `AReceberTab` is used consistently in Tasks 6 and 7.

**Cross-task note:** Tasks 1, 8, 10 each append distinct keys to `pt-BR.ts`; Task 10 edits `App.tsx`. Sequential execution avoids conflicts.

---

## Decomposition Note

This is plan **4 of 5** (AEL-633 under epic AEL-620), branched off `origin/main` (which now contains plans 1–3). It consumes the workflow components (plan 1) and the receivable stage-summary/audit-trail endpoints (plan 2). Plan 5 (global approvals inbox) is the last. The old `contas-a-receber/` routes remain during transition; a later migration step redirects/removes them.
