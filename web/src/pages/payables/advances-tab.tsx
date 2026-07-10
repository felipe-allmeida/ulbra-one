import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayableStageItems } from '../../queries';
import { PayableTable } from './payable-table';
import { PayableStatusBadge } from './badges';

/**
 * "Adiantamentos" tab of Registro de Despesa: every payable created as an advance (origin=Advance),
 * whatever its current stage — an adiantamento is paid before the NF exists, so the accountant tracks
 * them here. Each row shows the payable's actual status plus the Adiantamento tag.
 */
export function AdvancesTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems({ companyId, origin: 'Advance', page }, { enabled: !!companyId });
  const data = list.data;

  return (
    <PayableTable
      companyId={companyId}
      stageLabel={t('expenseRecording.tabs.advances')}
      items={data?.items ?? []}
      total={data?.total ?? 0}
      page={page}
      totalPages={data?.totalPages ?? 1}
      onPage={setPage}
      statusCell={(item) => (
        <>
          <PayableStatusBadge s={item.status} />
          <span className='badge-status badge-remessa' style={{ marginLeft: 6 }}>
            {t('payables.tags.advance')}
          </span>
        </>
      )}
    />
  );
}
