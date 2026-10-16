import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Landmark } from 'lucide-react';
import { usePayableStageItems } from '../../queries';
import { useApp } from '../../store';
import { P, usePermissions } from '../../permissions';
import { Button } from '../../ui';
import { STAGE_STATUS } from './stages';
import { PayableStageList, PayableStageDetail, RequestFiscalDocumentButton } from './payable-stage-list';
import { TaxAnalysisDrawer } from './tax-analysis-drawer';
import { useNames } from './use-names';

/**
 * Tax stage ("Análise Tributária"): lists PendingTaxAnalysis payables (service/fixed-expense) as a compact
 * expandable list and opens the tax drawer to validate the withholdings and release for payment. Shared by the
 * standalone tax page and the Finance payables pipeline. "Solicitar NF" is always offered (even with attachments).
 */
export function TaxAnalysisTab({
  companyId,
  search,
  isAllCompanies,
}: {
  companyId: string;
  search?: string;
  isAllCompanies?: boolean;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const { empresas } = useApp();
  // Read-only in the consolidated "Todas" view (AEL-906): act after switching to a company.
  const canReview = can(P.payablesTax) && !isAllCompanies;
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId, search]);
  const list = usePayableStageItems(
    { companyId, status: STAGE_STATUS.taxAnalysis, search, page },
    { enabled: !!companyId },
  );
  const data = list.data;
  const { centroNome } = useNames(companyId);
  const [reviewId, setReviewId] = useState<string | null>(null);

  return (
    <>
      <PayableStageList
        items={data?.items ?? []}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        loading={list.isLoading}
        emptyTitle={`${t('payables.stages.taxAnalysis')} — ${t('payables.empty.noItemsTitle')}`}
        companyNameFor={isAllCompanies ? (item) => empresas.find((e) => e.id === item.companyId)?.legalName : undefined}
        flagFor={(item) =>
          item.hasFiscalDocument === false ? { text: t('payables.tags.fiscalDocPending'), tone: 'warn' } : undefined
        }
        renderBody={(item) => (
          <>
            <PayableStageDetail item={item} centroName={centroNome[item.costCenterId] ?? '—'} />
            {canReview && (
              <div className='xrow-actions'>
                <Button variant='brand' icon={<Landmark />} onClick={() => setReviewId(item.payableId)}>
                  {t('payables.taxAnalysis.reviewButton')}
                </Button>
                {/* "Solicitar NF" always available (even with attachments) — a tax title is always requisition-sourced. */}
                <RequestFiscalDocumentButton payableId={item.payableId} />
              </div>
            )}
          </>
        )}
      />
      {reviewId && <TaxAnalysisDrawer payableId={reviewId} onClose={() => setReviewId(null)} />}
    </>
  );
}
