import { useTranslation } from 'react-i18next';
import { Receipt } from 'lucide-react';
import { type Payable, type Installment } from '../../api';
import { brl, brlCents } from '../../lib';
import { Button, Panel } from '../../ui';
import { fmtDate } from '../../datetime';
import { InstallmentStatusBadge } from './badges';

/** Installments panel for the payable detail. `onRegister` opens the payment drawer. */
export function InstallmentsTable({
  payable: p,
  onRegister,
}: {
  payable: Payable;
  onRegister: (installment: Installment) => void;
}) {
  const { t } = useTranslation();
  return (
    <Panel title={t('payables.installmentsTitle')}>
      <table>
        <thead>
          <tr>
            <th>{t('installments.columns.sequence')}</th>
            <th>{t('installments.columns.dueDate')}</th>
            <th style={{ textAlign: 'right' }}>{t('installments.columns.amount')}</th>
            <th>{t('installments.columns.status')}</th>
            <th />
          </tr>
        </thead>
        <tbody>
          {[...p.installments]
            .sort((a, b) => a.sequence - b.sequence)
            .map((inst) => {
              const hasWithholding = inst.netAmount !== inst.amount;
              return (
                <tr key={inst.id}>
                  <td className='t-mono'>
                    {inst.sequence}/{p.installments.length}
                  </td>
                  <td className='t-mono'>{fmtDate(inst.dueDate)}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {hasWithholding ? (
                      <span>
                        <span
                          style={{ textDecoration: 'line-through', color: 'var(--text-muted, #888)', fontSize: 12 }}
                        >
                          {brl(inst.amount)}
                        </span>{' '}
                        <span title={t('installments.columns.net')}>{brlCents(inst.netAmount)}</span>
                      </span>
                    ) : (
                      brl(inst.amount)
                    )}
                  </td>
                  <td>
                    <InstallmentStatusBadge s={inst.status} />
                  </td>
                  <td className='td-action'>
                    {p.status === 'Scheduled' && inst.status !== 'Paid' && inst.status !== 'InRemittance' && (
                      <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                        <Button icon={<Receipt />} onClick={() => onRegister(inst)}>
                          {t('payments.register')}
                        </Button>
                      </div>
                    )}
                  </td>
                </tr>
              );
            })}
        </tbody>
      </table>
    </Panel>
  );
}
