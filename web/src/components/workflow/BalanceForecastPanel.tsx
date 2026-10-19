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
