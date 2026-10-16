import { useTranslation } from 'react-i18next';
import { type PeriodSummary } from '../../api';
import { brl } from '../../lib';
import { Kpi, LoadingState } from '../../ui';

/** Top strip with the period totals grouped by reconciliation status, plus debit/credit totals. */
export function PeriodSummaryTiles({ summary }: { summary: PeriodSummary | undefined }) {
  const { t } = useTranslation();
  if (!summary) return <LoadingState />;

  const count = (n: number) => t('conciliacao.summary.count', { count: n });

  return (
    <div
      className='kpi-grid reconciliation-summary'
      style={{ gridTemplateColumns: 'repeat(auto-fit, minmax(160px, 1fr))' }}
    >
      <Kpi
        label={t('conciliacao.status.Reconciled')}
        value={brl(summary.reconciled.total)}
        foot={count(summary.reconciled.count)}
      />
      <Kpi
        label={t('conciliacao.status.Suggested')}
        value={brl(summary.suggested.total)}
        foot={count(summary.suggested.count)}
      />
      <Kpi
        label={t('conciliacao.status.Unmatched')}
        value={brl(summary.unmatched.total)}
        foot={count(summary.unmatched.count)}
      />
      <Kpi
        label={t('conciliacao.status.Ignored')}
        value={brl(summary.ignored.total)}
        foot={count(summary.ignored.count)}
      />
      <Kpi label={t('conciliacao.summary.debitTotal')} value={brl(summary.debitTotal)} />
      <Kpi label={t('conciliacao.summary.creditTotal')} value={brl(summary.creditTotal)} />
    </div>
  );
}
