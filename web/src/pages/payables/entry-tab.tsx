import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { usePayableStageItems } from '../../queries';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';

export function EntryTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems({ companyId, status: STAGE_STATUS.entry, page }, { enabled: !!companyId });
  const data = list.data;
  return (
    <PayableTable
      companyId={companyId}
      stageLabel={t('payables.stages.entry')}
      items={data?.items ?? []}
      total={data?.total ?? 0}
      page={page}
      totalPages={data?.totalPages ?? 1}
      onPage={setPage}
    />
  );
}
