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
    </Panel>
  );
}
