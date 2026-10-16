import { useParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { RotateCcw, Scale, X } from 'lucide-react';
import { useBudgetAdjustment, useCostCenters, useMe, useUnits } from '../../queries';
import { brl } from '../../lib';
import { BackLink } from '../../nav';
import { fmtDateTime } from '../../datetime';
import { EmptyState, Panel, LoadingState } from '../../ui';
import { AuditTimeline } from '../../components/workflow';
import { AdjustmentBudgetContext } from './adjustment-context';
import { fmtCompetency } from '../requisitions/competency';
import { BudgetAdjustmentSlaChip, BudgetAdjustmentStatusBadge } from './badges';
import { TransitionActions } from './transitions';
import { latestReason, transitionsToEvents } from './timeline';

export function BudgetAdjustmentDetailPage() {
  const { id } = useParams();
  const { t } = useTranslation();
  const query = useBudgetAdjustment(id);
  const a = query.data ?? null;
  const me = useMe().data;
  const units = useUnits(a?.companyId ?? '').data ?? [];
  const centros = useCostCenters(a?.companyId ?? '').data ?? [];

  if (query.isLoading) return <LoadingState />;
  if (!a) return <EmptyState icon={<Scale />} title={t('budgetAdjustments.empty.notFound')} />;

  const unitName = units.find((u) => u.id === a.unitId)?.name ?? a.unitId;
  const centroName = centros.find((c) => c.id === a.costCenterId)?.name ?? a.costCenterId;
  const reason = latestReason(a);
  const competencyText =
    a.type === 'Transfer' && a.sourceCompetency
      ? `${fmtCompetency(a.sourceCompetency)} → ${fmtCompetency(a.competency)}`
      : fmtCompetency(a.competency);

  return (
    <>
      <BackLink fallback='/app/budget-adjustments' />
      {/* Top action bar (mirrors Payable); TransitionActions renders its own `.detail-actions`. */}
      <TransitionActions a={a} />

      <div className='detail-stack'>
        {reason && <ReasonCallout action={reason.action} reason={reason.reason} />}

        {/* "Dados" card (same layout as Requisition/Payable) — label→value rows, not blocks. */}
        <div className='rq-summary'>
          <div className='rq-summary-head'>
            <div className='rq-summary-heading'>
              <h2 className='rq-summary-title'>
                #{a.code} — {t(`budgetAdjustments.type.${a.type}`)}
              </h2>
              <div className='rq-summary-meta'>
                <BudgetAdjustmentStatusBadge s={a.status} />
                <BudgetAdjustmentSlaChip a={a} />
                <span className='rq-summary-created'>
                  {t('budgetAdjustments.detail.createdAt', { date: fmtDateTime(a.createdAt) })}
                </span>
              </div>
            </div>
            <div className='rq-summary-amount'>
              <div className='rq-summary-amount-value'>{brl(a.value)}</div>
              <div className='rq-summary-amount-label'>{t('budgetAdjustments.fields.value')}</div>
            </div>
          </div>

          <div className='rq-summary-sec'>
            <div className='rq-seclabel rq-seclabel-brand'>{t('budgetAdjustments.form.section')}</div>
            <div className='rq-kv'>
              <KvCell label={t('budgetAdjustments.fields.type')} value={t(`budgetAdjustments.type.${a.type}`)} />
              <KvCell label={t('budgetAdjustments.fields.unit')} value={unitName} />
              <KvCell label={t('budgetAdjustments.fields.costCenter')} value={centroName} />
              <KvCell label={t('budgetAdjustments.fields.competency')} value={competencyText} />
              {a.counterparts && <KvCell label={t('budgetAdjustments.fields.counterparts')} value={a.counterparts} />}
            </div>
          </div>

          <div className='rq-summary-sec'>
            <div className='info-label'>{t('budgetAdjustments.fields.justification')}</div>
            <p className='detail-desc-text'>{a.justification}</p>
          </div>
        </div>

        {/* Budget context (before → adjustment → after); a Transfer shows source − and destination +. */}
        <Panel title={t('budgetAdjustments.context.title')}>
          <div style={{ display: 'grid', gap: 16 }}>
            {a.type === 'Transfer' && a.sourceCompetency && (
              <div>
                <div style={{ fontSize: 12, fontWeight: 600, opacity: 0.7, padding: '0 16px' }}>
                  {t('budgetAdjustments.context.source')}
                </div>
                <AdjustmentBudgetContext
                  unitId={a.unitId}
                  costCenterId={a.costCenterId}
                  competency={a.sourceCompetency}
                  value={a.value}
                  direction='remove'
                />
              </div>
            )}
            <div>
              {a.type === 'Transfer' && (
                <div style={{ fontSize: 12, fontWeight: 600, opacity: 0.7, padding: '0 16px' }}>
                  {t('budgetAdjustments.context.destination')}
                </div>
              )}
              <AdjustmentBudgetContext
                unitId={a.unitId}
                costCenterId={a.costCenterId}
                competency={a.competency}
                value={a.value}
                direction={a.type === 'Reduction' ? 'remove' : 'add'}
              />
            </div>
          </div>
        </Panel>

        <Panel title={t('budgetAdjustments.detail.timeline')}>
          <AuditTimeline events={transitionsToEvents(a.transitions, me?.userId)} />
        </Panel>
      </div>
    </>
  );
}

/** Label→value cell (muted label above, bold value below) for the "Dados" 2-column grid. */
function KvCell({ label, value }: { label: string; value: string }) {
  return (
    <div className='rq-kv-cell'>
      <div className='info-label'>{label}</div>
      <div className='rq-kv-value'>{value}</div>
    </div>
  );
}

/** Highlighted callout for the reversal (amber) or cancellation (red) reason. */
function ReasonCallout({ action, reason }: { action: 'Cancel' | 'Reverse'; reason: string }) {
  const { t } = useTranslation();
  const isCancel = action === 'Cancel';
  const Icon = isCancel ? X : RotateCcw;
  const color = isCancel ? 'var(--danger)' : 'var(--warning)';
  const bg = isCancel ? 'var(--danger-subtle)' : 'var(--warning-subtle)';
  return (
    <div
      role='alert'
      style={{
        display: 'flex',
        gap: 10,
        alignItems: 'flex-start',
        padding: '12px 16px',
        borderRadius: 'calc(var(--radius) + 2px)',
        border: `1px solid ${color}`,
        background: bg,
      }}
    >
      <Icon size={18} style={{ color, flexShrink: 0, marginTop: 1 }} />
      <div>
        <div style={{ fontWeight: 600, fontSize: 13, marginBottom: 2 }}>
          {t(isCancel ? 'budgetAdjustments.detail.cancelReasonLabel' : 'budgetAdjustments.detail.reverseReasonLabel')}
        </div>
        <div style={{ fontSize: 14 }}>{reason}</div>
      </div>
    </div>
  );
}
