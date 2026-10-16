import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { HandCoins } from 'lucide-react';
import { type ReceivableListItem } from '../../api';
import { useReceivables, useReceivable } from '../../queries';
import { P, usePermissions } from '../../permissions';
import { brl } from '../../lib';
import { Panel, Button, Drawer, EmptyState, LoadingState, SkeletonRows } from '../../ui';
import { fmtDate } from '../../datetime';
import { groupByPeriod } from './receipt-periods';
import { ReceivableInstallmentStatusBadge } from './badges';
import { ReceiptForm } from './receipt-form';

const GROUP_LABEL: Record<string, string> = {
  today: 'workflow.payment.groupToday',
  thisWeek: 'workflow.payment.groupThisWeek',
  nextWeek: 'workflow.payment.groupNextWeek',
  future: 'workflow.payment.groupFuture',
};

export function AReceberTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canReceive = can(P.receivablesReceive);
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = useReceivables({ companyId, status: 'AwaitingReceipt', page }, { enabled: !!companyId });
  const items = list.data?.items ?? [];
  const groups = groupByPeriod(items);
  const total = items.reduce((s, i) => s + i.amount, 0);

  const [baixa, setBaixa] = useState<ReceivableListItem | null>(null);
  const detailQuery = useReceivable(baixa?.receivableId);
  const detail = detailQuery ?? { data: undefined };
  const installment = detail.data?.installments.find((i) => i.id === baixa?.installmentId) ?? null;

  if (list.isLoading) {
    return (
      <Panel title={t('receivables.tabs.areceber')}>
        <table>
          <thead>
            <tr>
              <th>{t('receivables.columns.dueDate')}</th>
              <th>{t('receivables.columns.payer')}</th>
              <th>{t('receivables.columns.installment')}</th>
              <th style={{ textAlign: 'right' }}>{t('receivables.columns.amount')}</th>
              <th>{t('receivables.columns.status')}</th>
              <th />
            </tr>
          </thead>
          <tbody>
            <SkeletonRows rows={5} cols={6} />
          </tbody>
        </table>
      </Panel>
    );
  }

  if (items.length === 0) {
    return (
      <EmptyState
        icon={<HandCoins />}
        title={t('receivables.empty.noItemsTitle')}
        sub={t('receivables.empty.noItemsSub')}
      />
    );
  }

  return (
    <>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <strong>{t('workflow.payment.weekSummary', { amount: brl(total), count: items.length })}</strong>
      </div>

      {groups.map((g) => (
        <Panel key={g.key} title={`${t(GROUP_LABEL[g.key] ?? '')} · ${brl(g.subtotal)}`}>
          <table>
            <thead>
              <tr>
                <th>{t('receivables.columns.dueDate')}</th>
                <th>{t('receivables.columns.payer')}</th>
                <th>{t('receivables.columns.installment')}</th>
                <th style={{ textAlign: 'right' }}>{t('receivables.columns.amount')}</th>
                <th>{t('receivables.columns.status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {g.items.map((l) => (
                <tr key={l.installmentId}>
                  <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                  <td>{l.payerName || '—'}</td>
                  <td className='t-mono'>
                    {l.sequence}/{l.installmentCount}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td>
                    <ReceivableInstallmentStatusBadge s={l.installmentStatus} />
                  </td>
                  <td>
                    {canReceive && l.installmentStatus !== 'Received' && (
                      <Button icon={<HandCoins />} onClick={() => setBaixa(l)}>
                        {t('receivables.register')}
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      ))}

      <Drawer open={!!baixa} onClose={() => setBaixa(null)} title={t('receivables.register')} sub={baixa?.payerName}>
        {detailQuery?.isLoading ? (
          <LoadingState />
        ) : (
          detail.data &&
          installment && (
            <ReceiptForm
              receivable={detail.data}
              installment={installment}
              onDone={() => setBaixa(null)}
              onCancel={() => setBaixa(null)}
            />
          )
        )}
      </Drawer>
    </>
  );
}
