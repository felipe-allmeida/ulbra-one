# Payables & Receivables UX Overhaul — Design Spec

## Overview

Redesign the accounts payable and receivable experience with stage-based navigation, a triage dashboard, balance forecasting, SLA chips, a global approval inbox, grouped payment scheduling, and an audit timeline per transaction.

**Approach**: Independent pages per module (payables, receivables, approvals) sharing reusable workflow components. New pages in separate directories (`web/src/pages/payables/`, `web/src/pages/receivables/`, `web/src/pages/approvals/`) — existing `contas-a-pagar/` and `contas-a-receber/` are untouched until the new pages are ready to replace them.

**Constraints**:
- Follow existing FE patterns (components from `ui.tsx`, queries in `queries.ts`, generated API types, i18n with EN keys / pt-BR locale)
- No hardcoded pt-BR in JSX — all user-facing strings through `react-i18next`
- Generic/multitenant — no Ulbra-specific logic
- All code (variables, files, docs) in English; only i18n values in pt-BR
- Use existing endpoints where possible; document backend gaps explicitly

---

## 1. Shared Workflow Components

Location: `web/src/components/workflow/`

### 1.1 StageTabBar

Horizontal tab bar for stage-based navigation.

**Props**:
```ts
interface StageTab {
  key: string
  label: string       // i18n key
  count?: number
  badge?: 'warning' | 'danger'  // visual urgency
}

interface StageTabBarProps {
  tabs: StageTab[]
  active: string
  onChange: (key: string) => void
}
```

- Renders as underline-style tabs with optional counter badge
- Active tab controlled by parent (synced to `?stage=` query param)
- Count comes from the stage-summary endpoint

### 1.2 PendencyDashboard

Horizontal card row showing pending items per stage. Rendered above the tab bar.

**Props**:
```ts
interface PendencyCard {
  stageKey: string
  label: string        // i18n key
  icon: LucideIcon
  count: number
  totalAmount: number
}

interface PendencyDashboardProps {
  cards: PendencyCard[]
  onCardClick: (stageKey: string) => void
}
```

- Each card shows: icon + label + count + total amount (formatted with `brl()`)
- Clicking a card activates the corresponding stage tab
- Uses the `Kpi` component from `ui.tsx` as the card base
- Horizontally scrollable on mobile

### 1.3 SlaChips

Two chips side by side showing process phase and due date urgency.

**Props**:
```ts
interface SlaChipsProps {
  stage: string        // i18n key for current stage name
  dueDate: string      // ISO date string
}
```

- **Phase chip**: stage name, neutral color (uses `Chip` from `ui.tsx`)
- **Due chip**: computed from `dueDate` vs today
  - `> 7 days`: green, "Vence em Xd"
  - `3-7 days`: yellow, "Vence em Xd"
  - `< 3 days`: red, "Vence em Xd"
  - `overdue`: red, "Vencido Xd"
  - Labels via i18n keys: `workflow.sla.dueIn`, `workflow.sla.overdue`

### 1.4 AuditTimeline

Vertical timeline of typed events for a transaction.

**Props**:
```ts
interface AuditEvent {
  type: string           // event type key (Created, Classified, Approved, etc.)
  actorName: string
  timestamp: string      // ISO datetime
  metadata?: Record<string, unknown>
}

interface AuditTimelineProps {
  events: AuditEvent[]
  loading?: boolean
}
```

- Renders each event as: icon (by type) + description (i18n) + actor name + formatted timestamp
- Event types and their i18n descriptions:
  - `Created` — "Titulo criado"
  - `Classified` — "Classificado" (metadata: account IDs)
  - `Approved` — "Aprovado"
  - `Rejected` — "Rejeitado" (metadata: reason)
  - `Cancelled` — "Cancelado" (metadata: reason)
  - `PaymentRegistered` — "Pagamento registrado" (metadata: installment, amount)
  - `PaymentReversed` — "Pagamento estornado" (metadata: reason)
  - `ReceiptRegistered` — "Recebimento registrado"
  - `ReceiptReversed` — "Recebimento estornado"
  - `AttachmentUploaded` — "Anexo adicionado"
  - `AttachmentDeleted` — "Anexo removido"
