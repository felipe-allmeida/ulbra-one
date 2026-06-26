import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayables } from '../../queries';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';

export function ReconciledTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: STAGE_STATUS.reconciled, page }, { enabled: !!companyId });
  const data = list.data;
  return (
    <PayableTable
      companyId={companyId}
      stageLabel={t('payables.stages.reconciled')}
      items={data?.items ?? []}
      total={data?.total ?? 0}
      page={page}
      totalPages={data?.totalPages ?? 1}
      onPage={setPage}
    />
  );
}
