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
