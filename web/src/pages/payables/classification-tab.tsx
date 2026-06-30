import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Tags } from 'lucide-react';
import { usePayableStageItems } from '../../queries';
import { Button } from '../../ui';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';
import { ClassificationDrawer } from './classification-drawer';

export function ClassificationTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems({ companyId, status: STAGE_STATUS.classification, page }, { enabled: !!companyId });
  const data = list.data;
  const [classifyId, setClassifyId] = useState<string | null>(null);

  return (
    <>
      <PayableTable
        companyId={companyId}
        stageLabel={t('payables.stages.classification')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        extraColumn={{
          header: t('payables.classify.title'),
          cell: (item) => (
            <Button icon={<Tags />} onClick={() => setClassifyId(item.payableId)}>
              {t('payables.classify.title')}
            </Button>
          ),
        }}
      />
      {classifyId && <ClassificationDrawer payableId={classifyId} onClose={() => setClassifyId(null)} />}
    </>
  );
}
