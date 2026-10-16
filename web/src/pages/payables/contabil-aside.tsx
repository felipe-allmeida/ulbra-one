import { useTranslation } from 'react-i18next';
import { AlertTriangle, Coins } from 'lucide-react';
import { usePayableStageItems, usePayableStageSummary } from '../../queries';
import { brl } from '../../lib';
import { cx, LoadingState } from '../../ui';
import { money, Tile } from '../requisitions/budget-tiles';

/**
 * Accounting home aside ("Revisão de Contabilidade"), mirroring the other roles' pattern
 * (RequesterAside/ApproverAside): an "A caminho do Financeiro" panel (what flows on after
 * classification) + the accountant's workload KPI tiles. Reuses the existing endpoints:
 * - stage-summary → payment (queue heading to Finance) and fiscalDocumentPending (NF pending);
 * - stage-items?reviewPending → the "A classificar" count (classification review queue).
 */
export function ContabilAside({ companyId }: { companyId: string }) {
  const { t } = useTranslation();

  const summaryQuery = usePayableStageSummary(companyId);
  const summary = summaryQuery.data;
  const reviewPendingQuery = usePayableStageItems(
    { companyId, reviewPending: true, page: 1, pageSize: 1 },
    { enabled: !!companyId },
  );
  const reviewPending = reviewPendingQuery.data;

  const payment = summary?.payment;
  const fiscalPending = summary?.fiscalDocumentPending;

  // "na fila" = the queue scheduled for Finance (payment bucket = Scheduled). "Aguardando NF" is a
  // SEPARATE slice (fiscalDocumentPending spans PendingAccounting|Scheduled|Paid without an NF), so the
  // scheduled queue can NOT be decomposed into "ready vs awaiting NF" — the backend exposes no
  // Scheduled-without-NF count. We only show correct numbers: net total + scheduled count; "Aguardando NF"
  // becomes a standalone KPI.
  const inQueue = payment?.netTotalAmount ?? payment?.totalAmount ?? 0;
  const scheduledCount = payment?.count ?? 0;
  const awaitingNf = fiscalPending?.count ?? 0;
  const toClassify = reviewPending?.total ?? 0;

  if (summaryQuery.isLoading || reviewPendingQuery.isLoading) return <LoadingState />;

  return (
    <>
      <div className='spanel'>
        <div className='sph'>
          <div className='spt'>
            <Coins aria-hidden />
            {t('accounting.review.aside.flowTitle')}
          </div>
          <div className='sps'>{t('accounting.review.aside.flowSub')}</div>
        </div>
        <div className='spbody'>
          <div className='cfbig'>
            <span className='n'>{brl(inQueue)}</span>
            <span className='d'>{t('accounting.review.aside.inQueueLabel')}</span>
          </div>
          <div className='cfrows'>
            <div className='cfrow'>
              <span className='k'>{t('accounting.review.aside.scheduled')}</span>
              <span className={cx('val', scheduledCount > 0 && 'pos')}>{scheduledCount}</span>
            </div>
          </div>
          {awaitingNf > 0 && (
            <div className='cfnote'>
              <AlertTriangle aria-hidden />
              <span>{t('accounting.review.aside.nfNote', { count: awaitingNf })}</span>
            </div>
          )}
        </div>
      </div>

      <div className='spanel'>
        <div className='sph'>
          <div className='spt'>{t('accounting.review.aside.tilesTitle')}</div>
        </div>
        <div className='spbody'>
          <div className='kpitiles kpitiles--span-last'>
            <Tile
              n={String(toClassify)}
              l={t('accounting.review.aside.toClassify')}
              tone={toClassify > 0 ? 'warn' : undefined}
            />
            <Tile
              n={String(awaitingNf)}
              l={t('accounting.review.aside.awaitingNf')}
              tone={awaitingNf > 0 ? 'warn' : undefined}
            />
            <Tile {...money(inQueue)} l={t('accounting.review.aside.inQueue')} />
          </div>
        </div>
      </div>
    </>
  );
}
