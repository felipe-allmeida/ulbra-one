import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import type { TrialBalance } from '../../api';
import { brlCents } from '../../lib';
import { Panel } from '../../ui';

/** The balancete table: one row per account (código · cód. resumido · conta · saldo anterior · débitos ·
 *  créditos · saldo final) plus the period totals. The double-entry balancing badge (Σdébito = Σcrédito) is a
 *  valid proof only for the whole ledger — a cost-center slice filters by line, so its debit and credit legs
 *  usually fall under different (or no) cost centers and the slice legitimately does not balance. When a cost
 *  center is applied we therefore show a neutral "partial view" label instead of the balancing badge. */
export function BalanceteTable({
  report,
  from,
  to,
  costCenterId,
  unitId,
}: {
  report: TrialBalance;
  from: string;
  to: string;
  costCenterId: string;
  unitId: string;
}) {
  const { t } = useTranslation();
  // Drill-down into the Livro Razão for a single account, carrying over the current period/context. The Razão
  // filters by ledger-account id (its combobox stores the id and shows the code as label), so we pass the id.
  // The empresa/consolidado scope is not carried in the URL — it is the topbar's, shared by both screens.
  const ledgerHref = (ledgerAccountId: string) => {
    const qs = new URLSearchParams({ account: ledgerAccountId });
    if (from) qs.set('from', from);
    if (to) qs.set('to', to);
    if (costCenterId) qs.set('costCenterId', costCenterId);
    if (unitId) qs.set('unitId', unitId);
    return `/app/accounting/ledger?${qs.toString()}`;
  };
  return (
    <Panel
      title={t('accounting.trialBalance.title')}
      action={
        report.costCenterId ? (
          <span className='badge-status'>{t('accounting.trialBalance.costCenterPartial')}</span>
        ) : (
          <span className={`badge-status ${report.isBalanced ? 'badge-aprovado' : 'badge-rejeitado'}`}>
            {report.isBalanced ? t('accounting.trialBalance.balanced') : t('accounting.trialBalance.unbalanced')}
          </span>
        )
      }
    >
      <table>
        <thead>
          <tr>
            <th>{t('accounting.trialBalance.columns.code')}</th>
            <th>{t('accounting.trialBalance.columns.reducedCode')}</th>
            <th>{t('accounting.trialBalance.columns.account')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.openingBalance')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.debit')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.credit')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.balance')}</th>
          </tr>
        </thead>
        <tbody>
          {report.rows.map((r) => (
            <tr key={r.ledgerAccountId}>
              <td className='t-mono'>
                <Link className='linklike' to={ledgerHref(r.ledgerAccountId)}>
                  {r.accountCode}
                </Link>
              </td>
              <td className='t-mono'>{r.reducedCode ?? '—'}</td>
              <td>{r.accountName}</td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.openingBalance)}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.totalDebit)}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right' }}>
                {brlCents(r.totalCredit)}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 600 }}>
                {brlCents(r.balance)}
              </td>
            </tr>
          ))}
        </tbody>
        <tfoot>
          <tr>
            <td colSpan={4} style={{ fontWeight: 600 }}>
              {t('accounting.trialBalance.total')}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(report.totalDebit)}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(report.totalCredit)}
            </td>
            <td />
          </tr>
        </tfoot>
      </table>
    </Panel>
  );
}
