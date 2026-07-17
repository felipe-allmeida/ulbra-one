import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calculator } from 'lucide-react';
import { usePayableStageItems } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button } from '../../ui';
import { PayableTable } from './payable-table';
import { PayableStatusBadge } from './badges';
import { AccountingDrawer } from './accounting-drawer';

/**
 * "Adiantamentos" tab of Registro de Despesa: every payable created as an advance (origin=Advance),
 * whatever its current stage — an adiantamento is paid before the NF exists, so the accountant tracks
 * them here. Each row shows the payable's actual status plus the Adiantamento tag. Advances awaiting
 * accounting expose the "Registrar despesa" action (opens the accounting drawer, NF-exempt); this is
 * their only entry point since they never surface in the "Registro de Despesa" review tab.
 */
export function AdvancesTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canWrite = can(P.payablesWrite);
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems({ companyId, origin: 'Advance', page }, { enabled: !!companyId });
  const data = list.data;
  const [reviewId, setReviewId] = useState<string | null>(null);

  return (
    <>
      <PayableTable
        companyId={companyId}
        stageLabel={t('expenseRecording.tabs.advances')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        showNet
        statusCell={(item) => (
          <>
            <PayableStatusBadge s={item.status} />
            <span className='badge-status badge-remessa' style={{ marginLeft: 6 }}>
              {t('payables.tags.advance')}
            </span>
          </>
        )}
        extraColumn={
          canWrite
            ? {
                header: t('payables.accounting.actionColumn'),
                // Only advances still awaiting accounting can be contabilizadas; later stages have no action.
                cell: (item) =>
                  item.status === 'PendingAccounting' ? (
                    <Button icon={<Calculator />} onClick={() => setReviewId(item.payableId)}>
                      {t('payables.accounting.reviewButton')}
                    </Button>
                  ) : null,
              }
            : undefined
        }
      />
      {reviewId && <AccountingDrawer payableId={reviewId} onClose={() => setReviewId(null)} />}
    </>
  );
}
