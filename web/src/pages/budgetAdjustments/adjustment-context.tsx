import { useTranslation } from 'react-i18next';
import { useDimensionBudgetControl } from '../../queries';
import { brlCents } from '../../lib';
import { LoadingState } from '../../ui';

/**
 * Budget context of an adjustment for a single competency: current ceiling → adjustment → resulting.
 * `direction` sets the sign: Supplementation adds (+), Reduction subtracts (−); a Transfer renders two
 * (source 'remove', destination 'add'). Reflects the given competency (no month navigator) with
 * add-to-ceiling math — the opposite sign of a requisition's spend-from-available.
 */
export function AdjustmentBudgetContext({
  unitId,
  costCenterId,
  competency,
  value,
  direction,
}: {
  unitId: string;
  costCenterId: string;
  competency: string;
  /** Absolute adjustment amount (always ≥ 0 as typed in the form). */
  value: number;
  /** '+' adds to the ceiling (Supplementation / Transfer destination); '−' subtracts (Reduction / source). */
  direction: 'add' | 'remove';
}) {
  const { t } = useTranslation();
  const ready = !!unitId && !!costCenterId && !!competency;
  const control = useDimensionBudgetControl(
    { unitId, costCenterId, competencyFrom: competency, competencyTo: competency },
    { enabled: ready },
  );

  if (!ready) {
    return (
      <div className='empty-state' style={{ padding: 24 }}>
        <div className='s'>
          {!unitId || !costCenterId
            ? t('budgetAdjustments.context.selectDimensions')
            : t('budgetAdjustments.context.selectCompetency')}
        </div>
      </div>
    );
  }

  if (control.isLoading) return <LoadingState />;

  const month = control.data?.months?.[0];
  const orcado = month?.orcado ?? 0;
  const available = month?.available ?? 0;
  const signed = direction === 'add' ? value : -value; // signed adjustment (adds to / subtracts from the ceiling)
  const resultingCeiling = orcado + signed;
  const resultingAvailable = available + signed;
  const negative = resultingAvailable < 0; // reduction/source exceeds the available → overflow

  return (
    <div style={{ padding: 16 }}>
      <div className='adj-ctx-rows'>
        <div className='adj-ctx-row'>
          <span className='adj-ctx-row-label'>{t('budgetAdjustments.context.currentCeiling')}</span>
          <span className='adj-ctx-row-value'>{brlCents(orcado)}</span>
        </div>
        <div className='adj-ctx-row'>
          <span className='adj-ctx-row-label'>{t('budgetAdjustments.context.adjustment')}</span>
          <span className='adj-ctx-row-value' style={{ color: signed >= 0 ? 'var(--success)' : 'var(--warning)' }}>
            {signed >= 0 ? '+ ' : '− '}
            {brlCents(Math.abs(signed))}
          </span>
        </div>
        <div className='adj-ctx-row'>
          <span className='adj-ctx-row-label'>{t('budgetAdjustments.context.resultingCeiling')}</span>
          <span className='adj-ctx-row-value'>{brlCents(resultingCeiling)}</span>
        </div>
        <div className='adj-ctx-row'>
          <span className='adj-ctx-row-label'>{t('budgetAdjustments.context.resultingAvailable')}</span>
          <span className='adj-ctx-row-value' style={{ color: negative ? 'var(--danger)' : undefined }}>
            {brlCents(resultingAvailable)}
          </span>
        </div>
      </div>

      {negative && (
        <div className='proj-alert' role='alert' style={{ marginTop: 10 }}>
          <span>{t('budgetAdjustments.context.reductionNegative', { amount: brlCents(-resultingAvailable) })}</span>
        </div>
      )}

      {month && !month.budgeted && (
        <div style={{ marginTop: 10, fontSize: 12, opacity: 0.7 }}>{t('requisitions.forecast.unbudgeted')}</div>
      )}
    </div>
  );
}
