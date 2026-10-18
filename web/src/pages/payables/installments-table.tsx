import { useTranslation } from 'react-i18next';
import { Receipt } from 'lucide-react';
import { type Payable, type Installment } from '../../api';
import { brl } from '../../lib';
import { P, usePermissions } from '../../permissions';
import { Button, Panel } from '../../ui';
import { fmtDate } from '../../datetime';
import { ResponsiveTable, type Column } from '../../components/ResponsiveTable';
import { InstallmentStatusBadge } from './badges';

/**
 * Installments table. `onRegister` (optional) adds the pay-action column and opens the payment drawer; omit it
 * for a read-only view. `bare` drops the Panel card for a flat drawer-section (matching WithholdingSection).
 */
export function InstallmentsTable({
  payable: p,
  onRegister,
  bare,
}: {
  payable: Payable;
  onRegister?: (installment: Installment) => void;
  bare?: boolean;
}) {
  const { t } = useTranslation();
  const { can } = usePermissions();
  const canPay = can(P.payablesPay);
  // The net becomes final (and payable) once accounting completes — show it per installment from there on.
  const showNet = p.status === 'Scheduled' || p.status === 'Paid';

  // Ordem = a ordem já renderizada hoje. `sequence` é a única `title` (identifica a parcela); vencimento é
  // `meta`; bruto é o único `amount` (líquido vira `detail` — cardinalidade do papel `amount`, ver
  // ResponsiveTable.tsx); status e a ação de baixa seguem o mesmo tratamento do payable-table.tsx.
  const columns: Column<Installment>[] = [
    {
      key: 'sequence',
      header: t('installments.columns.sequence'),
      role: 'title',
      cell: (inst) => (
        <span className='t-mono'>
          {inst.sequence}/{p.installments.length}
        </span>
      ),
    },
    {
      key: 'dueDate',
      header: t('installments.columns.dueDate'),
      role: 'meta',
      cell: (inst) => <span className='t-mono'>{fmtDate(inst.dueDate)}</span>,
    },
    {
      key: 'amount',
      header: t(showNet ? 'payables.columns.grossAmount' : 'installments.columns.amount'),
      role: 'amount',
      align: 'right',
      cell: (inst) => <span className='amount-cell'>{brl(inst.amount)}</span>,
    },
    ...(showNet
      ? [
          {
            key: 'net',
            header: t('installments.columns.net'),
            role: 'detail' as const,
            align: 'right' as const,
            cell: (inst: Installment) => <span className='amount-cell'>{brl(inst.netAmount)}</span>,
          },
        ]
      : []),
    {
      key: 'status',
      header: t('installments.columns.status'),
      role: 'status',
      cell: (inst) => <InstallmentStatusBadge s={inst.status} />,
    },
    ...(onRegister
      ? [
          {
            key: 'actions',
            header: '',
            role: 'detail' as const,
            cell: (inst: Installment) =>
              canPay && p.status === 'Scheduled' && inst.status !== 'Paid' && inst.status !== 'InRemittance' ? (
                <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                  <Button icon={<Receipt />} onClick={() => onRegister(inst)}>
                    {t('payments.register')}
                  </Button>
                </div>
              ) : null,
          },
        ]
      : []),
  ];

  const table = (
    <ResponsiveTable
      rows={[...p.installments].sort((a, b) => a.sequence - b.sequence)}
      columns={columns}
      rowKey={(inst) => inst.id}
    />
  );

  if (bare) {
    return (
      <div className='drawer-section'>
        <div className='section-title'>{t('payables.installmentsTitle')}</div>
        {table}
      </div>
    );
  }
  return <Panel title={t('payables.installmentsTitle')}>{table}</Panel>;
}
