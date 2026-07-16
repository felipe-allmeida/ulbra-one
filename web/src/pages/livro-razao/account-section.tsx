import { useTranslation } from 'react-i18next';
import type { GeneralLedgerAccountView } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel } from '../../ui';

/** Uma conta do Livro Razão: cabeçalho `código · (reduzido) · nome`, linha de abertura, uma linha por
 *  movimento (com saldo acumulado à direita) e rodapé com totais + saldo final. Read-only. */
export function AccountSection({
  account,
  ccLabels,
}: {
  account: GeneralLedgerAccountView;
  ccLabels: Map<string, string>;
}) {
  const { t } = useTranslation();
  const title = [account.accountCode, account.reducedCode, account.accountName].filter(Boolean).join(' · ');
  return (
    <Panel title={title}>
      <table>
        <thead>
          <tr>
            <th>{t('accounting.generalLedger.columns.date')}</th>
            <th>{t('accounting.generalLedger.columns.history')}</th>
            <th>{t('accounting.generalLedger.columns.document')}</th>
            <th>{t('accounting.generalLedger.columns.costCenter')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.generalLedger.columns.debit')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.generalLedger.columns.credit')}</th>
            <th style={{ textAlign: 'right' }}>{t('accounting.generalLedger.columns.balance')}</th>
          </tr>
        </thead>
        <tbody>
          <tr>
            <td colSpan={6} style={{ fontStyle: 'italic' }}>
              {t('accounting.generalLedger.opening')}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right' }}>
              {brlCents(account.openingBalance)}
            </td>
          </tr>
          {account.movements.map((m, i) => (
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
        </tbody>
        <tfoot>
          <tr>
            <td colSpan={4} style={{ fontWeight: 700 }}>
              {t('accounting.generalLedger.closing')}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(account.totalDebit)}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(account.totalCredit)}
            </td>
            <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
              {brlCents(account.closingBalance)}
            </td>
          </tr>
        </tfoot>
      </table>
    </Panel>
  );
}
