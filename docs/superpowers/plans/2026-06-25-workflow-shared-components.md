# Workflow Shared Components Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the reusable workflow components (`web/src/components/workflow/`) that the payables, receivables, and approvals pages compose — the foundation of the UX overhaul.

**Architecture:** Six presentational components plus a small date helper, all in `web/src/components/workflow/`. Pure renderers take data via props (`StageTabBar`, `PendencyDashboard`, `SlaChips`, `AuditTimeline`); the two balance-forecast components fetch cash flow via the existing `useCashFlow` hook. No new pages or routes — those come in later plans. This plan is pure frontend and does not touch `contas-a-pagar/` or `contas-a-receber/`, so it cannot conflict with the in-progress CNAB return branch.

**Tech Stack:** React 19, TypeScript, Vite, TanStack Query, react-i18next, Radix (via existing `ui.tsx`), vitest + @testing-library/react.

## Global Constraints

- All code (file names, variables, comments, docs) in English. Only i18n values in pt-BR. (verbatim from spec §Constraints)
- No hardcoded pt-BR in JSX — every user-facing string goes through `react-i18next` `t(...)`. (spec §Constraints)
- Reuse existing UI primitives from `web/src/ui.tsx` (`Chip`, `Kpi`, `Drawer`, `SkeletonRows`, `cx`); do not re-implement them. (spec §1)
- Currency formatted with `brl()` from `web/src/lib.ts`. Dates via `fmtDate`/`fmtDateTime` from `web/src/datetime.ts`. (existing codebase convention)
- Generic/multitenant — no Ulbra-specific logic. (spec §Constraints)
- Tests use `renderWithProviders` from `web/src/test/renderWithProviders.tsx` and import real i18n via `import '../../../i18n'`. (existing test convention)
- Verification commands run from the `web/` directory: `pnpm test`, `pnpm typecheck`.

---

### Task 1: i18n keys for the `workflow` namespace

Adds every translation key the workflow components consume. Done first so all later component tests resolve real labels (the test convention imports real i18n).

**Files:**
- Modify: `web/src/i18n/locales/pt-BR.ts` (insert a new `workflow` object; file is a single default-exported object, ~1418 lines)

**Interfaces:**
- Produces: i18n keys under `workflow.*` — `workflow.sla.dueIn` (interpolates `{{days}}`), `workflow.sla.overdue` (`{{days}}`), `workflow.sla.today`, `workflow.dashboard.viewPending`, `workflow.dashboard.itemsCount` (`{{count}}`), `workflow.forecast.projectedBalance`, `workflow.forecast.reserve`, `workflow.forecast.lowest`, `workflow.forecast.runway` (`{{periods}}`), `workflow.forecast.netResult`, `workflow.forecast.impactLabel`, `workflow.forecast.confirm`, `workflow.forecast.afterApproval`, `workflow.timeline.*` (one per event type), `workflow.timeline.empty`, `workflow.timeline.by` (`{{actor}}`).

- [ ] **Step 1: Add the `workflow` namespace object**

In `web/src/i18n/locales/pt-BR.ts`, locate the top-level `payables: {` key (around line 224). Immediately **before** it, insert this block (keep the trailing comma so the surrounding object stays valid):

```ts
  workflow: {
    sla: {
      dueIn: 'Vence em {{days}}d',
      overdue: 'Vencido há {{days}}d',
      today: 'Vence hoje',
    },
    dashboard: {
      viewPending: 'Ver pendências',
      itemsCount: '{{count}} títulos',
    },
    forecast: {
      projectedBalance: 'Saldo projetado',
      reserve: 'Reserva mínima',
      lowest: 'Menor saldo projetado',
      runway: 'Autonomia: {{periods}} períodos',
      netResult: 'Resultado líquido',
      impactLabel: 'Impacto da aprovação',
      afterApproval: 'Saldo após aprovação',
      confirm: 'Confirmar aprovação',
    },
    timeline: {
      empty: 'Nenhum evento registrado',
      by: 'por {{actor}}',
      Created: 'Título criado',
      Classified: 'Classificado',
      Approved: 'Aprovado',
      Rejected: 'Rejeitado',
      Cancelled: 'Cancelado',
      WrittenOff: 'Baixa por perda',
      PaymentRegistered: 'Pagamento registrado',
      PaymentReversed: 'Pagamento estornado',
      ReceiptRegistered: 'Recebimento registrado',
      ReceiptReversed: 'Recebimento estornado',
      AttachmentUploaded: 'Anexo adicionado',
      AttachmentDeleted: 'Anexo removido',
    },
  },
```

- [ ] **Step 2: Verify the locale file still type-checks**

Run (from `web/`): `pnpm typecheck`
Expected: PASS (no errors). A trailing-comma or brace mistake surfaces here as a TS parse error in `pt-BR.ts`.

- [ ] **Step 3: Commit**

```bash
git add web/src/i18n/locales/pt-BR.ts
git commit -m "feat(payables-ux): add workflow i18n namespace"
```

---

### Task 2: `sla.ts` date helper + `SlaChips` component