- Component is a pure renderer — data source is the consumer's responsibility
- Skeleton loading state via `SkeletonRows`

### 1.5 BalanceForecastBanner

Compact inline banner for balance projection.

**Props**:
```ts
interface BalanceForecastBannerProps {
  companyId: string
  additionalOutflow?: number  // amount of the item being approved
}
```

- Fetches `GET /api/cash-flow` with `horizon=1, granularity=Month`
- Displays: "Saldo projetado: R$ X | Reserva: R$ Y"
- Color: green if projected balance > reserve, red if below
- If `additionalOutflow` provided, shows adjusted projection
- All labels via i18n

### 1.6 BalanceForecastPanel

Expanded panel/drawer with cash flow chart, shown when confirming an approval.

**Props**:
```ts
interface BalanceForecastPanelProps {
  companyId: string
  impactAmount: number       // amount being approved
  open: boolean
  onClose: () => void
  onConfirm: () => void
}
```

- Fetches `GET /api/cash-flow` with `horizon=6, granularity=Month`
- Renders:
  - Bar chart: inflows (green) vs outflows (red) per period
  - Line overlay: cumulative balance
  - Highlighted bar showing the impact of the item being approved
  - KPI row: projected low point, runway, net result
- Confirm button triggers the approval mutation
- Uses `Drawer` from `ui.tsx`

---

## 2. Payables Pages

Location: `web/src/pages/payables/`

### 2.1 Routing

```
/app/payables              → layout + entry-tab (default)
/app/payables?stage=X      → layout + corresponding tab
/app/payables/new          → payable-form (create)
/app/payables/:id          → detail
/app/payables/:id/edit     → payable-form (edit)
```

Stage is controlled via `?stage=` query param for bookmarkability.

### 2.2 Layout (`layout.tsx`)

Wrapper for the payables section:
1. `PageHeader` — title (`payables.title`) + `EmpresaPicker` + "New" button
2. `PendencyDashboard` — 5 cards fed by `GET /api/payables/stage-summary`
3. `StageTabBar` — 5 tabs: entry, classification, approval, payment, reconciled
4. Active tab content rendered below

Stage tabs and their backend status mapping:

| Tab key | i18n key | Backend status filter |
|---------|----------|----------------------|
| `entry` | `payables.stages.entry` | `Received` |
| `classification` | `payables.stages.classification` | `PendingClassification` |
| `approval` | `payables.stages.approval` | `PendingApproval` |
| `payment` | `payables.stages.payment` | `Scheduled` (+ `InRemittance` installments) |
| `reconciled` | `payables.stages.reconciled` | `Paid` |

### 2.3 Entry Tab (`entry-tab.tsx`)

List of payables with status `Received`.

- Table columns: dueDate, description, vendor, amount, `SlaChips`
- Row click → navigate to detail
- Empty state when no items

### 2.4 Classification Tab (`classification-tab.tsx`)

List of payables with status `PendingClassification`.

- Table columns: dueDate, description, vendor, amount, `SlaChips`, AI suggestion column
- AI suggestion column: if `classificationConfidence > 0`, show `Chip` with suggested accounts and confidence badge
- Inline action: "Classify" button → opens `Drawer` with:
  - Accounting account select (pre-filled from `suggestedAccountingAccountId` if available)
  - Financial account select (pre-filled from `suggestedFinancialAccountId` if available)
  - Confidence rationale text (read-only, from `classificationRationale`)
  - Confirm button → `POST /api/payables/{id}/classify`

### 2.5 Approval Tab (`approval-tab.tsx`)

List of payables with status `PendingApproval`.

- `BalanceForecastBanner` at the top of the tab
- Table columns: dueDate, description, vendor, amount, `SlaChips`, actions
- Toggle filter: "All" (default, no client-side approver filtering — all `PendingApproval` items shown). "My pending" filter deferred until backend exposes an `approverId` query param
- Inline actions per row:
  - **Approve**: opens `BalanceForecastPanel` → on confirm → `POST /api/payables/{id}/approve`
  - **Reject**: opens `ConfirmDialog` with reason input → `POST /api/payables/{id}/reject`

