import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Check, ChevronLeft, ChevronRight } from 'lucide-react';
import { type Requisition } from '../../api';
import { useRequisitionBudgetControl } from '../../queries';
import { brlCents } from '../../lib';
import { Chip, Panel, cx, LoadingState } from '../../ui';
import { BarLegend, money, Tile } from './budget-tiles';
import { fmtCompetency } from './competency';

/**
 * Approver budget decision: month-by-month navigation over the requisition's server-side budget
 * control (`/requisitions/{id}/budget-control`). The overage is authoritative (`breaches`) and is a hard
 * block — no override; resolved via a Budget Adjustment.
 */
export function DecisionBudgetPanel({ req, perCompetencyValue }: { req: Requisition; perCompetencyValue: number }) {
  const { t } = useTranslation();
  const control = useRequisitionBudgetControl(req.id);
  const [idx, setIdx] = useState(0);
  const months = control.data?.months ?? [];
  const breachMap = new Map((control.data?.breaches ?? []).map((b) => [b.competency, b]));

  if (control.isLoading) {
    return (
      <Panel title={t('requisitions.decision.title')}>
        <LoadingState />
      </Panel>
    );
  }
  if (months.length === 0) {
    return (
      <Panel title={t('requisitions.decision.title')}>
        <div className='empty-state' style={{ padding: 24 }}>
          <div className='s'>{t('requisitions.decision.noMonths')}</div>
        </div>
      </Panel>
    );
  }

  // Conta Fixa (AEL-881, R1): no budget ceiling — the server no longer returns breaches, but the value varies
  // month to month and the cell usually comes "sem orçamento"; here the signaling is informative, never a block.
  const noCeiling = req.type === 'FixedExpense';
  const safeIdx = Math.min(idx, months.length - 1);
  const m = months[safeIdx];
  const breach = breachMap.get(m.competency);
  // Authoritative server overage. The request's cost for this competency comes EXACT from the backend
  // (m.cost, remainder-on-last split); it only falls back to the uniform approximation if the backend sends 0.
  const over = !!breach;
  const thisRequest = m.cost > 0 ? m.cost : perCompetencyValue;
  const pct = (v: number) => (m.orcado > 0 ? Math.max(0, Math.min(100, (v / m.orcado) * 100)) : 0);
  // Verdict of the selected competency (fits/exceeds · X%) in the panel header — it used to live in the card.
  const pctThis = Math.round(pct(thisRequest));
  const verdict = m.budgeted ? (
    over ? (
      <Chip tone='warn'>{t('requisitions.summary.exceedsPct', { pct: pctThis })}</Chip>
    ) : (
      <Chip tone='auto' icon={<Check />}>
        {t('requisitions.summary.fitsPct', { pct: pctThis })}
      </Chip>
    )
  ) : undefined;

  return (
    <Panel title={t('requisitions.decision.title')} action={verdict}>
      <div style={{ padding: 16 }}>
        <p className='help' style={{ margin: '0 0 12px' }}>
          {t('requisitions.decision.hint')}
        </p>
        {/* The provision chart (line/area) lives in the aside — here only the month-by-month drill-down remains. */}
        <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', marginBottom: 12 }}>
          <button
            className='icon-btn'
            aria-label={t('requisitions.forecast.prevMonth')}
            title={t('requisitions.forecast.prevMonth')}
            disabled={safeIdx === 0}
            onClick={() => setIdx((i) => Math.max(0, i - 1))}
          >
            <ChevronLeft size={16} />
          </button>
          <span className='t-mono' style={{ fontWeight: 600, flex: 1, textAlign: 'center' }}>
            {fmtCompetency(m.competency)}
            <small style={{ display: 'block', fontWeight: 500, opacity: 0.65 }}>
              {t('requisitions.decision.monthOf', { i: safeIdx + 1, n: months.length })}
            </small>
          </span>
          <button
            className='icon-btn'
            aria-label={t('requisitions.forecast.nextMonth')}
            title={t('requisitions.forecast.nextMonth')}
            disabled={safeIdx === months.length - 1}
            onClick={() => setIdx((i) => Math.min(months.length - 1, i + 1))}
          >
            <ChevronRight size={16} />
          </button>
        </div>

        <div className='kpitiles'>
          <Tile {...money(m.orcado)} l={t('requisitions.forecast.budgeted')} />
          <Tile {...money(m.consumed)} l={t('requisitions.forecast.consumed')} />
          <Tile
            {...money(m.available)}
            l={t('requisitions.decision.available')}
            tone={m.available <= 0 ? 'bad' : undefined}
          />
          <Tile {...money(thisRequest)} l={t('requisitions.decision.thisRequest')} tone={over ? 'bad' : undefined} />
        </div>

        <div className='mbar' style={{ marginTop: 12 }}>
          <div className='mseg used' style={{ width: `${pct(m.consumed)}%` }} />
          <div
            className={cx('mseg', over ? 'over' : 'queued')}
            style={{ width: `${pct(over ? Math.max(0, m.available) : thisRequest)}%` }}
          />
        </div>
        <BarLegend
          items={[
            { color: 'var(--brand)', label: t('requisitions.forecast.consumed') },
            { color: over ? 'var(--danger)' : 'var(--accent)', label: t('requisitions.decision.thisRequest') },
            { color: 'var(--muted)', label: t('requisitions.decision.available') },
          ]}
        />

        {noCeiling ? (
          <div style={{ marginTop: 10, fontSize: 12.5, opacity: 0.75 }}>
            {t('requisitions.decision.fixedExpenseNoCeiling')}
          </div>
        ) : !m.budgeted ? (
          <div className='proj-alert' role='alert' style={{ marginTop: 10 }}>
            <span>{t('requisitions.decision.unbudgeted')}</span>
          </div>
        ) : over ? (
          <div className='proj-alert' role='alert' style={{ marginTop: 10 }}>
            <span>
              {t('requisitions.decision.exceeds', { amount: brlCents(Math.max(0, thisRequest - m.available)) })}
            </span>
          </div>
        ) : (
          <div style={{ marginTop: 10, fontSize: 12.5, opacity: 0.75 }}>
            {t('requisitions.decision.fits', { amount: brlCents(m.available - thisRequest) })}
          </div>
        )}
      </div>
    </Panel>
  );
}