The only component with real logic (day math + urgency tone), so it gets the most test coverage. A pure helper computes days-to-due and tone; the component renders two `Chip`s.

**Files:**
- Create: `web/src/components/workflow/sla.ts`
- Create: `web/src/components/workflow/SlaChips.tsx`
- Test: `web/src/components/workflow/SlaChips.test.tsx`

**Interfaces:**
- Produces:
  - `sla.ts`: `export function daysUntil(dueDate: string, now?: Date): number` (calendar-day difference, positive = future, negative = overdue, 0 = today; `now` defaults to `new Date()`), and `export function dueTone(days: number): 'ok' | 'warn' | 'danger'` (`> 7` → `'ok'`, `3..7` → `'warn'`, `< 3` or overdue → `'danger'`).
  - `SlaChips.tsx`: `export function SlaChips({ stageLabel, dueDate, now }: { stageLabel: string; dueDate: string; now?: Date }): JSX.Element`. `stageLabel` is an already-translated string (the consuming page resolves the stage i18n key). `now` is injectable for tests.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/workflow/SlaChips.test.tsx`:

```tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';
import { daysUntil, dueTone } from './sla';
import { SlaChips } from './SlaChips';

describe('daysUntil', () => {
  const now = new Date(2026, 5, 25); // 2026-06-25 local
  it('returns positive days for a future date', () => {
    expect(daysUntil('2026-06-30', now)).toBe(5);
  });
  it('returns 0 for today', () => {
    expect(daysUntil('2026-06-25', now)).toBe(0);
  });
  it('returns negative days for an overdue date', () => {
    expect(daysUntil('2026-06-20', now)).toBe(-5);
  });
});

describe('dueTone', () => {
  it('ok when more than 7 days out', () => expect(dueTone(8)).toBe('ok'));
  it('warn between 3 and 7 days', () => expect(dueTone(5)).toBe('warn'));
  it('danger under 3 days', () => expect(dueTone(2)).toBe('danger'));
  it('danger when overdue', () => expect(dueTone(-1)).toBe('danger'));
});

