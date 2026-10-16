import { useTranslation } from 'react-i18next';
import { useReceivableStageItems } from '../../queries';
import { usePageParam, useResetPageOnChange } from '../../nav';
import { TAB_STATUS } from './stages';
import { ReceivableTable } from './receivable-table';

export function ReconciledTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = usePageParam();
  useResetPageOnChange(setPage, [companyId]);
  const list = useReceivableStageItems({ companyId, status: TAB_STATUS.reconciled, page }, { enabled: !!companyId });
  const data = list.data;
  return (
    <ReceivableTable
      companyId={companyId}
      stageLabel={t('receivables.tabs.reconciled')}
      items={data?.items ?? []}
      total={data?.total ?? 0}
      page={page}
      totalPages={data?.totalPages ?? 1}
      onPage={setPage}
      loading={list.isLoading}
    />
  );
}
