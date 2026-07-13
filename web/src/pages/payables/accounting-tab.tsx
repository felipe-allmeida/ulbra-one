import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Calculator } from 'lucide-react';
import { usePayableStageItems } from '../../queries';
import { Button } from '../../ui';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';
import { AccountingDrawer } from './accounting-drawer';

/**
 * Accounting stage ("Contabilidade"): lists PendingAccounting payables and opens the "Registro de Despesa".
 * Advances (origin=Advance) are excluded — they are tracked in the "Adiantamentos" tab, which carries its
 * own "Registrar despesa" action.
 */
export function AccountingTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems(
    { companyId, status: STAGE_STATUS.accounting, excludeOrigin: 'Advance', page },
    { enabled: !!companyId },
  );
  const data = list.data;
  const [reviewId, setReviewId] = useState<string | null>(null);

  return (
    <>
      <PayableTable
        companyId={companyId}
        stageLabel={t('payables.stages.accounting')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        extraColumn={{
          header: t('payables.accounting.actionColumn'),
          cell: (item) => (
            <Button icon={<Calculator />} onClick={() => setReviewId(item.payableId)}>
              {t('payables.accounting.reviewButton')}
            </Button>
          ),
        }}
      />
      {reviewId && <AccountingDrawer payableId={reviewId} onClose={() => setReviewId(null)} />}
    </>
  );
}