describe('SlaChips', () => {
  const now = new Date(2026, 5, 25);
  it('renders the stage label and a due-in chip', () => {
    render(<SlaChips stageLabel="Aprovação" dueDate="2026-06-30" now={now} />);
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
    expect(screen.getByText('Vence em 5d')).toBeInTheDocument();
  });
  it('renders an overdue chip when past due', () => {
    render(<SlaChips stageLabel="Pagamento" dueDate="2026-06-20" now={now} />);
    expect(screen.getByText('Vencido há 5d')).toBeInTheDocument();
  });
  it('renders a today chip when due today', () => {
    render(<SlaChips stageLabel="Pagamento" dueDate="2026-06-25" now={now} />);
    expect(screen.getByText('Vence hoje')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- SlaChips`
Expected: FAIL — cannot resolve `./sla` / `./SlaChips` (modules not created yet).

- [ ] **Step 3: Implement `sla.ts`**

Create `web/src/components/workflow/sla.ts`:

```ts
/** Calendar-day difference from `now` to `dueDate` (YYYY-MM-DD). Positive = future, 0 = today, negative = overdue. */
export function daysUntil(dueDate: string, now: Date = new Date()): number {
  const [y, m, d] = dueDate.split('-').map(Number);
  const due = Date.UTC(y, m - 1, d);
  const today = Date.UTC(now.getFullYear(), now.getMonth(), now.getDate());
  return Math.round((due - today) / 86_400_000);
}

/** Urgency tone for a due-date chip. >7d safe, 3–7d warning, <3d or overdue danger. */
export function dueTone(days: number): 'ok' | 'warn' | 'danger' {
  if (days > 7) return 'ok';
  if (days >= 3) return 'warn';
  return 'danger';
}
```

- [ ] **Step 4: Implement `SlaChips.tsx`**

Create `web/src/components/workflow/SlaChips.tsx`:

```tsx
import { useTranslation } from 'react-i18next';
import { Chip } from '../../ui';
import { daysUntil, dueTone } from './sla';

/** Two chips: the current process stage and a due-date urgency chip. `stageLabel` is pre-translated. */
export function SlaChips({ stageLabel, dueDate, now }: { stageLabel: string; dueDate: string; now?: Date }) {
  const { t } = useTranslation();
  const days = daysUntil(dueDate, now);
  const tone = dueTone(days);
  const dueText =
    days === 0 ? t('workflow.sla.today') : days < 0 ? t('workflow.sla.overdue', { days: -days }) : t('workflow.sla.dueIn', { days });
  return (
    <span style={{ display: 'inline-flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
      <Chip>{stageLabel}</Chip>
      <span className={`sla-chip sla-${tone}`}>{dueText}</span>
    </span>
  );
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- SlaChips`
Expected: PASS (all 10 assertions).

- [ ] **Step 6: Commit**

```bash
git add web/src/components/workflow/sla.ts web/src/components/workflow/SlaChips.tsx web/src/components/workflow/SlaChips.test.tsx
git commit -m "feat(payables-ux): add SlaChips + sla date helpers"
```

---

### Task 3: `StageTabBar` component

Underline-style stage tabs with optional counters and an urgency dot.

**Files:**
- Create: `web/src/components/workflow/StageTabBar.tsx`
- Test: `web/src/components/workflow/StageTabBar.test.tsx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `export interface StageTab { key: string; label: string; count?: number; badge?: 'warning' | 'danger' }`
  - `export function StageTabBar({ tabs, active, onChange }: { tabs: StageTab[]; active: string; onChange: (key: string) => void }): JSX.Element`. `label` is a pre-translated string. Clicking a tab calls `onChange(key)`.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/workflow/StageTabBar.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { StageTabBar, type StageTab } from './StageTabBar';

const tabs: StageTab[] = [
  { key: 'entry', label: 'Entrada', count: 5 },
  { key: 'approval', label: 'Aprovação', count: 2, badge: 'danger' },
  { key: 'paid', label: 'Conciliado' },
];

describe('StageTabBar', () => {
  it('renders all tab labels and counts', () => {
    render(<StageTabBar tabs={tabs} active="entry" onChange={() => {}} />);
    expect(screen.getByText('Entrada')).toBeInTheDocument();
    expect(screen.getByText('5')).toBeInTheDocument();
    expect(screen.getByText('Aprovação')).toBeInTheDocument();
  });
  it('marks the active tab with aria-selected', () => {
    render(<StageTabBar tabs={tabs} active="approval" onChange={() => {}} />);
    expect(screen.getByRole('tab', { name: /Aprovação/ })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tab', { name: /Entrada/ })).toHaveAttribute('aria-selected', 'false');
  });
  it('calls onChange with the tab key when clicked', () => {
    const onChange = vi.fn();
    render(<StageTabBar tabs={tabs} active="entry" onChange={onChange} />);
    fireEvent.click(screen.getByRole('tab', { name: /Conciliado/ }));
    expect(onChange).toHaveBeenCalledWith('paid');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- StageTabBar`
Expected: FAIL — cannot resolve `./StageTabBar`.

- [ ] **Step 3: Implement `StageTabBar.tsx`**

Create `web/src/components/workflow/StageTabBar.tsx`:

```tsx
import { cx } from '../../ui';

export interface StageTab {
  key: string;
  label: string;
  count?: number;
  badge?: 'warning' | 'danger';
}

/** Underline-style stage tabs. `label` is pre-translated; active tab controlled by the parent. */
export function StageTabBar({
  tabs,
  active,
  onChange,
}: {
  tabs: StageTab[];
  active: string;
  onChange: (key: string) => void;
}) {
  return (
    <div className='stage-tabbar' role='tablist'>
      {tabs.map((tab) => (
        <button
          key={tab.key}
          type='button'
          role='tab'
          aria-selected={tab.key === active}
          className={cx('stage-tab', tab.key === active && 'active')}
          onClick={() => onChange(tab.key)}
        >
          <span className='stage-tab-label'>{tab.label}</span>
          {tab.count != null && (
            <span className={cx('stage-tab-count', tab.badge && `count-${tab.badge}`)}>{tab.count}</span>
          )}
        </button>
      ))}
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- StageTabBar`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/components/workflow/StageTabBar.tsx web/src/components/workflow/StageTabBar.test.tsx
git commit -m "feat(payables-ux): add StageTabBar"
```

---

### Task 4: `PendencyDashboard` component

A horizontal row of pending-per-stage cards; each card is a clickable shortcut to its stage tab.

**Files:**
- Create: `web/src/components/workflow/PendencyDashboard.tsx`
- Test: `web/src/components/workflow/PendencyDashboard.test.tsx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `export interface PendencyCard { stageKey: string; label: string; icon: ReactNode; count: number; totalAmount: number }`
  - `export function PendencyDashboard({ cards, onCardClick }: { cards: PendencyCard[]; onCardClick: (stageKey: string) => void }): JSX.Element`. `label` pre-translated; `totalAmount` formatted with `brl()`; clicking a card calls `onCardClick(stageKey)`.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/workflow/PendencyDashboard.test.tsx`:

```tsx
import { describe, it, expect, vi } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../../i18n';
import { PendencyDashboard, type PendencyCard } from './PendencyDashboard';

const cards: PendencyCard[] = [
  { stageKey: 'classification', label: 'Classificação', icon: <span data-testid='ic1' />, count: 3, totalAmount: 8750 },
  { stageKey: 'approval', label: 'Aprovação', icon: <span data-testid='ic2' />, count: 2, totalAmount: 45000 },
];

describe('PendencyDashboard', () => {
  it('renders a card per stage with count and formatted amount', () => {
    render(<PendencyDashboard cards={cards} onCardClick={() => {}} />);
    expect(screen.getByText('Classificação')).toBeInTheDocument();
    expect(screen.getByText('3 títulos')).toBeInTheDocument();
    expect(screen.getByText('R$ 8.750')).toBeInTheDocument();
  });
  it('calls onCardClick with the stage key when a card is clicked', () => {
    const onCardClick = vi.fn();
    render(<PendencyDashboard cards={cards} onCardClick={onCardClick} />);
    fireEvent.click(screen.getByText('Aprovação'));
    expect(onCardClick).toHaveBeenCalledWith('approval');
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- PendencyDashboard`
Expected: FAIL — cannot resolve `./PendencyDashboard`.

- [ ] **Step 3: Implement `PendencyDashboard.tsx`**

Create `web/src/components/workflow/PendencyDashboard.tsx`:

```tsx
import { type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { brl } from '../../lib';

export interface PendencyCard {
  stageKey: string;
  label: string;
  icon: ReactNode;
  count: number;
  totalAmount: number;
}

/** Horizontal row of pending-per-stage cards; each card jumps to its stage tab. `label` is pre-translated. */
export function PendencyDashboard({
  cards,
  onCardClick,
}: {
  cards: PendencyCard[];
  onCardClick: (stageKey: string) => void;
}) {
  const { t } = useTranslation();
  return (
    <div className='pendency-dashboard'>
      {cards.map((card) => (
        <button key={card.stageKey} type='button' className='pendency-card' onClick={() => onCardClick(card.stageKey)}>
          <div className='pendency-card-head'>
            <span className='pendency-card-icon'>{card.icon}</span>
            <span className='pendency-card-label'>{card.label}</span>
          </div>
          <div className='pendency-card-count'>{t('workflow.dashboard.itemsCount', { count: card.count })}</div>
          <div className='pendency-card-amount'>{brl(card.totalAmount)}</div>
          <span className='pendency-card-link'>{t('workflow.dashboard.viewPending')}</span>
        </button>
      ))}
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- PendencyDashboard`
Expected: PASS. (Note: `brl(8750)` returns `'R$ 8.750'` — no cents — per `web/src/lib.ts`.)

- [ ] **Step 5: Commit**

```bash
git add web/src/components/workflow/PendencyDashboard.tsx web/src/components/workflow/PendencyDashboard.test.tsx
git commit -m "feat(payables-ux): add PendencyDashboard"
```

---

### Task 5: `AuditTimeline` component

A vertical, typed event timeline. Pure renderer — the consuming page supplies `events` and `loading`.

**Files:**
- Create: `web/src/components/workflow/AuditTimeline.tsx`
- Test: `web/src/components/workflow/AuditTimeline.test.tsx`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `export interface AuditEvent { type: string; actorName: string; timestamp: string; metadata?: Record<string, unknown> }`
  - `export function AuditTimeline({ events, loading }: { events: AuditEvent[]; loading?: boolean }): JSX.Element`. Each event's description is `t('workflow.timeline.' + type)`; unknown types fall back to the raw `type` string. Timestamp via `fmtDateTime`. Empty list → `t('workflow.timeline.empty')`. `loading` → `SkeletonRows`-style placeholder.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/workflow/AuditTimeline.test.tsx`:

```tsx
import { describe, it, expect } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';
import { AuditTimeline, type AuditEvent } from './AuditTimeline';

const events: AuditEvent[] = [
  { type: 'Created', actorName: 'João Silva', timestamp: '2026-06-20T10:30:00Z' },
  { type: 'Approved', actorName: 'Carlos Lima', timestamp: '2026-06-21T14:15:00Z' },
];

describe('AuditTimeline', () => {
  it('renders each event with its translated description and actor', () => {
    render(<AuditTimeline events={events} />);
    expect(screen.getByText('Título criado')).toBeInTheDocument();
    expect(screen.getByText('Aprovado')).toBeInTheDocument();
    expect(screen.getByText('por João Silva')).toBeInTheDocument();
  });
  it('renders an empty message when there are no events', () => {
    render(<AuditTimeline events={[]} />);
    expect(screen.getByText('Nenhum evento registrado')).toBeInTheDocument();
  });
  it('falls back to the raw type for unknown event types', () => {
    render(<AuditTimeline events={[{ type: 'SomethingNew', actorName: 'Sys', timestamp: '2026-06-20T10:30:00Z' }]} />);
    expect(screen.getByText('SomethingNew')).toBeInTheDocument();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- AuditTimeline`
Expected: FAIL — cannot resolve `./AuditTimeline`.

- [ ] **Step 3: Implement `AuditTimeline.tsx`**

Create `web/src/components/workflow/AuditTimeline.tsx`:

```tsx
import { useTranslation } from 'react-i18next';
import i18n from 'i18next';
import { Circle } from 'lucide-react';
import { fmtDateTime } from '../../datetime';
import { SkeletonRows } from '../../ui';

export interface AuditEvent {
  type: string;
  actorName: string;
  timestamp: string;
  metadata?: Record<string, unknown>;
}

/** Vertical timeline of typed audit events. Pure renderer — data fetched by the consumer. */
export function AuditTimeline({ events, loading }: { events: AuditEvent[]; loading?: boolean }) {
  const { t } = useTranslation();
  if (loading) {
    return (
      <table className='audit-timeline-skeleton'>
        <tbody>
          <SkeletonRows rows={3} cols={2} />
        </tbody>
      </table>
    );
  }
  if (events.length === 0) {
    return <div className='audit-timeline-empty'>{t('workflow.timeline.empty')}</div>;
  }
  const describe = (type: string) => {
    const key = `workflow.timeline.${type}`;
    return i18n.exists(key) ? t(key) : type;
  };
  return (
    <ol className='audit-timeline'>
      {events.map((ev, idx) => (
        <li key={idx} className='audit-event'>
          <span className='audit-event-dot'>
            <Circle size={10} />
          </span>
          <div className='audit-event-body'>
            <div className='audit-event-desc'>{describe(ev.type)}</div>
            <div className='audit-event-meta'>
              <span className='audit-event-actor'>{t('workflow.timeline.by', { actor: ev.actorName })}</span>
              <span className='audit-event-time'>{fmtDateTime(ev.timestamp)}</span>
            </div>
          </div>
        </li>
      ))}
    </ol>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- AuditTimeline`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/components/workflow/AuditTimeline.tsx web/src/components/workflow/AuditTimeline.test.tsx
git commit -m "feat(payables-ux): add AuditTimeline"
```

---

### Task 6: `BalanceForecastBanner` component

Compact inline banner: projected balance + reserve status, with optional approval-impact adjustment. Fetches cash flow via the existing `useCashFlow` hook.

**Files:**
- Create: `web/src/components/workflow/BalanceForecastBanner.tsx`
- Test: `web/src/components/workflow/BalanceForecastBanner.test.tsx`

**Interfaces:**
- Consumes: `useCashFlow(params, options?)` from `web/src/queries.ts` — returns a TanStack Query result whose `data` is `CashFlowResponse` (`{ startingBalance, kpis: { lowestProjectedBalance, ... }, periods: CashFlowPeriod[] }`; each period has `closingBalance: number` and `belowReserve: boolean`). Type `CashFlowResponse` from `web/src/api.ts`.
- Produces: `export function BalanceForecastBanner({ companyId, additionalOutflow }: { companyId: string; additionalOutflow?: number }): JSX.Element`. Calls `useCashFlow({ companyId, horizon: 1, granularity: 'Month' }, { enabled: !!companyId })`. Reads the first period's `closingBalance`/`belowReserve`. If `additionalOutflow` given, shows the after-approval balance (`closingBalance - additionalOutflow`). Tone danger when `belowReserve` (or when the adjusted balance drops below 0), ok otherwise.

- [ ] **Step 1: Write the failing test**

Create `web/src/components/workflow/BalanceForecastBanner.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import '../../i18n';

const { mockUseCashFlow } = vi.hoisted(() => ({ mockUseCashFlow: vi.fn() }));
vi.mock('../../queries', () => ({ useCashFlow: mockUseCashFlow }));

import { BalanceForecastBanner } from './BalanceForecastBanner';

const cashFlow = {
  startingBalance: 20000,
  kpis: { lowestProjectedBalance: 5000, runwayPeriods: 4, netResult: 1200 },
  periods: [{ closingBalance: 12450, belowReserve: false }],
};

beforeEach(() => mockUseCashFlow.mockReset());

describe('BalanceForecastBanner', () => {
  it('shows the projected closing balance', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    render(<BalanceForecastBanner companyId="company_1" />);
    expect(screen.getByText('Saldo projetado')).toBeInTheDocument();
    expect(screen.getByText('R$ 12.450')).toBeInTheDocument();
  });
  it('shows the after-approval balance when additionalOutflow is provided', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    render(<BalanceForecastBanner companyId="company_1" additionalOutflow={2450} />);
    expect(screen.getByText('Saldo após aprovação')).toBeInTheDocument();
    expect(screen.getByText('R$ 10.000')).toBeInTheDocument();
  });
  it('renders nothing meaningful while loading', () => {
    mockUseCashFlow.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = render(<BalanceForecastBanner companyId="company_1" />);
    expect(container.querySelector('.forecast-banner-loading')).toBeTruthy();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- BalanceForecastBanner`
Expected: FAIL — cannot resolve `./BalanceForecastBanner`.

- [ ] **Step 3: Implement `BalanceForecastBanner.tsx`**

Create `web/src/components/workflow/BalanceForecastBanner.tsx`:

```tsx
import { useTranslation } from 'react-i18next';
import { useCashFlow } from '../../queries';
import { brl } from '../../lib';
import { cx, Skeleton } from '../../ui';

/** Compact inline cash-balance banner. Optionally previews the impact of approving an outflow. */
export function BalanceForecastBanner({
  companyId,
  additionalOutflow,
}: {
  companyId: string;
  additionalOutflow?: number;
}) {
  const { t } = useTranslation();
  const cf = useCashFlow({ companyId, horizon: 1, granularity: 'Month' }, { enabled: !!companyId });
  if (cf.isLoading || !cf.data) {
    return (
      <div className='forecast-banner forecast-banner-loading'>
        <Skeleton width={220} />
      </div>
    );
  }
  const period = cf.data.periods[0];
  const projected = period?.closingBalance ?? cf.data.startingBalance;
  const adjusted = additionalOutflow != null ? projected - additionalOutflow : projected;
  const danger = (period?.belowReserve ?? false) || adjusted < 0;
  return (
    <div className={cx('forecast-banner', danger ? 'tone-danger' : 'tone-ok')}>
      <span className='forecast-banner-label'>
        {additionalOutflow != null ? t('workflow.forecast.afterApproval') : t('workflow.forecast.projectedBalance')}
      </span>
      <span className='forecast-banner-value'>{brl(adjusted)}</span>
      <span className='forecast-banner-lowest'>
        {t('workflow.forecast.lowest')}: {brl(cf.data.kpis.lowestProjectedBalance)}
      </span>
    </div>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- BalanceForecastBanner`
Expected: PASS. (`brl(12450)` → `'R$ 12.450'`, `brl(10000)` → `'R$ 10.000'`.)

- [ ] **Step 5: Commit**

```bash
git add web/src/components/workflow/BalanceForecastBanner.tsx web/src/components/workflow/BalanceForecastBanner.test.tsx
git commit -m "feat(payables-ux): add BalanceForecastBanner"
```

---

### Task 7: `BalanceForecastPanel` component

A `Drawer` showing the cash-flow projection with the approval impact and KPI row, plus a confirm action. CSS bar chart (no chart lib dependency) — period bars sized by closing balance.

**Files:**
- Create: `web/src/components/workflow/BalanceForecastPanel.tsx`
- Test: `web/src/components/workflow/BalanceForecastPanel.test.tsx`

**Interfaces:**
- Consumes: `useCashFlow` (as Task 6); `Drawer`, `Button` from `web/src/ui.tsx`; `Kpi` from `web/src/ui.tsx`.
- Produces: `export function BalanceForecastPanel({ companyId, impactAmount, open, onClose, onConfirm, busy }: { companyId: string; impactAmount: number; open: boolean; onClose: () => void; onConfirm: () => void; busy?: boolean }): JSX.Element`. Fetches `useCashFlow({ companyId, horizon: 6, granularity: 'Month' }, { enabled: open && !!companyId })`. Renders a KPI row (lowest projected balance, runway, net result), a simple per-period bar chart, the `impactAmount` label, and a confirm button wired to `onConfirm` (disabled while `busy`).

- [ ] **Step 1: Write the failing test**

Create `web/src/components/workflow/BalanceForecastPanel.test.tsx`:

```tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import '../../i18n';

const { mockUseCashFlow } = vi.hoisted(() => ({ mockUseCashFlow: vi.fn() }));
vi.mock('../../queries', () => ({ useCashFlow: mockUseCashFlow }));

import { BalanceForecastPanel } from './BalanceForecastPanel';

const cashFlow = {
  startingBalance: 20000,
  kpis: { lowestProjectedBalance: 5000, runwayPeriods: 4, netResult: 1200 },
  periods: [
    { label: 'Jun', closingBalance: 12450, inflowForecast: 8000, outflowForecast: 6000, belowReserve: false },
    { label: 'Jul', closingBalance: 9800, inflowForecast: 7000, outflowForecast: 9650, belowReserve: false },
  ],
};

beforeEach(() => mockUseCashFlow.mockReset());

describe('BalanceForecastPanel', () => {
  it('renders KPIs and the impact label when open', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    render(
      <BalanceForecastPanel companyId="company_1" impactAmount={2450} open onClose={() => {}} onConfirm={() => {}} />,
    );
    expect(screen.getByText('Menor saldo projetado')).toBeInTheDocument();
    expect(screen.getByText('Impacto da aprovação')).toBeInTheDocument();
    expect(screen.getByText('R$ 2.450')).toBeInTheDocument();
  });
  it('calls onConfirm when the confirm button is clicked', () => {
    mockUseCashFlow.mockReturnValue({ data: cashFlow, isLoading: false });
    const onConfirm = vi.fn();
    render(
      <BalanceForecastPanel companyId="company_1" impactAmount={2450} open onClose={() => {}} onConfirm={onConfirm} />,
    );
    fireEvent.click(screen.getByText('Confirmar aprovação'));
    expect(onConfirm).toHaveBeenCalledOnce();
  });
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run (from `web/`): `pnpm test -- BalanceForecastPanel`
Expected: FAIL — cannot resolve `./BalanceForecastPanel`.

- [ ] **Step 3: Implement `BalanceForecastPanel.tsx`**

Create `web/src/components/workflow/BalanceForecastPanel.tsx`:

```tsx
import { useTranslation } from 'react-i18next';
import { useCashFlow } from '../../queries';
import { brl } from '../../lib';
import { Button, Drawer, Kpi } from '../../ui';

/** Expanded cash-flow drawer shown before confirming an approval. CSS bar chart, no chart lib. */
export function BalanceForecastPanel({
  companyId,
  impactAmount,
  open,
  onClose,
  onConfirm,
  busy,
}: {
  companyId: string;
  impactAmount: number;
  open: boolean;
  onClose: () => void;
  onConfirm: () => void;
  busy?: boolean;
}) {
  const { t } = useTranslation();
  const cf = useCashFlow({ companyId, horizon: 6, granularity: 'Month' }, { enabled: open && !!companyId });
  const periods = cf.data?.periods ?? [];
  const maxBalance = Math.max(1, ...periods.map((p) => Math.abs(p.closingBalance)));

  return (
    <Drawer
      open={open}
      onClose={onClose}
      width={560}
      title={t('workflow.forecast.projectedBalance')}
      footer={
        <Button variant='brand' onClick={onConfirm} disabled={busy}>
          {t('workflow.forecast.confirm')}
        </Button>
      }
    >
      <div className='forecast-impact'>
        <span className='forecast-impact-label'>{t('workflow.forecast.impactLabel')}</span>
        <span className='forecast-impact-value'>{brl(impactAmount)}</span>
      </div>

      {cf.data && (
        <div className='forecast-kpis'>
          <Kpi label={t('workflow.forecast.lowest')} value={brl(cf.data.kpis.lowestProjectedBalance)} />
          <Kpi
            label={t('workflow.forecast.netResult')}
            value={brl(cf.data.kpis.netResult)}
            foot={
              cf.data.kpis.runwayPeriods != null
                ? t('workflow.forecast.runway', { periods: cf.data.kpis.runwayPeriods })
                : undefined
            }
          />
        </div>
      )}

      <div className='forecast-chart'>
        {periods.map((p, idx) => (
          <div key={idx} className='forecast-bar-col'>
            <div
              className={p.belowReserve ? 'forecast-bar below' : 'forecast-bar'}
              style={{ height: `${Math.round((Math.abs(p.closingBalance) / maxBalance) * 100)}%` }}
              title={brl(p.closingBalance)}
            />
            <span className='forecast-bar-label'>{p.label}</span>
          </div>
        ))}
      </div>
    </Drawer>
  );
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run (from `web/`): `pnpm test -- BalanceForecastPanel`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add web/src/components/workflow/BalanceForecastPanel.tsx web/src/components/workflow/BalanceForecastPanel.test.tsx
git commit -m "feat(payables-ux): add BalanceForecastPanel"
```

---

### Task 8: Barrel export + styles + full verification

Wire a barrel export, add the component styles, and confirm the whole suite + typecheck pass.

**Files:**
- Create: `web/src/components/workflow/index.ts`
- Modify: `web/src/index.css` (append a `/* workflow components */` section) — confirm this is the global stylesheet imported by `web/src/main.tsx`; if the project uses a different global CSS entry, append there instead.

**Interfaces:**
- Produces: `web/src/components/workflow/index.ts` re-exporting every public symbol: `SlaChips`, `daysUntil`, `dueTone`, `StageTabBar`, `type StageTab`, `PendencyDashboard`, `type PendencyCard`, `AuditTimeline`, `type AuditEvent`, `BalanceForecastBanner`, `BalanceForecastPanel`.

- [ ] **Step 1: Create the barrel export**

Create `web/src/components/workflow/index.ts`:

```ts
export { SlaChips } from './SlaChips';
export { daysUntil, dueTone } from './sla';
export { StageTabBar, type StageTab } from './StageTabBar';
export { PendencyDashboard, type PendencyCard } from './PendencyDashboard';
export { AuditTimeline, type AuditEvent } from './AuditTimeline';
export { BalanceForecastBanner } from './BalanceForecastBanner';
export { BalanceForecastPanel } from './BalanceForecastPanel';
```

- [ ] **Step 2: Confirm the global stylesheet path**

Run (from `web/`): `grep -n "import.*\.css" src/main.tsx`
Expected: shows the global CSS import (e.g. `import './index.css'`). Use that file's path in Step 3.

- [ ] **Step 3: Append component styles**

Append to the global stylesheet identified in Step 2:

```css
/* ---------- workflow components ---------- */
.sla-chip { font-size: 12px; padding: 2px 8px; border-radius: 999px; font-weight: 600; }
.sla-ok { background: #ecfdf5; color: #047857; }
.sla-warn { background: #fffbeb; color: #b45309; }
.sla-danger { background: #fef2f2; color: #b91c1c; }

.stage-tabbar { display: flex; gap: 4px; border-bottom: 1px solid var(--border, #e5e7eb); margin-bottom: 16px; flex-wrap: wrap; }
.stage-tab { display: inline-flex; align-items: center; gap: 8px; padding: 10px 14px; border: 0; background: none; cursor: pointer; color: var(--muted, #6b7280); border-bottom: 2px solid transparent; font-weight: 500; }
.stage-tab.active { color: var(--brand, #4f46e5); border-bottom-color: var(--brand, #4f46e5); }
.stage-tab-count { font-size: 12px; background: #f3f4f6; color: #374151; border-radius: 999px; padding: 1px 7px; }
.stage-tab-count.count-warning { background: #fffbeb; color: #b45309; }
.stage-tab-count.count-danger { background: #fef2f2; color: #b91c1c; }

.pendency-dashboard { display: flex; gap: 12px; overflow-x: auto; padding-bottom: 4px; margin-bottom: 16px; }
.pendency-card { flex: 0 0 200px; text-align: left; background: var(--card, #fff); border: 1px solid var(--border, #e5e7eb); border-radius: 12px; padding: 14px; cursor: pointer; display: flex; flex-direction: column; gap: 4px; transition: border-color .15s, box-shadow .15s; }
.pendency-card:hover { border-color: var(--brand, #4f46e5); box-shadow: 0 1px 6px rgba(0,0,0,.06); }
.pendency-card-head { display: flex; align-items: center; gap: 8px; color: var(--muted, #6b7280); }
.pendency-card-count { font-size: 22px; font-weight: 700; }
.pendency-card-amount { color: var(--muted, #6b7280); font-size: 13px; }
.pendency-card-link { margin-top: 4px; font-size: 12px; color: var(--brand, #4f46e5); font-weight: 600; }

.audit-timeline { list-style: none; margin: 0; padding: 0; }
.audit-event { display: flex; gap: 12px; padding: 8px 0; }
.audit-event-dot { color: var(--brand, #4f46e5); display: flex; align-items: center; }
.audit-event-desc { font-weight: 500; }
.audit-event-meta { display: flex; gap: 10px; color: var(--muted, #6b7280); font-size: 12px; }
.audit-timeline-empty { color: var(--muted, #6b7280); padding: 12px 0; }

.forecast-banner { display: inline-flex; align-items: center; gap: 12px; padding: 8px 14px; border-radius: 10px; font-size: 14px; }
.forecast-banner.tone-ok { background: #ecfdf5; color: #065f46; }
.forecast-banner.tone-danger { background: #fef2f2; color: #991b1b; }
.forecast-banner-value { font-weight: 700; }
.forecast-banner-lowest { font-size: 12px; opacity: .8; }

.forecast-impact { display: flex; justify-content: space-between; padding: 12px 0; border-bottom: 1px solid var(--border, #e5e7eb); }
.forecast-impact-value { font-weight: 700; }
.forecast-kpis { display: flex; gap: 12px; margin: 16px 0; }
.forecast-chart { display: flex; align-items: flex-end; gap: 8px; height: 160px; margin-top: 16px; }
.forecast-bar-col { flex: 1; display: flex; flex-direction: column; align-items: center; gap: 6px; height: 100%; justify-content: flex-end; }
.forecast-bar { width: 100%; background: var(--brand, #4f46e5); border-radius: 6px 6px 0 0; min-height: 4px; }
.forecast-bar.below { background: #ef4444; }
.forecast-bar-label { font-size: 11px; color: var(--muted, #6b7280); }
```

- [ ] **Step 4: Run the full workflow test suite**

Run (from `web/`): `pnpm test -- workflow`
Expected: PASS — all `web/src/components/workflow/*.test.tsx` green.

- [ ] **Step 5: Run typecheck**

Run (from `web/`): `pnpm typecheck`
Expected: PASS — no TS errors.

- [ ] **Step 6: Commit**

```bash
git add web/src/components/workflow/index.ts web/src/index.css
git commit -m "feat(payables-ux): workflow barrel export + styles"
```

---

## Self-Review

**Spec coverage (this plan = spec §1 Shared Workflow Components):**
- §1.1 StageTabBar → Task 3 ✓
- §1.2 PendencyDashboard → Task 4 ✓
- §1.3 SlaChips → Task 2 ✓
- §1.4 AuditTimeline → Task 5 ✓
- §1.5 BalanceForecastBanner → Task 6 ✓
- §1.6 BalanceForecastPanel → Task 7 ✓
- §6 i18n keys (workflow.*) → Task 1 ✓
- Spec §2–§5 (pages, approvals inbox, backend gaps) are **out of scope** for this plan — covered by follow-up plans (see Decomposition note below).

**Placeholder scan:** none — every step contains complete code and exact commands.

**Type consistency:** `StageTab`/`PendencyCard`/`AuditEvent` shapes match between definition tasks and the barrel export (Task 8). `useCashFlow` params (`{ companyId, horizon, granularity }`) and `CashFlowResponse` field access (`periods[].closingBalance`, `periods[].belowReserve`, `kpis.lowestProjectedBalance`, `kpis.runwayPeriods`, `kpis.netResult`) verified against `web/src/api/schema.d.ts` and `web/src/queries.ts`. `SlaChips` takes a pre-translated `stageLabel` — consuming pages own the stage→i18n mapping (consistent with spec §2.2/§3.2 tables).

---

## Decomposition Note (follow-up plans)

This is plan **1 of 5** for the overhaul. Remaining plans, each independently testable, to be written after this one:

2. **Backend endpoints** — `GET /api/payables/stage-summary`, `GET /api/receivables/stage-summary`, `GET /api/payables/{id}/audit-trail`, `GET /api/receivables/{id}/audit-trail` (.NET; spec §5). Unblocks the dashboard counts and the audit timeline data source.
3. **Payables pages** — `web/src/pages/payables/` (spec §2). Depends on plans 1 + 2.
4. **Receivables pages** — `web/src/pages/receivables/` (spec §3). Depends on plans 1 + 2.
5. **Approvals inbox** — `web/src/pages/approvals/` + sidebar entry (spec §4). Depends on plan 1.

Routing/migration (spec §8) folds into plans 3–5 as each page set lands.
