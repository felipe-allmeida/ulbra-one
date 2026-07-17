import { Fragment } from 'react';
import { useTranslation } from 'react-i18next';
import type { GeneralLedgerAccountView } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel } from '../../ui';

/** Planilha (spreadsheet) view of the Livro Razão: a single flat table grouped by account — an account
 *  header row (title on the left, opening balance on the right), one row per movement with its running
 *  balance, and an account total (closing) row. Column alignment lives in .razao-planilha (index.css). */
export function SpreadsheetView({
  accounts,
  ccLabels,
}: {
  accounts: GeneralLedgerAccountView[];
  ccLabels: Map<string, string>;
}) {
  const { t } = useTranslation();
  const col = (k: string) => t(`accounting.generalLedger.columns.${k}`);
  return (
    <Panel title=''>
      <div style={{ overflowX: 'auto' }}>
        <table className='razao-planilha'>
          <thead>
            <tr>
              <th>{col('date')}</th>
              <th>{col('history')}</th>
              <th>{col('costCenter')}</th>
              <th>{col('debit')}</th>
              <th>{col('credit')}</th>
              <th>{col('balance')}</th>
            </tr>
          </thead>
          <tbody>
            {accounts.map((a) => (
              <Fragment key={a.ledgerAccountId}>
                <tr className='razao-planilha-account'>
                  <td colSpan={6}>
                    <div className='razao-planilha-account-row'>
                      <span>{[a.accountCode, a.reducedCode, a.accountName].filter(Boolean).join(' · ')}</span>
                      <span className='razao-planilha-account-opening'>
                        {t('accounting.generalLedger.opening')}: {brlCents(a.openingBalance)}
                      </span>
                    </div>
                  </td>
                </tr>
                {a.movements.map((m, i) => (
                  <tr key={i} className='razao-planilha-move'>
                    <td>{fmtDate(m.competenceDate)}</td>
                    <td>{m.history}</td>
                    <td>{m.costCenterId ? (ccLabels.get(m.costCenterId) ?? '') : ''}</td>
                    <td className='amount-cell'>{m.debit ? brlCents(m.debit) : ''}</td>
                    <td className='amount-cell'>{m.credit ? brlCents(m.credit) : ''}</td>
                    <td className='amount-cell'>{brlCents(m.accumulatedBalance)}</td>
                  </tr>
                ))}
                <tr className='razao-planilha-total'>
                  <td colSpan={3}>{t('accounting.generalLedger.closing')}</td>
                  <td className='amount-cell'>{brlCents(a.totalDebit)}</td>
                  <td className='amount-cell'>{brlCents(a.totalCredit)}</td>
                  <td className='amount-cell'>{brlCents(a.closingBalance)}</td>
                </tr>
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
