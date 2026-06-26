import { useEffect, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { Receipt, FileOutput } from 'lucide-react';
import { type PayableListItem } from '../../api';
import { usePayables, usePayable } from '../../queries';
import { brl } from '../../lib';
import { Panel, Button, Drawer, EmptyState } from '../../ui';
import { fmtDate } from '../../datetime';
import { groupByPeriod } from './payment-periods';
import { InstallmentStatusBadge } from './badges';
import { PaymentForm } from './payment-form';

const GROUP_LABEL: Record<string, string> = {
  today: 'workflow.payment.groupToday',
  thisWeek: 'workflow.payment.groupThisWeek',
  nextWeek: 'workflow.payment.groupNextWeek',
  future: 'workflow.payment.groupFuture',
};

export function PaymentTab({ companyId }: { companyId: string }) {
  const { t } = useTranslation();
  const nav = useNavigate();
  const [page, setPage] = useState(1);
  useEffect(() => setPage(1), [companyId]);
  const list = usePayables({ companyId, status: 'Scheduled', page }, { enabled: !!companyId });
  const items = list.data?.items ?? [];
  const groups = groupByPeriod(items);
  const total = items.reduce((s, i) => s + i.amount, 0);

  const [baixa, setBaixa] = useState<PayableListItem | null>(null);
  const detailQuery = usePayable(baixa?.payableId);
  const detail = detailQuery ?? { data: undefined };
  const installment = detail.data?.installments.find((i) => i.id === baixa?.installmentId) ?? null;

  if (items.length === 0) {
    return (
      <EmptyState icon={<Receipt />} title={t('payables.empty.noItemsTitle')} sub={t('payables.empty.noItemsSub')} />
    );
  }

  return (
    <>
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: 12 }}>
        <strong>{t('workflow.payment.weekSummary', { amount: brl(total), count: items.length })}</strong>
        <Button
          icon={<FileOutput />}
          onClick={() => {
            void nav('/app/remessas');
          }}
        >
          {t('payables.payment.generateRemittance')}
        </Button>
      </div>

      {groups.map((g) => (
        <Panel key={g.key} title={`${t(GROUP_LABEL[g.key] ?? '')} · ${brl(g.subtotal)}`}>
          <table>
            <thead>
              <tr>
                <th>{t('payables.columns.dueDate')}</th>
                <th>{t('payables.columns.vendor')}</th>
                <th>{t('payables.columns.installment')}</th>
                <th style={{ textAlign: 'right' }}>{t('payables.columns.amount')}</th>
                <th>{t('payables.columns.status')}</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {g.items.map((l) => (
                <tr key={l.installmentId}>
                  <td className='t-mono'>{fmtDate(l.dueDate)}</td>
                  <td>{l.counterpartyName || '—'}</td>
                  <td className='t-mono'>
                    {l.sequence}/{l.installmentCount}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brl(l.amount)}
                  </td>
                  <td>
                    <InstallmentStatusBadge s={l.installmentStatus} />
                  </td>
                  <td>
                    {l.installmentStatus !== 'InRemittance' && l.installmentStatus !== 'Paid' && (
                      <Button icon={<Receipt />} onClick={() => setBaixa(l)}>
                        {t('payments.register')}
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </Panel>
      ))}

      <Drawer
        open={!!baixa}
        onClose={() => setBaixa(null)}
        title={t('payments.register')}
        sub={baixa?.counterpartyName}
      >
        {detail.data && installment && (
          <PaymentForm
            payable={detail.data}
            installment={installment}
            onDone={() => setBaixa(null)}
            onCancel={() => setBaixa(null)}
          />
        )}
      </Drawer>
    </>
  );
}
