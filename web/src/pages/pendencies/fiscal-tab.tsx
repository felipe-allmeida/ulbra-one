import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { FileUp } from 'lucide-react';
import { type PayableStageItem } from '../../api';
import { usePayableStageItems } from '../../queries';
import { Button } from '../../ui';
import { STAGE_STATUS } from '../payables/stages';
import { PayableTable } from '../payables/payable-table';
import { AttachNfDrawer } from './attach-nf-drawer';

/**
 * "Nota Fiscal" pendency tab: approved payables (auto or manual) parked in PendingAccounting that still
 * have no NF. The row action attaches the NF in place; attaching does NOT complete accounting (that stays
 * in the Contabilidade tab of /app/payables).
 */
export function FiscalTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems(
    { companyId, status: STAGE_STATUS.accounting, hasFiscalDocument: false, page },
    { enabled: !!companyId },
  );
  const data = list.data;
  const [toAttach, setToAttach] = useState<PayableStageItem | null>(null);

  return (
    <>
      <PayableTable
        companyId={companyId}
        stageLabel={t('pendencies.tabs.fiscal')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        extraColumn={{
          header: t('pendencies.fiscal.actionHeader'),
          cell: (item) => (
            <Button variant='brand' icon={<FileUp />} onClick={() => setToAttach(item)}>
              {t('pendencies.fiscal.attach')}
            </Button>
          ),
        }}
      />

      {toAttach && <AttachNfDrawer item={toAttach} onClose={() => setToAttach(null)} />}
    </>
  );
}
