import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calculator } from 'lucide-react';
import { type PayableStatus } from '../../api';
import { usePayableStageItems } from '../../queries';
import { useApp } from '../../store';
import { P, usePermissions } from '../../permissions';
import { Button } from '../../ui';
import { PayableStageList, PayableStageDetail } from './payable-stage-list';
import { AccountingDrawer } from './accounting-drawer';
import { useNames } from './use-names';

// The whole tab is advances, so the redundant "Adiantamento" tag is dropped; the at-a-glance chip carries
// the payable's actual status instead (advances span stages — paid before the NF, then posted).
const STATUS_TONE: Record<PayableStatus, 'ok' | 'warn' | 'bad' | 'calm'> = {
  Received: 'calm',
  PendingClassification: 'warn',
  PendingApproval: 'warn',
  PendingAccounting: 'warn',
  PendingTaxAnalysis: 'warn',
  Scheduled: 'ok',
  Paid: 'calm',
  Rejected: 'bad',
  Cancelled: 'bad',
};

/**
 * "Adiantamentos" tab of the Contábil home: every payable created as an advance (origin=Advance), whatever
 * its stage — an adiantamento is paid before the NF exists, so the accountant tracks them here. Compact
 * expandable list: the status is the row chip; advances still awaiting accounting expose "Registrar despesa"
 * (opens the accounting drawer, NF-exempt) — their only entry point, since they never hit the review tab.
 */
export function AdvancesTab({
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
  const list = usePayableStageItems({ companyId, origin: 'Advance', search, page }, { enabled: !!companyId });
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
        emptyTitle={`${t('expenseRecording.tabs.advances')} — ${t('payables.empty.noItemsTitle')}`}
        companyNameFor={isAllCompanies ? (item) => empresas.find((e) => e.id === item.companyId)?.legalName : undefined}
        flagFor={(item) => ({ text: t(`payables.status.${item.status}`), tone: STATUS_TONE[item.status] })}
        renderBody={(item) => (
          <>
            <PayableStageDetail item={item} centroName={centroNome[item.costCenterId] ?? '—'} />
            {/* Only advances still awaiting accounting can be posted; later stages have no action. */}
            {canWrite && item.status === 'PendingAccounting' && (
              <div className='xrow-actions'>
                <Button variant='brand' icon={<Calculator />} onClick={() => setReviewId(item.payableId)}>
                  {t('payables.accounting.reviewButton')}
                </Button>
              </div>
            )}
          </>
        )}
      />
      {reviewId && <AccountingDrawer payableId={reviewId} onClose={() => setReviewId(null)} />}
    </>
  );
}