### 2.6 Payment Tab (`payment-tab.tsx`)

Installments with status `Scheduled` or `InRemittance`.

- Summary header: "Today: R$ X (N items) · This week: R$ Y (M items)" — computed client-side from installment due dates
- List grouped by period:
  - Today
  - This week (remaining)
  - Next week
  - Future
- Each group is collapsible, shows subtotal
- Columns per item: dueDate, vendor (from parent payable), installment sequence, amount, bank account, method, status
- `InRemittance` badge on installments already in a CNAB remittance
- Actions:
  - "Register Payment" → opens existing payment drawer
  - Batch select checkbox → "Generate Remittance" button (batch action)

### 2.7 Reconciled Tab (`reconciled-tab.tsx`)

Payables with status `Paid`. Read-only consultation.

- Table columns: description, vendor, totalAmount, paidAmount, last payment date, accounting account, financial account
- Row click → detail page
- Pagination

### 2.8 Detail Page (`detail.tsx`)

Full payable detail, replaces current `contas-a-pagar/detail.tsx`.

- Header: title + `PayableStatusBadge` + `SlaChips` + contextual actions (vary by status)
- Info grid: vendor, cost center, category, accounting/financial accounts, document number, issue date, total amount
- `InstallmentsTable` (reused/adapted from current)
- `PaymentHistory` per installment (reused/adapted)
- `AttachmentsPanel` (reused/adapted)
- `AuditTimeline` — fetches `GET /api/payables/{id}/audit-trail`
- `BalanceForecastPanel` — visible when status is `PendingApproval`

---

## 3. Receivables Pages

Location: `web/src/pages/receivables/`

### 3.1 Routing

```
/app/receivables              → layout + entry-tab (default)
/app/receivables?stage=X      → layout + corresponding tab
/app/receivables/new          → receivable-form (create)
/app/receivables/:id          → detail
/app/receivables/:id/edit     → receivable-form (edit)
```

### 3.2 Layout (`layout.tsx`)

Same pattern as payables:
1. `PageHeader` — title + `EmpresaPicker` + "New" button
2. `PendencyDashboard` — 3 cards fed by `GET /api/receivables/stage-summary`
3. `StageTabBar` — 3 tabs: entry, receipt, reconciled

Stage tabs:

| Tab key | i18n key | Backend status filter |
|---------|----------|----------------------|
| `entry` | `receivables.stages.entry` | `AwaitingReceipt` (recently created, no receipts) |
| `receipt` | `receivables.stages.receipt` | `AwaitingReceipt` (with open installments ready for receipt) |
| `reconciled` | `receivables.stages.reconciled` | `Received` |

Note: Entry vs Receipt is a client-side split of the same backend status `AwaitingReceipt`. The split criteria: Entry shows items where `receivedAmount === 0` across all installments (nothing received yet — still in triage). Receipt shows items where at least one installment has `receivedAmount > 0` OR the item has been in `AwaitingReceipt` for more than 24 hours (past triage). The stage-summary endpoint should apply this same logic server-side to return accurate counts.

### 3.3 Entry Tab (`entry-tab.tsx`)

Receivables recently created, awaiting first action.

- Table columns: dueDate, description, payer, amount, `SlaChips`
- Row click → detail

### 3.4 Receipt Tab (`receipt-tab.tsx`)

Receivables with open installments ready for receipt.

- Summary header: "Today: R$ X (N items) · This week: R$ Y"
- List grouped by period (today / this week / next week / future)
- Collapsible groups with subtotals
- Overdue badge on past-due installments
- Action: "Register Receipt" → drawer

### 3.5 Reconciled Tab (`reconciled-tab.tsx`)

Receivables with status `Received`. Read-only.

- Table columns: description, payer, totalAmount, receivedAmount, last receipt date
- Pagination

### 3.6 Detail Page (`detail.tsx`)

- Header + status + `SlaChips` + actions (edit, cancel, write-off)
- Info grid: payer, cost center, category, document
- `InstallmentsTable`
- `ReceiptHistory`
- `AuditTimeline` — fetches `GET /api/receivables/{id}/audit-trail`

