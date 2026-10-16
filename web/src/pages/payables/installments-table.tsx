import { useTranslation } from 'react-i18next';
import { Receipt } from 'lucide-react';
import { type Payable, type Installment } from '../../api';
import { brl } from '../../lib';
import { P, usePermissions } from '../../permissions';
import { Button, Panel } from '../../ui';
import { fmtDate } from '../../datetime';
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
  const table = (
    <table>
      <thead>
        <tr>
          <th>{t('installments.columns.sequence')}</th>
          <th>{t('installments.columns.dueDate')}</th>
          <th style={{ textAlign: 'right' }}>
            {t(showNet ? 'payables.columns.grossAmount' : 'installments.columns.amount')}
          </th>
          {showNet && <th style={{ textAlign: 'right' }}>{t('installments.columns.net')}</th>}
          <th>{t('installments.columns.status')}</th>
          {onRegister && <th />}
        </tr>
      </thead>
      <tbody>
        {[...p.installments]
          .sort((a, b) => a.sequence - b.sequence)
          .map((inst) => (
            <tr key={inst.id}>
              <td className='t-mono'>
                {inst.sequence}/{p.installments.length}
              </td>
              <td className='t-mono'>{fmtDate(inst.dueDate)}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brl(inst.amount)}
              </td>
              {showNet && (
                <td className='amount-cell' style={{ textAlign: 'right' }}>
                  {brl(inst.netAmount)}
                </td>
              )}
              <td>
                <InstallmentStatusBadge s={inst.status} />
              </td>
              {onRegister && (
                <td className='td-action'>
                  {canPay && p.status === 'Scheduled' && inst.status !== 'Paid' && inst.status !== 'InRemittance' && (
                    <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                      <Button icon={<Receipt />} onClick={() => onRegister(inst)}>
                        {t('payments.register')}
                      </Button>
                    </div>
                  )}
                </td>
              )}
            </tr>
          ))}
      </tbody>
    </table>
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
