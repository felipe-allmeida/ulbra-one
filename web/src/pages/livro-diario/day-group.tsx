import { useTranslation } from 'react-i18next';
import { type JournalBookDayView } from '../../api';
import { brlCents } from '../../lib';
import { fmtDate } from '../../datetime';
import { Panel } from '../../ui';
import { EntryBlock } from './entry-block';

/** One competence day: a panel titled with the date, containing a single table — the column header once,
 *  one <tbody> per entry, and a footer with the day's débito/crédito totals. */
export function DayGroup({ day }: { day: JournalBookDayView }) {
  const { t } = useTranslation();
  return (
    <Panel title={fmtDate(day.date)}>
      {/* Múltiplos <tbody> (um por lançamento), cada um com uma linha de cabeçalho colSpan seguida de
       *  suas linhas de débito/crédito: não cabe no modelo "uma linha, um papel por coluna" do
       *  ResponsiveTable sem adicionar coluna ou destruir o agrupamento — exceção declarada, mesma
       *  família das planilhas do Contábil. .table-scroll evita que a coluna de crédito fique
       *  inalcançável a 390px; `:not([colspan])` em index.css já exclui a linha de cabeçalho do
       *  lançamento (colSpan=3) da coluna fixa. */}
      <div className='table-scroll'>
        <table>
          <thead>
            <tr>
              <th>{t('accounting.journalBook.columns.account')}</th>
              <th style={{ textAlign: 'right' }}>{t('accounting.journalBook.columns.debit')}</th>
              <th style={{ textAlign: 'right' }}>{t('accounting.journalBook.columns.credit')}</th>
            </tr>
          </thead>
          {day.entries.map((e) => (
            <EntryBlock key={e.id} entry={e} />
          ))}
          <tfoot>
            <tr>
              <td style={{ fontWeight: 700 }}>{t('accounting.journalBook.dayTotal')}</td>
              <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                {brlCents(day.totalDebit)}
              </td>
              <td className='amount-cell' style={{ textAlign: 'right', fontWeight: 700 }}>
                {brlCents(day.totalCredit)}
              </td>
            </tr>
          </tfoot>
        </table>
      </div>
    </Panel>
  );
}
