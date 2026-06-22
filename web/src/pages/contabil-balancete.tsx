import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Scale } from 'lucide-react';
import { useTrialBalance } from '../queries';
import { brlCents } from '../lib';
import { EmptyState, PageHeader, Panel } from '../ui';
import { ConsolidatedPicker } from './contabil-shared';

export function ContabilBalancetePage() {
  const { t } = useTranslation();
  const [companyId, setCompanyId] = useState('');
  const query = useTrialBalance(companyId || undefined);
  const report = query.data;
  const rows = report?.rows ?? [];

  return (
    <>
      <PageHeader title={t('accounting.trialBalance.title')} sub={t('accounting.trialBalance.subtitle')}>
        <ConsolidatedPicker value={companyId} onChange={setCompanyId} />
      </PageHeader>

      {query.isLoading ? (
        <div className='help'>{t('accounting.journal.loading')}</div>
      ) : rows.length === 0 ? (
        <EmptyState
          icon={<Scale />}
          title={t('accounting.trialBalance.empty.title')}
          sub={t('accounting.trialBalance.empty.sub')}
        />
      ) : (
        <Panel
          title={t('accounting.trialBalance.title')}
          action={
            <span className={`badge-status ${report?.isBalanced ? 'badge-aprovado' : 'badge-rejeitado'}`}>
              {report?.isBalanced ? t('accounting.trialBalance.balanced') : t('accounting.trialBalance.unbalanced')}
            </span>
          }
        >
          <table>
            <thead>
              <tr>
                <th>{t('accounting.trialBalance.columns.code')}</th>
                <th>{t('accounting.trialBalance.columns.account')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.debit')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.credit')}</th>
                <th style={{ textAlign: 'right' }}>{t('accounting.trialBalance.columns.balance')}</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((r) => (
                <tr key={r.ledgerAccountId}>
                  <td className='t-mono'>{r.accountCode}</td>
                  <td>{r.accountName}</td>
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
                <td colSpan={2} style={{ fontWeight: 600 }}>
                  {t('accounting.trialBalance.total')}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                  {brlCents(report?.totalDebit ?? 0)}
                </td>
                <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                  {brlCents(report?.totalCredit ?? 0)}
                </td>
                <td />
              </tr>
            </tfoot>
          </table>
        </Panel>
      )}
    </>
  );
}
