import { useTranslation } from 'react-i18next';
import { AlertTriangle, TrendingDown } from 'lucide-react';
import { usePaymentQueueSummary, usePeriodSummary } from '../../queries';
import { brlCents } from '../../lib';
import { cx, LoadingState } from '../../ui';
import { money, Tile } from '../requisitions/budget-tiles';
import { addDaysIso, summaryWindowNet } from './queue-groups';

/**
 * Financeiro home aside — "só-Payables" by design: every number derives from scheduled
 * installments (queue summary) or the OFX reconciliation counters. No bank-balance claims —
 * a real cash position needs statement balances, which is a future delivery.
 * Deliberately UNFILTERED (no search/method), so the KPIs stay stable while the queue tab filters.
 */
export function TreasuryAside({ companyId }: { companyId: string }) {
  const { t } = useTranslation();

  const summaryQuery = usePaymentQueueSummary(
    { companyId, status: 'Scheduled', paid: false },
    { enabled: !!companyId },
  );
  const summary = summaryQuery.data;
  const periodQuery = usePeriodSummary({ companyId }, { enabled: !!companyId });
  const period = periodQuery.data;

  const asOf = summary?.asOf ?? new Date().toISOString().slice(0, 10);
  const overdueNet = summary?.overdueNetTotal ?? 0;
  const overdueCount = summary?.overdueCount ?? 0;
  const todayBucket = summary?.groups.find((g) => g.dueDate === asOf);
  const todayNet = todayBucket?.netTotal ?? 0;
  const todayCount = todayBucket?.count ?? 0;
  const next7 = summary ? summaryWindowNet(summary, addDaysIso(asOf, 1), addDaysIso(asOf, 7)) : 0;
  const next8to14 = summary ? summaryWindowNet(summary, addDaysIso(asOf, 8), addDaysIso(asOf, 14)) : 0;
  // Outflow over the window = everything already late + due through day 14.
  const next14Total = overdueNet + todayNet + next7 + next8to14;
  const toReconcile = period ? period.suggested.count + period.unmatched.count : 0;

  if (summaryQuery.isLoading || periodQuery.isLoading) return <LoadingState />;

  return (
    <>
      <div className='spanel'>
        <div className='sph'>
          <div className='spt'>
            <TrendingDown aria-hidden />
            {t('treasury.aside.outflowTitle')}
          </div>
          <div className='sps'>{t('treasury.aside.outflowSub')}</div>
        </div>
        <div className='spbody'>
          <div className='cfbig'>
            <span className='n'>{brlCents(next14Total)}</span>
            <span className='d'>{t('treasury.aside.next14Label')}</span>
          </div>
          <div className='cfrows'>
            <div className='cfrow'>
              <span className='k'>{t('treasury.aside.rowOverdue')}</span>
              <span className={cx('val', overdueNet > 0 && 'neg')}>{brlCents(overdueNet)}</span>
            </div>
            <div className='cfrow'>
              <span className='k'>{t('treasury.aside.rowToday')}</span>
              <span className='val'>{brlCents(todayNet)}</span>
            </div>
            <div className='cfrow'>
              <span className='k'>{t('treasury.aside.rowNext7')}</span>
              <span className='val'>{brlCents(next7)}</span>
            </div>
            <div className='cfrow'>
              <span className='k'>{t('treasury.aside.rowNext14')}</span>
              <span className='val'>{brlCents(next8to14)}</span>
            </div>
          </div>
          {overdueCount > 0 && (
            <div className='cfnote'>
              <AlertTriangle aria-hidden />
              <span>{t('treasury.aside.overdueNote', { count: overdueCount, amount: brlCents(overdueNet) })}</span>
            </div>
          )}
        </div>
      </div>

      <div className='spanel'>
        <div className='sph'>
          <div className='spt'>{t('treasury.aside.tilesTitle')}</div>
        </div>
        <div className='spbody'>
          <div className='kpitiles'>
            <Tile
              n={String(todayCount)}
              l={t('treasury.aside.tileDueToday')}
              tone={todayCount > 0 ? 'warn' : undefined}
            />
            <Tile
              n={String(overdueCount)}
              l={t('treasury.aside.tileOverdue')}
              tone={overdueCount > 0 ? 'bad' : undefined}
            />
            <Tile {...money(summary?.totalNetTotal ?? 0)} l={t('treasury.aside.tileQueueTotal')} />
            <Tile
              n={String(toReconcile)}
              l={t('treasury.aside.tileToReconcile')}
              tone={toReconcile > 0 ? 'warn' : undefined}
            />
          </div>
        </div>
      </div>
    </>
  );
}
