import { useTranslation } from 'react-i18next';
import { Receipt as ReceiptIcon } from 'lucide-react';
import { type Receivable, type ReceivableInstallment } from '../../api';
import { brl } from '../../lib';
import { P, usePermissions } from '../../permissions';
import { Button, Panel } from '../../ui';
import { fmtDate } from '../../datetime';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { ReceivableStatusBadge, ReceivableInstallmentStatusBadge, OverdueBadge } from './badges';

/** Installments panel for the receivable detail. `onRegister` opens the receipt drawer. */
export function InstallmentsTable({
  receivable: r,
  onRegister,
}: {
  receivable: Receivable;
  onRegister: (installment: ReceivableInstallment) => void;
}) {
  const { t } = useTranslation();
  const canReceive = usePermissions().can(P.receivablesReceive);

  // Mesma regra de papel do payables/installments-table.tsx: sequence é `title`, vencimento `meta`, valor o
  // único `amount`, status+atraso `status`, ação de baixa `detail`.
  const columns: Column<ReceivableInstallment>[] = [
    {
      key: 'sequence',
      header: t('receivableInstallments.columns.sequence'),
      role: 'title',
      cell: (inst) => (
        <span className='t-mono'>
          {inst.sequence}/{r.installments.length}
        </span>
      ),
    },
    {
      key: 'dueDate',
      header: t('receivableInstallments.columns.dueDate'),
      role: 'meta',
      cell: (inst) => (
        <span className='t-mono' style={inst.isOverdue ? { color: 'var(--danger, #dc2626)' } : undefined}>
          {fmtDate(inst.dueDate)}
        </span>
      ),
    },
    {
      key: 'amount',
      header: t('receivableInstallments.columns.amount'),
      role: 'amount',
      align: 'right',
      cell: (inst) => <span className='amount-cell'>{brl(inst.amount)}</span>,
    },
    {
      key: 'status',
      header: t('receivableInstallments.columns.status'),
      role: 'status',
      cell: (inst) => (
        <div style={{ display: 'flex', gap: 6, alignItems: 'center', flexWrap: 'wrap' }}>
          {r.status === 'Cancelled' || r.status === 'WrittenOff' ? (
            // Terminal receivable: the parcela is voided — show the receivable's status
            // (e.g. "Cancelada") instead of its stale "Aberta"/overdue badges.
            <ReceivableStatusBadge s={r.status} />
          ) : (
            <>
              <ReceivableInstallmentStatusBadge s={inst.status} />
              {inst.isOverdue && <OverdueBadge />}
            </>
          )}
        </div>
      ),
    },
    {
      key: 'actions',
      header: '',
      role: 'detail',
      cell: (inst) =>
        canReceive && r.status === 'AwaitingReceipt' && inst.status !== 'Received' ? (
          <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
            <Button icon={<ReceiptIcon />} onClick={() => onRegister(inst)}>
              {t('receipts.register')}
            </Button>
          </div>
        ) : null,
    },
  ];

  return (
    <Panel title={t('receivables.installmentsTitle')}>
      <ResponsiveTable
        rows={[...r.installments].sort((a, b) => a.sequence - b.sequence)}
        columns={columns}
        rowKey={(inst) => inst.id}
      />
    </Panel>
  );
}
