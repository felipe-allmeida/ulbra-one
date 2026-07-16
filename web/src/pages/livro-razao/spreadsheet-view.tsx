import { Fragment } from 'react';
import { useTranslation } from 'react-i18next';
import type { GeneralLedgerAccountView } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel } from '../../ui';

/** Planilha (spreadsheet) view of the Livro Razão: a single flat table grouped by account — an account
 *  header row, the opening-balance row, one row per movement with its running balance, and an account
 *  total (closing) row. Same data as the per-account panels, denser and continuous. */
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
              <th>{col('document')}</th>
              <th>{col('costCenter')}</th>
              <th style={{ textAlign: 'right' }}>{col('debit')}</th>
              <th style={{ textAlign: 'right' }}>{col('credit')}</th>
              <th style={{ textAlign: 'right' }}>{col('balance')}</th>
            </tr>
          </thead>
          <tbody>
            {accounts.map((a) => (
              <Fragment key={a.ledgerAccountId}>
                <tr className='razao-planilha-account'>
                  <td colSpan={7}>{[a.accountCode, a.reducedCode, a.accountName].filter(Boolean).join(' · ')}</td>
                </tr>
                <tr className='razao-planilha-opening'>
                  <td colSpan={6}>{t('accounting.generalLedger.opening')}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(a.openingBalance)}
                  </td>
                </tr>
                {a.movements.map((m, i) => (
                  <tr key={i}>
                    <td>{fmtDate(m.competenceDate)}</td>
                    <td>{m.history}</td>
                    <td className='t-mono'>{m.documentRef ?? ''}</td>
                    <td>{m.costCenterId ? (ccLabels.get(m.costCenterId) ?? '') : ''}</td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {m.debit ? brlCents(m.debit) : ''}
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {m.credit ? brlCents(m.credit) : ''}
                    </td>
                    <td className='amount-cell' style={{ textAlign: 'right' }}>
                      {brlCents(m.accumulatedBalance)}
                    </td>
                  </tr>
                ))}
                <tr className='razao-planilha-total'>
                  <td colSpan={4}>{t('accounting.generalLedger.closing')}</td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(a.totalDebit)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(a.totalCredit)}
                  </td>
                  <td className='amount-cell' style={{ textAlign: 'right' }}>
                    {brlCents(a.closingBalance)}
                  </td>
                </tr>
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>
    </Panel>
  );
}