### 3.7 Form

Reuse current `receivable-form.tsx` with minimal adaptations (routing update).

---

## 4. Global Approval Inbox

Location: `web/src/pages/approvals/`

### 4.1 Routing

```
/app/approvals              → pending tab (default)
/app/approvals?tab=history  → history tab
```

### 4.2 Page (`index.tsx`)

- `PageHeader` with title "Aprovacoes" (i18n: `approvals.title`)
- Two sub-tabs: Pending / History
- Optional filters: module type (Payables for now), company, period

**Pending tab**:
- List of items awaiting current user's approval
- Each item renders:
  - Module icon + type label
  - Description + counterparty name
  - Amount + `SlaChips`
  - `BalanceForecastBanner` inline
  - Actions: Approve (with `BalanceForecastPanel`) / Reject (with reason dialog)
  - Link to full detail page
- Data source: `GET /api/payables?status=PendingApproval` (shows all pending items — no user-scoped filtering until backend exposes `approverId` param)

**History tab**:
- Items already approved/rejected by current user
- Columns: decision date, module, description, amount, decision (Approved/Rejected badge)
- Read-only

### 4.3 Sidebar Entry

New "Approvals" item in the Finance section of the sidebar menu, with a badge counter showing pending count (reuses the same query).

---

## 5. Backend Gaps

### 5.1 Stage Summary (required)

**Payables**: `GET /api/payables/stage-summary?companyId={id}`

Response:
```json
{
  "entry": { "count": 5, "totalAmount": 12400.00 },
  "classification": { "count": 3, "totalAmount": 8750.00 },
  "approval": { "count": 2, "totalAmount": 45000.00 },
  "payment": { "count": 4, "totalAmount": 22100.00 },
  "reconciled": { "count": 18, "totalAmount": 134500.00 }
}
```

**Receivables**: `GET /api/receivables/stage-summary?companyId={id}`

Response:
```json
{
  "entry": { "count": 3, "totalAmount": 8200.00 },
  "receipt": { "count": 7, "totalAmount": 31400.00 },
  "reconciled": { "count": 12, "totalAmount": 95600.00 }
}
```

### 5.2 Audit Trail (required)

**Payables**: `GET /api/payables/{id}/audit-trail`
**Receivables**: `GET /api/receivables/{id}/audit-trail`

Response (both):
```json
[
  {
    "type": "Created",
    "actorName": "Joao Silva",
    "actorId": "usr_...",
    "timestamp": "2026-06-20T10:30:00Z",
    "metadata": {}
  },
  {
    "type": "Classified",
    "actorName": "Maria Santos",
    "actorId": "usr_...",
    "timestamp": "2026-06-21T14:15:00Z",
    "metadata": {
      "accountingAccountId": "cha_...",
      "financialAccountId": "cha_..."
    }
  }
]
```

Event types — Payables: Created, Classified, Approved, Rejected, Cancelled, PaymentRegistered, PaymentReversed, AttachmentUploaded, AttachmentDeleted.

Event types — Receivables: Created, Cancelled, WrittenOff, ReceiptRegistered, ReceiptReversed.

### 5.3 Future gaps (not blocking)

- Approver-scoped filtering: `GET /api/payables?approverId={userId}` — for now, client-side filtering works
- Aggregated approval inbox endpoint across modules — for now, only payables feed the inbox

---

## 6. i18n Keys (new)

New namespace additions to `web/src/i18n/locales/pt-BR.ts`:

