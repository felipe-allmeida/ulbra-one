import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Check, X } from 'lucide-react';
import { type PayableStageItem } from '../../api';
import { usePayableStageItems, useApprovePayable, useRejectPayable } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { Button, ConfirmDialog, Input } from '../../ui';
import { BalanceForecastBanner, BalanceForecastPanel } from '../../components/workflow';
import { STAGE_STATUS } from './stages';
import { PayableTable } from './payable-table';

export function ApprovalTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canApprove = can(P.payablesApprove);
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayableStageItems({ companyId, status: STAGE_STATUS.approval, page }, { enabled: !!companyId });
  const data = list.data;

  const approve = useApprovePayable();
  const reject = useRejectPayable();
  const [toApprove, setToApprove] = useState<PayableStageItem | null>(null);
  const [toReject, setToReject] = useState<PayableStageItem | null>(null);
  const [reason, setReason] = useState('');

  return (
    <>
      <div style={{ marginBottom: 12 }}>
        <BalanceForecastBanner companyId={companyId} />
      </div>
      <PayableTable
        companyId={companyId}
        stageLabel={t('payables.stages.approval')}
        items={data?.items ?? []}
        total={data?.total ?? 0}
        page={page}
        totalPages={data?.totalPages ?? 1}
        onPage={setPage}
        extraColumn={
          canApprove
            ? {
                header: t('payables.columns.status'),
                cell: (item) => (
                  <div style={{ display: 'flex', gap: 6 }}>
                    <Button variant='brand' icon={<Check />} onClick={() => setToApprove(item)}>
                      {t('payables.actions.approve')}
                    </Button>
                    <Button
                      icon={<X />}
                      onClick={() => {
                        setToReject(item);
                        setReason('');
                      }}
                    >
                      {t('payables.actions.reject')}
                    </Button>
                  </div>
                ),
              }
            : undefined
        }
      />

      {toApprove && (
        <BalanceForecastPanel
          companyId={companyId}
          impactAmount={toApprove.totalAmount}
          open
          busy={approve.isPending}
          onClose={() => setToApprove(null)}
          onConfirm={() => approve.mutate({ id: toApprove.payableId }, { onSuccess: () => setToApprove(null) })}
        />
      )}

      {toReject && (
        <ConfirmDialog
          title={t('payables.reject.title')}
          danger
          busy={reject.isPending}
          message={
            <Input
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder={t('payables.reject.placeholder')}
            />
          }
          onCancel={() => setToReject(null)}
          onConfirm={() => reject.mutate({ id: toReject.payableId, reason }, { onSuccess: () => setToReject(null) })}
        />
      )}
    </>
  );
}
