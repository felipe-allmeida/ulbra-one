import { useTranslation } from 'react-i18next';
import { AlertTriangle, Landmark } from 'lucide-react';
import { usePayableStageItems, usePayableStageSummary } from '../../queries';
import { brl } from '../../lib';
import { LoadingState } from '../../ui';
import { STAGE_STATUS } from './stages';

/**
 * Tributação home aside — the Tributário's "Precisa de você" panel, mirroring the other roles' pattern
 * (ContabilAside/RequesterAside). Shows the workload that needs the Tributário: titles in análise tributária
 * (PendingTaxAnalysis) blocking payment, the gross value under análise, and how many still lack a Nota Fiscal
 * (so "Solicitar NF"). Reuses stage-summary (taxAnalysis bucket) + stage-items (status + hasFiscalDocument).
 */
export function TributacaoAside({ companyId }: { companyId: string }) {
  const { t } = useTranslation();

  const summaryQuery = usePayableStageSummary(companyId);
  const summary = summaryQuery.data;
  const awaitingNfQuery = usePayableStageItems(
    { companyId, status: STAGE_STATUS.taxAnalysis, hasFiscalDocument: false, page: 1, pageSize: 1 },
    { enabled: !!companyId },
  );

  const inAnalysisCount = summary?.taxAnalysis.count ?? 0;
  const inAnalysisAmount = summary?.taxAnalysis.totalAmount ?? 0;
  const awaitingNf = awaitingNfQuery.data?.total ?? 0;

  if (summaryQuery.isLoading || awaitingNfQuery.isLoading) return <LoadingState />;

  return (
    <div className='spanel'>
      <div className='sph'>
        <div className='spt'>
          <Landmark aria-hidden />
          {t('tax.aside.needsYouTitle')}
        </div>
        <div className='sps'>{t('tax.aside.needsYouSub')}</div>
      </div>
      <div className='spbody'>
        <div className='cfbig'>
          <span className='n'>{inAnalysisCount}</span>
          <span className='d'>{t('tax.aside.inAnalysisLabel')}</span>
        </div>
        <div className='cfrows'>
          <div className='cfrow'>
            <span className='k'>{t('tax.aside.awaitingNf')}</span>
            <span className='val'>{awaitingNf}</span>
          </div>
          <div className='cfrow'>
            <span className='k'>{t('tax.aside.grossInAnalysis')}</span>
            <span className='val'>{brl(inAnalysisAmount)}</span>
          </div>
        </div>
        {awaitingNf > 0 && (
          <div className='cfnote'>
            <AlertTriangle aria-hidden />
            <span>{t('tax.aside.nfNote', { count: awaitingNf })}</span>
          </div>
        )}
      </div>
    </div>
  );
}