```
payables.stages.entry         = "Entrada"
payables.stages.classification = "Classificacao"
payables.stages.approval      = "Aprovacao"
payables.stages.payment       = "Pagamento"
payables.stages.reconciled    = "Conciliado"

receivables.stages.entry      = "Entrada"
receivables.stages.receipt    = "Recebimento"
receivables.stages.reconciled = "Conciliado"

workflow.sla.dueIn            = "Vence em {{days}}d"
workflow.sla.overdue          = "Vencido {{days}}d"
workflow.sla.today            = "Vence hoje"

workflow.dashboard.viewPending = "Ver pendencias"

workflow.forecast.projectedBalance = "Saldo projetado"
workflow.forecast.reserve          = "Reserva minima"
workflow.forecast.impactLabel      = "Impacto da aprovacao"
workflow.forecast.confirm          = "Confirmar aprovacao"

workflow.timeline.Created           = "Titulo criado"
workflow.timeline.Classified        = "Classificado"
workflow.timeline.Approved          = "Aprovado"
workflow.timeline.Rejected          = "Rejeitado"
workflow.timeline.Cancelled         = "Cancelado"
workflow.timeline.PaymentRegistered = "Pagamento registrado"
workflow.timeline.PaymentReversed   = "Pagamento estornado"
workflow.timeline.ReceiptRegistered = "Recebimento registrado"
workflow.timeline.ReceiptReversed   = "Recebimento estornado"
workflow.timeline.AttachmentUploaded = "Anexo adicionado"
workflow.timeline.AttachmentDeleted  = "Anexo removido"
workflow.timeline.WrittenOff         = "Baixa por perda"

workflow.payment.todaySummary     = "Hoje: {{amount}} ({{count}} titulos)"
workflow.payment.weekSummary      = "Semana: {{amount}} ({{count}} titulos)"
workflow.payment.groupToday       = "Hoje"
workflow.payment.groupThisWeek    = "Esta semana"
workflow.payment.groupNextWeek    = "Proxima semana"
workflow.payment.groupFuture      = "Futuro"

approvals.title    = "Aprovacoes"
approvals.pending  = "Pendentes"
approvals.history  = "Historico"
approvals.approve  = "Aprovar"
approvals.reject   = "Rejeitar"
```

(Accented characters omitted here — actual values will use proper pt-BR: "Aprovação", "Histórico", etc.)

---

## 7. File Structure

```
web/src/
  components/
    workflow/
      StageTabBar.tsx
      PendencyDashboard.tsx
      SlaChips.tsx
      AuditTimeline.tsx
      BalanceForecastBanner.tsx
      BalanceForecastPanel.tsx
      index.ts                    # barrel export

  pages/
    payables/
      index.tsx                   # barrel export
      layout.tsx                  # dashboard + tabs wrapper
      entry-tab.tsx
      classification-tab.tsx
      approval-tab.tsx
      payment-tab.tsx
      reconciled-tab.tsx
      detail.tsx
      payable-form.tsx            # adapted from contas-a-pagar/payable-form.tsx
      classification-drawer.tsx   # inline classification form
      installments-table.tsx      # adapted from contas-a-pagar
      payment-history.tsx         # adapted from contas-a-pagar
      payment-form.tsx            # adapted from contas-a-pagar
      attachments-panel.tsx       # adapted from contas-a-pagar
      badges.tsx                  # adapted from contas-a-pagar
      vendor-picker.tsx           # adapted from contas-a-pagar
      fiscal-xml-reader.tsx       # adapted from contas-a-pagar
      download.ts                 # adapted from contas-a-pagar

    receivables/
      index.tsx
      layout.tsx
      entry-tab.tsx
      receipt-tab.tsx
      reconciled-tab.tsx
      detail.tsx
      receivable-form.tsx         # adapted from contas-a-receber
      installments-table.tsx      # adapted from contas-a-receber
      receipt-history.tsx         # adapted from contas-a-receber
      receipt-form.tsx            # adapted from contas-a-receber
      customer-picker.tsx         # adapted from contas-a-receber
      badges.tsx                  # adapted from contas-a-receber
      download.ts                 # adapted from contas-a-receber

    approvals/
      index.tsx                   # full page: pending + history tabs
```

---

## 8. Migration Strategy

1. Build new pages in `payables/`, `receivables/`, `approvals/` — fully independent from existing `contas-a-pagar/` and `contas-a-receber/`
2. Add new routes in `App.tsx` alongside old ones (new routes: `/app/payables`, `/app/receivables`, `/app/approvals`)
3. Update sidebar navigation to point to new routes
4. Redirect old routes (`/app/contas-a-pagar` → `/app/payables`, `/app/contas-a-receber` → `/app/receivables`)
5. Remove old pages once new ones are validated

This avoids conflicts with the CNAB return branch currently in progress on the old pages.
