import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calculator } from 'lucide-react';
import { usePayableStageItems } from '../../queries';
import { useApp } from '../../store';
import { P, usePermissions } from '../../permissions';
import { Button } from '../../ui';
import { STAGE_STATUS } from './stages';
import { PayableStageList, PayableStageDetail, RequestFiscalDocumentButton } from './payable-stage-list';
import { AccountingDrawer } from './accounting-drawer';
import { useNames } from './use-names';

/**
 * Accounting stage ("Despesas" / "Contabilidade"): lists PendingAccounting payables as a compact
 * expandable list and opens the "Registro de Despesa" drawer to review + post. Shared with the
 * Finance payables layout, so both surfaces render the same compact format.
 */
export function AccountingTab({
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
  const canWrite = can(P.payablesWrite) && !isAllCompanies;
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId, search]);
  const list = usePayableStageItems(
    { companyId, status: STAGE_STATUS.accounting, search, page },
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
        emptyTitle={`${t('payables.stages.accounting')} — ${t('payables.empty.noItemsTitle')}`}
        companyNameFor={isAllCompanies ? (item) => empresas.find((e) => e.id === item.companyId)?.legalName : undefined}
        flagFor={(item) =>
          item.hasFiscalDocument === false && item.origin !== 'Advance'
            ? { text: t('payables.tags.fiscalDocPending'), tone: 'warn' }
            : undefined
        }
        renderBody={(item) => (
          <>
            <PayableStageDetail item={item} centroName={centroNome[item.costCenterId] ?? '—'} />
            {canWrite && (
              <div className='xrow-actions'>
                <Button variant='brand' icon={<Calculator />} onClick={() => setReviewId(item.payableId)}>
                  {t('payables.accounting.reviewButton')}
                </Button>
                {item.hasFiscalDocument === false && item.origin !== 'Advance' && (
                  <RequestFiscalDocumentButton payableId={item.payableId} />
                )}
              </div>
            )}
          </>
        )}
      />
      {reviewId && <AccountingDrawer payableId={reviewId} onClose={() => setReviewId(null)} />}
    </>
  );
}
