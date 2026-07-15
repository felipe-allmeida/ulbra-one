import { useTranslation } from 'react-i18next';
import { Receipt as ReceiptIcon } from 'lucide-react';
import { type Receivable, type ReceivableInstallment } from '../../api';
import { brl } from '../../lib';
import { P, usePermissions } from '../../permissions';
import { Button, Panel } from '../../ui';
import { fmtDate } from '../../datetime';
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
  return (
    <Panel title={t('receivables.installmentsTitle')}>
      <table>
        <thead>
          <tr>
            <th>{t('receivableInstallments.columns.sequence')}</th>
            <th>{t('receivableInstallments.columns.dueDate')}</th>
            <th style={{ textAlign: 'right' }}>{t('receivableInstallments.columns.amount')}</th>
            <th>{t('receivableInstallments.columns.status')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {[...r.installments]
            .sort((a, b) => a.sequence - b.sequence)
            .map((inst) => (
              <tr key={inst.id}>
                <td className='t-mono'>
                  {inst.sequence}/{r.installments.length}
                </td>
                <td className='t-mono' style={inst.isOverdue ? { color: 'var(--danger, #dc2626)' } : undefined}>
                  {fmtDate(inst.dueDate)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brl(inst.amount)}
                </td>
                <td>
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
                </td>
                <td className='td-action'>
                  {canReceive && r.status === 'AwaitingReceipt' && inst.status !== 'Received' && (
                    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                      <Button icon={<ReceiptIcon />} onClick={() => onRegister(inst)}>
                        {t('receipts.register')}
                      </Button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
        </tbody>
      </table>
    </Panel>
  );
}
